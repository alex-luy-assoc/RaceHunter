using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RaceHunter.Application.Agents;
using RaceHunter.Application.Hunts;
using RaceHunter.Application.Messaging;
using RaceHunter.Contracts;
using RaceHunter.Domain.Budgets;
using RaceHunter.Gemini;
using RaceHunter.Worker.Execution;
using Xunit;

namespace RaceHunter.Worker.Tests;

public sealed class WorkDispatcherTests
{
    [Fact]
    public async Task Busy_delivery_is_nacked_for_eventual_recovery()
    {
        var inbox = new FakeInbox { Acquire = new WorkAcquireResult(WorkAcquireOutcome.Busy, 1, null) };
        var plan = new FakePlanHandler();
        var dispatcher = CreateDispatcher(inbox, plan);

        var outcome = await dispatcher.DispatchAsync(Message("PlanRequested"), "message-1", CancellationToken.None);

        Assert.Equal(WorkDispatchOutcome.Retry, outcome);
        Assert.Equal(0, plan.Calls);
    }

    [Fact]
    public async Task Lost_heartbeat_cancels_processing_without_old_owner_state_writes()
    {
        var inbox = new FakeInbox
        {
            Acquire = new WorkAcquireResult(WorkAcquireOutcome.Acquired, 1, null),
            HeartbeatResult = false
        };
        var plan = new FakePlanHandler { WaitForCancellation = true };
        var dispatcher = CreateDispatcher(inbox, plan, heartbeatMilliseconds: 10);

        var outcome = await dispatcher.DispatchAsync(Message("PlanRequested"), "message-2", CancellationToken.None);

        Assert.Equal(WorkDispatchOutcome.Retry, outcome);
        Assert.True(plan.CancellationObserved);
        Assert.Equal(0, inbox.CompleteCalls);
        Assert.Equal(0, inbox.FailureCalls);
    }

    [Fact]
    public async Task Transient_planning_failure_remains_retryable_until_persisted_budget_exhaustion()
    {
        var inbox = new FakeInbox { Acquire = new WorkAcquireResult(WorkAcquireOutcome.Acquired, 1, null) };
        var plan = new FakePlanHandler { Failure = new ModelOutputException(ModelOutcome.TransientFailure, "provider unavailable", modelCallsConsumed: 1) };
        var subjects = new FakeSubjectStore { MaxRetries = 1 };
        var dispatcher = CreateDispatcher(inbox, plan, subjects: subjects);

        var outcome = await dispatcher.DispatchAsync(Message("PlanRequested"), "message-3", CancellationToken.None);

        Assert.Equal(WorkDispatchOutcome.Retry, outcome);
        Assert.Equal(1, inbox.FailureCalls);
        Assert.Equal(1, inbox.LastMaxRetries);
        Assert.Equal(0, subjects.DeadLetterCalls);
    }

    [Fact]
    public async Task Transient_model_failure_does_not_terminalize_planning_subject()
    {
        var hunt = new HuntSnapshot(Guid.NewGuid(), "test", ExperimentBudget.PublicSandbox, HuntStatus.Planning, null, null, null, DateTime.UtcNow);
        var store = new FakeHuntStore(hunt);
        var inbox = new PlanningInbox();
        var handler = new PlanWorkHandler(store, new TransientPlanner(), inbox);

        var error = await Assert.ThrowsAsync<ModelOutputException>(() =>
            handler.ExecuteAsync(hunt.Id, Guid.NewGuid(), "worker-a", null, CancellationToken.None));

        Assert.Equal(ModelOutcome.TransientFailure, error.Outcome);
        Assert.Equal(0, store.PlanningFailedCalls);
    }

    [Fact]
    public async Task Planning_model_budget_is_cumulative_across_transient_redelivery()
    {
        var budget = new ExperimentBudget(2, 2, 4, 1, TimeSpan.FromSeconds(30), 5);
        var hunt = new HuntSnapshot(Guid.NewGuid(), "test", budget, HuntStatus.Planning, null, null, null, DateTime.UtcNow);
        var store = new FakeHuntStore(hunt);
        var inbox = new PlanningInbox();
        var model = new AlwaysTransientModelClient();
        var handler = new PlanWorkHandler(store, new ScenarioPlanner(model), inbox);
        var workId = Guid.NewGuid();

        await Assert.ThrowsAsync<ModelOutputException>(() =>
            handler.ExecuteAsync(hunt.Id, workId, "worker-a", null, CancellationToken.None));
        var usageCheckpoint = Assert.IsType<WorkCheckpoint>(inbox.LastCheckpoint);
        await handler.ExecuteAsync(hunt.Id, workId, "worker-b", usageCheckpoint, CancellationToken.None);

        Assert.Equal(1, model.Calls);
        Assert.Equal(1, store.PlanningFailedCalls);
        Assert.Equal(ModelOutcome.BudgetExhausted, store.LastFailureOutcome);
        Assert.Contains("\"modelCallsConsumed\":1", inbox.LastCheckpoint!.StateJson, StringComparison.Ordinal);
    }

    private static WorkDispatcher CreateDispatcher(
        FakeInbox inbox,
        FakePlanHandler plan,
        FakeSubjectStore? subjects = null,
        int heartbeatMilliseconds = 1000)
    {
        var services = new ServiceCollection().AddSingleton<IWorkInbox>(inbox).BuildServiceProvider();
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Work:LeaseSeconds"] = "30",
            ["Work:HeartbeatIntervalMilliseconds"] = heartbeatMilliseconds.ToString()
        }).Build();
        return new WorkDispatcher(
            inbox,
            new FakePublisher(),
            plan,
            new FakeCampaignHandler(),
            subjects ?? new FakeSubjectStore(),
            configuration,
            services.GetRequiredService<IServiceScopeFactory>());
    }

    private static WorkMessage Message(string kind) => WorkMessage.Create(kind, Guid.NewGuid(), "test", DateTime.UtcNow);

    private sealed class FakeInbox : IWorkInbox
    {
        public required WorkAcquireResult Acquire { get; init; }
        public bool HeartbeatResult { get; init; } = true;
        public int CompleteCalls { get; private set; }
        public int FailureCalls { get; private set; }
        public int LastMaxRetries { get; private set; }
        public Task<WorkAcquireResult> TryAcquireAsync(Guid workId, string messageId, string owner, DateTime nowUtc, TimeSpan leaseDuration, CancellationToken cancellationToken) => Task.FromResult(Acquire);
        public Task<bool> HeartbeatAsync(Guid workId, string owner, DateTime nowUtc, TimeSpan leaseDuration, CancellationToken cancellationToken) => Task.FromResult(HeartbeatResult);
        public Task SaveCheckpointAsync(Guid workId, string owner, WorkCheckpoint checkpoint, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task CompleteAsync(Guid workId, string owner, DateTime nowUtc, CancellationToken cancellationToken) { CompleteCalls++; return Task.CompletedTask; }
        public Task<WorkFailureOutcome> RecordFailureAsync(Guid workId, string owner, WorkFailure failure, int maxRetries, DateTime nowUtc, CancellationToken cancellationToken) { FailureCalls++; LastMaxRetries = maxRetries; return Task.FromResult(WorkFailureOutcome.RetryScheduled); }
    }

    private sealed class FakePlanHandler : IPlanWorkHandler
    {
        public int Calls { get; private set; }
        public bool WaitForCancellation { get; init; }
        public bool CancellationObserved { get; private set; }
        public Exception? Failure { get; init; }
        public async Task ExecuteAsync(Guid huntId, Guid workId, string leaseOwner, WorkCheckpoint? checkpoint, CancellationToken cancellationToken)
        {
            Calls++;
            if (Failure is not null) throw Failure;
            if (!WaitForCancellation) return;
            try { await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { CancellationObserved = true; throw; }
        }
    }

    private sealed class FakeCampaignHandler : ICampaignWorkHandler
    {
        public Task ExecuteAsync(Guid runId, Guid workId, string leaseOwner, WorkCheckpoint? checkpoint, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeSubjectStore : IWorkSubjectStore
    {
        public int MaxRetries { get; init; } = 2;
        public int DeadLetterCalls { get; private set; }
        public Task<int> GetMaxRetriesAsync(WorkKind kind, Guid subjectId, CancellationToken cancellationToken) => Task.FromResult(MaxRetries);
        public Task MarkDeadLetteredAsync(WorkKind kind, Guid subjectId, WorkFailure failure, DateTime nowUtc, CancellationToken cancellationToken) { DeadLetterCalls++; return Task.CompletedTask; }
    }

    private sealed class FakePublisher : IWorkPublisher
    {
        public Task PublishAsync(WorkDispatch message, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task PublishDeadLetterAsync(WorkDispatch message, WorkFailure failure, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class TransientPlanner : IScenarioPlanner
    {
        public Task<ScenarioPlan> PlanAsync(PlanningContext context, CancellationToken cancellationToken) =>
            throw new ModelOutputException(ModelOutcome.TransientFailure, "provider unavailable", modelCallsConsumed: 1);
    }

    private sealed class FakeHuntStore(HuntSnapshot hunt) : IHuntStore
    {
        public int PlanningFailedCalls { get; private set; }
        public ModelOutcome? LastFailureOutcome { get; private set; }
        public Task AddAsync(HuntSnapshot value, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<HuntSnapshot?> GetAsync(Guid huntId, CancellationToken cancellationToken) => Task.FromResult<HuntSnapshot?>(hunt);
        public Task<HuntSnapshot?> GetByRunAsync(Guid runId, CancellationToken cancellationToken) => Task.FromResult<HuntSnapshot?>(null);
        public Task<IReadOnlyList<HuntEvent>> GetEventsAsync(Guid huntId, long after, CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<HuntEvent>>([]);
        public Task<WorkDispatch?> RequestPlanningAsync(Guid huntId, DateTime nowUtc, CancellationToken cancellationToken) => Task.FromResult<WorkDispatch?>(null);
        public Task SavePlanAsync(Guid huntId, ScenarioPlan plan, DateTime nowUtc, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task MarkPlanningFailedAsync(Guid huntId, ModelOutcome outcome, string sanitizedDiagnostic, DateTime nowUtc, CancellationToken cancellationToken) { PlanningFailedCalls++; LastFailureOutcome = outcome; return Task.CompletedTask; }
    }

    private sealed class PlanningInbox : IWorkInbox
    {
        public WorkCheckpoint? LastCheckpoint { get; private set; }
        public Task<WorkAcquireResult> TryAcquireAsync(Guid workId, string messageId, string owner, DateTime nowUtc, TimeSpan leaseDuration, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> HeartbeatAsync(Guid workId, string owner, DateTime nowUtc, TimeSpan leaseDuration, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task SaveCheckpointAsync(Guid workId, string owner, WorkCheckpoint checkpoint, CancellationToken cancellationToken) { LastCheckpoint = checkpoint; return Task.CompletedTask; }
        public Task CompleteAsync(Guid workId, string owner, DateTime nowUtc, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<WorkFailureOutcome> RecordFailureAsync(Guid workId, string owner, WorkFailure failure, int maxRetries, DateTime nowUtc, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class AlwaysTransientModelClient : IStructuredModelClient
    {
        public int Calls { get; private set; }
        public Task<ModelResponse> GenerateAsync(ModelRequest request, CancellationToken cancellationToken)
        {
            Calls++;
            throw new ModelOutputException(ModelOutcome.TransientFailure, "provider unavailable");
        }
    }
}

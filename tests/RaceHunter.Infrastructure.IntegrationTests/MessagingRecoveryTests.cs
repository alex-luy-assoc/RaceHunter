using Microsoft.EntityFrameworkCore;
using RaceHunter.Application.Hunts;
using RaceHunter.Application.Messaging;
using RaceHunter.Contracts;
using RaceHunter.Domain.Budgets;
using RaceHunter.Domain.Runs;
using RaceHunter.Infrastructure.Persistence;
using Xunit;

namespace RaceHunter.Infrastructure.IntegrationTests;

public sealed class MessagingRecoveryTests(PersistenceDatabaseFixture fixture) : IClassFixture<PersistenceDatabaseFixture>
{
    [Fact]
    public void Work_envelope_round_trips_versioned_pubsub_contract()
    {
        var message = WorkMessage.Create("RunRequested", Guid.NewGuid(), "correlation-1", DateTime.UnixEpoch);
        var parsed = WorkMessage.Parse(message.Serialize());
        Assert.Equal("work-v1", parsed.Version);
        Assert.Equal(message.WorkId, parsed.WorkId);
        Assert.Equal("RunRequested", parsed.Kind);
    }

    [Fact]
    public async Task First_delivery_acquires_one_database_backed_lease()
    {
        await using var context = await CreateContextAsync();
        var result = await new WorkInboxStore(context).TryAcquireAsync(Guid.NewGuid(), "message-1", "worker-a", DateTime.UtcNow, TimeSpan.FromSeconds(30), CancellationToken.None);
        Assert.Equal(WorkAcquireOutcome.Acquired, result.Outcome);
        Assert.Equal(1, result.DeliveryAttempt);
    }

    [Fact]
    public async Task Completed_delivery_is_acknowledged_as_duplicate()
    {
        await using var context = await CreateContextAsync();
        var store = new WorkInboxStore(context);
        var workId = Guid.NewGuid();
        await store.TryAcquireAsync(workId, "message-2", "worker-a", DateTime.UtcNow, TimeSpan.FromSeconds(30), CancellationToken.None);
        await store.CompleteAsync(workId, "worker-a", DateTime.UtcNow, CancellationToken.None);
        var duplicate = await store.TryAcquireAsync(workId, "message-duplicate", "worker-b", DateTime.UtcNow, TimeSpan.FromSeconds(30), CancellationToken.None);
        Assert.Equal(WorkAcquireOutcome.Duplicate, duplicate.Outcome);
    }

    [Fact]
    public async Task Active_lease_prevents_concurrent_ownership()
    {
        await using var context = await CreateContextAsync();
        var store = new WorkInboxStore(context);
        var now = DateTime.UtcNow;
        var workId = Guid.NewGuid();
        await store.TryAcquireAsync(workId, "message-3", "worker-a", now, TimeSpan.FromSeconds(30), CancellationToken.None);
        var busy = await store.TryAcquireAsync(workId, "message-3-redelivery", "worker-b", now.AddSeconds(1), TimeSpan.FromSeconds(30), CancellationToken.None);
        Assert.Equal(WorkAcquireOutcome.Busy, busy.Outcome);
    }

    [Fact]
    public async Task Expired_lease_resumes_latest_persisted_attempt_boundary()
    {
        await using var context = await CreateContextAsync();
        var store = new WorkInboxStore(context);
        var now = DateTime.UtcNow;
        var workId = Guid.NewGuid();
        var run = ExperimentRun.Queue(Guid.NewGuid(), ExperimentBudget.PublicSandbox, now);
        await new RunStore(context).AddAsync(run, CancellationToken.None);
        await store.TryAcquireAsync(workId, "message-4", "worker-a", now, TimeSpan.FromSeconds(5), CancellationToken.None);
        await new AgentDecisionCheckpointStore(context).PersistAsync(
            workId,
            "worker-a",
            new AgentIterationRecord(Guid.NewGuid(), run.Id, 2, "pass", "Repeat", "bounded", "fake", "strategy-v1", "invocation-2", now.AddSeconds(2)),
            new WorkCheckpoint("attempt-completed", 2, "{\"attemptId\":\"a2\"}", now.AddSeconds(2)),
            "Iteration 2 persisted.",
            CancellationToken.None);
        var resumed = await store.TryAcquireAsync(workId, "message-4-redelivery", "worker-b", now.AddSeconds(6), TimeSpan.FromSeconds(5), CancellationToken.None);
        Assert.Equal(WorkAcquireOutcome.Resumed, resumed.Outcome);
        Assert.Equal("attempt-completed", resumed.Checkpoint!.Boundary);
        Assert.Equal(2, resumed.Checkpoint.Iteration);
        Assert.Equal(1, await context.AgentIterations.CountAsync(item => item.RunId == run.Id));
        Assert.Equal(1, await context.RunEvents.CountAsync(item => item.RunId == run.Id && item.Kind == "agent-decision"));
    }

    [Fact]
    public async Task Heartbeat_renews_only_the_current_lease_owner()
    {
        await using var context = await CreateContextAsync();
        var store = new WorkInboxStore(context);
        var now = DateTime.UtcNow;
        var workId = Guid.NewGuid();
        await store.TryAcquireAsync(workId, "message-5", "worker-a", now, TimeSpan.FromSeconds(5), CancellationToken.None);
        Assert.False(await store.HeartbeatAsync(workId, "worker-b", now.AddSeconds(1), TimeSpan.FromSeconds(10), CancellationToken.None));
        Assert.True(await store.HeartbeatAsync(workId, "worker-a", now.AddSeconds(1), TimeSpan.FromSeconds(10), CancellationToken.None));
        var busy = await store.TryAcquireAsync(workId, "message-5-redelivery", "worker-b", now.AddSeconds(7), TimeSpan.FromSeconds(5), CancellationToken.None);
        Assert.Equal(WorkAcquireOutcome.Busy, busy.Outcome);
    }

    [Fact]
    public async Task Classified_retry_and_dead_letter_outcomes_are_durable()
    {
        await using var context = await CreateContextAsync();
        var store = new WorkInboxStore(context);
        var now = DateTime.UtcNow;
        var retryWork = Guid.NewGuid();
        await store.TryAcquireAsync(retryWork, "message-6", "worker-a", now, TimeSpan.FromSeconds(5), CancellationToken.None);
        var retry = await store.RecordFailureAsync(retryWork, "worker-a", new WorkFailure(WorkFailureCategory.Transport, true, true, "timeout"), maxRetries: 2, now, CancellationToken.None);
        Assert.Equal(WorkFailureOutcome.RetryScheduled, retry);
        var retryLater = await store.TryAcquireAsync(retryWork, "message-6-redelivery", "worker-b", now.AddMilliseconds(100), TimeSpan.FromSeconds(5), CancellationToken.None);
        Assert.Equal(WorkAcquireOutcome.RetryLater, retryLater.Outcome);

        var poisonWork = Guid.NewGuid();
        await store.TryAcquireAsync(poisonWork, "message-7", "worker-a", now, TimeSpan.FromSeconds(5), CancellationToken.None);
        var deadLetter = await store.RecordFailureAsync(poisonWork, "worker-a", new WorkFailure(WorkFailureCategory.Target, true, false, "unsafe mutation"), maxRetries: 2, now, CancellationToken.None);
        Assert.Equal(WorkFailureOutcome.DeadLettered, deadLetter);
        Assert.Equal(1, await context.DeadLetters.CountAsync(item => item.WorkId == poisonWork));
    }

    private async Task<RaceHunterDbContext> CreateContextAsync()
    {
        var context = new RaceHunterDbContext(new DbContextOptionsBuilder<RaceHunterDbContext>().UseNpgsql(fixture.Database.GetConnectionString()).Options);
        await context.Database.MigrateAsync();
        return context;
    }
}

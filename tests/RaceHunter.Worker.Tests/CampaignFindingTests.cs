using System.Text.Json;
using RaceHunter.Application.Abstractions;
using RaceHunter.Application.Agents;
using RaceHunter.Application.Messaging;
using RaceHunter.Concurrency.Minimization;
using RaceHunter.Concurrency.Replay;
using RaceHunter.Concurrency.Scheduling;
using RaceHunter.Domain.Budgets;
using RaceHunter.Domain.Invariants;
using RaceHunter.Domain.Replays;
using RaceHunter.Domain.Runs;
using RaceHunter.Domain.Tracing;
using RaceHunter.Worker.Execution;
using Xunit;

namespace RaceHunter.Worker.Tests;

public sealed class CampaignFindingTests
{
    [Fact]
    public void Retryable_target_timeout_preserves_the_active_run_for_redelivery()
    {
        var run = ExperimentRun.Queue(Guid.NewGuid(), ExperimentBudget.PublicSandbox, DateTime.UtcNow);
        run.Start(DateTime.UtcNow);

        CampaignRunner.RecordRetryableTargetTimeout(run, DateTime.UtcNow);

        Assert.Equal(RunStatus.Running, run.Status);
        Assert.Null(run.CompletedAtUtc);
        Assert.Equal("target-timeout-retry", Assert.Single(run.Events).Kind);
    }

    [Fact]
    public void Replay_candidate_preserves_the_exact_failed_seeded_jitter_schedule()
    {
        var settings = new CampaignSettings(3, "seeded-jitter", 25);
        var failed = new DeterministicAttemptResult(
            InvariantOutcome.Fail,
            ["trace:1", "trace:2", "trace:3"],
            3,
            [new DeterministicReplayStep(1, 3, "reserve"), new DeterministicReplayStep(2, 17, "reserve"), new DeterministicReplayStep(3, 8, "confirm")]);

        var candidate = CampaignRunner.CreateReplayCandidate(settings, failed, 1729);

        Assert.Equal("seeded-jitter", candidate.Strategy);
        Assert.Equal(1729, candidate.Seed);
        Assert.Equal([(1, 3), (2, 17), (3, 8)], candidate.Steps.Select(item => (item.ActorId, item.OffsetMilliseconds)));
        Assert.Equal(["reserve", "reserve", "confirm"], candidate.Steps.Select(item => item.OperationId));
    }

    [Fact]
    public void Manual_replay_snapshot_preserves_the_planned_custom_invariant()
    {
        var invariant = new PlannedInvariant("numeric-boundary", "reservation-count", 7);
        var target = new RaceHunter.Application.Hunts.ManualTargetSnapshot(
            Guid.NewGuid(), new Uri("https://api.example.test"), "api.example.test",
            "projects/demo/secrets/token/versions/latest",
            [new RaceHunter.Application.Hunts.ManualTargetOperation("reserve", "POST", "/reservations", "{}",
                new Dictionary<string, string> { ["reservation-count"] = "$.count" })], [], DateTime.UtcNow);

        var json = ManualReplaySnapshot.Serialize(Guid.NewGuid(), target, invariant);
        var compiled = Assert.IsType<NumericBoundaryInvariant>(ReferenceReplayExecution.CompileManualInvariant(json));

        Assert.Equal("reservation-count", compiled.Metric);
        Assert.Equal(7, compiled.Maximum);
    }

    [Fact]
    public void Stable_campaign_execution_key_uses_the_durable_iteration_not_attempt_identity()
    {
        var runId = Guid.NewGuid();
        Assert.Equal(CampaignRunner.CreateAttemptExecutionKey(runId, 2), CampaignRunner.CreateAttemptExecutionKey(runId, 2));
        Assert.NotEqual(CampaignRunner.CreateAttemptExecutionKey(runId, 1), CampaignRunner.CreateAttemptExecutionKey(runId, 2));
    }

    [Fact]
    public void Manual_replay_rejects_a_changed_sensitive_data_policy()
    {
        var target = new RaceHunter.Application.Hunts.ManualTargetSnapshot(
            Guid.NewGuid(), new Uri("https://api.example.test"), "api.example.test",
            "projects/demo/secrets/token/versions/latest",
            [new RaceHunter.Application.Hunts.ManualTargetOperation("reserve", "POST", "/reservations", "{}",
                new Dictionary<string, string> { ["reservation-count"] = "$.count" })], ["$.token"], DateTime.UtcNow);
        var snapshot = ManualReplaySnapshot.Deserialize(ManualReplaySnapshot.Serialize(Guid.NewGuid(), target,
            new PlannedInvariant("numeric-boundary", "reservation-count", 7)));
        var changed = target with { SensitiveJsonPaths = ["$.different-secret"] };

        var failure = Assert.Throws<RaceHunter.Infrastructure.Security.TargetSafetyException>(() => ReferenceReplayExecution.EnsureSnapshotMatches(changed, snapshot));
        Assert.Equal("snapshot_mismatch", failure.Code);
    }

    [Fact]
    public void Manual_plan_keeps_duplicate_actor_operation_assignments_in_exact_order()
    {
        var schedule = new SimultaneousStartStrategy().Create(3, 42);
        var mapped = ReferenceCampaignAttemptExecutor.ApplyManualOperations(schedule, ["reserve", "reserve", "confirm"]);

        Assert.Equal(["reserve", "reserve", "confirm"], mapped.Actors.Select(actor => actor.OperationKey));
    }

    [Fact]
    public async Task Development_planner_respects_custom_operations_metric_and_non_default_maximum()
    {
        var request = new ModelRequest("fake", "plan-v1", "plan-v1",
            "prompt\nobjective=Reservation maximum is 7\noperations=reserve:POST:/reservations,confirm:POST:/confirm\ninvariantTypes=numeric-boundary\nobservationMetrics=reservation-count\nstrategies=checkpoint-interleaving",
            "{}", false);

        var response = await new DevelopmentModelClient().GenerateAsync(request, CancellationToken.None);

        Assert.Contains("\"operationId\":\"reserve\"", response.Json, StringComparison.Ordinal);
        Assert.Contains("\"operationId\":\"confirm\"", response.Json, StringComparison.Ordinal);
        Assert.Contains("\"metric\":\"reservation-count\"", response.Json, StringComparison.Ordinal);
        Assert.Contains("\"maximum\":7", response.Json, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("cardinality", "invariant-family=cardinality; metric=correlation", "\"type\":\"cardinality\"")]
    [InlineData("cross-observation", "invariant-family=cross-observation; left-metric=count; right-metric=capacity; relation=less-than-or-equal", "\"rightMetric\":\"capacity\"")]
    public async Task Development_planner_honors_the_configured_manual_invariant_family(string family, string directive, string expected)
    {
        var request = new ModelRequest("fake", "plan-v1", "plan-v1",
            $"prompt\nobjective=Check external state {directive}\noperations=reserve:POST:/reservations\ninvariantTypes=numeric-boundary,cardinality,cross-observation\nobservationMetrics=count,capacity,correlation\nstrategies=checkpoint-interleaving",
            "{}", false);

        var response = await new DevelopmentModelClient().GenerateAsync(request, CancellationToken.None);

        Assert.Contains($"\"type\":\"{family}\"", response.Json, StringComparison.Ordinal);
        Assert.Contains(expected, response.Json, StringComparison.Ordinal);
    }

    [Fact]
    public void Start_minimization_checkpoint_recovers_exact_failure_without_another_campaign_iteration()
    {
        var failedSettings = new CampaignSettings(3, "seeded-jitter", 25);
        var schedule = new[] { new DeterministicReplayStep(1, 3), new DeterministicReplayStep(2, 17), new DeterministicReplayStep(3, 8) };
        var state = new CampaignRunner.CheckpointState(
            2,
            "checkpoint-interleaving",
            0,
            3,
            2,
            InvariantOutcome.Fail.ToString(),
            ["trace:1", "trace:2", "trace:3"],
            schedule,
            AgentActionKind.StartMinimization.ToString(),
            failedSettings);
        var checkpoint = new WorkCheckpoint("agent-decision-persisted", 1, JsonSerializer.Serialize(state), DateTime.UtcNow);
        var plan = new ScenarioPlan(
            "plan-v1", "plan-v1", "plan-v1", "fake", "model-1",
            [new PlannedActor("buyer-1", "place-order"), new PlannedActor("buyer-2", "place-order")],
            new PlannedInvariant("numeric-boundary", "successful-orders", 1),
            new PlannedStrategy("checkpoint-interleaving", 2, 1729),
            1,
            "{}");

        var recovered = CampaignRunner.RecoverSettings(checkpoint, plan);

        Assert.True(recovered.FinalizeFinding);
        Assert.Equal(failedSettings, recovered.FailedSettings);
        Assert.Equal(schedule, recovered.RecoveredAttempt!.Schedule);
    }

    [Fact]
    public async Task Repeat_checkpoint_preserves_verified_failure_at_the_next_request_budget_boundary()
    {
        var failedSettings = new CampaignSettings(3, "simultaneous-start", 0);
        var schedule = new[] { new DeterministicReplayStep(1, 0), new DeterministicReplayStep(2, 0), new DeterministicReplayStep(3, 0) };
        var failedAttempt = new DeterministicAttemptResult(InvariantOutcome.Fail, ["trace:global-snapshot"], 4, schedule);
        var state = new CampaignRunner.CheckpointState(
            10,
            "simultaneous-start",
            0,
            9,
            2,
            InvariantOutcome.Pass.ToString(),
            ["trace:later-pass"],
            [new DeterministicReplayStep(1, 0)],
            AgentActionKind.Repeat.ToString(),
            new CampaignSettings(10, "simultaneous-start", 0),
            VerifiedFailedSettings: failedSettings,
            VerifiedFailedAttempt: failedAttempt);
        var checkpoint = new WorkCheckpoint("agent-decision-persisted", 1, JsonSerializer.Serialize(state), DateTime.UtcNow);
        var plan = new ScenarioPlan(
            "plan-v1", "plan-v1", "plan-v1", "fake", "model-1",
            [new PlannedActor("buyer-1", "place-order"), new PlannedActor("buyer-2", "place-order")],
            new PlannedInvariant("cross-observation", "successful-orders", null, "successful-orders", "inventory-capacity", "less-than-or-equal"),
            new PlannedStrategy("simultaneous-start", 2, 1729),
            1,
            "{}");
        var recovered = CampaignRunner.RecoverSettings(checkpoint, plan);
        var context = new AdaptiveCampaignContext(
            Guid.NewGuid(),
            recovered.Settings,
            ["simultaneous-start"],
            new ExperimentBudget(10, 10, 10, 5, TimeSpan.FromSeconds(30), 0),
            maxIterations: 5,
            recovered.ResumeAfterIteration,
            recovered.RequestsConsumed,
            recovered.ModelCallsConsumed,
            fixedRequestsPerAttempt: ReferenceCampaignAttemptExecutor.ReferenceSnapshotRequestsPerAttempt,
            verifiedFailedSettings: recovered.FailedSettings,
            verifiedFailedAttempt: recovered.VerifiedFailedAttempt);

        var result = await new AdaptiveStrategyLoop(new NeverStrategist()).RunAsync(
            context,
            (_, _, _) => throw new InvalidOperationException("The next attempt must be rejected before execution."),
            CancellationToken.None);

        Assert.Equal(CampaignOutcome.VerifiedViolation, result.Outcome);
        Assert.Equal(failedSettings, result.FailedSettings);
        Assert.Equal(schedule, result.FailedAttempt!.Schedule);
    }

    [Fact]
    public void Campaign_request_reconciliation_counts_a_pre_call_snapshot_reservation_once()
    {
        var runId = Guid.NewGuid();
        var attemptId = Guid.NewGuid();
        var traces = new[]
        {
            new TraceEvent(1, runId, attemptId, 1, "target-operation", "response-success", "order-1", DateTime.UtcNow),
            new TraceEvent(2, runId, attemptId, 0, "inventory-snapshot", "request-started", "snapshot-1", DateTime.UtcNow),
            new TraceEvent(3, runId, attemptId, 0, "inventory-snapshot", "response-success", "snapshot-1", DateTime.UtcNow)
        };

        Assert.Equal(2, CampaignRunner.CountCampaignRequests(traces));
    }

    [Fact]
    public async Task Reproduction_restart_reuses_completed_boundaries_and_target_idempotency_after_the_call_persistence_gap()
    {
        var runId = Guid.NewGuid();
        var store = new MemoryProbeStore { FailBeforeSaveKey = "reproduction:2" };
        var target = new IdempotentPhysicalProbe();
        var candidate = Candidate(3);
        var first = new DurableFindingReplayProbe(runId, 20, store, target.CountMissingAsync, target.ExecuteAsync);

        await Assert.ThrowsAsync<IOException>(() => new ReproductionVerifier().VerifyReferenceAsync(candidate, first, CancellationToken.None));
        store.FailBeforeSaveKey = null;
        var resumed = new DurableFindingReplayProbe(runId, 20 - 6, store, target.CountMissingAsync, target.ExecuteAsync);
        var result = await new ReproductionVerifier().VerifyReferenceAsync(candidate, resumed, CancellationToken.None);

        Assert.True(result.Verified);
        Assert.Equal(9, target.PhysicalMutations);
        Assert.Equal(3, store.Items.Count);
        Assert.Equal(6, store.RequestsConsumed);
    }

    [Fact]
    public async Task Snapshot_request_is_durably_reserved_before_each_replay_call_across_a_crash_gap()
    {
        var runId = Guid.NewGuid();
        var store = new MemoryProbeStore { FailBeforeSaveKey = "reproduction:1" };
        var physicalSnapshots = 0;
        var candidate = Candidate(2);
        Task<ReplayObservation> Execute(string probeKey, ReplayCandidate replayCandidate, ReplayTargetMode mode, CancellationToken token)
        {
            physicalSnapshots++;
            return Task.FromResult(new ReplayObservation(InvariantOutcome.Fail, ["trace:snapshot"], physicalSnapshots == 1 ? 3 : 1));
        }
        var first = new DurableFindingReplayProbe(runId, 5, store, (_, _, _, _) => Task.FromResult(3), Execute,
            ReferenceCampaignAttemptExecutor.ReferenceSnapshotRequestsPerAttempt);

        await Assert.ThrowsAsync<IOException>(() => first.ExecuteAsync("reproduction:1", candidate, ReplayTargetMode.Vulnerable, CancellationToken.None));
        var resumed = new DurableFindingReplayProbe(runId, 5 - store.RequestsConsumed, store, (_, _, _, _) => Task.FromResult(3), Execute,
            ReferenceCampaignAttemptExecutor.ReferenceSnapshotRequestsPerAttempt);
        var result = await resumed.ExecuteAsync("reproduction:1", candidate, ReplayTargetMode.Vulnerable, CancellationToken.None);

        Assert.Equal(InvariantOutcome.Fail, result.Outcome);
        Assert.Equal(2, physicalSnapshots);
        Assert.Equal(2, store.RequestsConsumed);
    }

    [Fact]
    public async Task Minimization_restart_resumes_after_the_latest_accepted_candidate_with_the_same_final_artifact()
    {
        var runId = Guid.NewGuid();
        var store = new MemoryProbeStore { FailAfterSaveKey = ReplayProbeKey.ForCandidate("minimize:actor:4", Candidate(3)) };
        var target = new IdempotentPhysicalProbe();
        var original = Candidate(4);
        var first = new DurableFindingReplayProbe(runId, 20, store, target.CountMissingAsync, target.ExecuteAsync);

        await Assert.ThrowsAsync<IOException>(() => new FailureMinimizer().MinimizeAsync(original, first, CancellationToken.None));
        store.FailAfterSaveKey = null;
        var resumed = new DurableFindingReplayProbe(runId,
            CampaignRunner.RemainingFindingRequests(20, 0,
                await store.GetRequestsConsumedAsync(runId, CancellationToken.None)),
            store, target.CountMissingAsync, target.ExecuteAsync);
        var result = await new FailureMinimizer().MinimizeAsync(original, resumed, CancellationToken.None);

        Assert.Equal(2, result.Candidate.ActorCount);
        Assert.Equal([1, 2], result.Candidate.Steps.Select(item => item.ActorId));
        Assert.Equal(5, target.PhysicalMutations);
        Assert.Equal(5, store.RequestsConsumed);
    }

    [Fact]
    public void Final_artifact_identity_and_fingerprint_are_stable_across_worker_restart()
    {
        var run = ExperimentRun.Queue(Guid.NewGuid(), ExperimentBudget.PublicSandbox, new DateTime(2026, 8, 18, 12, 0, 0, DateTimeKind.Utc));
        run.Start(new DateTime(2026, 8, 18, 12, 0, 1, DateTimeKind.Utc));
        var plan = new ScenarioPlan(
            "plan-v1", "plan-v1", "plan-v1", "fake", "model-1",
            [new PlannedActor("buyer-1", "place-order"), new PlannedActor("buyer-2", "place-order")],
            new PlannedInvariant("numeric-boundary", "successful-orders", 1),
            new PlannedStrategy("checkpoint-interleaving", 2, 1729),
            1,
            "{}");

        var beforeCrash = CampaignRunner.CreateReplayArtifact(run, plan, Candidate(2));
        var afterRestart = CampaignRunner.CreateReplayArtifact(run, plan, Candidate(2));

        Assert.Equal(beforeCrash.Id, afterRestart.Id);
        Assert.Equal(beforeCrash.FindingId, afterRestart.FindingId);
        Assert.Equal(beforeCrash.Fingerprint, afterRestart.Fingerprint);
    }

    [Fact]
    public async Task Durable_probe_rejects_candidate_before_target_when_unique_work_exceeds_remaining_budget()
    {
        var store = new MemoryProbeStore();
        var target = new IdempotentPhysicalProbe();
        var probe = new DurableFindingReplayProbe(Guid.NewGuid(), 1, store, target.CountMissingAsync, target.ExecuteAsync);

        var observation = await probe.ExecuteAsync("reproduction:1", Candidate(3), ReplayTargetMode.Vulnerable, CancellationToken.None);

        Assert.Equal(InvariantOutcome.Inconclusive, observation.Outcome);
        Assert.Equal(0, target.PhysicalMutations);
        Assert.Empty(store.Items);
    }

    [Fact]
    public async Task Cached_target_result_without_prior_trace_consumes_logical_budget_before_a_later_probe()
    {
        var store = new MemoryProbeStore();
        var executions = 0;
        var probe = new DurableFindingReplayProbe(
            Guid.NewGuid(),
            1,
            store,
            (_, _, _, _) => Task.FromResult(1),
            (_, _, _, _) =>
            {
                executions++;
                return Task.FromResult(new ReplayObservation(InvariantOutcome.Fail, ["trace:recovered"], 1));
            });

        var recovered = await probe.ExecuteAsync("reproduction:1", Candidate(2), ReplayTargetMode.Vulnerable, CancellationToken.None);
        var later = await probe.ExecuteAsync("reproduction:2", Candidate(2), ReplayTargetMode.Vulnerable, CancellationToken.None);

        Assert.Equal(InvariantOutcome.Fail, recovered.Outcome);
        Assert.Equal(InvariantOutcome.Inconclusive, later.Outcome);
        Assert.Equal(1, executions);
    }

    [Fact]
    public async Task Reproduction_and_minimization_are_persisted_before_their_target_work()
    {
        var run = ExperimentRun.Queue(Guid.NewGuid(), ExperimentBudget.PublicSandbox, DateTime.UnixEpoch);
        run.Start(DateTime.UnixEpoch.AddSeconds(1));
        var store = new RecordingRunStore(run);

        await PersistedRunLifecycle.RunReproductionAsync(run, store, _ =>
        {
            Assert.Equal(RunStatus.Reproducing, store.LastPersistedStatus);
            Assert.Equal("reproduction-started", store.LastPersistedEvents.Last().Kind);
            return Task.FromResult(1);
        }, CancellationToken.None);
        await PersistedRunLifecycle.RunMinimizationAsync(run, store, _ =>
        {
            Assert.Equal(RunStatus.Minimizing, store.LastPersistedStatus);
            Assert.Equal("minimization-started", store.LastPersistedEvents.Last().Kind);
            return Task.FromResult(2);
        }, CancellationToken.None);

        Assert.Equal([RunStatus.Reproducing, RunStatus.Minimizing], store.SavedStatuses);
    }

    [Fact]
    public async Task Recovery_in_minimization_does_not_duplicate_or_regress_lifecycle_history()
    {
        var run = ExperimentRun.Queue(Guid.NewGuid(), ExperimentBudget.PublicSandbox, DateTime.UnixEpoch);
        run.Start(DateTime.UnixEpoch.AddSeconds(1));
        run.BeginReproduction(DateTime.UnixEpoch.AddSeconds(2));
        run.BeginMinimization(DateTime.UnixEpoch.AddSeconds(3));
        var store = new RecordingRunStore(run);

        await PersistedRunLifecycle.RunReproductionAsync(run, store, _ => Task.FromResult(1), CancellationToken.None);
        await PersistedRunLifecycle.RunMinimizationAsync(run, store, _ => Task.FromResult(2), CancellationToken.None);

        Assert.Empty(store.SavedStatuses);
        Assert.Equal(RunStatus.Minimizing, run.Status);
        Assert.Equal(["reproduction-started", "minimization-started"], run.Events.Select(item => item.Kind));
    }

    private static ReplayCandidate Candidate(int actors) => new(
        "checkpoint-interleaving",
        1729,
        Enumerable.Range(1, actors).Select(actor => new ReplayStep(actor, "place-order", "place-order", 0)));

    private sealed class MemoryProbeStore : IFindingProbeCheckpointStore
    {
        public Dictionary<string, FindingProbeCheckpoint> Items { get; } = [];
        public string? FailBeforeSaveKey { get; set; }
        public string? FailAfterSaveKey { get; set; }
        public int RequestsConsumed => Items.Values.Sum(item => item.RequestsConsumed);

        public Task<FindingProbeCheckpoint?> GetAsync(Guid runId, string probeKey, CancellationToken cancellationToken) =>
            Task.FromResult(Items.GetValueOrDefault(probeKey));

        public Task<int> GetRequestsConsumedAsync(Guid runId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.Values.Where(item => item.RunId == runId).Sum(item => item.RequestsConsumed));

        public Task SaveAsync(FindingProbeCheckpoint checkpoint, CancellationToken cancellationToken)
        {
            if (checkpoint.ProbeKey == FailBeforeSaveKey)
            {
                FailBeforeSaveKey = null;
                throw new IOException("crash after target call before persistence");
            }
            Items.TryAdd(checkpoint.ProbeKey, checkpoint);
            if (checkpoint.ProbeKey == FailAfterSaveKey)
            {
                FailAfterSaveKey = null;
                throw new IOException("crash after boundary commit");
            }
            return Task.CompletedTask;
        }
    }

    private sealed class IdempotentPhysicalProbe
    {
        private readonly Dictionary<string, ReplayObservation> completed = [];
        public int PhysicalMutations { get; private set; }

        public Task<int> CountMissingAsync(string key, ReplayCandidate candidate, ReplayTargetMode mode, CancellationToken cancellationToken) =>
            Task.FromResult(completed.ContainsKey(key) ? 0 : candidate.Steps.Count);

        public Task<ReplayObservation> ExecuteAsync(string key, ReplayCandidate candidate, ReplayTargetMode mode, CancellationToken cancellationToken)
        {
            var reused = true;
            if (!completed.TryGetValue(key, out var observation))
            {
                reused = false;
                PhysicalMutations += candidate.Steps.Count;
                observation = new ReplayObservation(InvariantOutcome.Fail, [$"trace:{key}"], candidate.Steps.Count);
                completed.Add(key, observation);
            }
            return Task.FromResult(reused ? observation with { RequestsConsumed = 0 } : observation);
        }
    }

    private sealed class RecordingRunStore(ExperimentRun run) : IRunStore
    {
        public List<RunStatus> SavedStatuses { get; } = [];
        public RunStatus? LastPersistedStatus { get; private set; }
        public IReadOnlyList<RunEvent> LastPersistedEvents { get; private set; } = [];

        public Task AddAsync(ExperimentRun value, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task SaveAsync(ExperimentRun value, CancellationToken cancellationToken)
        {
            LastPersistedStatus = value.Status;
            LastPersistedEvents = value.Events.ToArray();
            SavedStatuses.Add(value.Status);
            return Task.CompletedTask;
        }
        public Task<ExperimentRun?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<ExperimentRun?>(run);
        public Task<IReadOnlyList<RunEvent>> GetEventsAsync(Guid id, long after, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<RunEvent>>(run.Events.Where(item => item.Cursor > after).ToArray());
        public Task<bool> RequestCancellationAsync(Guid id, DateTime requestedAtUtc, CancellationToken cancellationToken) => Task.FromResult(false);
    }

    private sealed class NeverStrategist : IExperimentStrategist
    {
        public Task<StrategyDecision> SelectNextAsync(StrategySelectionContext context, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The strategist must not run after the request budget is exhausted.");
    }
}

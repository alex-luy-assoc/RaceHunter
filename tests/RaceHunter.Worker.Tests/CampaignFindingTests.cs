using System.Text.Json;
using RaceHunter.Application.Abstractions;
using RaceHunter.Application.Agents;
using RaceHunter.Application.Messaging;
using RaceHunter.Concurrency.Minimization;
using RaceHunter.Concurrency.Replay;
using RaceHunter.Domain.Budgets;
using RaceHunter.Domain.Invariants;
using RaceHunter.Domain.Replays;
using RaceHunter.Domain.Runs;
using RaceHunter.Worker.Execution;
using Xunit;

namespace RaceHunter.Worker.Tests;

public sealed class CampaignFindingTests
{
    [Fact]
    public void Replay_candidate_preserves_the_exact_failed_seeded_jitter_schedule()
    {
        var settings = new CampaignSettings(3, "seeded-jitter", 25);
        var failed = new DeterministicAttemptResult(
            InvariantOutcome.Fail,
            ["trace:1", "trace:2", "trace:3"],
            3,
            [new DeterministicReplayStep(1, 3), new DeterministicReplayStep(2, 17), new DeterministicReplayStep(3, 8)]);

        var candidate = CampaignRunner.CreateReplayCandidate(settings, failed, 1729);

        Assert.Equal("seeded-jitter", candidate.Strategy);
        Assert.Equal(1729, candidate.Seed);
        Assert.Equal([(1, 3), (2, 17), (3, 8)], candidate.Steps.Select(item => (item.ActorId, item.OffsetMilliseconds)));
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
    public async Task Minimization_restart_resumes_after_the_latest_accepted_candidate_with_the_same_final_artifact()
    {
        var runId = Guid.NewGuid();
        var store = new MemoryProbeStore { FailAfterSaveKey = ReplayProbeKey.ForCandidate("minimize:actor:4", Candidate(3)) };
        var target = new IdempotentPhysicalProbe();
        var original = Candidate(4);
        var first = new DurableFindingReplayProbe(runId, 20, store, target.CountMissingAsync, target.ExecuteAsync);

        await Assert.ThrowsAsync<IOException>(() => new FailureMinimizer().MinimizeAsync(original, first, CancellationToken.None));
        store.FailAfterSaveKey = null;
        var resumed = new DurableFindingReplayProbe(runId, 20 - store.RequestsConsumed, store, target.CountMissingAsync, target.ExecuteAsync);
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
}

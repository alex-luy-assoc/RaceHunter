using RaceHunter.Application.Abstractions;
using RaceHunter.Application.Findings;
using RaceHunter.Application.Hunts;
using RaceHunter.Application.Replays;
using RaceHunter.Domain.Findings;
using RaceHunter.Domain.Invariants;
using RaceHunter.Domain.Replays;
using RaceHunter.Domain.Tracing;
using Xunit;

namespace RaceHunter.Application.Tests;

public sealed class FindingWorkflowTests
{
    [Fact]
    public void Finding_truth_requires_a_deterministic_failed_invariant()
    {
        var artifact = Artifact();

        Assert.Throws<InvalidOperationException>(() => Finding.CreateReference(
            Guid.NewGuid(), Guid.NewGuid(), "invariant-v1",
            new InvariantResult(InvariantOutcome.Pass, ["trace:1"], "within boundary"),
            Reproductions(), artifact, Utc(5), "Model interpretation"));
    }

    [Fact]
    public async Task Finding_projection_exposes_exact_judge_message_evidence_timeline_and_agent_activity()
    {
        var state = State();

        var projection = await new GetFinding(state, state, state, state)
            .ExecuteAsync(state.Finding.Id, CancellationToken.None);

        Assert.NotNull(projection);
        Assert.Equal("Race condition verified — reproduced 3/3 and minimized to 2 actors.", projection.SuccessMessage);
        Assert.Equal(InvariantOutcome.Fail, projection.InvariantOutcome);
        Assert.Equal("Gemini interpretation", projection.AgentInterpretation);
        Assert.Equal(2, projection.Timeline.Count);
        Assert.Equal([1L, 2L], projection.Timeline.SelectMany(lane => lane.Events).Select(item => item.Sequence).Order());
        Assert.Single(projection.Timeline.SelectMany(lane => lane.Events).Select(item => item.AttemptId).Distinct());
        Assert.Single(projection.AgentActivity);
        Assert.Equal(state.Artifact.Fingerprint, projection.ReplayArtifact.Fingerprint);
    }

    [Fact]
    public async Task Verify_fix_persists_a_separate_pass_attempt_without_mutating_finding_or_artifact()
    {
        var state = State();
        var findingCreatedAt = state.Finding.CreatedAtUtc;
        var fingerprint = state.Artifact.Fingerprint;

        var attempt = await new VerifyFix(state, state, new PassingReplayExecution())
            .ExecuteAsync(state.Finding.Id, "verify-once", CancellationToken.None);

        Assert.Equal(InvariantOutcome.Pass, attempt.Outcome);
        Assert.Equal(ReplayTargetMode.Fixed, attempt.TargetMode);
        Assert.Equal(fingerprint, state.Artifact.Fingerprint);
        Assert.Equal(findingCreatedAt, state.Finding.CreatedAtUtc);
        Assert.Equal(InvariantOutcome.Fail, state.Finding.OriginalInvariant.Outcome);
    }

    [Fact]
    public async Task Verify_fix_is_idempotent_for_the_same_request_key()
    {
        var state = State();
        var command = new VerifyFix(state, state, new PassingReplayExecution());

        var first = await command.ExecuteAsync(state.Finding.Id, "verify-once", CancellationToken.None);
        var duplicate = await command.ExecuteAsync(state.Finding.Id, "verify-once", CancellationToken.None);

        Assert.Equal(first.Id, duplicate.Id);
        Assert.Single(state.Attempts);
    }

    [Fact]
    public async Task Verify_fix_has_one_server_owned_execution_allowance_even_with_distinct_request_keys()
    {
        var state = State();
        var execution = new PassingReplayExecution();
        var command = new VerifyFix(state, state, execution);

        var first = await command.ExecuteAsync(state.Finding.Id, "verify-one", CancellationToken.None);
        var second = await command.ExecuteAsync(state.Finding.Id, "verify-two", CancellationToken.None);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, execution.Calls);
        Assert.Single(state.Attempts);
    }

    [Fact]
    public async Task Verify_fix_rejects_an_unknown_finding_without_running_a_replay()
    {
        var state = State();
        var execution = new PassingReplayExecution();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => new VerifyFix(state, state, execution)
            .ExecuteAsync(Guid.NewGuid(), "verify-once", CancellationToken.None));

        Assert.Equal(0, execution.Calls);
    }

    private static MemoryFindingState State()
    {
        var artifact = Artifact();
        var finding = Finding.CreateReference(
            artifact.FindingId,
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            "invariant-v1",
            new InvariantResult(InvariantOutcome.Fail, ["trace:1", "trace:2"], "2 successful orders exceeded capacity 1"),
            Reproductions(), artifact, Utc(5), "Gemini interpretation");
        return new MemoryFindingState(finding, artifact);
    }

    private static ReplayArtifact Artifact() => ReplayArtifact.Create(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        "scenario-v1", "invariant-v1", "inventory:one-unit", "checkpoint-interleaving", 1729,
        [new ReplayStep(1, "place-order", "place-order", 0), new ReplayStep(2, "place-order", "place-order", 0)],
        "{\"quantity\":1}", Utc(4));

    private static IReadOnlyList<ReproductionAttempt> Reproductions() =>
        Enumerable.Range(1, 3).Select(index => new ReproductionAttempt(index, InvariantOutcome.Fail, [$"trace:r{index}"])).ToArray();

    private static DateTime Utc(int seconds) => new(2026, 8, 18, 12, 0, seconds, DateTimeKind.Utc);

    private sealed class PassingReplayExecution : IReplayExecution
    {
        public int Calls { get; private set; }
        public Task<ReplayAttempt> ExecuteAsync(ReplayArtifact artifact, ReplayTargetMode targetMode, string idempotencyKey, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(ReplayAttempt.Complete(Guid.NewGuid(), artifact.Id, targetMode, InvariantOutcome.Pass,
                ["trace:fixed"], artifact.Fingerprint, idempotencyKey, Utc(8)));
        }
    }

    private sealed class MemoryFindingState(Finding finding, ReplayArtifact artifact)
        : IFindingStore, IReplayStore, IAgentIterationReader, ITraceStore
    {
        public Finding Finding { get; } = finding;
        public ReplayArtifact Artifact { get; } = artifact;
        public List<ReplayAttempt> Attempts { get; } = [];

        public Task AddAsync(Finding finding, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<Finding?> GetAsync(Guid findingId, CancellationToken cancellationToken) =>
            Task.FromResult<Finding?>(findingId == Finding.Id ? Finding : null);
        public Task AddArtifactAsync(ReplayArtifact artifact, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<ReplayArtifact?> GetArtifactAsync(Guid artifactId, CancellationToken cancellationToken) =>
            Task.FromResult<ReplayArtifact?>(artifactId == Artifact.Id ? Artifact : null);
        public Task AddAttemptAsync(ReplayAttempt attempt, CancellationToken cancellationToken)
        {
            Attempts.Add(attempt);
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<ReplayAttempt>> GetAttemptsAsync(Guid artifactId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ReplayAttempt>>(Attempts.Where(item => item.ArtifactId == artifactId).ToArray());
        public Task<IReadOnlyList<AgentIterationRecord>> GetIterationsByRunAsync(Guid runId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<AgentIterationRecord>>([new(Guid.NewGuid(), runId, 1, "trace refs only", "StartMinimization", "Reduce actors", "gemini-3.5-flash", "strategy-v1", "model-1", Utc(3))]);
        public Task AppendAsync(TraceEvent traceEvent, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<IReadOnlyList<TraceEvent>> GetAsync(Guid runId, long after, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<TraceEvent>>(CreateTraces(runId));

        private static IReadOnlyList<TraceEvent> CreateTraces(Guid runId)
        {
            var failedAttemptId = Guid.NewGuid();
            return [
                new(1, runId, failedAttemptId, 1, "place-order", "request", "request-a", Utc(1)),
                new(2, runId, failedAttemptId, 2, "place-order", "request", "request-b", Utc(1)),
                new(3, runId, Guid.NewGuid(), 3, "unrelated-attempt", "request", "request-c", Utc(2))];
        }
    }
}

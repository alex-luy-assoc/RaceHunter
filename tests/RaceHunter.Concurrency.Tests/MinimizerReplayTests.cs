using RaceHunter.Concurrency.Minimization;
using RaceHunter.Concurrency.Replay;
using RaceHunter.Domain.Invariants;
using RaceHunter.Domain.Replays;
using Xunit;

namespace RaceHunter.Concurrency.Tests;

public sealed class MinimizerReplayTests
{
    [Fact]
    public async Task Reference_reproduction_requires_three_failures_out_of_three()
    {
        var probe = new RecordingProbe([InvariantOutcome.Fail, InvariantOutcome.Fail, InvariantOutcome.Fail]);

        var result = await new ReproductionVerifier().VerifyReferenceAsync(Candidate(4, 1), probe, CancellationToken.None);

        Assert.True(result.Verified);
        Assert.Equal(3, result.Failures);
        Assert.Equal(3, result.Attempts.Count);
        Assert.All(result.Attempts, attempt => Assert.Equal(InvariantOutcome.Fail, attempt.Outcome));
    }

    [Fact]
    public async Task Reference_reproduction_does_not_overclaim_two_failures_out_of_three()
    {
        var result = await new ReproductionVerifier().VerifyReferenceAsync(
            Candidate(4, 1),
            new RecordingProbe([InvariantOutcome.Fail, InvariantOutcome.Pass, InvariantOutcome.Fail]),
            CancellationToken.None);

        Assert.False(result.Verified);
        Assert.Equal(2, result.Failures);
        Assert.Equal(3, result.Attempts.Count);
    }

    [Fact]
    public async Task Reference_reproduction_is_bounded_to_exactly_three_attempts()
    {
        var probe = new RecordingProbe(Enumerable.Repeat(InvariantOutcome.Fail, 10));

        await new ReproductionVerifier().VerifyReferenceAsync(Candidate(5, 1), probe, CancellationToken.None);

        Assert.Equal(3, probe.Candidates.Count);
    }

    [Fact]
    public async Task Minimizer_removes_actors_in_stable_descending_order_until_two_remain()
    {
        var probe = new RecordingProbe(Enumerable.Repeat(InvariantOutcome.Fail, 10));

        var result = await new FailureMinimizer().MinimizeAsync(Candidate(5, 1), probe, CancellationToken.None);

        Assert.Equal(2, result.Candidate.ActorCount);
        Assert.Equal([4, 3, 2], probe.Candidates.Take(3).Select(candidate => candidate.ActorCount));
        Assert.All(result.AcceptedReductions, reduction => Assert.Equal(InvariantOutcome.Fail, reduction.Outcome));
    }

    [Fact]
    public async Task Minimizer_rejects_one_actor_reduction_but_still_tries_the_remaining_stable_candidates()
    {
        var probe = new RecordingProbe([InvariantOutcome.Pass, InvariantOutcome.Fail]);

        var result = await new FailureMinimizer().MinimizeAsync(Candidate(3, 1), probe, CancellationToken.None);

        Assert.Equal(2, result.Candidate.ActorCount);
        Assert.Equal([2, 2], probe.Candidates.Select(candidate => candidate.ActorCount));
        Assert.Contains(result.AcceptedReductions, item => item.Kind == ReductionKind.Actor && item.Removed == "actor:2");
    }

    [Fact]
    public async Task Minimizer_removes_redundant_steps_only_after_actor_reduction()
    {
        var probe = new RecordingProbe(Enumerable.Repeat(InvariantOutcome.Fail, 20));

        var result = await new FailureMinimizer().MinimizeAsync(Candidate(3, 2), probe, CancellationToken.None);

        Assert.Equal(2, result.Candidate.ActorCount);
        Assert.Equal(2, result.Candidate.Steps.Count);
        Assert.Equal(ReductionKind.Actor, result.AcceptedReductions[0].Kind);
        Assert.All(result.Candidate.Steps.GroupBy(step => step.ActorId), lane => Assert.Single(lane));
    }

    [Fact]
    public async Task Minimizer_never_tests_a_candidate_with_fewer_than_two_actors()
    {
        var probe = new RecordingProbe(Enumerable.Repeat(InvariantOutcome.Fail, 20));

        await new FailureMinimizer().MinimizeAsync(Candidate(2, 2), probe, CancellationToken.None);

        Assert.All(probe.Candidates, candidate => Assert.True(candidate.ActorCount >= 2));
    }

    [Fact]
    public async Task Minimizer_removes_only_the_selected_step_when_value_equal_steps_exist()
    {
        var duplicate = new ReplayStep(1, "read", "place-order", 0);
        var original = new ReplayCandidate("checkpoint-interleaving", 1729,
            [duplicate, duplicate with { }, new ReplayStep(2, "place-order", "place-order", 0)]);

        var result = await new FailureMinimizer().MinimizeAsync(
            original,
            new RecordingProbe(Enumerable.Repeat(InvariantOutcome.Fail, 10)),
            CancellationToken.None);

        Assert.Equal(2, result.Candidate.ActorCount);
        Assert.Single(result.Candidate.Steps, item => item.ActorId == 1);
    }

    [Fact]
    public async Task Replay_executor_sends_the_exact_immutable_artifact_to_the_target()
    {
        var artifact = Artifact();
        var fingerprint = artifact.Fingerprint;
        var probe = new RecordingProbe([InvariantOutcome.Pass]);

        var attempt = await new ReplayExecutor(() => Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"))
            .ExecuteAsync(artifact, ReplayTargetMode.Fixed, probe, CancellationToken.None);

        Assert.Equal(InvariantOutcome.Pass, attempt.Outcome);
        Assert.Equal(fingerprint, attempt.ArtifactFingerprint);
        Assert.Equal(fingerprint, artifact.Fingerprint);
        Assert.Equal(artifact.Steps, probe.Candidates.Single().Steps);
        Assert.Equal(ReplayTargetMode.Fixed, probe.Modes.Single());
    }

    [Fact]
    public void Replay_artifact_rehydrate_rejects_content_that_does_not_match_its_fingerprint()
    {
        var artifact = Artifact();

        Assert.Throws<InvalidDataException>(() => ReplayArtifact.Rehydrate(
            artifact.Id,
            artifact.FindingId,
            artifact.ScenarioVersionId,
            artifact.InvariantVersionId,
            artifact.TargetSnapshot,
            artifact.Strategy,
            artifact.Seed + 1,
            artifact.Steps,
            artifact.RequestTemplateJson,
            artifact.CreatedAtUtc,
            artifact.Fingerprint));
    }

    [Fact]
    public void Replay_artifact_does_not_expose_a_mutable_step_collection()
    {
        var artifact = Artifact();

        Assert.False(artifact.Steps is ReplayStep[]);
        Assert.Throws<NotSupportedException>(() =>
            ((IList<ReplayStep>)artifact.Steps)[0] = new ReplayStep(1, "tampered", "place-order", 0));
        artifact.VerifyIntegrity();
    }

    [Fact]
    public void Replay_fingerprint_is_unambiguous_when_step_values_contain_delimiters()
    {
        var baseline = Artifact([
            new ReplayStep(1, "a", "op", 0),
            new ReplayStep(2, "b", "op", 0)
        ]);
        var delimiterCollision = Artifact([
            new ReplayStep(1, "a:op:0|2:b", "op", 0)
        ]);

        Assert.NotEqual(baseline.Fingerprint, delimiterCollision.Fingerprint);
    }

    [Fact]
    public void Timeline_projection_orders_causal_events_and_groups_actor_lanes()
    {
        var projector = new CausalTimelineProjector();
        var events = new[]
        {
            new CausalTrace(3, 2, "commit", "response", "request-b", Utc(3)),
            new CausalTrace(1, 1, "read", "request", "request-a", Utc(1)),
            new CausalTrace(2, 2, "read", "request", "request-b", Utc(1))
        };

        var timeline = projector.Project(events);

        Assert.Equal([1, 2], timeline.Select(lane => lane.ActorId));
        Assert.Equal([1L], timeline[0].Events.Select(item => item.Sequence));
        Assert.Equal([2L, 3L], timeline[1].Events.Select(item => item.Sequence));
    }

    private static ReplayCandidate Candidate(int actors, int stepsPerActor) => new(
        "checkpoint-interleaving",
        1729,
        Enumerable.Range(1, actors).SelectMany(actor => Enumerable.Range(1, stepsPerActor)
            .Select(step => new ReplayStep(actor, $"step-{step}", "place-order", step - 1))).ToArray());

    private static ReplayArtifact Artifact(IReadOnlyList<ReplayStep>? steps = null) => ReplayArtifact.Create(
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        "scenario-v1",
        "invariant-v1",
        "inventory:one-unit",
        "checkpoint-interleaving",
        1729,
        steps ?? Candidate(2, 1).Steps,
        "{\"quantity\":1}",
        Utc(0));

    private static DateTime Utc(int seconds) => new(2026, 8, 18, 12, 0, seconds, DateTimeKind.Utc);

    private sealed class RecordingProbe(IEnumerable<InvariantOutcome> outcomes) : IReplayProbe
    {
        private readonly Queue<InvariantOutcome> outcomes = new(outcomes);
        public List<ReplayCandidate> Candidates { get; } = [];
        public List<ReplayTargetMode> Modes { get; } = [];

        public Task<ReplayObservation> ExecuteAsync(ReplayCandidate candidate, ReplayTargetMode mode, CancellationToken cancellationToken)
        {
            Candidates.Add(candidate);
            Modes.Add(mode);
            var outcome = outcomes.Count == 0 ? InvariantOutcome.Pass : outcomes.Dequeue();
            return Task.FromResult(new ReplayObservation(outcome, [$"trace:{Candidates.Count}"]));
        }
    }
}

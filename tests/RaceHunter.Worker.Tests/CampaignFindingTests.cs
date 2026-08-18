using System.Text.Json;
using RaceHunter.Application.Abstractions;
using RaceHunter.Application.Agents;
using RaceHunter.Application.Messaging;
using RaceHunter.Domain.Invariants;
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
}

namespace RaceHunter.Concurrency.Scheduling;

public sealed class CheckpointStrategy : IScheduleStrategy
{
    public SchedulePlan Create(int actorCount, int seed) => new(
        ScheduleKind.CheckpointInterleaving,
        seed,
        SimultaneousStartStrategy.ValidateActorCount(actorCount)
            .Select(actor => new ScheduledActor(actor, TimeSpan.Zero, actor))
            .ToArray());
}

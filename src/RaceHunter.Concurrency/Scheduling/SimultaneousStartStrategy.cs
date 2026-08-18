namespace RaceHunter.Concurrency.Scheduling;

public sealed class SimultaneousStartStrategy : IScheduleStrategy
{
    public SchedulePlan Create(int actorCount, int seed) => new(
        ScheduleKind.SimultaneousStart,
        seed,
        ValidateActorCount(actorCount).Select(actor => new ScheduledActor(actor, TimeSpan.Zero)).ToArray());

    internal static IEnumerable<int> ValidateActorCount(int actorCount)
    {
        if (actorCount is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(actorCount));
        return Enumerable.Range(1, actorCount);
    }
}

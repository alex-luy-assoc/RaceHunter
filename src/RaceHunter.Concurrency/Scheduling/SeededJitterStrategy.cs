namespace RaceHunter.Concurrency.Scheduling;

public sealed class SeededJitterStrategy(TimeSpan maximumOffset) : IScheduleStrategy
{
    public SchedulePlan Create(int actorCount, int seed)
    {
        if (maximumOffset < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(maximumOffset));
        var random = new Random(seed);
        var maximumMilliseconds = checked((int)Math.Floor(maximumOffset.TotalMilliseconds));
        var actors = SimultaneousStartStrategy.ValidateActorCount(actorCount)
            .Select(actor => new ScheduledActor(actor, TimeSpan.FromMilliseconds(random.Next(0, maximumMilliseconds + 1))))
            .ToArray();
        return new SchedulePlan(ScheduleKind.SeededJitter, seed, actors);
    }
}

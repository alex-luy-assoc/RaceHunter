namespace RaceHunter.Concurrency.Scheduling;

public enum ScheduleKind
{
    SimultaneousStart,
    SeededJitter,
    CheckpointInterleaving
}

public sealed record ScheduledActor(int ActorId, TimeSpan Offset, int? CheckpointOrder = null);

public sealed record SchedulePlan(ScheduleKind Kind, int Seed, IReadOnlyList<ScheduledActor> Actors);

public interface IScheduleStrategy
{
    SchedulePlan Create(int actorCount, int seed);
}

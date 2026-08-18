using System.Collections.Concurrent;
using RaceHunter.Domain.Tracing;

namespace RaceHunter.Concurrency.Tracing;

public sealed class TraceCollector
{
    private readonly ConcurrentQueue<TraceEvent> entries = new();
    private long nextSequence;

    public TraceEvent Append(
        Guid runId,
        Guid attemptId,
        int actorId,
        string stepId,
        string kind,
        string requestId,
        DateTime occurredAtUtc)
    {
        var entry = new TraceEvent(
            Interlocked.Increment(ref nextSequence),
            runId,
            attemptId,
            actorId,
            stepId,
            kind,
            requestId,
            occurredAtUtc.Kind == DateTimeKind.Utc ? occurredAtUtc : DateTime.SpecifyKind(occurredAtUtc, DateTimeKind.Utc));
        entries.Enqueue(entry);
        return entry;
    }

    public IReadOnlyList<TraceEvent> Snapshot() => entries.OrderBy(entry => entry.Sequence).ToArray();
}

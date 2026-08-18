namespace RaceHunter.ReferenceTarget.Inventory;

internal sealed class ControlledCheckpoint
{
    private readonly object sync = new();
    private readonly Dictionary<string, CheckpointGate> gates = new(StringComparer.Ordinal);

    public async Task ReachAsync(string checkpoint, CancellationToken cancellationToken)
    {
        if (!checkpoint.StartsWith("oversell", StringComparison.Ordinal) &&
            !checkpoint.StartsWith("racehunter:", StringComparison.Ordinal) &&
            !checkpoint.StartsWith("observe:", StringComparison.Ordinal)) return;

        CheckpointGate gate;
        lock (sync)
        {
            if (!gates.TryGetValue(checkpoint, out gate!))
            {
                gate = new CheckpointGate();
                gates.Add(checkpoint, gate);
            }

            gate.Waiting++;
            if (gate.Waiting >= 2)
            {
                gates.Remove(checkpoint);
                gate.Release.TrySetResult();
            }
        }

        try
        {
            await gate.Release.Task.WaitAsync(TimeSpan.FromMilliseconds(250), cancellationToken);
        }
        catch (TimeoutException)
        {
            // A checkpoint is an exploration aid, not a requirement that valid low-concurrency schedules deadlock.
        }
        finally
        {
            lock (sync)
            {
                if (gates.TryGetValue(checkpoint, out var current) && ReferenceEquals(current, gate))
                {
                    gate.Waiting--;
                    if (gate.Waiting == 0) gates.Remove(checkpoint);
                }
            }
        }
    }

    private sealed class CheckpointGate
    {
        internal int Waiting { get; set; }
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}

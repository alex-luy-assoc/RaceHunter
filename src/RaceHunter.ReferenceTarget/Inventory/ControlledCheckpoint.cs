namespace RaceHunter.ReferenceTarget.Inventory;

internal sealed class ControlledCheckpoint
{
    private readonly object sync = new();
    private TaskCompletionSource<bool> release = NewRelease();
    private int waiting;

    public Task ReachAsync(string checkpoint, CancellationToken cancellationToken)
    {
        if (!string.Equals(checkpoint, "oversell", StringComparison.Ordinal)) return Task.CompletedTask;

        Task waitTask;
        lock (sync)
        {
            waiting++;
            if (waiting >= 2)
            {
                release.TrySetResult(true);
                waiting = 0;
            }

            waitTask = release.Task;
            if (waitTask.IsCompleted) release = NewRelease();
        }

        return waitTask.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
    }

    private static TaskCompletionSource<bool> NewRelease() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

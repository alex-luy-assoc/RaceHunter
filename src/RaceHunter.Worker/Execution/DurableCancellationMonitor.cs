using RaceHunter.Application.Abstractions;

namespace RaceHunter.Worker.Execution;

internal sealed class DurableCancellationMonitor(
    IRunCancellationProbe probe,
    TimeSpan? pollingInterval = null)
{
    private readonly TimeSpan pollingInterval = pollingInterval ?? TimeSpan.FromMilliseconds(250);

    public async Task WaitAsync(
        Guid runId,
        CancellationTokenSource executionCancellation,
        CancellationToken stopToken)
    {
        try
        {
            while (!stopToken.IsCancellationRequested && !executionCancellation.IsCancellationRequested)
            {
                var requestedAtUtc = await probe.GetRequestedAtUtcAsync(runId, stopToken);
                if (requestedAtUtc.HasValue)
                {
                    executionCancellation.Cancel();
                    return;
                }
                await Task.Delay(pollingInterval, stopToken);
            }
        }
        catch (OperationCanceledException) when (stopToken.IsCancellationRequested)
        {
        }
        catch
        {
            executionCancellation.Cancel();
            throw;
        }
    }
}

internal sealed class DurableCancellationProbeException(Exception innerException) :
    InvalidOperationException("The durable cancellation probe failed.", innerException);

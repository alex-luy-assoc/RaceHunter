namespace RaceHunter.Worker.Execution;

internal static class TargetTransportFailure
{
    internal static bool IsHttpClientTimeout(Exception exception, CancellationToken callerCancellation) =>
        exception is TaskCanceledException { InnerException: TimeoutException } &&
        !callerCancellation.IsCancellationRequested;
}

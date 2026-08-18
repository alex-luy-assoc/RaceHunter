using RaceHunter.Application.Messaging;

namespace RaceHunter.Worker.Execution;

internal sealed class UnavailableWorkPublisher : IWorkPublisher
{
    public Task PublishAsync(WorkDispatch message, CancellationToken cancellationToken) =>
        Task.FromException(new InvalidOperationException("Pub/Sub is not configured."));

    public Task PublishDeadLetterAsync(WorkDispatch message, WorkFailure failure, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

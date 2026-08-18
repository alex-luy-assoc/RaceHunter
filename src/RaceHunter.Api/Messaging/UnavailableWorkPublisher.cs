using RaceHunter.Application.Messaging;

namespace RaceHunter.Api.Messaging;

internal sealed class UnavailableWorkPublisher : IWorkPublisher
{
    public Task PublishAsync(WorkDispatch message, CancellationToken cancellationToken) =>
        Task.FromException(new InvalidOperationException("Pub/Sub is not configured; the durable outbox item remains pending."));

    public Task PublishDeadLetterAsync(WorkDispatch message, WorkFailure failure, CancellationToken cancellationToken) =>
        Task.FromException(new InvalidOperationException("The dead-letter Pub/Sub topic is not configured."));
}

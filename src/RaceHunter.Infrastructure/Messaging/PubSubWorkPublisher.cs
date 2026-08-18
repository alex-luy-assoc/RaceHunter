using Google.Cloud.PubSub.V1;
using Google.Protobuf;
using Microsoft.Extensions.DependencyInjection;
using RaceHunter.Application.Messaging;
using RaceHunter.Contracts;

namespace RaceHunter.Infrastructure.Messaging;

public sealed class PubSubWorkPublisher(PublisherClient publisher, PublisherClient deadLetterPublisher) : IWorkPublisher
{
    public async Task PublishAsync(WorkDispatch message, CancellationToken cancellationToken)
    {
        var contract = ToContract(message);
        await publisher.PublishAsync(new PubsubMessage
        {
            Data = ByteString.CopyFromUtf8(contract.Serialize()),
            Attributes = { ["workId"] = message.WorkId.ToString("N"), ["kind"] = message.Kind.ToString(), ["version"] = message.Version }
        });
        cancellationToken.ThrowIfCancellationRequested();
    }

    public async Task PublishDeadLetterAsync(WorkDispatch message, WorkFailure failure, CancellationToken cancellationToken)
    {
        var contract = ToContract(message);
        await deadLetterPublisher.PublishAsync(new PubsubMessage
        {
            Data = ByteString.CopyFromUtf8(contract.Serialize()),
            Attributes =
            {
                ["workId"] = message.WorkId.ToString("N"),
                ["kind"] = message.Kind.ToString(),
                ["failureCategory"] = failure.Category.ToString(),
                ["diagnostic"] = failure.SanitizedDiagnostic
            }
        });
        cancellationToken.ThrowIfCancellationRequested();
    }

    private static WorkMessage ToContract(WorkDispatch item) => new(
        item.Version,
        item.WorkId,
        item.Kind.ToString(),
        item.SubjectId,
        item.CorrelationId,
        item.CreatedAtUtc);
}

public static class PubSubRegistration
{
    public static IServiceCollection AddPubSubWorkPublisher(
        this IServiceCollection services,
        string projectId,
        string topicId,
        string deadLetterTopicId,
        bool useEmulator)
    {
        var publisher = new PublisherClientBuilder
        {
            TopicName = TopicName.FromProjectTopic(projectId, topicId),
            EmulatorDetection = useEmulator ? Google.Api.Gax.EmulatorDetection.EmulatorOnly : Google.Api.Gax.EmulatorDetection.None
        }.Build();
        var deadLetterPublisher = new PublisherClientBuilder
        {
            TopicName = TopicName.FromProjectTopic(projectId, deadLetterTopicId),
            EmulatorDetection = useEmulator ? Google.Api.Gax.EmulatorDetection.EmulatorOnly : Google.Api.Gax.EmulatorDetection.None
        }.Build();
        services.AddSingleton<IWorkPublisher>(new PubSubWorkPublisher(publisher, deadLetterPublisher));
        return services;
    }
}

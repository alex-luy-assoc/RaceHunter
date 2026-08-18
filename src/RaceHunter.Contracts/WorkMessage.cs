using System.Text.Json;
using System.Text.Json.Serialization;

namespace RaceHunter.Contracts;

public sealed record WorkMessage(
    string Version,
    Guid WorkId,
    string Kind,
    Guid SubjectId,
    string CorrelationId,
    DateTime CreatedAtUtc)
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    public static WorkMessage Create(string kind, Guid subjectId, string correlationId, DateTime createdAtUtc) =>
        new("work-v1", Guid.NewGuid(), kind, subjectId, correlationId, EnsureUtc(createdAtUtc));

    public string Serialize() => JsonSerializer.Serialize(this, Options);

    public static WorkMessage Parse(string json)
    {
        var message = JsonSerializer.Deserialize<WorkMessage>(json, Options)
            ?? throw new JsonException("The work message body is required.");
        if (!string.Equals(message.Version, "work-v1", StringComparison.Ordinal)) throw new JsonException("Unsupported work message version.");
        if (message.WorkId == Guid.Empty || message.SubjectId == Guid.Empty) throw new JsonException("Work and subject IDs are required.");
        if (string.IsNullOrWhiteSpace(message.Kind)) throw new JsonException("A work message kind is required.");
        if (string.IsNullOrWhiteSpace(message.CorrelationId)) throw new JsonException("A correlation ID is required.");
        return message with { CreatedAtUtc = EnsureUtc(message.CreatedAtUtc) };
    }

    private static DateTime EnsureUtc(DateTime value) => value.Kind == DateTimeKind.Utc
        ? value
        : DateTime.SpecifyKind(value, DateTimeKind.Utc);
}

public sealed record PubSubPushMessage(string MessageId, string Data, IReadOnlyDictionary<string, string>? Attributes);
public sealed record PubSubPushEnvelope(PubSubPushMessage Message, string? Subscription, int? DeliveryAttempt);

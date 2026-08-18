using System.Text.Json;
using RaceHunter.Application.Agents;
using RaceHunter.Application.Hunts;

namespace RaceHunter.Worker.Execution;

internal sealed record ManualReplaySnapshot(
    string Kind,
    Guid TargetId,
    string BaseUrl,
    string Host,
    string CredentialReference,
    IReadOnlyCollection<ManualTargetOperation> Operations,
    IReadOnlyCollection<string> SensitiveJsonPaths,
    PlannedInvariant Invariant,
    DateTime CreatedAtUtc)
{
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    public static string Serialize(ManualTargetSnapshot target, PlannedInvariant invariant) =>
        JsonSerializer.Serialize(new ManualReplaySnapshot(
            "manual-http-json",
            target.Id,
            target.BaseUri.AbsoluteUri,
            target.Host,
            target.CredentialReference,
            target.Operations,
            target.SensitiveJsonPaths,
            invariant,
            target.CreatedAtUtc), WebJson);

    public static ManualReplaySnapshot Deserialize(string json) =>
        JsonSerializer.Deserialize<ManualReplaySnapshot>(json, WebJson)
        ?? throw new InvalidDataException("The manual replay snapshot is invalid.");
}

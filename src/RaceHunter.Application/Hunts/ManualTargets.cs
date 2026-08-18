using RaceHunter.Domain.Common;

namespace RaceHunter.Application.Hunts;

public sealed record ManualTargetAuthorization(
    Uri BaseUri,
    IReadOnlyCollection<string> AllowedHosts,
    bool AuthorizationAcknowledged,
    string CredentialReference,
    IReadOnlyCollection<ManualTargetOperation> Operations,
    IReadOnlyCollection<string> SensitiveJsonPaths,
    string OwnerKeyId = "");

public sealed record ManualTargetOperation(
    string Id,
    string Method,
    string Path,
    string RequestTemplateJson,
    IReadOnlyDictionary<string, string> ObservationPaths,
    bool IsSetup = false,
    IReadOnlyDictionary<string, string>? ObservationTypes = null,
    string IdempotencyMode = "none")
{
    public string ObservationType(string metric) =>
        ObservationTypes is not null && ObservationTypes.TryGetValue(metric, out var type) ? type : "number";
}

public static class ManualTargetIdempotencyModes
{
    public const string None = "none";
    public const string ReceiverKeyed = "receiver-keyed";
}

public enum ManualSetupClaimDisposition
{
    Send,
    Completed,
    Ambiguous,
    BudgetExceeded
}

public sealed record ManualSetupClaim(
    ManualSetupClaimDisposition Disposition,
    int PhysicalRequestsReserved);

public interface IManualSetupExecutionStore
{
    Task<ManualSetupClaim> ReserveAsync(Guid runId, Guid targetId, string executionKey, string operationId,
        string idempotencyMode, CancellationToken cancellationToken);
    Task CompleteAsync(Guid runId, string executionKey, string operationId, CancellationToken cancellationToken);
    Task MarkAmbiguousAsync(Guid runId, string executionKey, string operationId, CancellationToken cancellationToken);
    Task<bool> CanStartAsync(Guid runId, int additionalRequests, CancellationToken cancellationToken);
}

public sealed record ValidatedManualTarget(
    Uri BaseUri,
    string Host,
    string CredentialReference,
    IReadOnlyCollection<ManualTargetOperation> Operations,
    IReadOnlyCollection<string> SensitiveJsonPaths,
    string OwnerKeyId = "");

public sealed record ManualTargetSnapshot(
    Guid Id,
    Uri BaseUri,
    string Host,
    string CredentialReference,
    IReadOnlyCollection<ManualTargetOperation> Operations,
    IReadOnlyCollection<string> SensitiveJsonPaths,
    DateTime CreatedAtUtc,
    string OwnerKeyId = "");

public interface IManualTargetSafetyPolicy
{
    Task<ValidatedManualTarget> ValidateAsync(ManualTargetAuthorization request, CancellationToken cancellationToken);
}

public interface IManualTargetStore
{
    Task AddAsync(ManualTargetSnapshot target, CancellationToken cancellationToken);
    Task<ManualTargetSnapshot?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<ManualTargetSnapshot?> GetByBaseUriAsync(Uri baseUri, CancellationToken cancellationToken);
}

public sealed class ConfigureManualTarget(IManualTargetSafetyPolicy safetyPolicy, IManualTargetStore store)
{
    public async Task<ManualTargetSnapshot> ExecuteAsync(ManualTargetAuthorization request, CancellationToken cancellationToken)
    {
        var validated = await safetyPolicy.ValidateAsync(request, cancellationToken);
        if (string.IsNullOrWhiteSpace(request.OwnerKeyId)) throw new DomainException("An authenticated manual-target owner is required.");
        if (validated.Operations.Count > 20) throw new DomainException("A manual target supports at most 20 allowlisted operations.");
        var existing = await store.GetByBaseUriAsync(validated.BaseUri, cancellationToken);
        if (existing is not null)
        {
            if (existing.CredentialReference != validated.CredentialReference ||
                existing.OwnerKeyId != request.OwnerKeyId ||
                System.Text.Json.JsonSerializer.Serialize(existing.Operations) != System.Text.Json.JsonSerializer.Serialize(validated.Operations) ||
                !existing.SensitiveJsonPaths.SequenceEqual(validated.SensitiveJsonPaths))
                throw new DomainException("That base URL already has a different immutable target snapshot.");
            return existing;
        }
        var target = new ManualTargetSnapshot(
            Guid.NewGuid(),
            validated.BaseUri,
            validated.Host,
            validated.CredentialReference,
            validated.Operations,
            validated.SensitiveJsonPaths,
            DateTime.UtcNow,
            request.OwnerKeyId);
        await store.AddAsync(target, cancellationToken);
        return target;
    }
}

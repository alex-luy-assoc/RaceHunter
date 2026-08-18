using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RaceHunter.Application.Hunts;

namespace RaceHunter.Infrastructure.Persistence;

internal sealed class ManualTargetStore(RaceHunterDbContext context) : IManualTargetStore
{
    public async Task AddAsync(ManualTargetSnapshot target, CancellationToken cancellationToken)
    {
        context.TargetSystems.Add(new TargetSystemRecord
        {
            Id = target.Id,
            BaseUrl = target.BaseUri.AbsoluteUri,
            Host = target.Host,
            CredentialReference = target.CredentialReference,
            OperationPathsJson = JsonSerializer.Serialize(target.Operations),
            SensitiveJsonPathsJson = JsonSerializer.Serialize(target.SensitiveJsonPaths),
            CreatedAtUtc = target.CreatedAtUtc
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<ManualTargetSnapshot?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var item = await context.TargetSystems.AsNoTracking().SingleOrDefaultAsync(target => target.Id == id, cancellationToken);
        return item is null ? null : new ManualTargetSnapshot(
            item.Id,
            new Uri(item.BaseUrl, UriKind.Absolute),
            item.Host,
            item.CredentialReference,
            JsonSerializer.Deserialize<ManualTargetOperation[]>(item.OperationPathsJson) ?? [],
            JsonSerializer.Deserialize<string[]>(item.SensitiveJsonPathsJson) ?? [],
            item.CreatedAtUtc);
    }

    public async Task<ManualTargetSnapshot?> GetByBaseUriAsync(Uri baseUri, CancellationToken cancellationToken)
    {
        var item = await context.TargetSystems.AsNoTracking().SingleOrDefaultAsync(
            target => target.BaseUrl == baseUri.AbsoluteUri, cancellationToken);
        return item is null ? null : new ManualTargetSnapshot(item.Id, new Uri(item.BaseUrl, UriKind.Absolute), item.Host,
            item.CredentialReference, JsonSerializer.Deserialize<ManualTargetOperation[]>(item.OperationPathsJson) ?? [],
            JsonSerializer.Deserialize<string[]>(item.SensitiveJsonPathsJson) ?? [], item.CreatedAtUtc);
    }
}

using Microsoft.EntityFrameworkCore;
using RaceHunter.Application.Hunts;

namespace RaceHunter.Infrastructure.Persistence;

internal sealed class SecurityAuditStore(RaceHunterDbContext context) : ISecurityAuditStore
{
    public async Task AppendAsync(SecurityAuditEvent item, CancellationToken cancellationToken)
    {
        context.SecurityAuditEvents.Add(new SecurityAuditEventRecord
        {
            Id = item.Id,
            ScopeId = item.ScopeId,
            Stage = item.Stage,
            Category = item.Category,
            Outcome = item.Outcome,
            SanitizedDetail = item.SanitizedDetail,
            OccurredAtUtc = item.OccurredAtUtc
        });
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<SecurityAuditEvent>> GetRecentAsync(int maximum, CancellationToken cancellationToken) =>
        await context.SecurityAuditEvents.AsNoTracking().OrderByDescending(item => item.OccurredAtUtc).Take(maximum)
            .Select(item => new SecurityAuditEvent(item.Id, item.ScopeId, item.Stage, item.Category, item.Outcome, item.SanitizedDetail, item.OccurredAtUtc))
            .ToArrayAsync(cancellationToken);
}

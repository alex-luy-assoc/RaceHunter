namespace RaceHunter.Application.Hunts;

public sealed record SecurityAuditEvent(
    Guid Id,
    Guid? ScopeId,
    string Stage,
    string Category,
    string Outcome,
    string SanitizedDetail,
    DateTime OccurredAtUtc);

public interface ISecurityAuditStore
{
    Task AppendAsync(SecurityAuditEvent auditEvent, CancellationToken cancellationToken);
    Task<IReadOnlyList<SecurityAuditEvent>> GetRecentAsync(int maximum, CancellationToken cancellationToken);
}

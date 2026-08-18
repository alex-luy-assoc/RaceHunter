using Microsoft.EntityFrameworkCore;
using RaceHunter.Application.Abstractions;

namespace RaceHunter.Infrastructure.Persistence;

internal sealed class RunCancellationProbe(IDbContextFactory<RaceHunterDbContext> contextFactory) : IRunCancellationProbe
{
    public async Task<DateTime?> GetRequestedAtUtcAsync(Guid runId, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        return await context.Runs.AsNoTracking()
            .Where(item => item.Id == runId)
            .Select(item => item.CancellationRequestedAtUtc)
            .SingleOrDefaultAsync(cancellationToken);
    }
}

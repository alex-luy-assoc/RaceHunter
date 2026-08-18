using RaceHunter.Application.Abstractions;
using RaceHunter.Domain.Runs;

namespace RaceHunter.Application.Runs;

public sealed class CancelRun(IRunStore runStore)
{
    public async Task<ExperimentRun?> ExecuteAsync(Guid id, CancellationToken cancellationToken)
    {
        var run = await runStore.GetAsync(id, cancellationToken);
        if (run is null) return null;
        await runStore.RequestCancellationAsync(id, DateTime.UtcNow, cancellationToken);
        return await runStore.GetAsync(id, cancellationToken);
    }
}

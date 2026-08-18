using RaceHunter.Application.Abstractions;
using RaceHunter.Domain.Runs;

namespace RaceHunter.Worker.Execution;

internal static class PersistedRunLifecycle
{
    public static Task<T> RunReproductionAsync<T>(
        ExperimentRun run,
        IRunStore runs,
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken) =>
        RunPhaseAsync(run, runs, run.BeginReproduction, work, cancellationToken);

    public static Task<T> RunMinimizationAsync<T>(
        ExperimentRun run,
        IRunStore runs,
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken) =>
        RunPhaseAsync(run, runs, run.BeginMinimization, work, cancellationToken);

    private static async Task<T> RunPhaseAsync<T>(
        ExperimentRun run,
        IRunStore runs,
        Func<DateTime, bool> begin,
        Func<CancellationToken, Task<T>> work,
        CancellationToken cancellationToken)
    {
        if (begin(DateTime.UtcNow))
            await runs.SaveAsync(run, cancellationToken);

        return await work(cancellationToken);
    }
}

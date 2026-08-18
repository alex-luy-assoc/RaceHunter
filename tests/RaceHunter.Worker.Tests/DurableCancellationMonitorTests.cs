using RaceHunter.Application.Abstractions;
using RaceHunter.Worker.Execution;
using Xunit;

namespace RaceHunter.Worker.Tests;

public sealed class DurableCancellationMonitorTests
{
    [Fact]
    public async Task Persisted_cancellation_stops_campaign_within_two_seconds()
    {
        var requestedAt = DateTime.UtcNow;
        var probe = new ImmediateCancellationProbe(requestedAt);
        using var execution = new CancellationTokenSource();
        var monitor = new DurableCancellationMonitor(probe, TimeSpan.FromMilliseconds(25));

        await monitor.WaitAsync(Guid.NewGuid(), execution, CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.True(execution.IsCancellationRequested);
        Assert.True(DateTime.UtcNow - requestedAt < TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task Probe_failure_immediately_cancels_campaign_instead_of_leaving_target_work_running()
    {
        using var execution = new CancellationTokenSource();
        var monitor = new DurableCancellationMonitor(new ThrowingCancellationProbe(), TimeSpan.FromMilliseconds(25));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            monitor.WaitAsync(Guid.NewGuid(), execution, CancellationToken.None));

        Assert.True(execution.IsCancellationRequested);
    }

    private sealed class ImmediateCancellationProbe(DateTime requestedAt) : IRunCancellationProbe
    {
        public Task<DateTime?> GetRequestedAtUtcAsync(Guid runId, CancellationToken cancellationToken) =>
            Task.FromResult<DateTime?>(requestedAt);
    }

    private sealed class ThrowingCancellationProbe : IRunCancellationProbe
    {
        public Task<DateTime?> GetRequestedAtUtcAsync(Guid runId, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("database unavailable");
    }
}

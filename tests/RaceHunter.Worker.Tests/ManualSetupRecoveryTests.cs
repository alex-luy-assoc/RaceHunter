using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using RaceHunter.Application.Hunts;
using RaceHunter.Concurrency.Invariants;
using RaceHunter.Concurrency.Scheduling;
using RaceHunter.Domain.Invariants;
using RaceHunter.Infrastructure.Security;
using RaceHunter.Worker.Execution;
using Xunit;

namespace RaceHunter.Worker.Tests;

public sealed class ManualSetupRecoveryTests
{
    [Fact]
    public async Task Receiver_keyed_setup_survives_commit_checkpoint_crash_without_resetting_twice()
    {
        await using var receiver = await ControlledReceiver.StartAsync();
        var runId = Guid.NewGuid();
        var store = new MemorySetupStore(failFirstCompletion: true, maxRequests: 10);
        var setupId = new string('s', 64);
        var client = CreateClient(receiver.BaseUri, store, ManualTargetIdempotencyModes.ReceiverKeyed, setupId);
        var executionKey = $"{runId:N}:minimize:step:1:{new string('f', 64)}";

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.PrepareAsync(TargetId, runId, executionKey, CancellationToken.None));
        var setupRequests = await client.PrepareAsync(TargetId, runId, executionKey, CancellationToken.None);
        var results = await Task.WhenAll(
            client.ExecuteAsync(TargetId, runId, new ScheduledActor(1, TimeSpan.Zero, null, "reserve-seat"), "reserve-seat", executionKey, CancellationToken.None),
            client.ExecuteAsync(TargetId, runId, new ScheduledActor(2, TimeSpan.Zero, null, "reserve-seat"), "reserve-seat", executionKey, CancellationToken.None));
        var invariant = new InvariantEvaluatorRegistry().Evaluate(
            new NumericBoundaryInvariant("reservation-count", 1), results.SelectMany(item => item.Observations).ToArray());

        Assert.Equal(1, receiver.ResetMutations);
        Assert.Equal(2, receiver.SetupRequests);
        Assert.All(receiver.SetupKeys, key => Assert.Equal(67, key.Length));
        Assert.Equal(2, setupRequests);
        Assert.Equal(4, setupRequests + results.Length);
        Assert.Equal(InvariantOutcome.Fail, invariant.Outcome);
    }

    [Fact]
    public async Task Ambiguous_non_idempotent_setup_is_not_retried_automatically()
    {
        await using var receiver = await ControlledReceiver.StartAsync();
        var store = new MemorySetupStore(failFirstCompletion: true, maxRequests: 10);
        var client = CreateClient(receiver.BaseUri, store, ManualTargetIdempotencyModes.None);
        var runId = Guid.NewGuid();

        var first = await Assert.ThrowsAsync<TargetSafetyException>(() => client.PrepareAsync(TargetId, runId, "probe:unsafe", CancellationToken.None));
        var error = await Assert.ThrowsAsync<TargetSafetyException>(() =>
            client.PrepareAsync(TargetId, runId, "probe:unsafe", CancellationToken.None));

        Assert.Equal("manual_recovery_required", first.Code);
        Assert.Equal("manual_recovery_required", error.Code);
        Assert.Equal(1, receiver.SetupRequests);
        Assert.Equal(1, receiver.ResetMutations);
        Assert.Equal(1, store.PhysicalRequests);
    }

    private static readonly Guid TargetId = Guid.Parse("1355e63e-e118-44ae-b4d9-63772bd12926");

    private static ManualHttpTargetClient CreateClient(Uri baseUri, MemorySetupStore setupStore, string mode, string setupId = "setup")
    {
        var target = new ManualTargetSnapshot(TargetId, baseUri, baseUri.Host,
            "projects/local-demo/secrets/manual-target-token/versions/latest",
            [
                new ManualTargetOperation(setupId, "POST", "/reset", "{\"quantity\":1}",
                    new Dictionary<string, string>(), true, new Dictionary<string, string>(), mode),
                new ManualTargetOperation("reserve-seat", "POST", "/reserve", "{\"actorId\":\"{{actorId}}\"}",
                    new Dictionary<string, string> { ["reservation-count"] = "$.reservationCount" })
            ], [], DateTime.UtcNow, "owner");
        var validator = new TargetDestinationValidator(new LoopbackDns(), true, [baseUri.Host]);
        return new ManualHttpTargetClient(new SingleTargetStore(target), validator,
            new SafeTargetClientFactory(validator),
            new DevelopmentSecretProvider(target.CredentialReference, "receiver-token"), setupStore);
    }

    private sealed class MemorySetupStore(bool failFirstCompletion, int maxRequests) : IManualSetupExecutionStore
    {
        private string status = "new";
        private bool failCompletion = failFirstCompletion;
        public int PhysicalRequests { get; private set; }

        public Task<ManualSetupClaim> ReserveAsync(Guid runId, Guid targetId, string executionKey,
            string operationId, string idempotencyMode, CancellationToken cancellationToken)
        {
            if (status == "completed") return Task.FromResult(new ManualSetupClaim(ManualSetupClaimDisposition.Completed, PhysicalRequests));
            if (status == "ambiguous" || status == "reserved" && idempotencyMode == ManualTargetIdempotencyModes.None)
            {
                status = "ambiguous";
                return Task.FromResult(new ManualSetupClaim(ManualSetupClaimDisposition.Ambiguous, PhysicalRequests));
            }
            if (PhysicalRequests >= maxRequests) return Task.FromResult(new ManualSetupClaim(ManualSetupClaimDisposition.BudgetExceeded, PhysicalRequests));
            status = "reserved";
            PhysicalRequests++;
            return Task.FromResult(new ManualSetupClaim(ManualSetupClaimDisposition.Send, PhysicalRequests));
        }

        public Task CompleteAsync(Guid runId, string executionKey, string operationId, CancellationToken cancellationToken)
        {
            if (failCompletion)
            {
                failCompletion = false;
                throw new InvalidOperationException("simulated crash after receiver commit");
            }
            status = "completed";
            return Task.CompletedTask;
        }

        public Task MarkAmbiguousAsync(Guid runId, string executionKey, string operationId, CancellationToken cancellationToken)
        {
            status = "ambiguous";
            return Task.CompletedTask;
        }

        public Task<bool> CanStartAsync(Guid runId, int additionalRequests, CancellationToken cancellationToken) =>
            Task.FromResult(additionalRequests <= maxRequests - PhysicalRequests);
    }

    private sealed class SingleTargetStore(ManualTargetSnapshot target) : IManualTargetStore
    {
        public Task AddAsync(ManualTargetSnapshot value, CancellationToken cancellationToken) => Task.CompletedTask;
        public Task<ManualTargetSnapshot?> GetAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<ManualTargetSnapshot?>(id == target.Id ? target : null);
        public Task<ManualTargetSnapshot?> GetByBaseUriAsync(Uri baseUri, CancellationToken cancellationToken) => Task.FromResult<ManualTargetSnapshot?>(baseUri == target.BaseUri ? target : null);
    }

    private sealed class LoopbackDns : IDnsResolver
    {
        public Task<IPAddress[]> GetHostAddressesAsync(string host, CancellationToken cancellationToken) => Task.FromResult(new[] { IPAddress.Loopback });
    }

    private sealed class ControlledReceiver(WebApplication app, ReceiverState state) : IAsyncDisposable
    {
        public Uri BaseUri { get; } = new(app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
        public int ResetMutations => state.ResetMutations;
        public int SetupRequests => state.SetupRequests;
        public IReadOnlyCollection<string> SetupKeys => state.SetupKeys.Keys.ToArray();

        public static async Task<ControlledReceiver> StartAsync()
        {
            var state = new ReceiverState();
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            var app = builder.Build();
            app.MapPost("/reset", (HttpRequest request) =>
            {
                if (request.Headers.Authorization != "Bearer receiver-token") return Results.Unauthorized();
                var key = request.Headers["X-RaceHunter-Idempotency-Key"].ToString();
                Interlocked.Increment(ref state.SetupRequests);
                if (state.SetupKeys.TryAdd(key, 0))
                {
                    Interlocked.Increment(ref state.ResetMutations);
                    Interlocked.Exchange(ref state.Reservations, 0);
                }
                return Results.Ok(new { reset = true });
            });
            app.MapPost("/reserve", async () =>
            {
                var count = Interlocked.Increment(ref state.Reservations);
                if (count == 2) state.BothActors.TrySetResult();
                await state.BothActors.Task.WaitAsync(TimeSpan.FromSeconds(5));
                return Results.Ok(new { reservationCount = Volatile.Read(ref state.Reservations) });
            });
            await app.StartAsync();
            return new ControlledReceiver(app, state);
        }

        public async ValueTask DisposeAsync() => await app.DisposeAsync();
    }

    private sealed class ReceiverState
    {
        public readonly ConcurrentDictionary<string, byte> SetupKeys = new(StringComparer.Ordinal);
        public readonly TaskCompletionSource BothActors = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int SetupRequests;
        public int ResetMutations;
        public int Reservations;
    }
}

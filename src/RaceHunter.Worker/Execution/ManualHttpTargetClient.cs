using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using RaceHunter.Application.Hunts;
using RaceHunter.Concurrency.Scheduling;
using RaceHunter.Domain.Invariants;
using RaceHunter.Infrastructure.Observability;
using RaceHunter.Infrastructure.Security;

namespace RaceHunter.Worker.Execution;

internal sealed class ManualHttpTargetClient(IManualTargetStore targets, IManualTargetSafetyPolicy safetyPolicy,
    SafeTargetClientFactory clients, ISecretProvider secrets)
{
    private const int MaximumResponseBytes = 1024 * 1024;
    private readonly ConcurrentDictionary<Guid, Task<ValidatedManualTarget>> validatedTargets = new();

    public async Task<int> PrepareAsync(Guid targetId, Guid runId, string executionKey, CancellationToken cancellationToken)
    {
        var target = await GetValidatedAsync(targetId, cancellationToken);
        var setup = target.Operations.SingleOrDefault(operation => operation.IsSetup);
        if (setup is not null)
        {
            await SendAsync(target, setup, runId, new ScheduledActor(1, TimeSpan.Zero, null, setup.Id), executionKey, false, cancellationToken);
            return 1;
        }
        return 0;
    }

    public async Task<TargetCallResult> ExecuteAsync(Guid targetId, Guid runId, ScheduledActor actor,
        string operationId, string executionKey, CancellationToken cancellationToken)
    {
        var target = await GetValidatedAsync(targetId, cancellationToken);
        var operation = target.Operations.SingleOrDefault(item => !item.IsSetup && item.Id == operationId)
            ?? throw new TargetSafetyException("operation_blocked", "The planned operation is not present in the immutable target snapshot.");
        return await SendAsync(target, operation, runId, actor, executionKey, true, cancellationToken)
            ?? throw new TargetSafetyException("observation_invalid", "An executable operation produced no observations.");
    }

    public async Task<ManualTargetSnapshot> GetSnapshotAsync(Guid targetId, CancellationToken cancellationToken) =>
        await targets.GetAsync(targetId, cancellationToken)
            ?? throw new TargetSafetyException("target_missing", "The authorized target no longer exists.");

    private Task<ValidatedManualTarget> GetValidatedAsync(Guid targetId, CancellationToken cancellationToken) =>
        validatedTargets.GetOrAdd(targetId, _ => LoadValidatedAsync(targetId, cancellationToken));

    private async Task<ValidatedManualTarget> LoadValidatedAsync(Guid targetId, CancellationToken cancellationToken)
    {
        var snapshot = await GetSnapshotAsync(targetId, cancellationToken);
        return await safetyPolicy.ValidateAsync(new ManualTargetAuthorization(snapshot.BaseUri, [snapshot.Host], true,
            snapshot.CredentialReference, snapshot.Operations, snapshot.SensitiveJsonPaths), cancellationToken);
    }

    private async Task<TargetCallResult?> SendAsync(ValidatedManualTarget target, ManualTargetOperation operation,
        Guid runId, ScheduledActor actor, string executionKey, bool collectObservations, CancellationToken cancellationToken)
    {
        var started = System.Diagnostics.Stopwatch.GetTimestamp();
        var outcome = "failure";
        using var activity = RaceHunterTelemetry.Activities.StartActivity("racehunter.target.manual", System.Diagnostics.ActivityKind.Client);
        activity?.SetTag("racehunter.run.id", runId.ToString());
        activity?.SetTag("racehunter.actor.id", actor.ActorId);
        activity?.SetTag("racehunter.step.id", operation.Id);
        try
        {
            var credential = await secrets.AccessAsync(target.CredentialReference, cancellationToken);
            using var client = clients.Create(target);
            using var request = new HttpRequestMessage(new HttpMethod(operation.Method), operation.Path);
            if (operation.Method != "GET")
                request.Content = new StringContent(Render(operation.RequestTemplateJson, runId, actor, executionKey), Encoding.UTF8, "application/json");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credential);
            request.Headers.TryAddWithoutValidation("X-RaceHunter-Idempotency-Key", $"{executionKey}:{actor.ActorId}:{operation.Id}");
            request.Headers.TryAddWithoutValidation("X-RaceHunter-Replay-Scope", executionKey);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var bytes = await ReadBoundedAsync(response.Content, cancellationToken);
            var sanitized = SensitiveDataRedactor.RedactJson(Encoding.UTF8.GetString(bytes), target.SensitiveJsonPaths);
            response.EnsureSuccessStatusCode();
            if (!collectObservations)
            {
                outcome = "success";
                return null;
            }
            using var document = JsonDocument.Parse(sanitized);
            var requestId = TryRead(document.RootElement, "$.correlationId", out var correlation)
                ? correlation.ToString() : Guid.NewGuid().ToString("N");
            var observations = new List<Observation>();
            foreach (var configured in operation.ObservationPaths)
            {
                if (!TryRead(document.RootElement, configured.Value, out var value))
                    throw new TargetSafetyException("response_contract_invalid", $"The sanitized response did not contain observation '{configured.Key}'.");
                if (operation.ObservationType(configured.Key) == "text")
                {
                    var text = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
                    if (string.IsNullOrWhiteSpace(text))
                        throw new TargetSafetyException("response_contract_invalid", $"The sanitized response did not contain text observation '{configured.Key}'.");
                    observations.Add(Observation.Text(configured.Key, text, $"target-response:{requestId}", requestId));
                }
                else
                {
                    if (!value.TryGetDecimal(out var number))
                        throw new TargetSafetyException("response_contract_invalid", $"The sanitized response did not contain numeric observation '{configured.Key}'.");
                    observations.Add(Observation.Number(configured.Key, number, $"target-response:{requestId}", requestId));
                }
            }
            activity?.SetTag("racehunter.request.id", requestId);
            outcome = "success";
            return TargetCallResult.Success(observations, requestId);
        }
        catch (TargetSafetyException)
        {
            outcome = "safety-failure";
            throw;
        }
        catch (OperationCanceledException)
        {
            outcome = "cancelled";
            throw;
        }
        catch (HttpRequestException)
        {
            outcome = "http-failure";
            throw;
        }
        finally
        {
            var tags = new TagList
            {
                { "outcome", outcome },
                { "operation", operation.Id }
            };
            RaceHunterTelemetry.ManualTargetRequests.Add(1, tags);
            RaceHunterTelemetry.ManualTargetLatency.Record(
                System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds, tags);
        }
    }

    private static string Render(string template, Guid runId, ScheduledActor actor, string executionKey)
    {
        static string Escape(string value) => JsonSerializer.Serialize(value)[1..^1];
        return template.Replace("{{actorId}}", Escape($"actor-{actor.ActorId}"), StringComparison.Ordinal)
            .Replace("{{runId}}", Escape(runId.ToString("N")), StringComparison.Ordinal)
            .Replace("{{executionKey}}", Escape(executionKey), StringComparison.Ordinal)
            .Replace("{{checkpoint}}", Escape(actor.CheckpointOrder.HasValue ? $"racehunter:{executionKey}" : string.Empty), StringComparison.Ordinal);
    }

    private static bool TryRead(JsonElement root, string path, out JsonElement value)
    {
        value = root;
        foreach (var segment in path[2..].Split('.'))
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(segment, out value)) return false;
        return true;
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var input = await content.ReadAsStreamAsync(cancellationToken);
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0) return output.ToArray();
            if (output.Length + read > MaximumResponseBytes)
                throw new TargetSafetyException("response_too_large", "The target response exceeded the one-megabyte evidence limit.");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
    }
}

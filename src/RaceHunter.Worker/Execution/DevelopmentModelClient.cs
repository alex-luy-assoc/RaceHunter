using RaceHunter.Application.Agents;

namespace RaceHunter.Worker.Execution;

internal sealed class DevelopmentModelClient : IStructuredModelClient
{
    public Task<ModelResponse> GenerateAsync(ModelRequest request, CancellationToken cancellationToken)
    {
        var json = request.SchemaVersion == "plan-v1"
            ? """{"schemaVersion":"plan-v1","actors":[{"name":"buyer","operationId":"place-order"}],"invariant":{"type":"numeric-boundary","metric":"successful-orders","maximum":1},"strategy":{"kind":"checkpoint-interleaving","actorCount":2,"seed":42}}"""
            : request.Input.Contains("attempt=1", StringComparison.Ordinal)
                ? """{"schemaVersion":"strategy-v1","action":"repeat","actorCount":2,"strategy":"checkpoint-interleaving","timingAdjustmentMs":0,"rationaleSummary":"Repeat the bounded controlled schedule once."}"""
                : """{"schemaVersion":"strategy-v1","action":"stop","actorCount":2,"strategy":"checkpoint-interleaving","timingAdjustmentMs":0,"rationaleSummary":"Stop after collecting bounded evidence."}""";
        return Task.FromResult(new ModelResponse(json, "deterministic-development-fake", Guid.NewGuid().ToString("N"), null));
    }
}

using System.Text.Json.Nodes;
using Google.GenAI;
using Google.GenAI.Types;
using RaceHunter.Application.Agents;

namespace RaceHunter.Gemini;

public sealed class GoogleGenAiModelClient : IStructuredModelClient, IAsyncDisposable
{
    private readonly Client client;

    public GoogleGenAiModelClient(string projectId, string location)
    {
        if (string.IsNullOrWhiteSpace(projectId)) throw new ArgumentException("A Google Cloud project ID is required.", nameof(projectId));
        if (string.IsNullOrWhiteSpace(location)) throw new ArgumentException("A Vertex AI location is required.", nameof(location));
        client = new Client(vertexAI: true, project: projectId, location: location);
    }

    public async Task<ModelResponse> GenerateAsync(ModelRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var response = await client.Models.GenerateContentAsync(
                request.ModelId,
                request.Input,
                new GenerateContentConfig
                {
                    ResponseMimeType = "application/json",
                    ResponseJsonSchema = JsonNode.Parse(request.ResponseSchemaJson),
                    Temperature = 0.1
                },
                cancellationToken);
            if (string.IsNullOrWhiteSpace(response.Text))
                throw new ModelOutputException(ModelOutcome.InvalidOutput, "empty structured response");
            return new ModelResponse(
                response.Text,
                request.ModelId,
                response.ResponseId ?? Guid.NewGuid().ToString("N"),
                response.UsageMetadata is null
                    ? null
                    : new ModelUsage(response.UsageMetadata.PromptTokenCount, response.UsageMetadata.CandidatesTokenCount));
        }
        catch (ModelOutputException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ModelOutputException(ModelOutcome.TransientFailure, "provider invocation failed", exception);
        }
    }

    public ValueTask DisposeAsync() => client.DisposeAsync();
}

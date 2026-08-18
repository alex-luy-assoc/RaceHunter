using System.Text;
using System.Text.Json;
using RaceHunter.Contracts;
using RaceHunter.Worker.Execution;

namespace RaceHunter.Worker.Endpoints;

internal static class PubSubPushEndpoint
{
    internal static IEndpointRouteBuilder MapPubSubPushEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/internal/pubsub/push", HandleAsync);
        return endpoints;
    }

    private static async Task<IResult> HandleAsync(
        HttpContext context,
        PubSubPushEnvelope envelope,
        WorkDispatcher dispatcher,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (configuration.GetValue("PubSub:RequireAuthentication", true) &&
            !context.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.Ordinal))
        {
            return Results.Problem(statusCode: StatusCodes.Status401Unauthorized, title: "Authenticated Pub/Sub push required");
        }
        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(envelope.Message.Data));
            var message = WorkMessage.Parse(json);
            string? traceParent = null;
            string? traceState = null;
            envelope.Message.Attributes?.TryGetValue("traceparent", out traceParent);
            envelope.Message.Attributes?.TryGetValue("tracestate", out traceState);
            var outcome = await dispatcher.DispatchAsync(message, envelope.Message.MessageId, cancellationToken, traceParent, traceState);
            return outcome == WorkDispatchOutcome.Acknowledged
                ? Results.NoContent()
                : Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
        }
        catch (Exception exception) when (exception is FormatException or JsonException)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Poison Pub/Sub message", detail: "The versioned work envelope is invalid.");
        }
    }
}

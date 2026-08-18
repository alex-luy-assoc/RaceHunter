using RaceHunter.Api.Security;
using RaceHunter.Application.Hunts;
using RaceHunter.Contracts;
using RaceHunter.Domain.Common;
using RaceHunter.Infrastructure.Security;

namespace RaceHunter.Api.Endpoints;

internal static class ManualTargetEndpoints
{
    internal static IEndpointRouteBuilder MapManualTargetEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/admin/targets", ConfigureAsync);
        return endpoints;
    }

    private static async Task<IResult> ConfigureAsync(
        HttpContext context,
        ConfigureManualTargetRequest request,
        ConfigureManualTarget command,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        if (!AdminAuthentication.IsAuthorized(context, configuration)) return Results.NotFound();

        try
        {
            if (!Uri.TryCreate(request.BaseUrl, UriKind.Absolute, out var baseUri))
                return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Unsafe manual target", detail: "The target URL is invalid.");
            var target = await command.ExecuteAsync(new ManualTargetAuthorization(
                baseUri,
                request.AllowedHosts,
                request.AuthorizationAcknowledged,
                request.CredentialReference,
                request.Operations.Select(operation => new ManualTargetOperation(
                    operation.Id, operation.Method, operation.Path, operation.RequestTemplateJson,
                    operation.ObservationPaths, operation.IsSetup)).ToArray(),
                request.SensitiveJsonPaths), cancellationToken);
            return Results.Created($"/api/admin/targets/{target.Id}", new ManualTargetResponse(
                target.Id,
                target.BaseUri.AbsoluteUri,
                target.Host,
                target.CredentialReference,
                target.Operations.Select(operation => new ManualTargetOperationRequest(
                    operation.Id, operation.Method, operation.Path, operation.RequestTemplateJson,
                    operation.ObservationPaths, operation.IsSetup)).ToArray(),
                target.SensitiveJsonPaths.ToArray(),
                target.CreatedAtUtc));
        }
        catch (Exception exception) when (exception is TargetSafetyException or DomainException or ArgumentException)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Unsafe manual target", detail: exception.Message);
        }
    }
}

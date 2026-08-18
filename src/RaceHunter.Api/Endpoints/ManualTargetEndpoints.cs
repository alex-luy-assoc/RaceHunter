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
        endpoints.MapGet("/api/admin/audit-events", async (HttpContext context, ISecurityAuditStore audits,
            IConfiguration configuration, CancellationToken cancellationToken) =>
        {
            var authentication = AdminAuthentication.Evaluate(context, configuration);
            if (authentication == AdminAuthenticationResult.Missing) return Results.Unauthorized();
            if (authentication != AdminAuthenticationResult.Authorized) return Results.StatusCode(StatusCodes.Status403Forbidden);
            return Results.Ok(await audits.GetRecentAsync(100, cancellationToken));
        });
        return endpoints;
    }

    private static async Task<IResult> ConfigureAsync(
        HttpContext context,
        ConfigureManualTargetRequest request,
        ConfigureManualTarget command,
        ISecurityAuditStore audits,
        IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var authentication = AdminAuthentication.Evaluate(context, configuration);
        if (authentication == AdminAuthenticationResult.Missing) return Results.Unauthorized();
        if (authentication != AdminAuthenticationResult.Authorized) return Results.StatusCode(StatusCodes.Status403Forbidden);
        AdminAuthentication.EstablishSession(context);

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
                    operation.ObservationPaths, operation.IsSetup, operation.ObservationTypes, operation.IdempotencyMode)).ToArray(),
                request.SensitiveJsonPaths,
                AdminAuthentication.OwnerKeyId(context)), cancellationToken);
            return Results.Created($"/api/admin/targets/{target.Id}", new ManualTargetResponse(
                target.Id,
                target.BaseUri.AbsoluteUri,
                target.Host,
                target.CredentialReference,
                target.Operations.Select(operation => new ManualTargetOperationRequest(
                    operation.Id, operation.Method, operation.Path, operation.RequestTemplateJson,
                    operation.ObservationPaths, operation.IsSetup, operation.ObservationTypes, operation.IdempotencyMode)).ToArray(),
                target.SensitiveJsonPaths.ToArray(),
                target.CreatedAtUtc));
        }
        catch (TargetSafetyException exception)
        {
            await audits.AppendAsync(new SecurityAuditEvent(Guid.NewGuid(), null, "configuration", exception.Code,
                "rejected", "Manual target configuration was rejected by the safety policy.", DateTime.UtcNow), CancellationToken.None);
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Unsafe manual target", detail: "The manual target was rejected by the safety policy.");
        }
        catch (Exception exception) when (exception is DomainException or ArgumentException)
        {
            return Results.Problem(statusCode: StatusCodes.Status400BadRequest, title: "Unsafe manual target", detail: exception.Message);
        }
    }
}

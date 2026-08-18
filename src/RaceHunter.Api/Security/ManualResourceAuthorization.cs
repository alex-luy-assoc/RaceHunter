using RaceHunter.Application.Hunts;

namespace RaceHunter.Api.Security;

internal static class ManualResourceAuthorization
{
    internal static async Task<IResult?> ForTargetAsync(HttpContext context, Guid targetId,
        IManualTargetStore targets, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var target = await targets.GetAsync(targetId, cancellationToken);
        if (target is null) return Results.NotFound();
        return Authorize(context, target.OwnerKeyId, configuration);
    }

    internal static async Task<IResult?> ForHuntAsync(HttpContext context, Guid huntId,
        IHuntStore hunts, IManualTargetStore targets, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var hunt = await hunts.GetAsync(huntId, cancellationToken);
        if (hunt is null) return Results.NotFound();
        return hunt.ManualTargetId.HasValue
            ? await ForTargetAsync(context, hunt.ManualTargetId.Value, targets, configuration, cancellationToken)
            : null;
    }

    internal static async Task<IResult?> ForRunAsync(HttpContext context, Guid runId,
        IHuntStore hunts, IManualTargetStore targets, IConfiguration configuration, CancellationToken cancellationToken)
    {
        var hunt = await hunts.GetByRunAsync(runId, cancellationToken);
        // Runs created by the public reference workflow (and legacy reference
        // records) do not carry a manual-target ownership boundary.
        if (hunt is null) return null;
        return hunt.ManualTargetId.HasValue
            ? await ForTargetAsync(context, hunt.ManualTargetId.Value, targets, configuration, cancellationToken)
            : null;
    }

    private static IResult? Authorize(HttpContext context, string ownerKeyId, IConfiguration configuration)
    {
        return AdminAuthentication.Evaluate(context, configuration) switch
        {
            AdminAuthenticationResult.Missing => Results.Unauthorized(),
            AdminAuthenticationResult.Invalid => Results.StatusCode(StatusCodes.Status403Forbidden),
            AdminAuthenticationResult.Authorized when !CryptographicEquals(
                AdminAuthentication.OwnerKeyId(context), ownerKeyId) => Results.StatusCode(StatusCodes.Status403Forbidden),
            _ => null
        };
    }

    private static bool CryptographicEquals(string left, string right)
    {
        var leftBytes = System.Text.Encoding.UTF8.GetBytes(left);
        var rightBytes = System.Text.Encoding.UTF8.GetBytes(right);
        return leftBytes.Length == rightBytes.Length &&
            System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}

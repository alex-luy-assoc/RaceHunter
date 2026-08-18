using System.Security.Cryptography;
using System.Text;

namespace RaceHunter.Api.Security;

internal static class AdminAuthentication
{
    internal static AdminAuthenticationResult Evaluate(HttpContext context, IConfiguration configuration)
    {
        var expected = configuration["ManualTargets:AdminToken"];
        var suppliedToken = SuppliedToken(context);
        if (string.IsNullOrEmpty(suppliedToken)) return AdminAuthenticationResult.Missing;
        if (string.IsNullOrWhiteSpace(expected)) return AdminAuthenticationResult.Invalid;
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(suppliedToken));
        return CryptographicOperations.FixedTimeEquals(expectedHash, suppliedHash)
            ? AdminAuthenticationResult.Authorized
            : AdminAuthenticationResult.Invalid;
    }

    internal static bool IsAuthorized(HttpContext context, IConfiguration configuration) =>
        Evaluate(context, configuration) == AdminAuthenticationResult.Authorized;

    internal static string OwnerKeyId(HttpContext context) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            SuppliedToken(context) ?? string.Empty))).ToLowerInvariant();

    internal static void EstablishSession(HttpContext context)
    {
        var token = SuppliedToken(context);
        if (string.IsNullOrEmpty(token)) return;
        context.Response.Cookies.Append("racehunter_manual_session", token, new CookieOptions
        {
            HttpOnly = true,
            Secure = context.Request.IsHttps,
            SameSite = SameSiteMode.Strict,
            MaxAge = TimeSpan.FromHours(2),
            Path = "/"
        });
    }

    private static string? SuppliedToken(HttpContext context)
    {
        var supplied = context.Request.Headers.Authorization.ToString();
        if (supplied.StartsWith("Bearer ", StringComparison.Ordinal)) return supplied["Bearer ".Length..];
        return context.Request.Cookies.TryGetValue("racehunter_manual_session", out var cookie) ? cookie : null;
    }
}

internal enum AdminAuthenticationResult { Missing, Invalid, Authorized }

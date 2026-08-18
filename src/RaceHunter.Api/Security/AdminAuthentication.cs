using System.Security.Cryptography;
using System.Text;

namespace RaceHunter.Api.Security;

internal static class AdminAuthentication
{
    internal static bool IsAuthorized(HttpContext context, IConfiguration configuration)
    {
        var expected = configuration["ManualTargets:AdminToken"];
        var supplied = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrWhiteSpace(expected) || !supplied.StartsWith("Bearer ", StringComparison.Ordinal)) return false;
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expected));
        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(supplied["Bearer ".Length..]));
        return CryptographicOperations.FixedTimeEquals(expectedHash, suppliedHash);
    }
}

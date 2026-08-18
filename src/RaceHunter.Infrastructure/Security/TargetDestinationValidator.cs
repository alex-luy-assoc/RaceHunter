using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.RegularExpressions;
using RaceHunter.Application.Hunts;

namespace RaceHunter.Infrastructure.Security;

public sealed class TargetSafetyException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

public interface IDnsResolver
{
    Task<IPAddress[]> GetHostAddressesAsync(string host, CancellationToken cancellationToken);
}

public sealed class SystemDnsResolver : IDnsResolver
{
    public Task<IPAddress[]> GetHostAddressesAsync(string host, CancellationToken cancellationToken) =>
        Dns.GetHostAddressesAsync(host, cancellationToken);
}

public sealed partial class TargetDestinationValidator(
    IDnsResolver dnsResolver,
    bool allowDevelopmentHttp = false,
    IReadOnlyCollection<string>? developmentHosts = null) : IManualTargetSafetyPolicy
{
    private readonly HashSet<string> developmentHosts = (developmentHosts ?? [])
        .Select(host => host.Trim().TrimEnd('.').ToLowerInvariant())
        .ToHashSet(StringComparer.Ordinal);
    private static readonly HashSet<string> BlockedHostNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "metadata.google.internal",
        "metadata",
        "localhost"
    };

    public async Task<ValidatedManualTarget> ValidateAsync(ManualTargetAuthorization request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!request.AuthorizationAcknowledged)
            throw new TargetSafetyException("authorization_required", "Target ownership and authorization must be acknowledged.");
        if (!request.BaseUri.IsAbsoluteUri || request.BaseUri.UserInfo.Length > 0)
            throw new TargetSafetyException("destination_invalid", "The target must be an absolute URI without embedded credentials.");

        var isHttps = string.Equals(request.BaseUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        var normalizedHost = request.BaseUri.IdnHost.TrimEnd('.').ToLowerInvariant();
        var allowedDevelopmentHttp = allowDevelopmentHttp &&
            string.Equals(request.BaseUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) &&
            (IsLoopbackHost(normalizedHost) || developmentHosts.Contains(normalizedHost));
        if (!isHttps && !allowedDevelopmentHttp)
            throw new TargetSafetyException("https_required", "Manual targets require HTTPS outside explicit development loopback mode.");

        var host = normalizedHost;
        if (BlockedHostNames.Contains(host) && !allowedDevelopmentHttp)
            throw new TargetSafetyException("destination_blocked", "The target resolves to a prohibited destination.");
        var allowlist = request.AllowedHosts.Select(item => item.Trim().TrimEnd('.').ToLowerInvariant()).ToHashSet(StringComparer.Ordinal);
        if (allowlist.Count == 0 || allowlist.Any(item => item.Length == 0 || item.Contains('*')) || !allowlist.Contains(host))
            throw new TargetSafetyException("host_not_allowlisted", "The exact target host must appear in the host allowlist.");
        if (!SecretManagerReference().IsMatch(request.CredentialReference))
            throw new TargetSafetyException("credential_reference_invalid", "Credentials must use a Secret Manager version reference; raw values are not accepted.");
        var operations = ValidateOperations(request.Operations);

        await ResolvePublicAddressesAsync(host, cancellationToken, allowedDevelopmentHttp);

        return new ValidatedManualTarget(
            request.BaseUri,
            host,
            request.CredentialReference,
            operations,
            request.SensitiveJsonPaths.Select(item => item.Trim()).Where(item => item.Length > 0).Distinct(StringComparer.Ordinal).ToArray());
    }

    public async Task<IPAddress[]> ResolvePublicAddressesAsync(
        string host,
        CancellationToken cancellationToken,
        bool allowDevelopmentLoopback = false)
    {
        host = host.Trim().TrimEnd('.').ToLowerInvariant();
        var allowDevelopmentDestination = allowDevelopmentLoopback || (allowDevelopmentHttp && developmentHosts.Contains(host));
        if (BlockedHostNames.Contains(host) && !allowDevelopmentDestination)
            throw new TargetSafetyException("destination_blocked", "The target resolves to a prohibited destination.");
        var addresses = IPAddress.TryParse(host, out var literal)
            ? [literal]
            : await dnsResolver.GetHostAddressesAsync(host, cancellationToken);
        if (addresses.Length == 0 || addresses.Any(address => IsBlocked(address) && !allowDevelopmentDestination))
            throw new TargetSafetyException("destination_blocked", "The target resolves to a prohibited destination.");
        return addresses;
    }

    public async Task<Uri> ValidateRedirectAsync(
        Uri original,
        Uri redirect,
        IReadOnlyCollection<string> allowedHosts,
        CancellationToken cancellationToken)
    {
        if (!redirect.IsAbsoluteUri ||
            !string.Equals(original.Scheme, redirect.Scheme, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(original.IdnHost.TrimEnd('.'), redirect.IdnHost.TrimEnd('.'), StringComparison.OrdinalIgnoreCase))
            throw new TargetSafetyException("redirect_blocked", "Redirects may not change target scheme or host.");

        await ValidateAsync(new ManualTargetAuthorization(
            redirect,
            allowedHosts,
            true,
            "projects/redirect-validation/secrets/unused/versions/latest",
            [new ManualTargetOperation("redirect-check", "GET", redirect.PathAndQuery, "{}", new Dictionary<string, string> { ["status"] = "$.status" })],
            []), cancellationToken);
        return redirect;
    }

    internal static bool IsBlocked(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any)) return true;
        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var ipv6Bytes = address.GetAddressBytes();
            return address.IsIPv6LinkLocal || address.IsIPv6Multicast || address.IsIPv6SiteLocal ||
                (ipv6Bytes[0] & 0xfe) == 0xfc ||
                HasPrefix(ipv6Bytes, [0x20, 0x01, 0x0d, 0xb8], 32) ||
                HasPrefix(ipv6Bytes, [0x20, 0x01, 0x00, 0x02], 48) ||
                HasPrefix(ipv6Bytes, [0x20, 0x01, 0x00, 0x10], 28) ||
                HasPrefix(ipv6Bytes, [0x20, 0x01, 0x00, 0x20], 28) ||
                HasPrefix(ipv6Bytes, [0x20, 0x02], 16) ||
                HasPrefix(ipv6Bytes, [0x01, 0x00], 64);
        }

        var bytes = address.GetAddressBytes();
        return HasPrefix(bytes, [0], 8) || HasPrefix(bytes, [10], 8) || HasPrefix(bytes, [100, 64], 10) ||
            HasPrefix(bytes, [127], 8) || HasPrefix(bytes, [169, 254], 16) || HasPrefix(bytes, [172, 16], 12) ||
            HasPrefix(bytes, [192, 0, 0], 24) || HasPrefix(bytes, [192, 0, 2], 24) ||
            HasPrefix(bytes, [192, 168], 16) || HasPrefix(bytes, [198, 18], 15) ||
            HasPrefix(bytes, [198, 51, 100], 24) || HasPrefix(bytes, [203, 0, 113], 24) ||
            HasPrefix(bytes, [224], 4) || HasPrefix(bytes, [240], 4);
    }

    private static bool HasPrefix(ReadOnlySpan<byte> address, ReadOnlySpan<byte> prefix, int prefixBits)
    {
        var completeBytes = prefixBits / 8;
        if (!address[..completeBytes].SequenceEqual(prefix[..completeBytes])) return false;
        var remainingBits = prefixBits % 8;
        if (remainingBits == 0) return true;
        var mask = (byte)(0xff << (8 - remainingBits));
        return (address[completeBytes] & mask) == (prefix[completeBytes] & mask);
    }

    private static bool IsLoopbackHost(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
        (IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address));

    private static IReadOnlyCollection<ManualTargetOperation> ValidateOperations(IReadOnlyCollection<ManualTargetOperation> requested)
    {
        if (requested.Count == 0 || requested.Count > 20)
            throw new TargetSafetyException("operation_invalid", "One to twenty target operations are required.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var validated = new List<ManualTargetOperation>(requested.Count);
        foreach (var operation in requested)
        {
            var id = operation.Id.Trim();
            var method = operation.Method.Trim().ToUpperInvariant();
            var path = operation.Path.Trim();
            if (!OperationId().IsMatch(id) || !ids.Add(id) || method is not ("GET" or "POST") ||
                !path.StartsWith("/", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal) ||
                path.Contains("://", StringComparison.Ordinal) || path.Contains('\\') || path.Contains('?') || path.Contains('#') || path.Length > 500)
                throw new TargetSafetyException("operation_invalid", "Operations require unique stable IDs, GET/POST methods, and relative rooted paths.");
            if (operation.RequestTemplateJson.Length > 16 * 1024)
                throw new TargetSafetyException("template_invalid", "Request templates are limited to 16 KiB.");
            try { using var _ = JsonDocument.Parse(operation.RequestTemplateJson); }
            catch (JsonException) { throw new TargetSafetyException("template_invalid", "Every request template must be valid JSON."); }
            var placeholders = Placeholder().Matches(operation.RequestTemplateJson).Select(match => match.Groups[1].Value).ToArray();
            if (Placeholder().Replace(operation.RequestTemplateJson, string.Empty).Contains("{{", StringComparison.Ordinal) ||
                placeholders.Any(item => item is not ("actorId" or "runId" or "executionKey" or "checkpoint")))
                throw new TargetSafetyException("template_invalid", "Templates may use only actorId, runId, executionKey, and checkpoint placeholders.");
            if (!operation.IsSetup && operation.ObservationPaths.Count == 0)
                throw new TargetSafetyException("observation_invalid", "Executable operations require at least one deterministic observation path.");
            if (operation.ObservationPaths.Count > 20 || operation.ObservationPaths.Any(item =>
                    !MetricName().IsMatch(item.Key) || !JsonPath().IsMatch(item.Value)))
                throw new TargetSafetyException("observation_invalid", "Observation metrics and JSON paths must use the bounded allowlisted syntax.");
            validated.Add(operation with
            {
                Id = id,
                Method = method,
                Path = path,
                ObservationPaths = new Dictionary<string, string>(operation.ObservationPaths, StringComparer.Ordinal)
            });
        }
        if (validated.Count(item => item.IsSetup) > 1 || validated.All(item => item.IsSetup))
            throw new TargetSafetyException("operation_invalid", "At most one setup operation and at least one executable operation are required.");
        return validated;
    }

    [GeneratedRegex("^projects/[a-z][a-z0-9-]{4,28}[a-z0-9]/secrets/[A-Za-z0-9_-]{1,255}/versions/(?:latest|[1-9][0-9]*)$", RegexOptions.CultureInvariant)]
    private static partial Regex SecretManagerReference();
    [GeneratedRegex("^[a-z][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant)] private static partial Regex OperationId();
    [GeneratedRegex("\\{\\{([A-Za-z][A-Za-z0-9]*)\\}\\}", RegexOptions.CultureInvariant)] private static partial Regex Placeholder();
    [GeneratedRegex("^[a-z][a-z0-9-]{0,63}$", RegexOptions.CultureInvariant)] private static partial Regex MetricName();
    [GeneratedRegex("^\\$\\.[A-Za-z][A-Za-z0-9_]*(?:\\.[A-Za-z][A-Za-z0-9_]*){0,7}$", RegexOptions.CultureInvariant)] private static partial Regex JsonPath();
}

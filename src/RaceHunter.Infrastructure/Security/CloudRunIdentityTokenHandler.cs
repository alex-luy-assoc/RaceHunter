using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace RaceHunter.Infrastructure.Security;

public interface IIdentityTokenSource
{
    Task<string> GetTokenAsync(string audience, CancellationToken cancellationToken);
}

public sealed class MetadataIdentityTokenSource(HttpClient metadataClient) : IIdentityTokenSource
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private string? cachedToken;
    private string? cachedAudience;
    private DateTimeOffset cachedExpiresAtUtc;

    public async Task<string> GetTokenAsync(string audience, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(audience, UriKind.Absolute, out var target) || target.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("Cloud Run identity-token audience must be an absolute HTTPS URI.");
        if (CanReuse(audience)) return cachedToken!;
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (CanReuse(audience)) return cachedToken!;
            using var request = new HttpRequestMessage(HttpMethod.Get,
                $"http://metadata.google.internal/computeMetadata/v1/instance/service-accounts/default/identity?audience={Uri.EscapeDataString(audience)}&format=full");
            request.Headers.Add("Metadata-Flavor", "Google");
            using var response = await metadataClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            var token = (await response.Content.ReadAsStringAsync(cancellationToken)).Trim();
            if (token.Length is < 32 or > 16384) throw new InvalidOperationException("The metadata server returned an invalid identity token.");
            cachedToken = token;
            cachedAudience = audience;
            cachedExpiresAtUtc = ReadExpiry(token);
            return token;
        }
        finally
        {
            gate.Release();
        }
    }

    private bool CanReuse(string audience) =>
        cachedToken is not null && string.Equals(cachedAudience, audience, StringComparison.Ordinal) &&
        cachedExpiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(2);

    private static DateTimeOffset ReadExpiry(string token)
    {
        try
        {
            var payload = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + ((4 - payload.Length % 4) % 4), '=');
            using var json = JsonDocument.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload)));
            return DateTimeOffset.FromUnixTimeSeconds(json.RootElement.GetProperty("exp").GetInt64());
        }
        catch (Exception exception) when (exception is IndexOutOfRangeException or FormatException or JsonException or KeyNotFoundException)
        {
            throw new InvalidOperationException("The metadata server returned an invalid identity token.");
        }
    }
}

public sealed class CloudRunIdentityTokenHandler : DelegatingHandler
{
    private readonly string audience;
    private readonly Uri audienceUri;
    private readonly IIdentityTokenSource tokenSource;

    public CloudRunIdentityTokenHandler(string audience, IIdentityTokenSource tokenSource)
    {
        if (!Uri.TryCreate(audience.TrimEnd('/'), UriKind.Absolute, out var parsedAudience) || parsedAudience.Scheme != Uri.UriSchemeHttps)
            throw new ArgumentException("Cloud Run audience must be an absolute HTTPS URI.", nameof(audience));
        audienceUri = parsedAudience;
        this.audience = audienceUri.AbsoluteUri.TrimEnd('/');
        this.tokenSource = tokenSource;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.RequestUri is null ||
            !string.Equals(request.RequestUri.Scheme, audienceUri.Scheme, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(request.RequestUri.IdnHost, audienceUri.IdnHost, StringComparison.OrdinalIgnoreCase) ||
            request.RequestUri.Port != audienceUri.Port)
            throw new InvalidOperationException("Identity tokens may only be attached to the configured private Cloud Run service.");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await tokenSource.GetTokenAsync(audience, cancellationToken));
        return await base.SendAsync(request, cancellationToken);
    }
}

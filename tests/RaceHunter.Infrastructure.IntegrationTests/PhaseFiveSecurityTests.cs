using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using RaceHunter.Application.Hunts;
using RaceHunter.Infrastructure.Observability;
using RaceHunter.Infrastructure.Security;
using Xunit;

namespace RaceHunter.Infrastructure.IntegrationTests;

public sealed class PhaseFiveSecurityTests
{
    [Fact]
    public async Task Manual_target_requires_explicit_authorization_acknowledgement()
    {
        var validator = Validator("api.example.test", IPAddress.Parse("203.0.113.8"));

        var error = await Assert.ThrowsAsync<TargetSafetyException>(() => validator.ValidateAsync(Request(acknowledged: false), CancellationToken.None));

        Assert.Equal("authorization_required", error.Code);
    }

    [Fact]
    public async Task Manual_target_requires_https_outside_explicit_development_loopback()
    {
        var validator = Validator("api.example.test", IPAddress.Parse("203.0.113.8"));

        var error = await Assert.ThrowsAsync<TargetSafetyException>(() => validator.ValidateAsync(Request(baseUrl: "http://api.example.test"), CancellationToken.None));

        Assert.Equal("https_required", error.Code);
    }

    [Theory]
    [InlineData("169.254.169.254")]
    [InlineData("127.0.0.1")]
    [InlineData("10.20.30.40")]
    [InlineData("::1")]
    [InlineData("fd00::1")]
    public async Task Manual_target_blocks_metadata_loopback_and_private_destinations(string address)
    {
        var validator = Validator("api.example.test", IPAddress.Parse(address));

        var error = await Assert.ThrowsAsync<TargetSafetyException>(() => validator.ValidateAsync(Request(), CancellationToken.None));

        Assert.Equal("destination_blocked", error.Code);
    }

    [Theory]
    [InlineData("192.0.2.10")]
    [InlineData("198.18.0.1")]
    [InlineData("198.51.100.9")]
    [InlineData("203.0.113.7")]
    [InlineData("2001:db8::1")]
    public async Task Manual_target_blocks_reserved_non_global_destinations(string address)
    {
        var validator = Validator("api.example.test", IPAddress.Parse(address));

        var error = await Assert.ThrowsAsync<TargetSafetyException>(() => validator.ValidateAsync(Request(), CancellationToken.None));

        Assert.Equal("destination_blocked", error.Code);
    }

    [Fact]
    public async Task Manual_target_rejects_mixed_public_private_dns_answers()
    {
        var validator = new TargetDestinationValidator(new StubDnsResolver(
            IPAddress.Parse("8.8.8.8"), IPAddress.Parse("10.0.0.8")));

        var error = await Assert.ThrowsAsync<TargetSafetyException>(() => validator.ValidateAsync(Request(), CancellationToken.None));

        Assert.Equal("destination_blocked", error.Code);
    }

    [Fact]
    public async Task Manual_target_revalidates_and_rejects_cross_host_redirects()
    {
        var validator = Validator("api.example.test", IPAddress.Parse("8.8.8.8"));

        var error = await Assert.ThrowsAsync<TargetSafetyException>(() => validator.ValidateRedirectAsync(
            new Uri("https://api.example.test/orders"), new Uri("https://attacker.test/capture"), ["api.example.test"], CancellationToken.None));

        Assert.Equal("redirect_blocked", error.Code);
    }

    [Fact]
    public async Task Manual_target_accepts_allowlisted_public_https_and_secret_manager_reference()
    {
        var validator = Validator("api.example.test", IPAddress.Parse("8.8.8.8"));

        var validated = await validator.ValidateAsync(Request(), CancellationToken.None);

        Assert.Equal("api.example.test", validated.Host);
        Assert.Equal("projects/demo-project/secrets/orders-token/versions/latest", validated.CredentialReference);
    }

    [Fact]
    public void Redactor_removes_credentials_headers_and_configured_json_paths()
    {
        var headers = SensitiveDataRedactor.RedactHeaders(new Dictionary<string, IEnumerable<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["Authorization"] = ["Bearer raw-token"],
            ["Cookie"] = ["session=raw-cookie"],
            ["X-Correlation-Id"] = ["corr-7"]
        });
        var json = SensitiveDataRedactor.RedactJson("{\"customer\":{\"token\":\"raw-json-token\",\"id\":7}}", ["$.customer.token"]);

        Assert.Equal("[REDACTED]", headers["Authorization"]);
        Assert.Equal("[REDACTED]", headers["Cookie"]);
        Assert.Equal("corr-7", headers["X-Correlation-Id"]);
        Assert.DoesNotContain("raw-json-token", json, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", json, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cloud_run_identity_handler_uses_exact_audience_and_sets_bearer_token()
    {
        var tokenSource = new CapturingIdentityTokenSource();
        var transport = new CapturingHandler();
        using var client = new HttpClient(new CloudRunIdentityTokenHandler("https://worker.example.test", tokenSource) { InnerHandler = transport });

        using var response = await client.GetAsync("https://worker.example.test/internal/replays");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal("https://worker.example.test", tokenSource.Audience);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", "signed-id-token"), transport.Authorization);
    }

    [Fact]
    public async Task Metadata_identity_token_is_cached_until_near_expiry()
    {
        var transport = new TokenMetadataHandler(CreateJwt(DateTimeOffset.UtcNow.AddMinutes(30)));
        using var metadata = new HttpClient(transport);
        var source = new MetadataIdentityTokenSource(metadata);

        var first = await source.GetTokenAsync("https://worker.example.test", CancellationToken.None);
        var second = await source.GetTokenAsync("https://worker.example.test", CancellationToken.None);

        Assert.Equal(first, second);
        Assert.Equal(1, transport.Calls);
    }

    [Fact]
    public async Task Safe_target_client_never_follows_even_same_host_redirect_automatically()
    {
        var validator = Validator("api.example.test", IPAddress.Parse("8.8.8.8"));
        var target = await validator.ValidateAsync(Request(), CancellationToken.None);
        using var client = new SafeTargetClientFactory(validator).Create(target, new RedirectHandler());

        var error = await Assert.ThrowsAsync<TargetSafetyException>(() => client.PostAsync("/orders", new StringContent("{}")));

        Assert.Equal("redirect_blocked", error.Code);
    }

    [Fact]
    public async Task Safe_target_client_rejects_an_alternate_port()
    {
        var validator = Validator("api.example.test", IPAddress.Parse("8.8.8.8"));
        var target = await validator.ValidateAsync(Request(), CancellationToken.None);
        using var client = new SafeTargetClientFactory(validator).Create(target, new CapturingHandler());

        var error = await Assert.ThrowsAsync<TargetSafetyException>(() => client.GetAsync("https://api.example.test:8443/orders"));

        Assert.Equal("operation_blocked", error.Code);
    }

    [Fact]
    public async Task Development_loopback_http_is_deliberately_supported()
    {
        var validator = new TargetDestinationValidator(new StubDnsResolver(IPAddress.Loopback), allowDevelopmentHttp: true);
        var request = new ManualTargetAuthorization(
            new Uri("http://127.0.0.1:5050"),
            ["127.0.0.1"],
            true,
            "projects/demo-project/secrets/orders-token/versions/latest",
            [Operation()],
            []);

        var target = await validator.ValidateAsync(request, CancellationToken.None);

        Assert.Equal("127.0.0.1", target.Host);
    }

    [Fact]
    public void Telemetry_activity_preserves_run_attempt_actor_step_request_and_model_correlations()
    {
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == RaceHunterTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = _ => { }
        };
        ActivitySource.AddActivityListener(listener);

        using var activity = RaceHunterTelemetry.StartCampaignActivity(
            Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),
            Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"),
            7, "place-order", "request-7", "model-7");

        Assert.NotNull(activity);
        Assert.Equal("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", activity.GetTagItem("racehunter.run.id"));
        Assert.Equal("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", activity.GetTagItem("racehunter.attempt.id"));
        Assert.Equal(7, activity.GetTagItem("racehunter.actor.id"));
        Assert.Equal("place-order", activity.GetTagItem("racehunter.step.id"));
        Assert.Equal("request-7", activity.GetTagItem("racehunter.request.id"));
        Assert.Equal("model-7", activity.GetTagItem("racehunter.model.invocation_id"));
    }

    private static TargetDestinationValidator Validator(string host, IPAddress address) =>
        new(new StubDnsResolver(address));

    private static ManualTargetAuthorization Request(bool acknowledged = true, string baseUrl = "https://api.example.test") => new(
        new Uri(baseUrl),
        ["api.example.test"],
        acknowledged,
        "projects/demo-project/secrets/orders-token/versions/latest",
        [Operation()],
        ["$.customer.token"]);

    private static ManualTargetOperation Operation(string template = "{\"actorId\":\"{{actorId}}\"}") => new(
        "place-order", "POST", "/orders", template,
        new Dictionary<string, string> { ["successful-orders"] = "$.successfulOrders" });

    [Fact]
    public async Task Manual_target_rejects_unknown_template_placeholders()
    {
        var validator = Validator("api.example.test", IPAddress.Parse("8.8.8.8"));
        var request = Request() with { Operations = [Operation("{\"token\":\"{{secret}}\"}")] };

        var error = await Assert.ThrowsAsync<TargetSafetyException>(() => validator.ValidateAsync(request, CancellationToken.None));

        Assert.Equal("template_invalid", error.Code);
    }

    [Fact]
    public async Task Manual_target_rejects_an_unknown_setup_idempotency_contract()
    {
        var validator = Validator("api.example.test", IPAddress.Parse("8.8.8.8"));
        var setup = new ManualTargetOperation("setup", "POST", "/reset", "{}",
            new Dictionary<string, string>(), true, new Dictionary<string, string>(), "trust-me");
        var request = Request() with { Operations = [setup, Operation()] };

        var error = await Assert.ThrowsAsync<TargetSafetyException>(() => validator.ValidateAsync(request, CancellationToken.None));

        Assert.Equal("idempotency_invalid", error.Code);
    }

    private sealed class StubDnsResolver(params IPAddress[] addresses) : IDnsResolver
    {
        public Task<IPAddress[]> GetHostAddressesAsync(string host, CancellationToken cancellationToken) => Task.FromResult(addresses);
    }

    private sealed class CapturingIdentityTokenSource : IIdentityTokenSource
    {
        public string? Audience { get; private set; }
        public Task<string> GetTokenAsync(string audience, CancellationToken cancellationToken)
        {
            Audience = audience;
            return Task.FromResult("signed-id-token");
        }
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public AuthenticationHeaderValue? Authorization { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Authorization = request.Headers.Authorization;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }

    private sealed class TokenMetadataHandler(string token) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Assert.Equal("Google", request.Headers.GetValues("Metadata-Flavor").Single());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(token) });
        }
    }

    private sealed class RedirectHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.TemporaryRedirect)
            {
                Headers = { Location = new Uri("https://api.example.test/orders") }
            });
    }

    private static string CreateJwt(DateTimeOffset expiresAtUtc)
    {
        static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{Encode("{\"alg\":\"RS256\"}")}.{Encode($"{{\"exp\":{expiresAtUtc.ToUnixTimeSeconds()}}}")}.{new string('x', 48)}";
    }
}

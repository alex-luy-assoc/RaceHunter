using Google.Cloud.SecretManager.V1;

namespace RaceHunter.Infrastructure.Security;

public interface ISecretProvider
{
    Task<string> AccessAsync(string secretVersionReference, CancellationToken cancellationToken);
}

public sealed class GoogleSecretProvider(SecretManagerServiceClient client) : ISecretProvider
{
    public async Task<string> AccessAsync(string secretVersionReference, CancellationToken cancellationToken)
    {
        SecretVersionName name;
        try { name = SecretVersionName.Parse(secretVersionReference); }
        catch (FormatException) { throw new TargetSafetyException("credential_reference_invalid", "The Secret Manager version reference is invalid."); }
        var response = await client.AccessSecretVersionAsync(name, cancellationToken);
        var value = response.Payload.Data.ToStringUtf8();
        if (string.IsNullOrEmpty(value)) throw new InvalidOperationException("The referenced Secret Manager version is empty.");
        return value;
    }
}

public sealed class DeferredGoogleSecretProvider : ISecretProvider
{
    private readonly Lazy<SecretManagerServiceClient> client = new(
        SecretManagerServiceClient.Create,
        LazyThreadSafetyMode.ExecutionAndPublication);

    public Task<string> AccessAsync(string secretVersionReference, CancellationToken cancellationToken) =>
        new GoogleSecretProvider(client.Value).AccessAsync(secretVersionReference, cancellationToken);
}

public sealed class DevelopmentSecretProvider(string allowedReference, string value) : ISecretProvider
{
    public Task<string> AccessAsync(string secretVersionReference, CancellationToken cancellationToken)
    {
        if (!string.Equals(secretVersionReference, allowedReference, StringComparison.Ordinal))
            throw new TargetSafetyException("credential_reference_denied", "The development secret reference is not allowlisted.");
        return Task.FromResult(value);
    }
}

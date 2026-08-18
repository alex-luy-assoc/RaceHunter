using System.Net;
using System.Net.Sockets;
using RaceHunter.Application.Hunts;

namespace RaceHunter.Infrastructure.Security;

public sealed class SafeTargetClientFactory(TargetDestinationValidator validator)
{
    public HttpClient Create(ValidatedManualTarget target, HttpMessageHandler? transport = null)
    {
        var handler = new ValidatingTargetHandler(target, validator)
        {
            InnerHandler = transport ?? CreatePinnedTransport(validator)
        };
        return new HttpClient(handler)
        {
            BaseAddress = target.BaseUri,
            Timeout = TimeSpan.FromSeconds(15),
            MaxResponseContentBufferSize = 1024 * 1024
        };
    }

    private static SocketsHttpHandler CreatePinnedTransport(TargetDestinationValidator validator) => new()
    {
        AllowAutoRedirect = false,
        UseProxy = false,
        ConnectTimeout = TimeSpan.FromSeconds(5),
        PooledConnectionLifetime = TimeSpan.FromMinutes(2),
        ConnectCallback = async (context, cancellationToken) =>
        {
            Exception? lastError = null;
            var allowDevelopmentLoopback = context.DnsEndPoint.Port != 443 &&
                (string.Equals(context.DnsEndPoint.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                 (IPAddress.TryParse(context.DnsEndPoint.Host, out var literal) && IPAddress.IsLoopback(literal)));
            foreach (var address in await validator.ResolvePublicAddressesAsync(context.DnsEndPoint.Host, cancellationToken, allowDevelopmentLoopback))
            {
                var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch (Exception exception) when (exception is SocketException or OperationCanceledException)
                {
                    socket.Dispose();
                    lastError = exception;
                    if (exception is OperationCanceledException) throw;
                }
            }
            throw new HttpRequestException("The validated target destination could not be reached.", lastError);
        }
    };

    private sealed class ValidatingTargetHandler(ValidatedManualTarget target, TargetDestinationValidator validator) : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri ?? throw new TargetSafetyException("destination_invalid", "The request destination is missing.");
            if (!uri.IsAbsoluteUri) uri = new Uri(target.BaseUri, uri);
            if (!string.Equals(uri.Scheme, target.BaseUri.Scheme, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(uri.IdnHost, target.Host, StringComparison.OrdinalIgnoreCase) ||
                uri.Port != target.BaseUri.Port ||
                !target.Operations.Any(operation =>
                    string.Equals(operation.Path, uri.AbsolutePath, StringComparison.Ordinal) &&
                    string.Equals(operation.Method, request.Method.Method, StringComparison.OrdinalIgnoreCase)))
                throw new TargetSafetyException("operation_blocked", "The request is outside the authorized target operations.");

            await validator.ValidateAsync(new ManualTargetAuthorization(
                uri,
                [target.Host],
                true,
                target.CredentialReference,
                target.Operations,
                target.SensitiveJsonPaths), cancellationToken);
            request.RequestUri = uri;
            var response = await base.SendAsync(request, cancellationToken);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is not null)
            {
                var redirect = response.Headers.Location.IsAbsoluteUri ? response.Headers.Location : new Uri(uri, response.Headers.Location);
                await validator.ValidateRedirectAsync(uri, redirect, [target.Host], cancellationToken);
                response.Dispose();
                throw new TargetSafetyException("redirect_blocked", "Manual target redirects are not followed automatically.");
            }
            return response;
        }
    }
}

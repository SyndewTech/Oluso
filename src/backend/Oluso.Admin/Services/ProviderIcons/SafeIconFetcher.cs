using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using Oluso.Core.Security;

namespace Oluso.Admin.Services.ProviderIcons;

public sealed record IconFetchResult(bool Success, Uri? FinalUrl, byte[]? Bytes, string? ContentType, string? Error)
{
    public static IconFetchResult Fail(string error) => new(false, null, null, null, error);
}

/// <summary>
/// Fetches a URL an administrator pointed us at. The server is making the
/// request, so the target is treated as hostile until proven otherwise.
/// </summary>
public interface IProviderIconFetcher
{
    Task<IconFetchResult> FetchAsync(Uri url, int maxBytes, CancellationToken cancellationToken = default);
}

/// <summary>
/// Outbound fetch with server-side request forgery defences:
/// <list type="bullet">
/// <item>https only, no credentials in the URL, hostnames must be public DNS names or public IP literals;</item>
/// <item>every hostname is resolved first and every resolved address must be public, so an
/// attacker-controlled name cannot point at loopback, link-local metadata endpoints or private ranges;</item>
/// <item>the connection is then pinned to the address that was checked, so a second lookup during
/// connect (DNS rebinding) cannot redirect it; TLS is still validated against the hostname;</item>
/// <item>redirects are not followed automatically; each hop is re-validated the same way, at most three;</item>
/// <item>responses are read through a hard byte cap and a short timeout.</item>
/// </list>
/// </summary>
public sealed class SafeIconFetcher : IProviderIconFetcher
{
    public const string ClientName = "ProviderIconDiscovery";
    public const int MaxRedirects = 3;
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(6);

    private readonly IHttpClientFactory _httpClientFactory;

    public SafeIconFetcher(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>Handler configuration for the named client (see <see cref="PublicHttpTargets.CreateHandler"/>).</summary>
    public static SocketsHttpHandler CreateHandler() => PublicHttpTargets.CreateHandler();

    /// <summary>Returns a rejection reason, or null when the URL may be fetched.</summary>
    public static string? ValidateTarget(Uri url) => PublicHttpTargets.ValidateTarget(url);

    public async Task<IconFetchResult> FetchAsync(Uri url, int maxBytes, CancellationToken cancellationToken = default)
    {
        var client = _httpClientFactory.CreateClient(ClientName);
        client.Timeout = RequestTimeout;

        var current = url;
        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            var rejection = ValidateTarget(current);
            if (rejection != null)
                return IconFetchResult.Fail($"{current}: {rejection}");

            var address = await PublicHttpTargets.ResolvePublicAddressAsync(current, cancellationToken);
            if (address.Error != null)
                return IconFetchResult.Fail($"{current}: {address.Error}");

            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            PublicHttpTargets.Pin(request, address.Address!);
            request.Headers.Accept.ParseAdd("image/*;q=1.0, text/html;q=0.9, application/xml;q=0.8, */*;q=0.1");
            request.Headers.UserAgent.ParseAdd("Oluso-ProviderIcon/1.0");

            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            }
            catch (HttpRequestException ex)
            {
                return IconFetchResult.Fail($"{current}: {ex.Message}");
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return IconFetchResult.Fail($"{current}: timed out");
            }

            using (response)
            {
                var status = (int)response.StatusCode;
                if (status is 301 or 302 or 303 or 307 or 308)
                {
                    var location = response.Headers.Location;
                    if (location == null)
                        return IconFetchResult.Fail($"{current}: redirect without a location");
                    current = location.IsAbsoluteUri ? location : new Uri(current, location);
                    continue;
                }

                if (!response.IsSuccessStatusCode)
                    return IconFetchResult.Fail($"{current}: HTTP {status}");

                if (response.Content.Headers.ContentLength is { } length && length > maxBytes)
                    return IconFetchResult.Fail($"{current}: {length} bytes exceeds the {maxBytes} byte limit");

                var bytes = await ReadBoundedAsync(response.Content, maxBytes, cancellationToken);
                if (bytes == null)
                    return IconFetchResult.Fail($"{current}: exceeds the {maxBytes} byte limit");

                return new IconFetchResult(true, current, bytes, response.Content.Headers.ContentType?.MediaType, null);
            }
        }

        return IconFetchResult.Fail($"{url}: too many redirects");
    }

    private static async Task<byte[]?> ReadBoundedAsync(HttpContent content, int maxBytes, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > maxBytes)
                return null;
            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }
}

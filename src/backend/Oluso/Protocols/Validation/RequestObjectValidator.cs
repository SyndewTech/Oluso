using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Protocols.Validation;
using Oluso.Core.Security;
using Oluso.Core.Services;

namespace Oluso.Protocols.Validation;

/// <summary>
/// Request objects per OpenID Connect Core 1.0 section 6 and RFC 9101.
/// <list type="bullet">
/// <item>By value (<c>request</c>) or by reference (<c>request_uri</c>, fetched over https); never both.</item>
/// <item>Signed with a key the client registered (a public JWK or certificate), or unsigned (<c>alg</c> none)
/// unless the client requires request objects. A client's registered <c>request_object_signing_alg</c> is
/// enforced. Encrypted request objects are refused: no request object encryption is advertised.</item>
/// <item><c>iss</c>, when present, must be the client; <c>aud</c>, when present, must name this issuer; an
/// expired request object is refused. <c>client_id</c> inside must match the client, and a request object may
/// not itself carry <c>request</c> or <c>request_uri</c>.</item>
/// </list>
/// </summary>
public class RequestObjectValidator : IRequestObjectValidator
{
    /// <summary>The named HttpClient request_uri fetches go through (configured with <see cref="PublicHttpTargets.CreateHandler"/>).</summary>
    public const string HttpClientName = "Oluso.RequestObjects";

    /// <summary>
    /// Admits request_uri targets on private, loopback and link-local addresses. Off by default; only for a local
    /// test setup (such as the OpenID conformance suite in Docker), never for a deployment.
    /// </summary>
    public const string AllowPrivateNetworkSetting = "Oluso:RequestObjects:AllowPrivateNetworkRequestUris";

    /// <summary>The largest request object accepted from a request_uri.</summary>
    public const int MaxRequestObjectBytes = 64 * 1024;

    private static readonly TimeSpan FetchTimeout = TimeSpan.FromSeconds(10);

    /// <summary>JWT claims about the request object itself rather than authorization request parameters.</summary>
    private static readonly HashSet<string> JwtClaims = new(StringComparer.Ordinal)
    {
        JwtRegisteredClaimNames.Iss,
        JwtRegisteredClaimNames.Aud,
        JwtRegisteredClaimNames.Exp,
        JwtRegisteredClaimNames.Nbf,
        JwtRegisteredClaimNames.Iat,
        JwtRegisteredClaimNames.Jti
    };

    private readonly IClientStore _clientStore;
    private readonly IIssuerResolver _issuerResolver;
    private readonly ITenantSettingsProvider _tenantSettingsProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IClientSecretProtector _secrets;
    private readonly IClientJwksUriKeys? _jwksUriKeys;
    private readonly IConfiguration _configuration;
    private readonly ILogger<RequestObjectValidator> _logger;

    public RequestObjectValidator(
        IClientStore clientStore,
        IIssuerResolver issuerResolver,
        ITenantSettingsProvider tenantSettingsProvider,
        IHttpClientFactory httpClientFactory,
        IClientSecretProtector secrets,
        IConfiguration configuration,
        ILogger<RequestObjectValidator> logger,
        IClientJwksUriKeys? jwksUriKeys = null)
    {
        _secrets = secrets;
        _jwksUriKeys = jwksUriKeys;
        _configuration = configuration;
        _clientStore = clientStore;
        _issuerResolver = issuerResolver;
        _tenantSettingsProvider = tenantSettingsProvider;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<RequestObjectValidationResult> ValidateAsync(
        string clientId,
        string? request,
        string? requestUri,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(request) && !string.IsNullOrEmpty(requestUri))
        {
            return RequestObjectValidationResult.Failure(
                OidcConstants.Errors.InvalidRequest,
                "request and request_uri must not both be used");
        }

        var client = string.IsNullOrEmpty(clientId)
            ? null
            : await _clientStore.FindClientByIdAsync(clientId, cancellationToken);
        if (client == null || !client.Enabled)
        {
            return RequestObjectValidationResult.Failure(OidcConstants.Errors.InvalidClient, "Unknown client");
        }

        var protocolSettings = await _tenantSettingsProvider.GetProtocolSettingsAsync(cancellationToken);

        string requestObject;
        if (!string.IsNullOrEmpty(requestUri))
        {
            if (!protocolSettings.RequestUriParameterSupported)
            {
                return RequestObjectValidationResult.Failure(
                    OidcConstants.Errors.RequestUriNotSupported,
                    "request_uri is not supported");
            }

            var fetched = await FetchAsync(requestUri, cancellationToken);
            if (fetched.Error != null)
            {
                return RequestObjectValidationResult.Failure(OidcConstants.Errors.InvalidRequestUri, fetched.Error);
            }
            requestObject = fetched.Content!;
        }
        else if (!string.IsNullOrEmpty(request))
        {
            if (!protocolSettings.RequestParameterSupported)
            {
                return RequestObjectValidationResult.Failure(
                    OidcConstants.Errors.RequestNotSupported,
                    "request is not supported");
            }
            requestObject = request.Trim();
        }
        else
        {
            return RequestObjectValidationResult.Failure(
                OidcConstants.Errors.InvalidRequest,
                "request or request_uri is required");
        }

        return await ValidateRequestObjectAsync(client, requestObject, cancellationToken);
    }

    private async Task<RequestObjectValidationResult> ValidateRequestObjectAsync(
        Client client,
        string requestObject,
        CancellationToken cancellationToken)
    {
        // A JWE has five parts. No request_object_encryption_alg_values_supported is advertised.
        if (requestObject.Count(c => c == '.') == 4)
        {
            return Invalid("Encrypted request objects are not supported");
        }

        var handler = new JwtSecurityTokenHandler();
        JwtSecurityToken jwt;
        try
        {
            jwt = handler.ReadJwtToken(requestObject);
        }
        catch (Exception ex) when (ex is ArgumentException or SecurityTokenException)
        {
            return Invalid("The request object is not a valid JWT");
        }

        var algorithm = jwt.Header.Alg;
        var registeredAlgorithm = client.Properties
            .FirstOrDefault(p => p.Key == RequestObjectSigningAlgorithms.ClientProperty)?.Value;
        if (!string.IsNullOrEmpty(registeredAlgorithm) && !string.Equals(algorithm, registeredAlgorithm, StringComparison.Ordinal))
        {
            return Invalid($"The request object must be signed with {registeredAlgorithm}, the algorithm the client registered");
        }

        var issuer = await _issuerResolver.GetIssuerAsync(cancellationToken);
        var isSigned = !string.Equals(algorithm, RequestObjectSigningAlgorithms.None, StringComparison.Ordinal);

        if (!isSigned)
        {
            if (client.RequireRequestObject)
            {
                return Invalid("This client must sign its request objects");
            }

            if (!string.IsNullOrEmpty(jwt.RawSignature))
            {
                return Invalid("An unsigned request object must have an empty signature");
            }

            var claimsError = ValidateClaims(jwt, client.ClientId, issuer);
            if (claimsError != null)
            {
                return Invalid(claimsError);
            }
        }
        else
        {
            if (!RequestObjectSigningAlgorithms.IsSupportedSigned(algorithm))
            {
                return Invalid($"Request object signing algorithm '{algorithm}' is not supported");
            }

            // A client publishing its keys at a jwks_uri gets one fresh fetch when no cached key matches or verifies.
            for (var attempt = 0; ; attempt++)
            {
                var refresh = attempt > 0;
                var canRefresh = !refresh && !string.IsNullOrEmpty(client.JwksUri);
                var match = ClientJwtKeys.Find(client, jwt, _secrets, await PublishedKeysAsync(client, refresh, cancellationToken));
                if (match == null)
                {
                    if (canRefresh) continue;
                    return Invalid("No key registered by the client verifies the request object");
                }

                var validationParameters = new TokenValidationParameters
                {
                    IssuerSigningKeys = match.Value.Keys,
                    ValidateIssuerSigningKey = true,
                    ValidAlgorithms = new[] { algorithm },
                    ValidateIssuer = !string.IsNullOrEmpty(jwt.Issuer),
                    ValidIssuer = client.ClientId,
                    ValidateAudience = jwt.Audiences.Any(),
                    ValidAudience = issuer,
                    ValidateLifetime = true,
                    RequireExpirationTime = false,
                    RequireSignedTokens = true,
                    ClockSkew = TimeSpan.FromMinutes(1)
                };

                try
                {
                    handler.ValidateToken(requestObject, validationParameters, out _);
                    break;
                }
                catch (SecurityTokenInvalidSignatureException) when (canRefresh)
                {
                }
                catch (Exception ex) when (ex is SecurityTokenException or ArgumentException)
                {
                    _logger.LogInformation("Request object from client {ClientId} failed validation: {Reason}",
                        client.ClientId, ex.Message);
                    return Invalid("The request object signature or claims are not valid");
                }
            }
        }

        Dictionary<string, string> parameters;
        try
        {
            parameters = ReadParameters(jwt.RawPayload);
        }
        catch (Exception ex) when (ex is FormatException or JsonException or ArgumentException)
        {
            return Invalid("The request object payload is not a JSON object");
        }

        if (parameters.ContainsKey("request") || parameters.ContainsKey("request_uri"))
        {
            return Invalid("A request object must not contain request or request_uri");
        }

        if (parameters.TryGetValue("client_id", out var innerClientId) &&
            !string.Equals(innerClientId, client.ClientId, StringComparison.Ordinal))
        {
            return Invalid("client_id in the request object does not match the client");
        }

        return RequestObjectValidationResult.Success(parameters, requestObject, isSigned);
    }

    /// <summary>
    /// The iss/aud/exp checks for an unsigned request object (a signed one gets them from token validation).
    /// </summary>
    private static string? ValidateClaims(JwtSecurityToken jwt, string clientId, string issuer)
    {
        if (!string.IsNullOrEmpty(jwt.Issuer) && !string.Equals(jwt.Issuer, clientId, StringComparison.Ordinal))
        {
            return "iss in the request object must be the client_id";
        }

        if (jwt.Audiences.Any() && !jwt.Audiences.Contains(issuer, StringComparer.Ordinal))
        {
            return "aud in the request object must be the issuer";
        }

        if (jwt.Payload.Expiration is { } exp &&
            DateTimeOffset.FromUnixTimeSeconds(exp) < DateTimeOffset.UtcNow.AddMinutes(-1))
        {
            return "The request object has expired";
        }

        return null;
    }

    /// <summary>
    /// The request object's authorization request parameters as single strings, the form the authorize
    /// parameters take: text as-is, numbers and booleans as their JSON text, a list of resources
    /// space-separated (as resource parameters are held), anything else (e.g. claims) as its JSON.
    /// </summary>
    private static Dictionary<string, string> ReadParameters(string rawPayload)
    {
        using var document = JsonDocument.Parse(Base64UrlEncoder.Decode(rawPayload));
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("The payload is not a JSON object");
        }

        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (JwtClaims.Contains(property.Name))
                continue;

            var value = property.Value;
            parameters[property.Name] = value.ValueKind switch
            {
                JsonValueKind.String => value.GetString() ?? string.Empty,
                JsonValueKind.Array when property.Name == Oidc.OidcResourceParameters.Name &&
                                         value.EnumerateArray().All(e => e.ValueKind == JsonValueKind.String)
                    => string.Join(' ', value.EnumerateArray().Select(e => e.GetString())),
                _ => value.GetRawText()
            };
        }

        return parameters;
    }

    /// <summary>
    /// Fetches a by-reference request object. The URL is the client's choice, so the fetch is held to the
    /// server-side request forgery defences in <see cref="PublicHttpTargets"/>: https only, every resolved address
    /// public and the connection pinned to it, no redirects, a size cap and a timeout. What went wrong is logged;
    /// the client only learns that the request_uri could not be used, so the fetch is no probe of other hosts.
    /// </summary>
    /// <summary>The keys the client publishes at its jwks_uri, or none when it has none.</summary>
    private async Task<IReadOnlyList<string>?> PublishedKeysAsync(Client client, bool refresh, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(client.JwksUri)) return null;
        if (_jwksUriKeys == null)
        {
            _logger.LogError("Client {ClientId} publishes its keys at a jwks_uri, but no IClientJwksUriKeys is registered", client.ClientId);
            return null;
        }
        return await _jwksUriKeys.GetAsync(client.JwksUri, refresh, cancellationToken);
    }

    private async Task<(string? Content, string? Error)> FetchAsync(string requestUri, CancellationToken cancellationToken)
    {
        const string unusable = "request_uri could not be used";
        var allowPrivateNetwork = _configuration.GetValue<bool>(AllowPrivateNetworkSetting);

        if (!Uri.TryCreate(requestUri, UriKind.Absolute, out var uri))
        {
            return (null, "request_uri must be an absolute https URL");
        }

        // The fragment only identifies a cached copy (OpenID Connect Core 1.0 section 6.2); it is not sent.
        var target = new Uri(uri.GetLeftPart(UriPartial.Query));

        if (PublicHttpTargets.ValidateTarget(target, allowPrivateNetwork) is { } rejection)
        {
            _logger.LogInformation("request_uri {RequestUri} refused: {Reason}", target, rejection);
            return (null, rejection.StartsWith("only https", StringComparison.Ordinal)
                ? "request_uri must be an absolute https URL"
                : unusable);
        }

        var address = await PublicHttpTargets.ResolvePublicAddressAsync(target, cancellationToken, allowPrivateNetwork);
        if (address.Error != null)
        {
            _logger.LogInformation("request_uri {RequestUri} refused: {Reason}", target, address.Error);
            return (null, unusable);
        }

        try
        {
            var http = _httpClientFactory.CreateClient(HttpClientName);
            http.Timeout = FetchTimeout;
            using var message = new HttpRequestMessage(HttpMethod.Get, target);
            PublicHttpTargets.Pin(message, address.Address!, allowPrivateNetwork);
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/oauth-authz-req+jwt"));
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/jwt"));

            using var response = await http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // Redirects included: they are not followed.
                _logger.LogInformation("request_uri {RequestUri} returned HTTP {Status}", target, (int)response.StatusCode);
                return (null, unusable);
            }

            if (response.Content.Headers.ContentLength > MaxRequestObjectBytes)
            {
                return (null, "The request object at request_uri is too large");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var buffer = new byte[MaxRequestObjectBytes + 1];
            var read = 0;
            int chunk;
            while (read < buffer.Length &&
                   (chunk = await stream.ReadAsync(buffer.AsMemory(read, buffer.Length - read), cancellationToken)) > 0)
            {
                read += chunk;
            }

            if (read > MaxRequestObjectBytes)
            {
                return (null, "The request object at request_uri is too large");
            }

            var content = Encoding.UTF8.GetString(buffer, 0, read).Trim();
            return string.IsNullOrEmpty(content) ? (null, unusable) : (content, null);
        }
        catch (Exception ex) when (ex is HttpRequestException ||
                                   (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            _logger.LogInformation("Fetching request_uri {RequestUri} failed: {Reason}", target, ex.Message);
            return (null, unusable);
        }
    }

    private static RequestObjectValidationResult Invalid(string description) =>
        RequestObjectValidationResult.Failure(OidcConstants.Errors.InvalidRequestObject, description);
}

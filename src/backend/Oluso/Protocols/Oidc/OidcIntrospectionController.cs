using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Protocols.Validation;

namespace Oluso.Protocols.Oidc;

/// <summary>
/// OAuth 2.0 Token Introspection Endpoint (RFC 7662).
/// Route is configured via OidcEndpointRouteConvention.
/// </summary>

public class OidcIntrospectionController : ControllerBase
{
    private readonly IClientAuthenticator _clientAuthenticator;
    private readonly IPersistedGrantStore _grantStore;
    private readonly IResourceStore _resourceStore;
    private readonly ISigningCredentialStore _signingCredentialStore;
    private readonly IIssuerResolver _issuerResolver;
    private readonly ILogger<OidcIntrospectionController> _logger;

    public OidcIntrospectionController(
        IClientAuthenticator clientAuthenticator,
        IPersistedGrantStore grantStore,
        IResourceStore resourceStore,
        ISigningCredentialStore signingCredentialStore,
        IIssuerResolver issuerResolver,
        ILogger<OidcIntrospectionController> logger)
    {
        _clientAuthenticator = clientAuthenticator;
        _grantStore = grantStore;
        _resourceStore = resourceStore;
        _signingCredentialStore = signingCredentialStore;
        _issuerResolver = issuerResolver;
        _logger = logger;
    }

    [HttpPost]
    [Consumes("application/x-www-form-urlencoded")]
    public async Task<IActionResult> Introspect(CancellationToken cancellationToken)
    {
        // Authenticate the calling client
        var clientAuth = await _clientAuthenticator.AuthenticateAsync(Request, cancellationToken);
        if (!clientAuth.IsValid)
        {
            return Unauthorized(new TokenErrorResponse
            {
                Error = clientAuth.Error!,
                ErrorDescription = clientAuth.ErrorDescription
            });
        }

        var callingClient = clientAuth.Client!;

        var form = await Request.ReadFormAsync(cancellationToken);
        var token = form["token"].FirstOrDefault();

        if (string.IsNullOrEmpty(token))
        {
            return BadRequest(new TokenErrorResponse
            {
                Error = OidcConstants.Errors.InvalidRequest,
                ErrorDescription = "token is required"
            });
        }

        var tokenTypeHint = form["token_type_hint"].FirstOrDefault();

        // Try to introspect the token
        var response = await IntrospectTokenAsync(token, tokenTypeHint, callingClient, cancellationToken);

        return Ok(response);
    }

    private async Task<IntrospectionResponse> IntrospectTokenAsync(
        string token,
        string? tokenTypeHint,
        Client callingClient,
        CancellationToken cancellationToken)
    {
        // Try reference token first
        var grant = await _grantStore.GetAsync(token, cancellationToken);
        if (grant != null)
        {
            // Check if calling client is authorized to introspect this token
            if (!await IsAuthorizedToIntrospectAsync(callingClient, grant.ClientId, grant.Data, cancellationToken))
            {
                _logger.LogWarning(
                    "Client {CallingClient} attempted to introspect token owned by {TokenClient}",
                    callingClient.ClientId, grant.ClientId);
                return new IntrospectionResponse { Active = false };
            }

            return IntrospectReferenceToken(grant);
        }

        // Try JWT
        return await IntrospectJwtTokenAsync(token, callingClient, cancellationToken);
    }

    /// <summary>
    /// Check if the calling client is authorized to introspect a token.
    /// A client can introspect if:
    /// 1. It owns the token (is the client the token was issued to)
    /// 2. The calling client's ID is in the token's audience (RFC 8707 resource-based)
    /// </summary>
    private Task<bool> IsAuthorizedToIntrospectAsync(
        Client callingClient,
        string tokenClientId,
        string? tokenData,
        CancellationToken cancellationToken)
    {
        // Client owns the token
        if (callingClient.ClientId == tokenClientId)
        {
            return Task.FromResult(true);
        }

        // Check if calling client is in the token's audience (RFC 8707)
        // Per RFC 8707, resources become the audience in tokens
        if (!string.IsNullOrEmpty(tokenData))
        {
            try
            {
                var data = JsonSerializer.Deserialize<Dictionary<string, object>>(tokenData);

                // Check if the calling client's ID is in the audience
                if (data?.TryGetValue("Audience", out var audObj) == true)
                {
                    var audiences = JsonSerializer.Deserialize<List<string>>(audObj.ToString()!);
                    if (audiences != null && audiences.Contains(callingClient.ClientId))
                    {
                        return Task.FromResult(true);
                    }
                }

                // Also check for Resources (RFC 8707 resource parameter values)
                if (data?.TryGetValue("Resources", out var resObj) == true)
                {
                    var resources = JsonSerializer.Deserialize<List<string>>(resObj.ToString()!);
                    // Check if calling client's ID matches any resource URI
                    // This allows resource servers to introspect tokens for their resources
                    if (resources != null && resources.Any(r =>
                        r == callingClient.ClientId ||
                        r.EndsWith($"/{callingClient.ClientId}", StringComparison.OrdinalIgnoreCase)))
                    {
                        return Task.FromResult(true);
                    }
                }
            }
            catch
            {
                // Ignore deserialization errors
            }
        }

        return Task.FromResult(false);
    }

    private IntrospectionResponse IntrospectReferenceToken(PersistedGrant grant)
    {
        // Check if consumed or expired
        if (grant.ConsumedTime.HasValue)
        {
            return new IntrospectionResponse { Active = false };
        }

        if (grant.Expiration.HasValue && grant.Expiration.Value < DateTime.UtcNow)
        {
            return new IntrospectionResponse { Active = false };
        }

        // Parse grant data
        try
        {
            var data = JsonSerializer.Deserialize<Dictionary<string, object>>(grant.Data);

            var response = new IntrospectionResponse
            {
                Active = true,
                TokenType = grant.Type == "refresh_token" ? "refresh_token" : "access_token",
                ClientId = grant.ClientId,
                Sub = grant.SubjectId,
                Iat = new DateTimeOffset(grant.CreationTime).ToUnixTimeSeconds(),
                Exp = grant.Expiration.HasValue ? new DateTimeOffset(grant.Expiration.Value).ToUnixTimeSeconds() : null
            };

            // Add scopes
            if (data?.TryGetValue("Scopes", out var scopes) == true)
            {
                var scopeList = JsonSerializer.Deserialize<List<string>>(scopes.ToString()!);
                response.Scope = string.Join(" ", scopeList ?? new List<string>());
            }

            return response;
        }
        catch
        {
            return new IntrospectionResponse { Active = false };
        }
    }

    private async Task<IntrospectionResponse> IntrospectJwtTokenAsync(
        string token,
        Client callingClient,
        CancellationToken cancellationToken)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();

            if (!handler.CanReadToken(token))
            {
                return new IntrospectionResponse { Active = false };
            }

            // Get validation keys for signature verification
            var validationKeys = await _signingCredentialStore.GetValidationKeysAsync(cancellationToken);
            if (validationKeys == null || !validationKeys.Any())
            {
                _logger.LogError("No validation keys available for token introspection");
                return new IntrospectionResponse { Active = false };
            }

            var issuer = await _issuerResolver.GetIssuerAsync(cancellationToken);

            // Validate the token with signature verification
            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = validationKeys.Select(k => k.Key),
                ValidateIssuer = !string.IsNullOrEmpty(issuer),
                ValidIssuer = issuer,
                ValidateAudience = false, // Audience validation depends on the use case
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1)
            };

            try
            {
                var principal = handler.ValidateToken(token, validationParameters, out var validatedToken);
                var jwt = validatedToken as JwtSecurityToken;

                if (jwt == null)
                {
                    return new IntrospectionResponse { Active = false };
                }

                var tokenClientId = jwt.Claims.FirstOrDefault(c => c.Type == "client_id")?.Value;
                var scopes = jwt.Claims.Where(c => c.Type == "scope").Select(c => c.Value).ToList();

                // Check authorization for JWT tokens
                if (!string.IsNullOrEmpty(tokenClientId))
                {
                    // Build scope data for authorization check
                    var scopeData = scopes.Any()
                        ? JsonSerializer.Serialize(new Dictionary<string, object> { ["Scopes"] = scopes })
                        : null;

                    if (!await IsAuthorizedToIntrospectAsync(callingClient, tokenClientId, scopeData, cancellationToken))
                    {
                        _logger.LogWarning(
                            "Client {CallingClient} attempted to introspect JWT token owned by {TokenClient}",
                            callingClient.ClientId, tokenClientId);
                        return new IntrospectionResponse { Active = false };
                    }
                }

                // Build response from validated JWT claims
                var response = new IntrospectionResponse
                {
                    Active = true,
                    TokenType = "access_token",
                    ClientId = tokenClientId,
                    Sub = jwt.Subject,
                    Iss = jwt.Issuer,
                    Aud = jwt.Audiences.FirstOrDefault(),
                    Exp = new DateTimeOffset(jwt.ValidTo).ToUnixTimeSeconds(),
                    Iat = jwt.IssuedAt != DateTime.MinValue ? new DateTimeOffset(jwt.IssuedAt).ToUnixTimeSeconds() : null,
                    Nbf = jwt.ValidFrom != DateTime.MinValue ? new DateTimeOffset(jwt.ValidFrom).ToUnixTimeSeconds() : null,
                    Jti = jwt.Id
                };

                // Add scope
                if (scopes.Any())
                {
                    response.Scope = string.Join(" ", scopes);
                }

                return response;
            }
            catch (SecurityTokenValidationException ex)
            {
                _logger.LogDebug(ex, "Token validation failed during introspection");
                return new IntrospectionResponse { Active = false };
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Error during token introspection");
            return new IntrospectionResponse { Active = false };
        }
    }
}

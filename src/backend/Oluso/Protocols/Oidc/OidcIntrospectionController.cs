using System.IdentityModel.Tokens.Jwt;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Protocols.Validation;

namespace Oluso.Protocols.Oidc;

/// <summary>
/// OAuth 2.0 Token Introspection Endpoint (RFC 7662).
/// Route is configured via OidcEndpointRouteConvention.
/// </summary>

public class OidcIntrospectionController : OidcControllerBase
{
    private readonly IClientAuthenticator _clientAuthenticator;
    private readonly IPersistedGrantStore _grantStore;
    private readonly IResourceStore _resourceStore;
    private readonly ISigningCredentialStore _signingCredentialStore;
    private readonly IIssuerResolver _issuerResolver;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantStore _tenants;
    private readonly IOlusoEventService? _eventService;
    private readonly ILogger<OidcIntrospectionController> _logger;

    public OidcIntrospectionController(
        IClientAuthenticator clientAuthenticator,
        IPersistedGrantStore grantStore,
        IResourceStore resourceStore,
        ISigningCredentialStore signingCredentialStore,
        IIssuerResolver issuerResolver,
        ITenantContext tenantContext,
        ITenantStore tenants,
        ILogger<OidcIntrospectionController> logger,
        IOlusoEventService? eventService = null)
    {
        _tenants = tenants;
        _clientAuthenticator = clientAuthenticator;
        _grantStore = grantStore;
        _resourceStore = resourceStore;
        _signingCredentialStore = signingCredentialStore;
        _issuerResolver = issuerResolver;
        _tenantContext = tenantContext;
        _eventService = eventService;
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

        if (_eventService != null)
        {
            await _eventService.RaiseAsync(new TokenIntrospectedEvent
            {
                TenantId = _tenantContext.TenantId,
                ClientId = callingClient.ClientId,
                TokenType = response.TokenType ?? tokenTypeHint ?? "access_token",
                IsActive = response.Active,
                SubjectId = response.Sub
            }, cancellationToken);
        }

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
            if (!IsAuthorizedToIntrospect(callingClient, grant.ClientId, grant.Type == "reference_token", ReferenceTokenAudiences(grant.Data)))
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
    /// Whether the calling client may introspect a token (RFC 7662 §2.1): the client the token was
    /// issued to, or, for an access token, a resource server for one of the token's audiences
    /// (<see cref="Client.IntrospectionResources"/>). Refresh tokens are only ever their client's.
    /// </summary>
    private static bool IsAuthorizedToIntrospect(
        Client callingClient,
        string tokenClientId,
        bool isAccessToken,
        IReadOnlyCollection<string> audiences)
    {
        if (callingClient.ClientId == tokenClientId)
        {
            return true;
        }

        return isAccessToken
            && callingClient.IntrospectionResources.Count > 0
            && audiences.Any(a => callingClient.IntrospectionResources.Contains(a, StringComparer.Ordinal));
    }

    /// <summary>The audiences saved with a reference token; none for other grants or unreadable data.</summary>
    private static IReadOnlyCollection<string> ReferenceTokenAudiences(string? grantData)
    {
        if (string.IsNullOrEmpty(grantData))
        {
            return Array.Empty<string>();
        }

        try
        {
            using var data = JsonDocument.Parse(grantData);
            if (data.RootElement.ValueKind == JsonValueKind.Object
                && data.RootElement.TryGetProperty("Audiences", out var audiences)
                && audiences.ValueKind == JsonValueKind.Array)
            {
                return audiences.EnumerateArray()
                    .Where(a => a.ValueKind == JsonValueKind.String)
                    .Select(a => a.GetString()!)
                    .ToList();
            }
        }
        catch (JsonException)
        {
        }

        return Array.Empty<string>();
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
                Aud = ReferenceTokenAudiences(grant.Data).FirstOrDefault(),
                Iat = new DateTimeOffset(grant.CreationTime).ToUnixTimeSeconds(),
                Exp = grant.Expiration.HasValue ? new DateTimeOffset(grant.Expiration.Value).ToUnixTimeSeconds() : null
            };

            response.Cnf = Confirmation(
                data?.GetValueOrDefault("DPoPKeyThumbprint")?.ToString(),
                data?.GetValueOrDefault("MtlsCertificateThumbprint")?.ToString());

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

            // Validated as Oluso's own APIs validate it: against the keys of the tenant the token names (and
            // the platform's), and accepting any issuer of ours for that tenant. The unverified claims only pick
            // the keys; the signature check below is what is trusted.
            var unverified = handler.ReadJwtToken(token);
            var tokenTenantId = unverified.Claims.FirstOrDefault(c => c.Type == "tenant_id")?.Value;
            var tokenTenant = string.IsNullOrEmpty(tokenTenantId)
                ? null
                : await _tenants.GetByIdAsync(tokenTenantId, cancellationToken);
            var validationKeys = await _signingCredentialStore.GetValidationKeysAsync(
                tokenTenantId ?? _tenantContext.TenantId, cancellationToken);
            if (validationKeys == null || !validationKeys.Any())
            {
                _logger.LogError("No validation keys available for token introspection");
                return new IntrospectionResponse { Active = false };
            }

            if (!await _issuerResolver.IsOwnIssuerAsync(unverified.Issuer, tokenTenant, cancellationToken))
            {
                _logger.LogDebug("Introspected token has a foreign issuer {Issuer}", unverified.Issuer);
                return new IntrospectionResponse { Active = false };
            }

            // Validate the token with signature verification
            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys = validationKeys.Select(k => k.Key),
                ValidateIssuer = true,
                ValidIssuer = unverified.Issuer,
                ValidateAudience = false, // Audience validation depends on the use case
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(1),
                // A token-exchange ticket or logout token is not an access token: inactive here.
                TypeValidator = Oluso.Protocols.Validation.AccessTokenTypes.Validate
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
                    if (!IsAuthorizedToIntrospect(callingClient, tokenClientId, isAccessToken: true, jwt.Audiences.ToList()))
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
                    Jti = jwt.Id,
                    Cnf = ConfirmationClaim(jwt.Claims.FirstOrDefault(c => c.Type == "cnf")?.Value)
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

    /// <summary>The <c>cnf</c> of a reference token from the key and certificate thumbprints it was bound to.</summary>
    private static Dictionary<string, string>? Confirmation(string? dpopJkt, string? mtlsThumbprint)
    {
        var cnf = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(dpopJkt)) cnf["jkt"] = dpopJkt;
        if (!string.IsNullOrEmpty(mtlsThumbprint)) cnf["x5t#S256"] = mtlsThumbprint;
        return cnf.Count > 0 ? cnf : null;
    }

    /// <summary>The members of a JWT's <c>cnf</c> claim, or null when it has none.</summary>
    private static Dictionary<string, string>? ConfirmationClaim(string? cnfClaimValue)
    {
        if (string.IsNullOrWhiteSpace(cnfClaimValue)) return null;
        using var cnf = JsonDocument.Parse(cnfClaimValue);
        var members = cnf.RootElement.EnumerateObject()
            .Where(m => m.Value.ValueKind == JsonValueKind.String)
            .ToDictionary(m => m.Name, m => m.Value.GetString()!);
        return members.Count > 0 ? members : null;
    }
}

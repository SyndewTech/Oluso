using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Services;

namespace Oluso.Protocols.Services;

/// <summary>
/// Default token service implementation.
/// Supports both JWT and reference token access tokens.
/// Fully tenant-aware for multi-tenant deployments.
/// </summary>
public class TokenService : ITokenService
{
    private readonly ISigningCredentialStore _signingCredentialStore;
    private readonly IPersistedGrantStore _grantStore;
    private readonly IResourceStore _resourceStore;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantSettingsProvider _tenantSettings;
    private readonly IClaimsProviderRegistry _claimsProviderRegistry;
    private readonly IConfiguration _configuration;
    private readonly ILogger<TokenService> _logger;

    public TokenService(
        ISigningCredentialStore signingCredentialStore,
        IPersistedGrantStore grantStore,
        IResourceStore resourceStore,
        ITenantContext tenantContext,
        ITenantSettingsProvider tenantSettings,
        IClaimsProviderRegistry claimsProviderRegistry,
        IConfiguration configuration,
        ILogger<TokenService> logger)
    {
        _signingCredentialStore = signingCredentialStore;
        _grantStore = grantStore;
        _resourceStore = resourceStore;
        _tenantContext = tenantContext;
        _tenantSettings = tenantSettings;
        _claimsProviderRegistry = claimsProviderRegistry;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task<TokenResponse> CreateTokenResponseAsync(
        GrantResult grant,
        TokenRequest request,
        CancellationToken cancellationToken = default)
    {
        var client = request.Client!;
        var scopes = grant.Scopes.ToList();

        // Get tenant-specific token settings
        var tokenSettings = await _tenantSettings.GetTokenSettingsAsync(cancellationToken);

        // Collect claims from all registered providers
        var providerClaims = await GetProviderClaimsAsync(grant, scopes, client.ClientId, cancellationToken);

        // Merge provider claims with grant claims (grant claims take precedence)
        var mergedClaims = new Dictionary<string, object>(providerClaims);
        foreach (var claim in grant.Claims)
        {
            mergedClaims[claim.Key] = claim.Value;
        }

        // Determine audiences from API resources associated with the requested scopes
        var audiences = await GetAudiencesFromScopesAsync(scopes, client.ClientId, cancellationToken);

        // Create token request with DPoP thumbprint from validated proof
        var tokenRequest = new TokenCreationRequest
        {
            SubjectId = grant.SubjectId,
            ClientId = client.ClientId,
            ClientName = client.ClientName,
            Scopes = scopes,
            Claims = mergedClaims,
            Lifetime = client.AccessTokenLifetime,
            IdentityTokenLifetime = client.IdentityTokenLifetime,
            SessionId = grant.SessionId,
            IsReference = client.AccessTokenType == (int)AccessTokenType.Reference,
            IncludeJwtId = client.IncludeJwtId,
            DPoPKeyThumbprint = request.DPoPKeyThumbprint,
            PairWiseSubjectSalt = client.PairWiseSubjectSalt,
            Audiences = audiences
        };

        // Nonce from authorization code
        if (grant.Claims.TryGetValue(OidcConstants.StandardClaims.Nonce, out var nonce))
        {
            tokenRequest.Nonce = nonce?.ToString();
        }

        var response = new TokenResponse
        {
            ExpiresIn = client.AccessTokenLifetime,
            TokenType = tokenRequest.DPoPKeyThumbprint != null ? "DPoP" : "Bearer",
            Scope = string.Join(" ", scopes),
            AccessToken = await CreateAccessTokenAsync(tokenRequest, cancellationToken)
        };

        // Create ID token if openid scope is requested
        if (scopes.Contains(OidcConstants.Scopes.OpenId) && !string.IsNullOrEmpty(grant.SubjectId))
        {
            var accessTokenHash = ComputeHash(response.AccessToken);
            response.IdToken = await CreateIdTokenAsync(tokenRequest, accessTokenHash, null, cancellationToken);
        }

        // Handle refresh token based on grant type and client settings
        var shouldIssueRefreshToken = ShouldIssueRefreshToken(grant, scopes, client, request.GrantType);
        if (shouldIssueRefreshToken)
        {
            response.RefreshToken = await CreateRefreshTokenAsync(tokenRequest, client, cancellationToken);

            // For refresh token grant with OneTimeOnly, remove the consumed token
            if (grant.CustomData.TryGetValue("rotate_refresh_token", out var rotate) && rotate is true)
            {
                if (grant.CustomData.TryGetValue("original_refresh_token", out var originalToken) && originalToken is string tokenKey)
                {
                    await _grantStore.RemoveAsync(tokenKey, cancellationToken);
                }
            }
        }

        // Token exchange specific
        if (grant.CustomData.TryGetValue("issued_token_type", out var issuedTokenType))
        {
            response.IssuedTokenType = issuedTokenType?.ToString();
        }

        return response;
    }

    private static bool ShouldIssueRefreshToken(GrantResult grant, List<string> scopes, ValidatedClient client, string? grantType)
    {
        // Must have offline_access scope and client must allow it
        if (!scopes.Contains(OidcConstants.Scopes.OfflineAccess) || !client.AllowOfflineAccess)
        {
            return false;
        }

        // For refresh_token grant, check if we should rotate
        if (grantType == OidcConstants.GrantTypes.RefreshToken)
        {
            // Always issue new token for OneTimeOnly
            if (grant.CustomData.TryGetValue("rotate_refresh_token", out var rotate) && rotate is true)
            {
                return true;
            }

            // For ReUse, don't issue a new token (client keeps using the existing one)
            return false;
        }

        // For other grants (authorization_code, etc.), issue refresh token
        return true;
    }

    public async Task<string> CreateAccessTokenAsync(
        TokenCreationRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.IsReference)
        {
            // Reference token - store data and return opaque handle
            return await CreateReferenceTokenAsync(request, cancellationToken);
        }

        // JWT access token
        return await CreateJwtAccessTokenAsync(request, cancellationToken);
    }

    private async Task<string> CreateJwtAccessTokenAsync(
        TokenCreationRequest request,
        CancellationToken cancellationToken)
    {
        var signingCredentials = await _signingCredentialStore.GetSigningCredentialsAsync(cancellationToken);
        if (signingCredentials == null)
        {
            throw new InvalidOperationException("No signing credentials configured");
        }

        var issuer = GetIssuer();
        var now = DateTime.UtcNow;

        var claims = new List<Claim>
        {
            new(OidcConstants.StandardClaims.ClientId, request.ClientId),
            new(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };

        // Add JWT ID if client requires it
        if (request.IncludeJwtId)
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()));
        }

        // Add subject if present (not for client_credentials)
        if (!string.IsNullOrEmpty(request.SubjectId))
        {
            claims.Add(new Claim(JwtRegisteredClaimNames.Sub, request.SubjectId));
        }

        // Add scopes
        foreach (var scope in request.Scopes)
        {
            claims.Add(new Claim("scope", scope));
        }

        // Add session ID
        if (!string.IsNullOrEmpty(request.SessionId))
        {
            claims.Add(new Claim(OidcConstants.StandardClaims.SessionId, request.SessionId));
        }

        // Add tenant ID
        if (_tenantContext.HasTenant)
        {
            claims.Add(new Claim(OidcConstants.StandardClaims.TenantId, _tenantContext.TenantId!));
        }

        // Add DPoP confirmation
        if (!string.IsNullOrEmpty(request.DPoPKeyThumbprint))
        {
            var cnf = JsonSerializer.Serialize(new { jkt = request.DPoPKeyThumbprint });
            claims.Add(new Claim("cnf", cnf, JsonClaimValueTypes.Json));
        }

        // Add additional claims, skipping any that are already present to avoid duplicates
        var existingClaimTypes = new HashSet<string>(claims.Select(c => c.Type), StringComparer.OrdinalIgnoreCase);
        foreach (var claim in request.Claims)
        {
            if (existingClaimTypes.Contains(claim.Key))
            {
                continue;
            }
            var value = claim.Value is string s ? s : JsonSerializer.Serialize(claim.Value);
            claims.Add(new Claim(claim.Key, value));
        }

        // Determine audiences - default to client_id if no audiences specified
        var audiences = request.Audiences?.ToList() ?? new List<string> { request.ClientId };

        var token = new JwtSecurityToken(
            issuer: issuer,
            claims: claims,
            notBefore: now,
            expires: now.AddSeconds(request.Lifetime),
            signingCredentials: signingCredentials);

        // Add audiences - use array if multiple, single value otherwise
        if (audiences.Count == 1)
        {
            token.Payload["aud"] = audiences[0];
        }
        else if (audiences.Count > 1)
        {
            token.Payload["aud"] = audiences;
        }

        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(token);
    }

    private async Task<string> CreateReferenceTokenAsync(
        TokenCreationRequest request,
        CancellationToken cancellationToken)
    {
        var handle = GenerateHandle();

        var data = JsonSerializer.Serialize(new
        {
            request.SubjectId,
            request.ClientId,
            request.Scopes,
            request.Claims,
            request.SessionId,
            request.DPoPKeyThumbprint,
            CreatedAt = DateTime.UtcNow
        });

        var grant = new PersistedGrant
        {
            Key = handle,
            Type = "reference_token",
            SubjectId = request.SubjectId,
            SessionId = request.SessionId,
            ClientId = request.ClientId,
            CreationTime = DateTime.UtcNow,
            Expiration = DateTime.UtcNow.AddSeconds(request.Lifetime),
            Data = data
        };

        if (_tenantContext.HasTenant)
        {
            grant.TenantId = _tenantContext.TenantId;
        }

        await _grantStore.StoreAsync(grant, cancellationToken);

        return handle;
    }

    public async Task<string> CreateIdTokenAsync(
        TokenCreationRequest request,
        string? accessTokenHash = null,
        string? codeHash = null,
        CancellationToken cancellationToken = default)
    {
        var signingCredentials = await _signingCredentialStore.GetSigningCredentialsAsync(cancellationToken);
        if (signingCredentials == null)
        {
            throw new InvalidOperationException("No signing credentials configured");
        }

        var issuer = GetIssuer();
        var now = DateTime.UtcNow;

        // Compute subject ID - use pairwise if salt is configured
        var subjectId = ComputeSubjectId(
            request.SubjectId ?? throw new ArgumentNullException(nameof(request.SubjectId)),
            request.ClientId,
            request.PairWiseSubjectSalt);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, subjectId),
            new(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };

        // Add nonce
        if (!string.IsNullOrEmpty(request.Nonce))
        {
            claims.Add(new Claim(OidcConstants.StandardClaims.Nonce, request.Nonce));
        }

        // Add auth_time
        if (request.AuthTime.HasValue)
        {
            claims.Add(new Claim(OidcConstants.StandardClaims.AuthTime,
                new DateTimeOffset(request.AuthTime.Value).ToUnixTimeSeconds().ToString(),
                ClaimValueTypes.Integer64));
        }

        // Add session ID
        if (!string.IsNullOrEmpty(request.SessionId))
        {
            claims.Add(new Claim(OidcConstants.StandardClaims.SessionId, request.SessionId));
        }

        // Add amr (authentication methods)
        if (request.Amr?.Any() == true)
        {
            foreach (var amr in request.Amr)
            {
                claims.Add(new Claim(OidcConstants.StandardClaims.Amr, amr));
            }
        }

        // Add acr (authentication context)
        if (!string.IsNullOrEmpty(request.Acr))
        {
            claims.Add(new Claim(OidcConstants.StandardClaims.Acr, request.Acr));
        }

        // Add at_hash
        if (!string.IsNullOrEmpty(accessTokenHash))
        {
            claims.Add(new Claim(OidcConstants.StandardClaims.AtHash, accessTokenHash));
        }

        // Add c_hash
        if (!string.IsNullOrEmpty(codeHash))
        {
            claims.Add(new Claim(OidcConstants.StandardClaims.CHash, codeHash));
        }

        // Add tenant ID
        if (_tenantContext.HasTenant)
        {
            claims.Add(new Claim(OidcConstants.StandardClaims.TenantId, _tenantContext.TenantId!));
        }

        // Add user claims (profile, email, etc.) if scopes allow
        // Skip claims that are already added (sub, nonce, iat) to avoid duplicates
        var existingClaimTypes = new HashSet<string>(claims.Select(c => c.Type), StringComparer.OrdinalIgnoreCase);
        foreach (var claim in request.Claims)
        {
            // Skip claims we've already added to avoid duplicates (which cause array values in JWT)
            if (existingClaimTypes.Contains(claim.Key))
            {
                continue;
            }
            var value = claim.Value is string s ? s : JsonSerializer.Serialize(claim.Value);
            claims.Add(new Claim(claim.Key, value));
        }

        // ID token lifetime: use client's setting if available, otherwise tenant settings
        // Client's IdentityTokenLifetime takes precedence over tenant default
        var idTokenLifetime = request.IdentityTokenLifetime > 0
            ? request.IdentityTokenLifetime
            : 300; // Default 5 minutes if not configured

        // If client lifetime not set, try tenant settings
        if (request.IdentityTokenLifetime <= 0)
        {
            var tokenSettings = await _tenantSettings.GetTokenSettingsAsync(cancellationToken);
            if (tokenSettings.DefaultIdentityTokenLifetime > 0)
            {
                idTokenLifetime = tokenSettings.DefaultIdentityTokenLifetime;
            }
        }

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: request.ClientId,
            claims: claims,
            notBefore: now,
            expires: now.AddSeconds(idTokenLifetime),
            signingCredentials: signingCredentials);

        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(token);
    }

    public async Task<string> CreateRefreshTokenAsync(
        TokenCreationRequest request,
        CancellationToken cancellationToken = default)
    {
        // Get lifetime from tenant settings
        var tokenSettings = await _tenantSettings.GetTokenSettingsAsync(cancellationToken);
        var lifetime = tokenSettings.DefaultRefreshTokenLifetime;

        return await CreateRefreshTokenInternalAsync(request, lifetime, cancellationToken);
    }

    private async Task<string> CreateRefreshTokenAsync(
        TokenCreationRequest request,
        ValidatedClient client,
        CancellationToken cancellationToken)
    {
        // Use client's absolute refresh token lifetime
        var absoluteLifetime = client.AbsoluteRefreshTokenLifetime;

        // For sliding expiration, use the sliding lifetime for initial expiration
        var refreshTokenExpiration = (TokenExpiration)client.RefreshTokenExpiration;
        var initialLifetime = refreshTokenExpiration == TokenExpiration.Sliding
            ? Math.Min(client.SlidingRefreshTokenLifetime, absoluteLifetime)
            : absoluteLifetime;

        return await CreateRefreshTokenInternalAsync(request, initialLifetime, cancellationToken);
    }

    private async Task<string> CreateRefreshTokenInternalAsync(
        TokenCreationRequest request,
        int lifetime,
        CancellationToken cancellationToken)
    {
        var handle = GenerateHandle();
        var now = DateTime.UtcNow;

        var data = JsonSerializer.Serialize(new RefreshTokenData
        {
            CreatedAt = now,
            Scopes = request.Scopes.ToList(),
            Claims = request.Claims.ToDictionary(c => c.Key, c => c.Value?.ToString() ?? string.Empty),
            DPoPJkt = request.DPoPKeyThumbprint
        });

        var grant = new PersistedGrant
        {
            Key = handle,
            Type = "refresh_token",
            SubjectId = request.SubjectId,
            SessionId = request.SessionId,
            ClientId = request.ClientId,
            CreationTime = now,
            Expiration = now.AddSeconds(lifetime),
            Data = data
        };

        if (_tenantContext.HasTenant)
        {
            grant.TenantId = _tenantContext.TenantId;
        }

        await _grantStore.StoreAsync(grant, cancellationToken);

        return handle;
    }

    private async Task<string> GetIssuerAsync(CancellationToken cancellationToken = default)
    {
        // Priority order for issuer:
        // 1. Tenant's IssuerUri from token settings (explicit override)
        // 2. Tenant's CustomDomain (e.g., auth.customer.com -> https://auth.customer.com)
        // 3. Server's configured IssuerUri
        if (_tenantContext.HasTenant)
        {
            var tokenSettings = await _tenantSettings.GetTokenSettingsAsync(cancellationToken);
            if (!string.IsNullOrEmpty(tokenSettings.IssuerUri))
            {
                return tokenSettings.IssuerUri.TrimEnd('/');
            }

            // Use tenant's custom domain if configured
            var tenant = _tenantContext.Tenant;
            if (!string.IsNullOrEmpty(tenant?.CustomDomain))
            {
                // Construct issuer from custom domain (assume https)
                return $"https://{tenant.CustomDomain.TrimEnd('/')}";
            }
        }

        return GetIssuer();
    }

    // Synchronous version - only uses server configuration
    // For tenant-aware issuer resolution, use GetIssuerAsync
    private string GetIssuer()
    {
        var baseUrl = _configuration["Oluso:IssuerUri"]
            ?? _configuration["IdentityServer:IssuerUri"]
            ?? throw new InvalidOperationException("IssuerUri not configured");

        return baseUrl.TrimEnd('/');
    }

    /// <summary>
    /// Gets audiences (aud claim) based on API resources associated with the requested scopes.
    /// If scopes have associated API resources, their names become audiences.
    /// If no API resources are found, defaults to the client ID.
    /// </summary>
    private async Task<ICollection<string>> GetAudiencesFromScopesAsync(
        IEnumerable<string> scopes,
        string clientId,
        CancellationToken cancellationToken)
    {
        // Filter to only API scopes (not identity scopes like openid, profile, etc.)
        var apiScopes = scopes.Where(s =>
            s != OidcConstants.Scopes.OpenId &&
            s != OidcConstants.Scopes.Profile &&
            s != OidcConstants.Scopes.Email &&
            s != OidcConstants.Scopes.Address &&
            s != OidcConstants.Scopes.Phone &&
            s != OidcConstants.Scopes.OfflineAccess).ToList();

        if (apiScopes.Count == 0)
        {
            // Only identity scopes requested, use client ID as audience
            return new List<string> { clientId };
        }

        // Find API resources that contain these scopes
        var apiResources = await _resourceStore.FindApiResourcesByScopeNameAsync(apiScopes, cancellationToken);
        var resourceNames = apiResources.Select(r => r.Name).Distinct().ToList();

        if (resourceNames.Count == 0)
        {
            // Scopes exist but no API resources defined, use client ID as audience
            return new List<string> { clientId };
        }

        return resourceNames;
    }

    private async Task<IDictionary<string, object>> GetProviderClaimsAsync(
        GrantResult grant,
        IEnumerable<string> scopes,
        string? clientId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(grant.SubjectId))
        {
            // No subject (e.g., client_credentials), no user claims to collect
            return new Dictionary<string, object>();
        }

        var context = new ClaimsProviderContext
        {
            SubjectId = grant.SubjectId,
            TenantId = _tenantContext.TenantId,
            ClientId = clientId,
            Scopes = scopes,
            SessionId = grant.SessionId,
            AdditionalData = grant.CustomData
        };

        try
        {
            return await _claimsProviderRegistry.GetAllClaimsAsync(context, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error collecting claims from providers for user {SubjectId}", grant.SubjectId);
            return new Dictionary<string, object>();
        }
    }

    private static string GenerateHandle()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    private static string ComputeHash(string value)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.ASCII.GetBytes(value);
        var hash = sha256.ComputeHash(bytes);
        // Take left-most half of hash (for at_hash, c_hash per OIDC spec)
        var halfHash = hash.Take(hash.Length / 2).ToArray();
        return Convert.ToBase64String(halfHash)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    /// <summary>
    /// Computes the subject identifier based on subject type.
    /// For pairwise subject type, creates a unique sub per client using HMAC-SHA256.
    /// See OpenID Connect Core 1.0 Section 8 (Subject Identifier Types).
    /// </summary>
    private static string ComputeSubjectId(string subjectId, string clientId, string? pairWiseSalt)
    {
        if (string.IsNullOrEmpty(pairWiseSalt))
        {
            // Public subject type - same sub for all clients
            return subjectId;
        }

        // Pairwise subject type - unique sub per client
        // Formula: Base64URL(HMAC-SHA256(salt, clientId + subjectId))
        // Using HMAC for security (protects against salt extraction attacks)
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(pairWiseSalt));
        var input = Encoding.UTF8.GetBytes($"{clientId}{subjectId}");
        var hash = hmac.ComputeHash(input);

        return Convert.ToBase64String(hash)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}

/// <summary>
/// Data stored with refresh tokens
/// </summary>
public class RefreshTokenData
{
    public DateTime CreatedAt { get; set; }
    public ICollection<string> Scopes { get; set; } = new List<string>();
    public IDictionary<string, string>? Claims { get; set; }
    public string? AccessTokenHash { get; set; }

    /// <summary>
    /// DPoP key thumbprint bound to this refresh token (RFC 9449)
    /// </summary>
    public string? DPoPJkt { get; set; }
}

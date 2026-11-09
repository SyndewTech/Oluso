using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Oluso.Core;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Authorization;
using Oluso.Core.Services;
using Oluso.Protocols.Grants;

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
    private readonly ITenantSettingsProvider _tenantSettings;
    private readonly IClaimsProviderRegistry _claimsProviderRegistry;
    private readonly IIssuerResolver _issuerResolver;
    private readonly IIdentityScopeClaimResolver _identityScopeClaims;
    private readonly IAudienceResolver _audiences;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantStore _tenants;
    private readonly ILogger<TokenService> _logger;

    public TokenService(
        ISigningCredentialStore signingCredentialStore,
        IPersistedGrantStore grantStore,
        ITenantSettingsProvider tenantSettings,
        IClaimsProviderRegistry claimsProviderRegistry,
        IIssuerResolver issuerResolver,
        IIdentityScopeClaimResolver identityScopeClaims,
        IAudienceResolver audiences,
        ITenantContext tenantContext,
        ITenantStore tenants,
        ILogger<TokenService> logger)
    {
        _audiences = audiences;
        _tenantContext = tenantContext;
        _tenants = tenants;
        _signingCredentialStore = signingCredentialStore;
        _grantStore = grantStore;
        _tenantSettings = tenantSettings;
        _claimsProviderRegistry = claimsProviderRegistry;
        _issuerResolver = issuerResolver;
        _identityScopeClaims = identityScopeClaims;
        _logger = logger;
    }

    public async Task<TokenResponse> CreateTokenResponseAsync(
        GrantResult grant,
        TokenRequest request,
        CancellationToken cancellationToken = default)
    {
        var client = request.Client!;
        var scopes = grant.Scopes.ToList();

        // A token-exchange ticket is not an access token: no provider claims, no refresh or ID token.
        if (grant.CustomData.TryGetValue(ExchangeTickets.CustomDataKey, out var ticketData) && ticketData is ExchangeTicketDescriptor ticket)
        {
            return await CreateTicketResponseAsync(grant, client, scopes, ticket, cancellationToken);
        }

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

        // Authorization claims (super_admin, is_org_admin, org_ids, org_roles,
        // allowed_tenants) come from DefaultProfileService.AddAuthorizationClaimsAsync
        // alongside the OIDC standard claims — single source of truth shared with
        // OlusoClaimsPrincipalFactory.

        // The grant's resources (code and refresh grants keep them), else this request's.
        var resources = grant.Resources ?? request.Resource;
        var audiences = await _audiences.ResolveAsync(resources, scopes, client.ClientId, client.AllowedResources, cancellationToken);

        // Token exchange naming an audience (RFC 8693 §2.1, checked by the grant handler): the token is for it.
        if (grant.CustomData.TryGetValue(ExchangeTickets.AudienceCustomDataKey, out var exchangeAudience)
            && exchangeAudience is string audience && !string.IsNullOrEmpty(audience))
        {
            audiences = new List<string> { audience };
        }

        // Create token request with DPoP thumbprint from validated proof
        var tokenRequest = new TokenCreationRequest
        {
            SubjectId = grant.SubjectId,
            ClientId = client.ClientId,
            ClientName = client.ClientName,
            Scopes = scopes,
            Claims = mergedClaims,
            Permissions = grant.Permissions.ToList(),
            Lifetime = client.AccessTokenLifetime,
            IdentityTokenLifetime = client.IdentityTokenLifetime,
            SessionId = grant.SessionId,
            IsReference = client.AccessTokenType == (int)AccessTokenType.Reference,
            IncludeJwtId = client.IncludeJwtId,
            AlwaysIncludeUserClaimsInIdToken = client.AlwaysIncludeUserClaimsInIdToken,
            DPoPKeyThumbprint = request.DPoPKeyThumbprint,
            MtlsCertificateThumbprint = request.MtlsCertificateThumbprint,
            PairWiseSubjectSalt = client.PairWiseSubjectSalt,
            Audiences = audiences,
            GrantedResources = grant.GrantedResources ?? request.Resource.ToList(),
            GrantedScopes = grant.GrantedScopes?.ToList()
        };

        // How the user signed in, carried by the authorization code
        if (grant.Amr?.Count > 0)
        {
            tokenRequest.Amr = grant.Amr.ToList();
        }
        tokenRequest.Acr ??= grant.Acr;

        // Nonce from authorization code
        if (grant.Claims.TryGetValue(OidcConstants.StandardClaims.Nonce, out var nonce))
        {
            tokenRequest.Nonce = nonce?.ToString();
        }

        // Include auth_time in ID token when max_age was requested (OIDC Core §2 REQUIRED)
        // or when auth_time is available (OPTIONAL per spec, but improves compatibility)
        var maxAgeRequested = grant.Claims.ContainsKey("max_age_requested");
        if (grant.Claims.TryGetValue(OidcConstants.StandardClaims.AuthTime, out var authTimeValue)
            && long.TryParse(authTimeValue?.ToString(), out var epoch))
        {
            if (maxAgeRequested)
            {
                // REQUIRED per spec — max_age was in the original authorize request
                tokenRequest.AuthTime = DateTimeOffset.FromUnixTimeSeconds(epoch).UtcDateTime;
            }
            // When max_age is not requested, auth_time stays null and won't appear in the ID token.
            // To always include it (like Auth0/Keycloak), remove the maxAgeRequested check above.
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
        var protocolSettings = await _tenantSettings.GetProtocolSettingsAsync(cancellationToken) ?? TenantProtocolSettings.Default;
        // offline_access is the grant's: a refresh asking for fewer scopes still rotates the refresh token.
        var shouldIssueRefreshToken = ShouldIssueRefreshToken(grant, grant.GrantedScopes?.ToList() ?? scopes, client, request.GrantType, protocolSettings);
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

    /// <summary>
    /// Signs a one-time ticket (<see cref="OidcConstants.TokenTypes.Ticket"/>): typ <c>oluso-ticket+jwt</c>,
    /// claims iss, sub, aud (the audience client), azp (the requesting client), scope, jti (its single-use
    /// record), iat, exp, tenant_id. Never a <c>cnf</c>: the audience redeems it server-side.
    /// </summary>
    private async Task<TokenResponse> CreateTicketResponseAsync(
        GrantResult grant, ValidatedClient client, List<string> scopes, ExchangeTicketDescriptor ticket, CancellationToken cancellationToken)
    {
        var signerRequest = new TokenCreationRequest
        {
            SubjectId = grant.SubjectId,
            ClientId = client.ClientId,
            Claims = grant.Claims,
        };
        var (signingCredentials, issuer) = await SignerForAsync(signerRequest, cancellationToken);
        var now = DateTime.UtcNow;

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, grant.SubjectId!),
            new(JwtRegisteredClaimNames.Azp, client.ClientId),
            new("scope", string.Join(" ", scopes)),
            new(JwtRegisteredClaimNames.Jti, ticket.Jti),
            new(JwtRegisteredClaimNames.Iat, new DateTimeOffset(now).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
        };
        if (grant.Claims.TryGetValue(OidcConstants.StandardClaims.TenantId, out var tenantId) && tenantId?.ToString() is { Length: > 0 } tenant)
        {
            claims.Add(new Claim(OidcConstants.StandardClaims.TenantId, tenant));
        }

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: ticket.Audience,
            claims: claims,
            notBefore: now,
            expires: now.AddSeconds(ticket.LifetimeSeconds),
            signingCredentials: signingCredentials);
        token.Header["typ"] = OidcConstants.JwtTypes.Ticket;

        return new TokenResponse
        {
            AccessToken = new JwtSecurityTokenHandler().WriteToken(token),
            TokenType = "N_A",
            ExpiresIn = ticket.LifetimeSeconds,
            Scope = string.Join(" ", scopes),
            IssuedTokenType = OidcConstants.TokenTypes.Ticket,
        };
    }

    /// <summary>
    /// Whether this response carries a refresh token: <c>offline_access</c> granted to a client allowed offline
    /// access, or a refresh-implying scope (<see cref="TenantProtocolSettings.RefreshImplyingScopes"/>, the Matrix
    /// client API scope by default) granted to a client allowed the <c>refresh_token</c> grant. Matrix clients never
    /// ask for <c>offline_access</c> yet must be able to refresh (Matrix spec, OAuth 2.0 API).
    /// </summary>
    private static bool ShouldIssueRefreshToken(GrantResult grant, List<string> scopes, ValidatedClient client, string? grantType,
        TenantProtocolSettings? protocolSettings = null)
    {
        var offline = scopes.Contains(OidcConstants.Scopes.OfflineAccess) && client.AllowOfflineAccess;
        if (!offline && !HasRefreshImplyingScope(scopes, client, protocolSettings ?? TenantProtocolSettings.Default))
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

    private static bool HasRefreshImplyingScope(List<string> scopes, ValidatedClient client, TenantProtocolSettings protocolSettings)
    {
        if (!client.AllowedGrantTypes.Contains(OidcConstants.GrantTypes.RefreshToken))
        {
            return false;
        }

        // A tenant that limits its grant types must allow refresh for the token to be usable.
        if (protocolSettings.AllowedGrantTypes?.Count > 0
            && !protocolSettings.AllowedGrantTypes.Contains(OidcConstants.GrantTypes.RefreshToken))
        {
            return false;
        }

        var implying = protocolSettings.EffectiveRefreshImplyingScopes;
        return implying.Count > 0 && scopes.Any(s => implying.Contains(s, StringComparer.Ordinal));
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
        var (signingCredentials, issuer) = await SignerForAsync(request, cancellationToken);
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

        // Tenant ID comes from claims only — the user's tenant (via profile service) for user tokens,
        // or the client's registered tenant (via grant handler) for client_credentials.
        // Never sourced from _tenantContext (the HTTP request routing context) — that would allow
        // tokens to be stamped with whatever tenant URL the request arrived on, bypassing the
        // profile service's tenant membership validation. If no tenant_id claim exists, the token
        // is platform-scoped intentionally.

        // Add proof-of-possession confirmation (cnf claim)
        var cnfMembers = new Dictionary<string, string>();
        if (!string.IsNullOrEmpty(request.DPoPKeyThumbprint))
            cnfMembers["jkt"] = request.DPoPKeyThumbprint;
        if (!string.IsNullOrEmpty(request.MtlsCertificateThumbprint))
            cnfMembers["x5t#S256"] = request.MtlsCertificateThumbprint;
        if (cnfMembers.Count > 0)
            claims.Add(new Claim("cnf", JsonSerializer.Serialize(cnfMembers), JsonClaimValueTypes.Json));

        // Add additional claims, skipping any that are already present to avoid duplicates
        // Also filter out ASP.NET Identity SOAP-style claims — we already emit proper short-name equivalents
        var existingClaimTypes = new HashSet<string>(claims.Select(c => c.Type), StringComparer.OrdinalIgnoreCase);
        foreach (var claim in request.Claims)
        {
            if (existingClaimTypes.Contains(claim.Key))
            {
                continue;
            }

            // Skip internal claims that should not appear in tokens:
            // - ASP.NET Identity SOAP-style claims (duplicated by short-name OIDC equivalents)
            // - "permission" (singular) is a reserved RoleClaim storage type; only "permissions" (plural JSON array) goes in the token
            // - "permissions" (plural) from ClaimsPrincipalFactory is skipped here — the dedicated Permissions path below handles it
            // - "max_age_requested" is an internal flag for auth_time inclusion logic
            if (claim.Key.StartsWith("http://schemas.xmlsoap.org/", StringComparison.Ordinal) ||
                claim.Key.StartsWith("http://schemas.microsoft.com/", StringComparison.Ordinal) ||
                claim.Key.StartsWith("AspNet.Identity.", StringComparison.Ordinal) ||
                claim.Key == OlusoRole.PermissionClaimType ||
                claim.Key == "permissions" ||
                claim.Key == "max_age_requested")
            {
                continue;
            }

            if (claim.Value is string s)
            {
                claims.Add(new Claim(claim.Key, s));
            }
            else if (claim.Value is IEnumerable<string> list)
            {
                var json = JsonSerializer.Serialize(list);
                claims.Add(new Claim(claim.Key, json, JsonClaimValueTypes.JsonArray));
            }
            else
            {
                var json = JsonSerializer.Serialize(claim.Value);
                claims.Add(new Claim(claim.Key, json, JsonClaimValueTypes.Json));
            }
        }

        // Add permissions as a dedicated claim when the "permissions" scope is requested
        // Grant handlers are responsible for populating permissions on the token request
        if (request.Scopes.Contains(OidcConstants.Scopes.Permissions) && request.Permissions.Count > 0)
        {
            var distinctPermissions = request.Permissions.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var permissionsJson = JsonSerializer.Serialize(distinctPermissions);
            claims.Add(new Claim("permissions", permissionsJson, JsonClaimValueTypes.JsonArray));
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
            // The aud a JWT would carry: introspection lets the resource servers for these see the token.
            Audiences = request.Audiences ?? new List<string> { request.ClientId },
            request.Claims,
            request.SessionId,
            request.DPoPKeyThumbprint,
            request.MtlsCertificateThumbprint,
            CreatedAt = DateTime.UtcNow
        });

        // Derive tenant from authenticated request claims — never from _tenantContext.
        // The token endpoint (/connect/token) is an OIDC protocol path, not an API path,
        // so the middleware resolves tenant from URL/header without JWT validation.
        // Using _tenantContext here would let a client store grants under arbitrary tenants.
        var grantTenantId = request.Claims.TryGetValue("tenant_id", out var refTid)
            ? refTid?.ToString() : null;

        var grant = new PersistedGrant
        {
            Key = handle,
            Type = "reference_token",
            SubjectId = request.SubjectId,
            SessionId = request.SessionId,
            ClientId = request.ClientId,
            CreationTime = DateTime.UtcNow,
            Expiration = DateTime.UtcNow.AddSeconds(request.Lifetime),
            Data = data,
            TenantId = grantTenantId
        };

        await _grantStore.StoreAsync(grant, cancellationToken);

        return handle;
    }

    /// <summary>
    /// The key a token is signed with and the issuer it names. A token's key must belong to the tenant it is
    /// for (its <c>tenant_id</c>) or to the platform, whose keys every tenant's tokens are validated against.
    /// Normally that is the tenant the request was routed to. A platform-scoped token requested through a
    /// tenant's routing (a platform client using a tenant's host) is signed and issued by the platform, so it
    /// validates as the platform token it claims to be; a token for one tenant is never signed by another's key.
    /// </summary>
    private async Task<(SigningCredentials Credentials, string Issuer)> SignerForAsync(
        TokenCreationRequest request, CancellationToken cancellationToken)
    {
        var routedTenant = _tenantContext.TenantId ?? PlatformConstants.TenantId;
        var tokenTenant = request.Claims.TryGetValue(OidcConstants.StandardClaims.TenantId, out var claimed)
            ? claimed?.ToString()
            : null;

        if (string.IsNullOrEmpty(tokenTenant) || tokenTenant == routedTenant || routedTenant == PlatformConstants.TenantId)
        {
            var credentials = await _signingCredentialStore.GetSigningCredentialsAsync(cancellationToken)
                ?? throw new InvalidOperationException("No signing credentials configured");
            return (credentials, await _issuerResolver.GetIssuerAsync(cancellationToken));
        }

        if (tokenTenant != PlatformConstants.TenantId)
        {
            throw new TokenTenantMismatchException(tokenTenant, routedTenant);
        }

        var platform = await _tenants.GetByIdAsync(PlatformConstants.TenantId, cancellationToken)
            ?? throw new InvalidOperationException("The platform tenant is not configured.");
        var platformCredentials = await _signingCredentialStore.GetSigningCredentialsAsync(PlatformConstants.TenantId, cancellationToken)
            ?? throw new InvalidOperationException("No signing credentials configured for the platform");
        return (platformCredentials, await _issuerResolver.GetIssuerForTenantAsync(platform, cancellationToken));
    }

    public async Task<string> CreateIdTokenAsync(
        TokenCreationRequest request,
        string? accessTokenHash = null,
        string? codeHash = null,
        CancellationToken cancellationToken = default)
    {
        var (signingCredentials, issuer) = await SignerForAsync(request, cancellationToken);
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

        // Remembered (persistent cookie) sign-ins are marked so the app can keep its
        // tokens across browser restarts; session-only sign-ins carry nothing.
        if (request.Claims.TryGetValue(Oluso.Core.Authorization.ReservedClaimKeys.RememberMe, out var remembered)
            && remembered is string rememberedText && rememberedText == "true")
        {
            claims.Add(new Claim(Oluso.Core.Authorization.ReservedClaimKeys.RememberMe, "true"));
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

        // Tenant ID comes from claims (same as access token)

        // Add the identity claims the granted identity scopes release — and only
        // those. The scope set here is already "requested ∩ client AllowedScopes"
        // (ScopeValidator ran at request validation), so an ID token can only
        // carry what the client was actually granted.
        //
        // Allowlist rather than denylist: the ID token asserts who authenticated,
        // so authorization attributes (role, permissions, entitlements, tenant
        // and org context) stay out by construction instead of needing a new
        // exclusion every time a claim is added. Those ride the access token,
        // per RFC 9068 §2.2.3.1, and remain available at /userinfo.
        // OIDC Core §5.4: when an access token is issued, the identity claims for
        // profile/email/phone are fetched from /userinfo, and the ID token carries
        // them only if the client asks. A response with no access token (a bare
        // id_token flow) has no userinfo round trip available at that point, so
        // the claims go in regardless — an empty at_hash is that signal.
        var accessTokenIssued = !string.IsNullOrEmpty(accessTokenHash);
        var includeIdentityClaims =
            request.AlwaysIncludeUserClaimsInIdToken || !accessTokenIssued;

        var identityClaimTypes = includeIdentityClaims
            ? await _identityScopeClaims.ResolveAsync(request.Scopes, cancellationToken)
            : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // tenant_id is the one authorization-adjacent claim kept, and it is kept
        // unconditionally: in a multi-tenant IdP it says which tenant the user
        // authenticated into, and TokenTenantIntegrityTests asserts the two tokens
        // agree on it. Everything else authorization-shaped stays out.
        identityClaimTypes.Add(ReservedClaimKeys.TenantId);

        var existingClaimTypes = new HashSet<string>(claims.Select(c => c.Type), StringComparer.OrdinalIgnoreCase);
        foreach (var claim in request.Claims)
        {
            if (existingClaimTypes.Contains(claim.Key))
            {
                continue;
            }

            if (!identityClaimTypes.Contains(claim.Key))
            {
                continue;
            }

            if (claim.Value is string s)
            {
                claims.Add(new Claim(claim.Key, s));
            }
            else if (claim.Value is IEnumerable<string> list)
            {
                var json = JsonSerializer.Serialize(list);
                claims.Add(new Claim(claim.Key, json, JsonClaimValueTypes.JsonArray));
            }
            else
            {
                var json = JsonSerializer.Serialize(claim.Value);
                claims.Add(new Claim(claim.Key, json, JsonClaimValueTypes.Json));
            }
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

        var claims = RefreshTokenData.StoreClaims(request.Claims);

        // Store auth_time in claims so it round-trips through refresh token data
        if (request.AuthTime.HasValue && !claims.ContainsKey(OidcConstants.StandardClaims.AuthTime))
        {
            claims[OidcConstants.StandardClaims.AuthTime] = JsonSerializer.SerializeToElement(
                new DateTimeOffset(request.AuthTime.Value).ToUnixTimeSeconds().ToString());
        }

        var data = JsonSerializer.Serialize(new RefreshTokenData
        {
            CreatedAt = now,
            Scopes = (request.GrantedScopes ?? request.Scopes).ToList(),
            Claims = claims,
            ClaimsFormat = RefreshTokenData.JsonClaimsFormat,
            DPoPJkt = request.DPoPKeyThumbprint,
            Resources = request.GrantedResources?.ToList(),
            Amr = request.Amr is { Count: > 0 } amr ? amr.ToList() : null,
            Acr = request.Acr
        });

        // Same pattern as reference tokens — derive tenant from claims, not routing context.
        var refreshTenantId = request.Claims.TryGetValue("tenant_id", out var rtTid)
            ? rtTid?.ToString() : null;

        var grant = new PersistedGrant
        {
            Key = handle,
            Type = "refresh_token",
            SubjectId = request.SubjectId,
            SessionId = request.SessionId,
            ClientId = request.ClientId,
            CreationTime = now,
            Expiration = now.AddSeconds(lifetime),
            Data = data,
            TenantId = refreshTenantId
        };

        await _grantStore.StoreAsync(grant, cancellationToken);

        return handle;
    }


    /// <summary>
    /// Gets audiences (aud claim) based on the resource parameter (RFC 8707).
    /// Per RFC 8707, the resource URIs directly become the audience values.
    /// If no resources are specified, defaults to the client ID.
    /// </summary>
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

        // Derive tenant from the grant's claims (set by the grant handler or profile service),
        // not from the current request routing context.
        var claimsTenantId = grant.Claims.TryGetValue("tenant_id", out var cpTid)
            ? cpTid?.ToString() : null;

        var context = new ClaimsProviderContext
        {
            SubjectId = grant.SubjectId,
            TenantId = claimsTenantId,
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

    internal static string ComputeHash(string value)
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
    /// See OpenID Connect Core 1.0 Section 8 (Subject Identifier Types).
    /// </summary>
    private static string ComputeSubjectId(string subjectId, string clientId, string? pairWiseSalt)
        => SubjectIdentifiers.ForClient(subjectId, clientId, pairWiseSalt);
}

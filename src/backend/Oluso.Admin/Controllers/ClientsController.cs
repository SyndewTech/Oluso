using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oluso.Admin.Authorization;
using Oluso.Core.Authorization;
using Oluso.Core.Api;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Protocols.Models;
using Oluso.Admin.Controllers.Service;
using Oluso.Admin.Dto.Clients;
using Oluso.Admin.Services;

using Oluso.Core.Services;

namespace Oluso.Admin.Controllers;

/// <summary>
/// Admin API for managing OAuth clients
/// </summary>
[Route("api/admin/clients")]
public class ClientsController : AdminBaseController
{
    private readonly IClientStore _clientStore;
    private readonly ICorsOriginsCacheInvalidator? _corsCache;
    private readonly ITenantSettingsProvider _tenantSettingsProvider;
    private readonly IIssuerResolver _issuerResolver;
    private readonly IOlusoEventService _eventService;
    private readonly IServiceApiScopeProvisioner _serviceApiScopeProvisioner;
    private readonly IAuthorizationService _authorization;
    private readonly ILogger<ClientsController> _logger;

    public ClientsController(
        ITenantContext tenantContext,
        IClientStore clientStore,
        ITenantSettingsProvider tenantSettingsProvider,
        IIssuerResolver issuerResolver,
        IOlusoEventService eventService,
        IServiceApiScopeProvisioner serviceApiScopeProvisioner,
        IAuthorizationService authorization,
        ILogger<ClientsController> logger,
        ICorsOriginsCacheInvalidator? corsCache = null)
        : base(tenantContext)
    {
        _corsCache = corsCache;
        _clientStore = clientStore;
        _tenantSettingsProvider = tenantSettingsProvider;
        _issuerResolver = issuerResolver;
        _eventService = eventService;
        _serviceApiScopeProvisioner = serviceApiScopeProvisioner;
        _authorization = authorization;
        _logger = logger;
    }

    /// <summary>
    /// Get all clients for the current tenant
    /// </summary>
    [HttpGet]
    [RequirePermission(AdminPermissions.ClientsRead)]
    public async Task<ActionResult<IEnumerable<ClientListDto>>> GetClients(CancellationToken cancellationToken)
    {
        var headerTenantId = HttpContext.Request.Headers["X-Tenant-Id"].FirstOrDefault();
        var jwtTenantId = User.FindFirst("tenant_id")?.Value;
        _logger.LogWarning(
            "GetClients trace: header={Header}, jwt={Jwt}, ITenantContext.TenantId={Resolved}",
            headerTenantId ?? "(none)", jwtTenantId ?? "(none)",
            TenantContext.TenantId ?? "(null)");

        var clients = (await _clientStore.GetAllClientsAsync(cancellationToken)).ToList();
        _logger.LogWarning(
            "GetClients trace: returned {Count} clients with tenants=[{Tenants}]",
            clients.Count, string.Join(",", clients.Select(c => c.TenantId ?? "(null)").Distinct()));

        var result = clients.Select(c => new ClientListDto
        {
            Id = c.Id,
            ClientId = c.ClientId,
            ClientName = c.ClientName,
            Description = c.Description,
            Enabled = c.Enabled,
            AllowedGrantTypes = c.AllowedGrantTypes.Select(g => g.GrantType).ToList(),
            RequireClientSecret = c.RequireClientSecret,
            RequirePkce = c.RequirePkce,
            Created = c.Created,
            Updated = c.Updated
        });

        return Ok(result);
    }

    /// <summary>
    /// Get a specific client by database ID
    /// </summary>
    [HttpGet("{id:int}")]
    [RequirePermission(AdminPermissions.ClientsRead)]
    public async Task<ActionResult<ClientDetailDto>> GetClient(int id, CancellationToken cancellationToken)
    {
        var client = await _clientStore.GetByIdAsync(id, cancellationToken);

        if (client == null)
        {
            return NotFound();
        }

        return Ok(MapToDetailDto(client));
    }

    /// <summary>
    /// Get the integration / connection details for a client — everything an
    /// integrator needs to copy into their app (issuer, OIDC endpoints,
    /// redirect URIs, scopes). Resolves the issuer through IIssuerResolver
    /// so per-tenant CustomDomain / TokenSettings.IssuerUri overrides are
    /// reflected here. Standard OIDC paths are derived from the issuer host
    /// to match what /.well-known/openid-configuration actually serves.
    /// </summary>
    [HttpGet("{id:int}/connection")]
    [RequirePermission(AdminPermissions.ClientsRead)]
    public async Task<ActionResult<ClientConnectionDto>> GetClientConnection(int id, CancellationToken cancellationToken)
    {
        var client = await _clientStore.GetByIdAsync(id, cancellationToken);
        if (client == null) return NotFound();

        var issuer = (await _issuerResolver.GetIssuerAsync(cancellationToken)).TrimEnd('/');

        return Ok(new ClientConnectionDto
        {
            ClientId = client.ClientId,
            ClientName = client.ClientName,
            RequireClientSecret = client.RequireClientSecret,
            RequirePkce = client.RequirePkce,
            AllowedGrantTypes = client.AllowedGrantTypes.Select(g => g.GrantType).ToList(),
            AllowedScopes = client.AllowedScopes.Select(s => s.Scope).ToList(),
            RedirectUris = client.RedirectUris.Select(r => r.RedirectUri).ToList(),
            PostLogoutRedirectUris = client.PostLogoutRedirectUris.Select(r => r.PostLogoutRedirectUri).ToList(),
            AllowedCorsOrigins = client.AllowedCorsOrigins.Select(c => c.Origin).ToList(),
            IssuerUri = issuer,
            DiscoveryUrl = $"{issuer}/.well-known/openid-configuration",
            JwksUri = $"{issuer}/.well-known/jwks",
            AuthorizationEndpoint = $"{issuer}/connect/authorize",
            TokenEndpoint = $"{issuer}/connect/token",
            UserInfoEndpoint = $"{issuer}/connect/userinfo",
            EndSessionEndpoint = $"{issuer}/connect/endsession",
            IntrospectionEndpoint = $"{issuer}/connect/introspect",
            RevocationEndpoint = $"{issuer}/connect/revocation",
        });
    }

    /// <summary>
    /// Create a new client
    /// </summary>
    [HttpPost]
    [RequirePermission(AdminPermissions.ClientsWrite)]
    public async Task<ActionResult<ClientDetailDto>> CreateClient(
        [FromBody] CreateClientRequest request,
        CancellationToken cancellationToken)
    {
        // Check if client already exists
        var existing = await _clientStore.FindClientByIdAsync(request.ClientId, cancellationToken);
        if (existing != null)
        {
            return BadRequest(new { error = "Client with this ID already exists" });
        }

        // Validate client configuration against tenant protocol settings
        var protocolSettings = await _tenantSettingsProvider.GetProtocolSettingsAsync(cancellationToken);
        var validationError = ValidateClientAgainstTenantSettings(request, protocolSettings);
        if (validationError != null)
        {
            return BadRequest(new { error = validationError });
        }

        // Validate client claims don't contain protected claim types
        var claimValidationError = ValidateClientClaims(request.Claims);
        if (claimValidationError != null)
        {
            return BadRequest(new { error = claimValidationError });
        }

        // Validate authorize-parameter claim mappings don't target protected claims
        var authParamError = ValidateAuthorizeParameters(request.AllowedAuthorizeParameters);
        if (authParamError != null)
        {
            return BadRequest(new { error = authParamError });
        }

        // Validate mTLS settings
        var mtlsError = ValidateMtlsSettings(
            request.TokenEndpointAuthMethod,
            request.TlsClientAuthSubjectDn,
            request.TlsClientAuthSanDns,
            request.TlsClientAuthSanUri,
            request.TlsClientAuthSanEmail,
            request.TlsClientAuthSanIp);
        if (mtlsError != null)
        {
            return BadRequest(new { error = mtlsError });
        }

        // Provision the ApiScope rows the service scopes resolve against —
        // without them token issuance fails with invalid_scope.
        var serviceScopeError = ValidateServiceApiScopes(request.AllowedScopes, request.AllowedGrantTypes);
        if (serviceScopeError != null)
        {
            return BadRequest(new { error = serviceScopeError });
        }

        if (await RejectUnheldSensitiveScopesAsync(request.AllowedScopes, Array.Empty<string>()) is { } sensitiveScopeError)
        {
            return sensitiveScopeError;
        }

        if (InvalidAllowedResource(request.AllowedResources) is { } resourceError)
        {
            return BadRequest(new { error = resourceError });
        }

        if (InvalidAllowedResource(request.IntrospectionResources, "Introspection resource") is { } introspectionResourceError)
        {
            return BadRequest(new { error = introspectionResourceError });
        }

        await _serviceApiScopeProvisioner.EnsureAsync(
            request.AllowedScopes ?? Array.Empty<string>(), cancellationToken);

        var client = MapFromCreateRequest(request);

        // Map collections
        MapCollectionsToClient(client, request);

        // Generate client secret if needed
        if (request.RequireClientSecret != false && !string.IsNullOrEmpty(request.ClientSecret))
        {
            client.ClientSecrets = new List<ClientSecret>
            {
                new ClientSecret
                {
                    Value = HashSecret(request.ClientSecret),
                    LastThreeChars = GetLastThreeChars(request.ClientSecret)
                }
            };
        }

        var created = await _clientStore.AddClientAsync(client, cancellationToken);
        if (_corsCache != null) await _corsCache.InvalidateCacheAsync(cancellationToken);

        _logger.LogInformation("Created client {ClientId} for tenant {TenantId}",
            client.ClientId, TenantId);

        // Raise audit event
        await _eventService.RaiseAsync(new AdminClientCreatedEvent
        {
            TenantId = TenantId,
            AdminUserId = AdminUserId!,
            AdminUserName = AdminUserName,
            IpAddress = ClientIp,
            ResourceId = client.ClientId,
            ResourceName = client.ClientName,
            ClientId = client.ClientId
        }, cancellationToken);

        return CreatedAtAction(nameof(GetClient), new { id = created.Id }, MapToDetailDto(created));
    }

    /// <summary>
    /// Update a client by database ID
    /// </summary>
    [HttpPut("{id:int}")]
    [RequirePermission(AdminPermissions.ClientsWrite)]
    public async Task<ActionResult<ClientDetailDto>> UpdateClient(
        int id,
        [FromBody] UpdateClientRequest request,
        CancellationToken cancellationToken)
    {
        var client = await _clientStore.GetByIdAsync(id, cancellationToken);

        if (client == null)
        {
            return NotFound();
        }

        return await UpdateClientInternal(client, request, cancellationToken);
    }

    private async Task<ActionResult<ClientDetailDto>> UpdateClientInternal(
        Client client,
        UpdateClientRequest request,
        CancellationToken cancellationToken)
    {
        // Validate client configuration against tenant protocol settings
        var protocolSettings = await _tenantSettingsProvider.GetProtocolSettingsAsync(cancellationToken);
        var validationError = ValidateClientAgainstTenantSettings(request, protocolSettings, client);
        if (validationError != null)
        {
            return BadRequest(new { error = validationError });
        }

        // Validate client claims don't contain protected claim types
        var claimValidationError = ValidateClientClaims(request.Claims);
        if (claimValidationError != null)
        {
            return BadRequest(new { error = claimValidationError });
        }

        // Validate authorize-parameter claim mappings don't target protected claims
        var authParamError = ValidateAuthorizeParameters(request.AllowedAuthorizeParameters);
        if (authParamError != null)
        {
            return BadRequest(new { error = authParamError });
        }

        // Validate mTLS settings — use the effective value after applying the update
        var effectiveAuthMethod = request.TokenEndpointAuthMethod ?? client.TokenEndpointAuthMethod;
        var effectiveSubjectDn = request.TlsClientAuthSubjectDn ?? client.TlsClientAuthSubjectDn;
        var effectiveSanDns = request.TlsClientAuthSanDns ?? client.TlsClientAuthSanDns;
        var effectiveSanUri = request.TlsClientAuthSanUri ?? client.TlsClientAuthSanUri;
        var effectiveSanEmail = request.TlsClientAuthSanEmail ?? client.TlsClientAuthSanEmail;
        var effectiveSanIp = request.TlsClientAuthSanIp ?? client.TlsClientAuthSanIp;
        var mtlsError = ValidateMtlsSettings(
            effectiveAuthMethod, effectiveSubjectDn, effectiveSanDns,
            effectiveSanUri, effectiveSanEmail, effectiveSanIp);
        if (mtlsError != null)
        {
            return BadRequest(new { error = mtlsError });
        }

        var effectiveScopes = request.AllowedScopes
            ?? client.AllowedScopes.Select(s => s.Scope).ToList();
        var effectiveGrantTypes = request.AllowedGrantTypes
            ?? client.AllowedGrantTypes.Select(g => g.GrantType).ToList();

        var serviceScopeError = ValidateServiceApiScopes(effectiveScopes, effectiveGrantTypes);
        if (serviceScopeError != null)
        {
            return BadRequest(new { error = serviceScopeError });
        }

        if (await RejectUnheldSensitiveScopesAsync(effectiveScopes, client.AllowedScopes.Select(s => s.Scope).ToList()) is { } sensitiveScopeError)
        {
            return sensitiveScopeError;
        }

        if (InvalidAllowedResource(request.AllowedResources) is { } resourceError)
        {
            return BadRequest(new { error = resourceError });
        }

        if (InvalidAllowedResource(request.IntrospectionResources, "Introspection resource") is { } introspectionResourceError)
        {
            return BadRequest(new { error = introspectionResourceError });
        }

        await _serviceApiScopeProvisioner.EnsureAsync(effectiveScopes, cancellationToken);

        // Apply all field updates and collection mappings
        ApplyUpdateRequest(client, request);

        // Note: Secrets are managed via regenerate-secret and delete-secret endpoints, not through update

        client.Updated = DateTime.UtcNow;
        var updated = await _clientStore.UpdateClientAsync(client, cancellationToken);
        if (_corsCache != null) await _corsCache.InvalidateCacheAsync(cancellationToken);

        _logger.LogInformation("Updated client {ClientId}", client.ClientId);

        // Raise audit event
        await _eventService.RaiseAsync(new AdminClientUpdatedEvent
        {
            TenantId = TenantId,
            AdminUserId = AdminUserId!,
            AdminUserName = AdminUserName,
            IpAddress = ClientIp,
            ResourceId = client.ClientId,
            ResourceName = client.ClientName,
            ClientId = client.ClientId
        }, cancellationToken);

        return Ok(MapToDetailDto(updated));
    }

    /// <summary>
    /// Delete a client
    /// </summary>
    [HttpDelete("{id:int}")]
    [RequirePermission(AdminPermissions.ClientsDelete)]
    public async Task<IActionResult> DeleteClient(int id, CancellationToken cancellationToken)
    {
        var client = await _clientStore.GetByIdAsync(id, cancellationToken);

        if (client == null)
        {
            return NotFound();
        }

        await _clientStore.DeleteByIdAsync(id, cancellationToken);
        if (_corsCache != null) await _corsCache.InvalidateCacheAsync(cancellationToken);

        _logger.LogInformation("Deleted client {ClientId}", client.ClientId);

        // Raise audit event
        await _eventService.RaiseAsync(new AdminClientDeletedEvent
        {
            TenantId = TenantId,
            AdminUserId = AdminUserId!,
            AdminUserName = AdminUserName,
            IpAddress = ClientIp,
            ResourceId = client.ClientId,
            ResourceName = client.ClientName,
            ClientId = client.ClientId
        }, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Regenerate client secret
    /// </summary>
    [HttpPost("{id:int}/regenerate-secret")]
    [RequirePermission(AdminPermissions.ClientsManageSecrets)]
    public async Task<ActionResult<RegenerateSecretResponse>> RegenerateSecret(
        int id,
        CancellationToken cancellationToken)
    {
        var client = await _clientStore.GetByIdAsync(id, cancellationToken);

        if (client == null)
        {
            return NotFound();
        }

        if (!client.RequireClientSecret)
        {
            return BadRequest(new { error = "Client does not require a secret" });
        }

        // Generate new secret and add it (keeping existing secrets)
        var newSecret = GenerateClientSecret();
        client.ClientSecrets.Add(new ClientSecret
        {
            Value = HashSecret(newSecret),
            LastThreeChars = GetLastThreeChars(newSecret)
        });

        await _clientStore.UpdateClientAsync(client, cancellationToken);

        _logger.LogInformation("Regenerated secret for client {ClientId}", client.ClientId);

        return Ok(new RegenerateSecretResponse
        {
            ClientId = client.ClientId,
            ClientSecret = newSecret, // Only time we return the plain secret
            SecretId = client.ClientSecrets.Last().Id // Return the new secret's ID
        });
    }

    /// <summary>
    /// Delete a specific client secret
    /// </summary>
    [HttpDelete("{id:int}/secrets/{secretId:int}")]
    [RequirePermission(AdminPermissions.ClientsManageSecrets)]
    public async Task<ActionResult> DeleteSecret(
        int id,
        int secretId,
        CancellationToken cancellationToken)
    {
        var client = await _clientStore.GetByIdAsync(id, cancellationToken);

        if (client == null)
        {
            return NotFound();
        }

        var secret = client.ClientSecrets.FirstOrDefault(s => s.Id == secretId);
        if (secret == null)
        {
            return NotFound(new { error = "Secret not found" });
        }

        // Ensure at least one secret remains if client requires secret
        if (client.RequireClientSecret && client.ClientSecrets.Count <= 1)
        {
            return BadRequest(new { error = "Cannot delete the last secret for a client that requires a secret" });
        }

        client.ClientSecrets.Remove(secret);
        await _clientStore.UpdateClientAsync(client, cancellationToken);

        _logger.LogInformation("Deleted secret {SecretId} from client {ClientId}", secretId, client.ClientId);

        return NoContent();
    }

    /// <summary>
    /// Creates a new Client entity from a CreateClientRequest. Used by both regular and platform client controllers.
    /// </summary>
    internal static Client MapFromCreateRequest(CreateClientRequest request)
    {
        return new Client
        {
            // Basic settings
            ClientId = request.ClientId,
            ClientName = request.ClientName,
            Description = request.Description,
            ClientUri = request.ClientUri,
            LogoUri = request.LogoUri,
            Enabled = request.Enabled ?? true,

            // Authentication settings
            RequireClientSecret = request.RequireClientSecret ?? true,
            RequirePkce = request.RequirePkce ?? true,
            AllowPlainTextPkce = request.AllowPlainTextPkce ?? false,
            RequireRequestObject = request.RequireRequestObject ?? false,
            RequireDPoP = request.RequireDPoP ?? false,
            RequirePushedAuthorization = request.RequirePushedAuthorization ?? false,
            PushedAuthorizationLifetime = request.PushedAuthorizationLifetime ?? 60,

            // Consent settings
            RequireConsent = request.RequireConsent ?? false,
            AllowRememberConsent = request.AllowRememberConsent ?? true,
            ConsentLifetime = request.ConsentLifetime,

            // Token settings
            AllowOfflineAccess = request.AllowOfflineAccess ?? false,
            AllowAccessTokensViaBrowser = request.AllowAccessTokensViaBrowser ?? false,
            AlwaysIncludeUserClaimsInIdToken = request.AlwaysIncludeUserClaimsInIdToken ?? false,
            AccessTokenLifetime = request.AccessTokenLifetime ?? 3600,
            IdentityTokenLifetime = request.IdentityTokenLifetime ?? 300,
            AuthorizationCodeLifetime = request.AuthorizationCodeLifetime ?? 300,
            AbsoluteRefreshTokenLifetime = request.AbsoluteRefreshTokenLifetime ?? 2592000,
            SlidingRefreshTokenLifetime = request.SlidingRefreshTokenLifetime ?? 1296000,
            RefreshTokenUsage = request.RefreshTokenUsage ?? (int)TokenUsage.OneTimeOnly,
            RefreshTokenExpiration = request.RefreshTokenExpiration ?? (int)TokenExpiration.Absolute,
            UpdateAccessTokenClaimsOnRefresh = request.UpdateAccessTokenClaimsOnRefresh ?? false,
            AccessTokenType = request.AccessTokenType ?? (int)Oluso.Core.Domain.Entities.AccessTokenType.Jwt,
            AllowedIdentityTokenSigningAlgorithms = request.AllowedIdentityTokenSigningAlgorithms,
            IncludeJwtId = request.IncludeJwtId ?? true,

            // Client claims settings
            AlwaysSendClientClaims = request.AlwaysSendClientClaims ?? false,
            ClientClaimsPrefix = request.ClientClaimsPrefix ?? "client_",
            PairWiseSubjectSalt = request.PairWiseSubjectSalt,

            // Logout settings
            FrontChannelLogoutUri = request.FrontChannelLogoutUri,
            FrontChannelLogoutSessionRequired = request.FrontChannelLogoutSessionRequired ?? true,
            BackChannelLogoutUri = request.BackChannelLogoutUri,
            BackChannelLogoutSessionRequired = request.BackChannelLogoutSessionRequired ?? true,

            // SSO and device settings
            EnableLocalLogin = request.EnableLocalLogin ?? true,
            UserSsoLifetime = request.UserSsoLifetime,
            UserCodeType = request.UserCodeType,
            DeviceCodeLifetime = request.DeviceCodeLifetime ?? 300,

            // CIBA settings
            CibaEnabled = request.CibaEnabled ?? false,
            CibaTokenDeliveryMode = request.CibaTokenDeliveryMode ?? "poll",
            CibaClientNotificationEndpoint = request.CibaClientNotificationEndpoint,
            CibaRequestLifetime = request.CibaRequestLifetime ?? 120,
            CibaPollingInterval = request.CibaPollingInterval ?? 5,
            CibaRequireUserCode = request.CibaRequireUserCode ?? false,

            // UI Flow settings
            UseJourneyFlow = request.UseJourneyFlow,

            // Access restrictions
            RequiredPermission = request.RequiredPermission,
            RequireVerifiedEmail = request.RequireVerifiedEmail ?? false,

            // mTLS / token endpoint auth settings
            TokenEndpointAuthMethod = request.TokenEndpointAuthMethod,
            TlsClientAuthSubjectDn = request.TlsClientAuthSubjectDn,
            TlsClientAuthSanDns = request.TlsClientAuthSanDns,
            TlsClientAuthSanUri = request.TlsClientAuthSanUri,
            TlsClientAuthSanEmail = request.TlsClientAuthSanEmail,
            TlsClientAuthSanIp = request.TlsClientAuthSanIp
        };
    }

    /// <summary>
    /// Applies an UpdateClientRequest to an existing Client entity. Used by both regular and platform client controllers.
    /// </summary>
    internal static void ApplyUpdateRequest(Client client, UpdateClientRequest request)
    {
        // Update basic settings
        if (request.ClientName != null) client.ClientName = request.ClientName;
        if (request.Description != null) client.Description = request.Description;
        if (request.ClientUri != null) client.ClientUri = request.ClientUri;
        if (request.LogoUri != null) client.LogoUri = request.LogoUri;
        if (request.Enabled.HasValue) client.Enabled = request.Enabled.Value;

        // Update authentication settings
        if (request.RequireClientSecret.HasValue) client.RequireClientSecret = request.RequireClientSecret.Value;
        if (request.RequirePkce.HasValue) client.RequirePkce = request.RequirePkce.Value;
        if (request.AllowPlainTextPkce.HasValue) client.AllowPlainTextPkce = request.AllowPlainTextPkce.Value;
        if (request.RequireRequestObject.HasValue) client.RequireRequestObject = request.RequireRequestObject.Value;
        if (request.RequireDPoP.HasValue) client.RequireDPoP = request.RequireDPoP.Value;
        if (request.RequirePushedAuthorization.HasValue) client.RequirePushedAuthorization = request.RequirePushedAuthorization.Value;
        if (request.PushedAuthorizationLifetime.HasValue) client.PushedAuthorizationLifetime = request.PushedAuthorizationLifetime.Value;

        // Update consent settings
        if (request.RequireConsent.HasValue) client.RequireConsent = request.RequireConsent.Value;
        if (request.AllowRememberConsent.HasValue) client.AllowRememberConsent = request.AllowRememberConsent.Value;
        if (request.ConsentLifetime.HasValue) client.ConsentLifetime = request.ConsentLifetime.Value;

        // Update token settings
        if (request.AllowOfflineAccess.HasValue) client.AllowOfflineAccess = request.AllowOfflineAccess.Value;
        if (request.AllowAccessTokensViaBrowser.HasValue) client.AllowAccessTokensViaBrowser = request.AllowAccessTokensViaBrowser.Value;
        if (request.AlwaysIncludeUserClaimsInIdToken.HasValue) client.AlwaysIncludeUserClaimsInIdToken = request.AlwaysIncludeUserClaimsInIdToken.Value;
        if (request.AccessTokenLifetime.HasValue) client.AccessTokenLifetime = request.AccessTokenLifetime.Value;
        if (request.IdentityTokenLifetime.HasValue) client.IdentityTokenLifetime = request.IdentityTokenLifetime.Value;
        if (request.AuthorizationCodeLifetime.HasValue) client.AuthorizationCodeLifetime = request.AuthorizationCodeLifetime.Value;
        if (request.AbsoluteRefreshTokenLifetime.HasValue) client.AbsoluteRefreshTokenLifetime = request.AbsoluteRefreshTokenLifetime.Value;
        if (request.SlidingRefreshTokenLifetime.HasValue) client.SlidingRefreshTokenLifetime = request.SlidingRefreshTokenLifetime.Value;
        if (request.RefreshTokenUsage.HasValue) client.RefreshTokenUsage = request.RefreshTokenUsage.Value;
        if (request.RefreshTokenExpiration.HasValue) client.RefreshTokenExpiration = request.RefreshTokenExpiration.Value;
        if (request.UpdateAccessTokenClaimsOnRefresh.HasValue) client.UpdateAccessTokenClaimsOnRefresh = request.UpdateAccessTokenClaimsOnRefresh.Value;
        if (request.AccessTokenType.HasValue) client.AccessTokenType = request.AccessTokenType.Value;
        if (request.AllowedIdentityTokenSigningAlgorithms != null) client.AllowedIdentityTokenSigningAlgorithms = request.AllowedIdentityTokenSigningAlgorithms;
        if (request.IncludeJwtId.HasValue) client.IncludeJwtId = request.IncludeJwtId.Value;

        // Update client claims settings
        if (request.AlwaysSendClientClaims.HasValue) client.AlwaysSendClientClaims = request.AlwaysSendClientClaims.Value;
        if (request.ClientClaimsPrefix != null) client.ClientClaimsPrefix = request.ClientClaimsPrefix;
        if (request.PairWiseSubjectSalt != null) client.PairWiseSubjectSalt = request.PairWiseSubjectSalt;

        // Update logout settings
        if (request.FrontChannelLogoutUri != null) client.FrontChannelLogoutUri = request.FrontChannelLogoutUri;
        if (request.FrontChannelLogoutSessionRequired.HasValue) client.FrontChannelLogoutSessionRequired = request.FrontChannelLogoutSessionRequired.Value;
        if (request.BackChannelLogoutUri != null) client.BackChannelLogoutUri = request.BackChannelLogoutUri;
        if (request.BackChannelLogoutSessionRequired.HasValue) client.BackChannelLogoutSessionRequired = request.BackChannelLogoutSessionRequired.Value;

        // Update SSO and device settings
        if (request.EnableLocalLogin.HasValue) client.EnableLocalLogin = request.EnableLocalLogin.Value;
        if (request.UserSsoLifetime.HasValue) client.UserSsoLifetime = request.UserSsoLifetime.Value;
        if (request.UserCodeType != null) client.UserCodeType = request.UserCodeType;
        if (request.DeviceCodeLifetime.HasValue) client.DeviceCodeLifetime = request.DeviceCodeLifetime.Value;

        // Update CIBA settings
        if (request.CibaEnabled.HasValue) client.CibaEnabled = request.CibaEnabled.Value;
        if (request.CibaTokenDeliveryMode != null) client.CibaTokenDeliveryMode = request.CibaTokenDeliveryMode;
        if (request.CibaClientNotificationEndpoint != null) client.CibaClientNotificationEndpoint = request.CibaClientNotificationEndpoint;
        if (request.CibaRequestLifetime.HasValue) client.CibaRequestLifetime = request.CibaRequestLifetime.Value;
        if (request.CibaPollingInterval.HasValue) client.CibaPollingInterval = request.CibaPollingInterval.Value;
        if (request.CibaRequireUserCode.HasValue) client.CibaRequireUserCode = request.CibaRequireUserCode.Value;

        // Update access restrictions
        if (request.RequiredPermission != null) client.RequiredPermission = request.RequiredPermission == "" ? null : request.RequiredPermission;
        if (request.RequireVerifiedEmail.HasValue) client.RequireVerifiedEmail = request.RequireVerifiedEmail.Value;

        // Update mTLS / token endpoint auth settings
        if (request.TokenEndpointAuthMethod != null) client.TokenEndpointAuthMethod = request.TokenEndpointAuthMethod == "" ? null : request.TokenEndpointAuthMethod;
        if (request.TlsClientAuthSubjectDn != null) client.TlsClientAuthSubjectDn = request.TlsClientAuthSubjectDn == "" ? null : request.TlsClientAuthSubjectDn;
        if (request.TlsClientAuthSanDns != null) client.TlsClientAuthSanDns = request.TlsClientAuthSanDns == "" ? null : request.TlsClientAuthSanDns;
        if (request.TlsClientAuthSanUri != null) client.TlsClientAuthSanUri = request.TlsClientAuthSanUri == "" ? null : request.TlsClientAuthSanUri;
        if (request.TlsClientAuthSanEmail != null) client.TlsClientAuthSanEmail = request.TlsClientAuthSanEmail == "" ? null : request.TlsClientAuthSanEmail;
        if (request.TlsClientAuthSanIp != null) client.TlsClientAuthSanIp = request.TlsClientAuthSanIp == "" ? null : request.TlsClientAuthSanIp;

        // Update UI Flow settings (UseJourneyFlow is nullable - null means inherit from tenant)
        if (request.ResetUseJourneyFlowToInherit == true)
        {
            client.UseJourneyFlow = null;
        }
        else if (request.UseJourneyFlow.HasValue)
        {
            client.UseJourneyFlow = request.UseJourneyFlow.Value;
        }

        // Update collections
        MapCollectionsToClient(client, request);
    }

    /// <summary>
    /// Why a client's allowed-resources list is unusable, or null. Each entry must be a resource
    /// indicator: an absolute http(s) URI with no fragment (RFC 8707 §2).
    /// </summary>
    internal static string? InvalidAllowedResource(IEnumerable<string>? resources, string label = "Allowed resource")
    {
        foreach (var value in resources ?? Array.Empty<string>())
        {
            var resource = value?.Trim() ?? "";
            if (resource.Length == 0) continue;
            if (!Uri.TryCreate(resource, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
                || !string.IsNullOrEmpty(uri.Fragment))
            {
                return $"{label} '{resource}' must be an absolute http(s) URI without a fragment.";
            }
        }
        return null;
    }

    internal static void MapCollectionsToClient(Client client, IClientCollections request)
    {
        // Map grant types
        if (request.AllowedGrantTypes != null)
        {
            client.AllowedGrantTypes = request.AllowedGrantTypes
                .Select(g => new ClientGrantType { GrantType = g })
                .ToList();
        }

        // Map redirect URIs
        if (request.RedirectUris != null)
        {
            client.RedirectUris = request.RedirectUris
                .Select(u => new ClientRedirectUri { RedirectUri = u })
                .ToList();
        }

        // Map post logout redirect URIs
        if (request.PostLogoutRedirectUris != null)
        {
            client.PostLogoutRedirectUris = request.PostLogoutRedirectUris
                .Select(u => new ClientPostLogoutRedirectUri { PostLogoutRedirectUri = u })
                .ToList();
        }

        // Map scopes
        if (request.AllowedScopes != null)
        {
            client.AllowedScopes = request.AllowedScopes
                .Select(s => new ClientScope { Scope = s })
                .ToList();
        }

        // Map CORS origins
        if (request.AllowedCorsOrigins != null)
        {
            client.AllowedCorsOrigins = request.AllowedCorsOrigins
                .Select(o => new ClientCorsOrigin { Origin = o })
                .ToList();
        }

        // Map claims
        if (request.Claims != null)
        {
            client.Claims = request.Claims
                .Select(c => new ClientClaim { Type = c.Type, Value = c.Value })
                .ToList();
        }

        // Map allowlisted custom authorize parameters
        if (request.AllowedAuthorizeParameters != null)
        {
            client.AllowedAuthorizeParameters = request.AllowedAuthorizeParameters
                .Select(p => new ClientAuthorizeParameter
                {
                    Parameter = p.Parameter,
                    ClaimType = p.ClaimType,
                    Required = p.Required
                })
                .ToList();
        }

        // Map properties
        if (request.Properties != null)
        {
            client.Properties = request.Properties
                .Select(p => new ClientProperty { Key = p.Key, Value = p.Value })
                .ToList();
        }

        // Map identity provider restrictions
        if (request.AllowedResources != null)
        {
            client.AllowedResources = request.AllowedResources
                .Select(r => r.Trim())
                .Where(r => r.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        if (request.IntrospectionResources != null)
        {
            client.IntrospectionResources = request.IntrospectionResources
                .Select(r => r.Trim())
                .Where(r => r.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        if (request.IdentityProviderRestrictions != null)
        {
            client.IdentityProviderRestrictions = request.IdentityProviderRestrictions
                .Select(p => new ClientIdPRestriction { Provider = p })
                .ToList();
        }

        // Map allowed roles
        if (request.AllowedRoles != null)
        {
            client.AllowedRoles = request.AllowedRoles
                .Select(r => new ClientAllowedRole { Role = r })
                .ToList();
        }

        // Map allowed users
        if (request.AllowedUsers != null)
        {
            client.AllowedUsers = request.AllowedUsers
                .Select(u => new ClientAllowedUser { SubjectId = u.SubjectId, DisplayName = u.DisplayName })
                .ToList();
        }
    }

    internal static ClientDetailDto MapToDetailDto(Client client) => new()
    {
        // Basic settings
        Id = client.Id,
        ClientId = client.ClientId,
        ClientName = client.ClientName,
        Description = client.Description,
        ClientUri = client.ClientUri,
        LogoUri = client.LogoUri,
        Enabled = client.Enabled,
        Created = client.Created,
        Updated = client.Updated,
        LastAccessed = client.LastAccessed,

        // Client secrets (metadata only, no hashed values)
        Secrets = client.ClientSecrets.Select(s => new ClientSecretDto
        {
            Id = s.Id,
            Description = s.Description,
            Expiration = s.Expiration,
            Type = s.Type,
            Created = s.Created,
            LastThreeChars = s.LastThreeChars
        }).ToList(),

        // Authentication settings
        RequireClientSecret = client.RequireClientSecret,
        RequirePkce = client.RequirePkce,
        AllowPlainTextPkce = client.AllowPlainTextPkce,
        RequireRequestObject = client.RequireRequestObject,
        RequireDPoP = client.RequireDPoP,
        RequirePushedAuthorization = client.RequirePushedAuthorization,
        PushedAuthorizationLifetime = client.PushedAuthorizationLifetime,

        // Consent settings
        RequireConsent = client.RequireConsent,
        AllowRememberConsent = client.AllowRememberConsent,
        ConsentLifetime = client.ConsentLifetime,

        // Token settings
        AllowOfflineAccess = client.AllowOfflineAccess,
        AllowAccessTokensViaBrowser = client.AllowAccessTokensViaBrowser,
        AlwaysIncludeUserClaimsInIdToken = client.AlwaysIncludeUserClaimsInIdToken,
        AccessTokenLifetime = client.AccessTokenLifetime,
        IdentityTokenLifetime = client.IdentityTokenLifetime,
        AuthorizationCodeLifetime = client.AuthorizationCodeLifetime,
        AbsoluteRefreshTokenLifetime = client.AbsoluteRefreshTokenLifetime,
        SlidingRefreshTokenLifetime = client.SlidingRefreshTokenLifetime,
        RefreshTokenUsage = client.RefreshTokenUsage,
        RefreshTokenExpiration = client.RefreshTokenExpiration,
        UpdateAccessTokenClaimsOnRefresh = client.UpdateAccessTokenClaimsOnRefresh,
        AccessTokenType = client.AccessTokenType,
        AllowedIdentityTokenSigningAlgorithms = client.AllowedIdentityTokenSigningAlgorithms,
        IncludeJwtId = client.IncludeJwtId,

        // Client claims settings
        AlwaysSendClientClaims = client.AlwaysSendClientClaims,
        ClientClaimsPrefix = client.ClientClaimsPrefix,
        PairWiseSubjectSalt = client.PairWiseSubjectSalt,

        // Logout settings
        FrontChannelLogoutUri = client.FrontChannelLogoutUri,
        FrontChannelLogoutSessionRequired = client.FrontChannelLogoutSessionRequired,
        BackChannelLogoutUri = client.BackChannelLogoutUri,
        BackChannelLogoutSessionRequired = client.BackChannelLogoutSessionRequired,

        // SSO and device settings
        EnableLocalLogin = client.EnableLocalLogin,
        UserSsoLifetime = client.UserSsoLifetime,
        UserCodeType = client.UserCodeType,
        DeviceCodeLifetime = client.DeviceCodeLifetime,

        // CIBA settings
        CibaEnabled = client.CibaEnabled,
        CibaTokenDeliveryMode = client.CibaTokenDeliveryMode,
        CibaClientNotificationEndpoint = client.CibaClientNotificationEndpoint,
        CibaRequestLifetime = client.CibaRequestLifetime,
        CibaPollingInterval = client.CibaPollingInterval,
        CibaRequireUserCode = client.CibaRequireUserCode,

        // UI Flow settings
        UseJourneyFlow = client.UseJourneyFlow,

        // Access restrictions
        RequiredPermission = client.RequiredPermission,
        RequireVerifiedEmail = client.RequireVerifiedEmail,

        // mTLS / token endpoint auth settings
        TokenEndpointAuthMethod = client.TokenEndpointAuthMethod,
        TlsClientAuthSubjectDn = client.TlsClientAuthSubjectDn,
        TlsClientAuthSanDns = client.TlsClientAuthSanDns,
        TlsClientAuthSanUri = client.TlsClientAuthSanUri,
        TlsClientAuthSanEmail = client.TlsClientAuthSanEmail,
        TlsClientAuthSanIp = client.TlsClientAuthSanIp,

        // Collections
        AllowedGrantTypes = client.AllowedGrantTypes.Select(g => g.GrantType).ToList(),
        RedirectUris = client.RedirectUris.Select(u => u.RedirectUri).ToList(),
        PostLogoutRedirectUris = client.PostLogoutRedirectUris.Select(u => u.PostLogoutRedirectUri).ToList(),
        AllowedScopes = client.AllowedScopes.Select(s => s.Scope).ToList(),
        AllowedCorsOrigins = client.AllowedCorsOrigins.Select(o => o.Origin).ToList(),
        Claims = client.Claims.Select(c => new ClientClaimDto { Type = c.Type, Value = c.Value }).ToList(),
        AllowedAuthorizeParameters = client.AllowedAuthorizeParameters
            .Select(p => new ClientAuthorizeParameterDto { Parameter = p.Parameter, ClaimType = p.ClaimType, Required = p.Required })
            .ToList(),
        Properties = client.Properties.Select(p => new ClientPropertyDto { Key = p.Key, Value = p.Value }).ToList(),
        IdentityProviderRestrictions = client.IdentityProviderRestrictions.Select(r => r.Provider).ToList(),
        AllowedResources = client.AllowedResources.ToList(),
        IntrospectionResources = client.IntrospectionResources.ToList(),
        AllowedRoles = client.AllowedRoles.Select(r => r.Role).ToList(),
        AllowedUsers = client.AllowedUsers.Select(u => new AllowedUserDto { SubjectId = u.SubjectId, DisplayName = u.DisplayName }).ToList()
    };

    internal static string HashSecret(string secret)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(secret);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }

    internal static string GenerateClientSecret()
    {
        var bytes = new byte[32];
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }

    internal static string? GetLastThreeChars(string secret)
    {
        if (string.IsNullOrEmpty(secret) || secret.Length < 3)
            return secret;
        return secret[^3..];
    }

    /// <summary>
    /// Validates client configuration against tenant protocol settings.
    /// Returns an error message if validation fails, null if successful.
    /// </summary>
    private static string? ValidateClientAgainstTenantSettings(
        IClientCollections request,
        TenantProtocolSettings protocolSettings,
        Client? existingClient = null)
    {
        // Get the effective grant types (from request or existing client)
        var grantTypes = request.AllowedGrantTypes ?? existingClient?.AllowedGrantTypes.Select(g => g.GrantType).ToList();

        // Validate grant types against tenant restrictions
        if (grantTypes != null && protocolSettings.AllowedGrantTypes?.Count > 0)
        {
            var disallowedGrantTypes = grantTypes
                .Where(g => !protocolSettings.AllowedGrantTypes.Contains(g))
                .ToList();

            if (disallowedGrantTypes.Any())
            {
                return $"The following grant types are not allowed for this tenant: {string.Join(", ", disallowedGrantTypes)}";
            }
        }

        // Determine effective client settings (request values or existing client values)
        var requirePkce = (request as CreateClientRequest)?.RequirePkce
            ?? (request as UpdateClientRequest)?.RequirePkce
            ?? existingClient?.RequirePkce
            ?? true;

        var allowPlainTextPkce = (request as CreateClientRequest)?.AllowPlainTextPkce
            ?? (request as UpdateClientRequest)?.AllowPlainTextPkce
            ?? existingClient?.AllowPlainTextPkce
            ?? false;

        var requireDPoP = (request as CreateClientRequest)?.RequireDPoP
            ?? (request as UpdateClientRequest)?.RequireDPoP
            ?? existingClient?.RequireDPoP
            ?? false;

        var requirePushedAuthorization = (request as CreateClientRequest)?.RequirePushedAuthorization
            ?? (request as UpdateClientRequest)?.RequirePushedAuthorization
            ?? existingClient?.RequirePushedAuthorization
            ?? false;

        // Validate PKCE requirement - if tenant requires PKCE, client cannot disable it
        if (protocolSettings.RequirePkce && requirePkce == false)
        {
            return "Cannot disable PKCE requirement: tenant requires PKCE for all clients";
        }

        // Validate plain PKCE - if tenant disallows plain PKCE, client cannot enable it
        if (!protocolSettings.AllowPlainPkce && allowPlainTextPkce == true)
        {
            return "Cannot allow plain PKCE: tenant prohibits plain PKCE for security reasons";
        }

        // Validate DPoP requirement - if tenant requires DPoP, client cannot disable it
        if (protocolSettings.RequireDPoP && requireDPoP == false)
        {
            return "Cannot disable DPoP requirement: tenant requires DPoP for all clients";
        }

        // Validate PAR requirement - if tenant requires PAR, client cannot disable it
        if (protocolSettings.RequirePushedAuthorizationRequests && requirePushedAuthorization == false)
        {
            return "Cannot disable PAR requirement: tenant requires Pushed Authorization Requests for all clients";
        }

        return null; // Validation passed
    }

    /// <summary>
    /// Validates the Service API scopes on a client. Callers pass effective
    /// values (request falling back to the existing client) so a partial update
    /// can't bypass the checks by omitting a collection.
    /// </summary>
    /// <returns>An error message, or null when the configuration is valid.</returns>
    internal static string? ValidateServiceApiScopes(
        ICollection<string>? effectiveScopes,
        ICollection<string>? effectiveGrantTypes)
    {
        var serviceScopes = effectiveScopes?
            .Where(ServiceScopes.IsReserved)
            .ToList() ?? new List<string>();

        if (serviceScopes.Count == 0)
        {
            return null;
        }

        var unknown = serviceScopes.Where(s => !ServiceScopes.IsManaged(s)).ToList();
        if (unknown.Count > 0)
        {
            return $"Unknown service scope(s): {string.Join(", ", unknown)}. "
                + $"'{ServiceScopes.ReservedPrefix}' is a reserved namespace — "
                + "use the Service API permissions on the client instead.";
        }

        if (!serviceScopes.Contains(ServiceScopes.ServiceApi, StringComparer.Ordinal))
        {
            return $"Service API permissions require the base scope '{ServiceScopes.ServiceApi}'. "
                + "Enable Service API access on the client first.";
        }

        var grantTypes = effectiveGrantTypes ?? new List<string>();
        if (!grantTypes.Contains(OidcConstants.GrantTypes.ClientCredentials, StringComparer.Ordinal))
        {
            return $"Service API access requires the '{OidcConstants.GrantTypes.ClientCredentials}' grant type.";
        }

        return null;
    }

    /// <summary>
    /// Sensitive Service API scopes in <paramref name="effectiveScopes"/> that the
    /// client doesn't already have.
    /// </summary>
    internal static IReadOnlyList<ServiceScopeDefinition> NewSensitiveServiceScopes(
        IEnumerable<string>? effectiveScopes,
        IEnumerable<string> currentScopes)
    {
        var current = currentScopes.ToHashSet(StringComparer.Ordinal);
        return (effectiveScopes ?? Enumerable.Empty<string>())
            .Where(s => !current.Contains(s))
            .Select(ServiceScopes.Find)
            .OfType<ServiceScopeDefinition>()
            .Where(d => d.RequiresExplicitGrant)
            .DistinctBy(d => d.Name)
            .ToList();
    }

    /// <summary>
    /// A sensitive scope is handed to a client only by an admin who could do the
    /// same thing themselves: they must hold its admin permission, which wildcards
    /// and the SuperAdmin shortcut never satisfy. Scopes the client already has
    /// aren't re-checked, so other edits to the client still save.
    /// </summary>
    private async Task<ActionResult?> RejectUnheldSensitiveScopesAsync(
        IEnumerable<string>? effectiveScopes,
        IEnumerable<string> currentScopes)
    {
        foreach (var scope in NewSensitiveServiceScopes(effectiveScopes, currentScopes))
        {
            var held = await _authorization.AuthorizeAsync(
                User, null, PermissionPolicyConstants.PolicyPrefix + scope.RequiredAdminPermission);
            if (!held.Succeeded)
            {
                _logger.LogWarning(
                    "Admin {AdminId} tried to grant {Scope} without holding {Permission}",
                    AdminUserId, scope.Name, scope.RequiredAdminPermission);
                return StatusCode(403, new
                {
                    error = "permission_required",
                    error_description =
                        $"Granting '{scope.Name}' requires you to hold the '{scope.RequiredAdminPermission}' permission, " +
                        "assigned to one of your roles by name. Wildcard and SuperAdmin access don't include it."
                });
            }
        }

        return null;
    }

    /// <summary>
    /// Validates mTLS / token endpoint auth method settings (RFC 8705).
    /// When token_endpoint_auth_method is "tls_client_auth", exactly one SAN/DN identifier must be provided.
    /// Returns an error message if validation fails, null if successful.
    /// </summary>
    internal static string? ValidateMtlsSettings(
        string? tokenEndpointAuthMethod,
        string? tlsClientAuthSubjectDn,
        string? tlsClientAuthSanDns,
        string? tlsClientAuthSanUri,
        string? tlsClientAuthSanEmail,
        string? tlsClientAuthSanIp)
    {
        if (!string.Equals(tokenEndpointAuthMethod, "tls_client_auth", StringComparison.Ordinal))
        {
            return null;
        }

        var identifiers = new[] { tlsClientAuthSubjectDn, tlsClientAuthSanDns, tlsClientAuthSanUri, tlsClientAuthSanEmail, tlsClientAuthSanIp };
        var providedCount = identifiers.Count(v => !string.IsNullOrEmpty(v));

        if (providedCount == 0)
        {
            return "When token_endpoint_auth_method is 'tls_client_auth', exactly one of TlsClientAuthSubjectDn, TlsClientAuthSanDns, TlsClientAuthSanUri, TlsClientAuthSanEmail, or TlsClientAuthSanIp must be provided";
        }

        if (providedCount > 1)
        {
            return "When token_endpoint_auth_method is 'tls_client_auth', only one of TlsClientAuthSubjectDn, TlsClientAuthSanDns, TlsClientAuthSanUri, TlsClientAuthSanEmail, or TlsClientAuthSanIp may be provided";
        }

        return null;
    }

    /// <summary>
    /// Validates that client claims don't contain protected claim types.
    /// Protected claims include system claims like "permissions", "role", "tenant_id", etc.
    /// Returns an error message if validation fails, null if successful.
    /// </summary>
    /// <summary>
    /// Validates custom authorize-parameter claim mappings: each entry needs a
    /// parameter and a claim type, and the claim type must not be a protected /
    /// system-managed claim. Returns an error message, or null when valid.
    /// </summary>
    private static string? ValidateAuthorizeParameters(ICollection<ClientAuthorizeParameterDto>? parameters)
    {
        if (parameters == null || parameters.Count == 0)
        {
            return null;
        }

        foreach (var p in parameters)
        {
            if (string.IsNullOrWhiteSpace(p.Parameter) || string.IsNullOrWhiteSpace(p.ClaimType))
            {
                return "Each authorize parameter mapping requires both 'parameter' and 'claimType'";
            }
        }

        var protectedClaims = parameters
            .Where(p => ReservedClaimTypes.IsProtectedClaimType(p.ClaimType))
            .Select(p => p.ClaimType)
            .Distinct()
            .ToList();

        if (protectedClaims.Count > 0)
        {
            return $"The following claim types are protected and cannot be sourced from an authorize parameter: {string.Join(", ", protectedClaims)}";
        }

        return null;
    }

    private static string? ValidateClientClaims(ICollection<ClientClaimDto>? claims)
    {
        if (claims == null || claims.Count == 0)
        {
            return null;
        }

        var protectedClaims = claims
            .Where(c => ReservedClaimTypes.IsProtectedFromClientClaims(c.Type))
            .Select(c => c.Type)
            .Distinct()
            .ToList();

        if (protectedClaims.Count > 0)
        {
            return $"The following claim types are protected and cannot be set via client claims: {string.Join(", ", protectedClaims)}";
        }

        return null;
    }
}

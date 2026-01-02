using Microsoft.AspNetCore.Mvc;
using Oluso.Core.Api;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;

namespace Oluso.Admin.Controllers;

/// <summary>
/// Admin API for managing OAuth clients
/// </summary>
[Route("api/admin/clients")]
public class ClientsController : AdminBaseController
{
    private readonly IClientStore _clientStore;
    private readonly IOlusoEventService _eventService;
    private readonly ILogger<ClientsController> _logger;

    public ClientsController(
        ITenantContext tenantContext,
        IClientStore clientStore,
        IOlusoEventService eventService,
        ILogger<ClientsController> logger)
        : base(tenantContext)
    {
        _clientStore = clientStore;
        _eventService = eventService;
        _logger = logger;
    }

    /// <summary>
    /// Get all clients for the current tenant
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ClientListDto>>> GetClients(CancellationToken cancellationToken)
    {
        var clients = await _clientStore.GetAllClientsAsync(cancellationToken);

        var result = clients.Select(c => new ClientListDto
        {
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
    /// Get a specific client by ID
    /// </summary>
    [HttpGet("{clientId}")]
    public async Task<ActionResult<ClientDetailDto>> GetClient(string clientId, CancellationToken cancellationToken)
    {
        var client = await _clientStore.FindClientByIdAsync(clientId, cancellationToken);

        if (client == null)
        {
            return NotFound();
        }

        return Ok(MapToDetailDto(client));
    }

    /// <summary>
    /// Create a new client
    /// </summary>
    [HttpPost]
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

        var client = new Client
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
            CibaRequireUserCode = request.CibaRequireUserCode ?? false
        };

        // Map collections
        MapCollectionsToClient(client, request);

        // Generate client secret if needed
        if (request.RequireClientSecret != false && !string.IsNullOrEmpty(request.ClientSecret))
        {
            client.ClientSecrets = new List<ClientSecret>
            {
                new ClientSecret { Value = HashSecret(request.ClientSecret) }
            };
        }

        var created = await _clientStore.AddClientAsync(client, cancellationToken);

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

        return CreatedAtAction(nameof(GetClient), new { clientId = created.ClientId }, MapToDetailDto(created));
    }

    /// <summary>
    /// Update a client
    /// </summary>
    [HttpPut("{clientId}")]
    public async Task<ActionResult<ClientDetailDto>> UpdateClient(
        string clientId,
        [FromBody] UpdateClientRequest request,
        CancellationToken cancellationToken)
    {
        var client = await _clientStore.FindClientByIdAsync(clientId, cancellationToken);

        if (client == null)
        {
            return NotFound();
        }

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

        // Update collections
        MapCollectionsToClient(client, request);

        // Update secret if provided
        if (!string.IsNullOrEmpty(request.ClientSecret))
        {
            client.ClientSecrets = new List<ClientSecret>
            {
                new ClientSecret { Value = HashSecret(request.ClientSecret) }
            };
        }

        client.Updated = DateTime.UtcNow;
        var updated = await _clientStore.UpdateClientAsync(client, cancellationToken);

        _logger.LogInformation("Updated client {ClientId}", clientId);

        // Raise audit event
        await _eventService.RaiseAsync(new AdminClientUpdatedEvent
        {
            TenantId = TenantId,
            AdminUserId = AdminUserId!,
            AdminUserName = AdminUserName,
            IpAddress = ClientIp,
            ResourceId = clientId,
            ResourceName = client.ClientName,
            ClientId = clientId
        }, cancellationToken);

        return Ok(MapToDetailDto(updated));
    }

    /// <summary>
    /// Delete a client
    /// </summary>
    [HttpDelete("{clientId}")]
    public async Task<IActionResult> DeleteClient(string clientId, CancellationToken cancellationToken)
    {
        var client = await _clientStore.FindClientByIdAsync(clientId, cancellationToken);

        if (client == null)
        {
            return NotFound();
        }

        await _clientStore.DeleteClientAsync(clientId, cancellationToken);

        _logger.LogInformation("Deleted client {ClientId}", clientId);

        // Raise audit event
        await _eventService.RaiseAsync(new AdminClientDeletedEvent
        {
            TenantId = TenantId,
            AdminUserId = AdminUserId!,
            AdminUserName = AdminUserName,
            IpAddress = ClientIp,
            ResourceId = clientId,
            ResourceName = client.ClientName,
            ClientId = clientId
        }, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Regenerate client secret
    /// </summary>
    [HttpPost("{clientId}/regenerate-secret")]
    public async Task<ActionResult<RegenerateSecretResponse>> RegenerateSecret(
        string clientId,
        CancellationToken cancellationToken)
    {
        var client = await _clientStore.FindClientByIdAsync(clientId, cancellationToken);

        if (client == null)
        {
            return NotFound();
        }

        if (!client.RequireClientSecret)
        {
            return BadRequest(new { error = "Client does not require a secret" });
        }

        // Generate new secret
        var newSecret = GenerateClientSecret();
        client.ClientSecrets = new List<ClientSecret>
        {
            new ClientSecret { Value = HashSecret(newSecret) }
        };

        await _clientStore.UpdateClientAsync(client, cancellationToken);

        _logger.LogInformation("Regenerated secret for client {ClientId}", clientId);

        return Ok(new RegenerateSecretResponse
        {
            ClientId = clientId,
            ClientSecret = newSecret // Only time we return the plain secret
        });
    }

    private static void MapCollectionsToClient(Client client, IClientCollections request)
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

        // Map properties
        if (request.Properties != null)
        {
            client.Properties = request.Properties
                .Select(p => new ClientProperty { Key = p.Key, Value = p.Value })
                .ToList();
        }

        // Map identity provider restrictions
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

    private static ClientDetailDto MapToDetailDto(Client client) => new()
    {
        // Basic settings
        ClientId = client.ClientId,
        ClientName = client.ClientName,
        Description = client.Description,
        ClientUri = client.ClientUri,
        LogoUri = client.LogoUri,
        Enabled = client.Enabled,
        Created = client.Created,
        Updated = client.Updated,
        LastAccessed = client.LastAccessed,

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

        // Collections
        AllowedGrantTypes = client.AllowedGrantTypes.Select(g => g.GrantType).ToList(),
        RedirectUris = client.RedirectUris.Select(u => u.RedirectUri).ToList(),
        PostLogoutRedirectUris = client.PostLogoutRedirectUris.Select(u => u.PostLogoutRedirectUri).ToList(),
        AllowedScopes = client.AllowedScopes.Select(s => s.Scope).ToList(),
        AllowedCorsOrigins = client.AllowedCorsOrigins.Select(o => o.Origin).ToList(),
        Claims = client.Claims.Select(c => new ClientClaimDto { Type = c.Type, Value = c.Value }).ToList(),
        Properties = client.Properties.Select(p => new ClientPropertyDto { Key = p.Key, Value = p.Value }).ToList(),
        IdentityProviderRestrictions = client.IdentityProviderRestrictions.Select(r => r.Provider).ToList(),
        AllowedRoles = client.AllowedRoles.Select(r => r.Role).ToList(),
        AllowedUsers = client.AllowedUsers.Select(u => new AllowedUserDto { SubjectId = u.SubjectId, DisplayName = u.DisplayName }).ToList()
    };

    private static string HashSecret(string secret)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var bytes = System.Text.Encoding.UTF8.GetBytes(secret);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }

    private static string GenerateClientSecret()
    {
        var bytes = new byte[32];
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes);
    }
}

#region DTOs

public class ClientClaimDto
{
    public string Type { get; set; } = null!;
    public string Value { get; set; } = null!;
}

public class ClientPropertyDto
{
    public string Key { get; set; } = null!;
    public string Value { get; set; } = null!;
}

public class AllowedUserDto
{
    public string SubjectId { get; set; } = null!;
    public string? DisplayName { get; set; }
}

public class ClientListDto
{
    public string ClientId { get; set; } = null!;
    public string? ClientName { get; set; }
    public string? Description { get; set; }
    public bool Enabled { get; set; }
    public List<string> AllowedGrantTypes { get; set; } = new();
    public bool RequireClientSecret { get; set; }
    public bool RequirePkce { get; set; }
    public DateTime Created { get; set; }
    public DateTime? Updated { get; set; }
}

public class ClientDetailDto : ClientListDto
{
    // Basic settings
    public string? ClientUri { get; set; }
    public string? LogoUri { get; set; }
    public DateTime? LastAccessed { get; set; }

    // Authentication settings
    public bool AllowPlainTextPkce { get; set; }
    public bool RequireRequestObject { get; set; }
    public bool RequireDPoP { get; set; }
    public bool RequirePushedAuthorization { get; set; }
    public int PushedAuthorizationLifetime { get; set; }

    // Consent settings
    public bool RequireConsent { get; set; }
    public bool AllowRememberConsent { get; set; }
    public int? ConsentLifetime { get; set; }

    // Token settings
    public bool AllowOfflineAccess { get; set; }
    public bool AllowAccessTokensViaBrowser { get; set; }
    public bool AlwaysIncludeUserClaimsInIdToken { get; set; }
    public int AccessTokenLifetime { get; set; }
    public int IdentityTokenLifetime { get; set; }
    public int AuthorizationCodeLifetime { get; set; }
    public int AbsoluteRefreshTokenLifetime { get; set; }
    public int SlidingRefreshTokenLifetime { get; set; }
    public int RefreshTokenUsage { get; set; }
    public int RefreshTokenExpiration { get; set; }
    public bool UpdateAccessTokenClaimsOnRefresh { get; set; }
    public int AccessTokenType { get; set; }
    public string? AllowedIdentityTokenSigningAlgorithms { get; set; }
    public bool IncludeJwtId { get; set; }

    // Client claims settings
    public bool AlwaysSendClientClaims { get; set; }
    public string ClientClaimsPrefix { get; set; } = "client_";
    public string? PairWiseSubjectSalt { get; set; }

    // Logout settings
    public string? FrontChannelLogoutUri { get; set; }
    public bool FrontChannelLogoutSessionRequired { get; set; }
    public string? BackChannelLogoutUri { get; set; }
    public bool BackChannelLogoutSessionRequired { get; set; }

    // SSO and device settings
    public bool EnableLocalLogin { get; set; }
    public int? UserSsoLifetime { get; set; }
    public string? UserCodeType { get; set; }
    public int DeviceCodeLifetime { get; set; }

    // CIBA settings
    public bool CibaEnabled { get; set; }
    public string CibaTokenDeliveryMode { get; set; } = "poll";
    public string? CibaClientNotificationEndpoint { get; set; }
    public int CibaRequestLifetime { get; set; }
    public int CibaPollingInterval { get; set; }
    public bool CibaRequireUserCode { get; set; }

    // Collections
    public List<string> RedirectUris { get; set; } = new();
    public List<string> PostLogoutRedirectUris { get; set; } = new();
    public List<string> AllowedScopes { get; set; } = new();
    public List<string> AllowedCorsOrigins { get; set; } = new();
    public List<ClientClaimDto> Claims { get; set; } = new();
    public List<ClientPropertyDto> Properties { get; set; } = new();
    public List<string> IdentityProviderRestrictions { get; set; } = new();
    public List<string> AllowedRoles { get; set; } = new();
    public List<AllowedUserDto> AllowedUsers { get; set; } = new();
}

public class CreateClientRequest : IClientCollections
{
    // Required
    public string ClientId { get; set; } = null!;

    // Basic settings
    public string? ClientName { get; set; }
    public string? Description { get; set; }
    public string? ClientUri { get; set; }
    public string? LogoUri { get; set; }
    public string? ClientSecret { get; set; }
    public bool? Enabled { get; set; }

    // Authentication settings
    public bool? RequireClientSecret { get; set; }
    public bool? RequirePkce { get; set; }
    public bool? AllowPlainTextPkce { get; set; }
    public bool? RequireRequestObject { get; set; }
    public bool? RequireDPoP { get; set; }
    public bool? RequirePushedAuthorization { get; set; }
    public int? PushedAuthorizationLifetime { get; set; }

    // Consent settings
    public bool? RequireConsent { get; set; }
    public bool? AllowRememberConsent { get; set; }
    public int? ConsentLifetime { get; set; }

    // Token settings
    public bool? AllowOfflineAccess { get; set; }
    public bool? AllowAccessTokensViaBrowser { get; set; }
    public bool? AlwaysIncludeUserClaimsInIdToken { get; set; }
    public int? AccessTokenLifetime { get; set; }
    public int? IdentityTokenLifetime { get; set; }
    public int? AuthorizationCodeLifetime { get; set; }
    public int? AbsoluteRefreshTokenLifetime { get; set; }
    public int? SlidingRefreshTokenLifetime { get; set; }
    public int? RefreshTokenUsage { get; set; }
    public int? RefreshTokenExpiration { get; set; }
    public bool? UpdateAccessTokenClaimsOnRefresh { get; set; }
    public int? AccessTokenType { get; set; }
    public string? AllowedIdentityTokenSigningAlgorithms { get; set; }
    public bool? IncludeJwtId { get; set; }

    // Client claims settings
    public bool? AlwaysSendClientClaims { get; set; }
    public string? ClientClaimsPrefix { get; set; }
    public string? PairWiseSubjectSalt { get; set; }

    // Logout settings
    public string? FrontChannelLogoutUri { get; set; }
    public bool? FrontChannelLogoutSessionRequired { get; set; }
    public string? BackChannelLogoutUri { get; set; }
    public bool? BackChannelLogoutSessionRequired { get; set; }

    // SSO and device settings
    public bool? EnableLocalLogin { get; set; }
    public int? UserSsoLifetime { get; set; }
    public string? UserCodeType { get; set; }
    public int? DeviceCodeLifetime { get; set; }

    // CIBA settings
    public bool? CibaEnabled { get; set; }
    public string? CibaTokenDeliveryMode { get; set; }
    public string? CibaClientNotificationEndpoint { get; set; }
    public int? CibaRequestLifetime { get; set; }
    public int? CibaPollingInterval { get; set; }
    public bool? CibaRequireUserCode { get; set; }

    // Collections
    public ICollection<string>? AllowedGrantTypes { get; set; }
    public ICollection<string>? RedirectUris { get; set; }
    public ICollection<string>? PostLogoutRedirectUris { get; set; }
    public ICollection<string>? AllowedScopes { get; set; }
    public ICollection<string>? AllowedCorsOrigins { get; set; }
    public ICollection<ClientClaimDto>? Claims { get; set; }
    public ICollection<ClientPropertyDto>? Properties { get; set; }
    public ICollection<string>? IdentityProviderRestrictions { get; set; }
    public ICollection<string>? AllowedRoles { get; set; }
    public ICollection<AllowedUserDto>? AllowedUsers { get; set; }
}

public class UpdateClientRequest : IClientCollections
{
    // Basic settings
    public string? ClientName { get; set; }
    public string? Description { get; set; }
    public string? ClientUri { get; set; }
    public string? LogoUri { get; set; }
    public string? ClientSecret { get; set; }
    public bool? Enabled { get; set; }

    // Authentication settings
    public bool? RequireClientSecret { get; set; }
    public bool? RequirePkce { get; set; }
    public bool? AllowPlainTextPkce { get; set; }
    public bool? RequireRequestObject { get; set; }
    public bool? RequireDPoP { get; set; }
    public bool? RequirePushedAuthorization { get; set; }
    public int? PushedAuthorizationLifetime { get; set; }

    // Consent settings
    public bool? RequireConsent { get; set; }
    public bool? AllowRememberConsent { get; set; }
    public int? ConsentLifetime { get; set; }

    // Token settings
    public bool? AllowOfflineAccess { get; set; }
    public bool? AllowAccessTokensViaBrowser { get; set; }
    public bool? AlwaysIncludeUserClaimsInIdToken { get; set; }
    public int? AccessTokenLifetime { get; set; }
    public int? IdentityTokenLifetime { get; set; }
    public int? AuthorizationCodeLifetime { get; set; }
    public int? AbsoluteRefreshTokenLifetime { get; set; }
    public int? SlidingRefreshTokenLifetime { get; set; }
    public int? RefreshTokenUsage { get; set; }
    public int? RefreshTokenExpiration { get; set; }
    public bool? UpdateAccessTokenClaimsOnRefresh { get; set; }
    public int? AccessTokenType { get; set; }
    public string? AllowedIdentityTokenSigningAlgorithms { get; set; }
    public bool? IncludeJwtId { get; set; }

    // Client claims settings
    public bool? AlwaysSendClientClaims { get; set; }
    public string? ClientClaimsPrefix { get; set; }
    public string? PairWiseSubjectSalt { get; set; }

    // Logout settings
    public string? FrontChannelLogoutUri { get; set; }
    public bool? FrontChannelLogoutSessionRequired { get; set; }
    public string? BackChannelLogoutUri { get; set; }
    public bool? BackChannelLogoutSessionRequired { get; set; }

    // SSO and device settings
    public bool? EnableLocalLogin { get; set; }
    public int? UserSsoLifetime { get; set; }
    public string? UserCodeType { get; set; }
    public int? DeviceCodeLifetime { get; set; }

    // CIBA settings
    public bool? CibaEnabled { get; set; }
    public string? CibaTokenDeliveryMode { get; set; }
    public string? CibaClientNotificationEndpoint { get; set; }
    public int? CibaRequestLifetime { get; set; }
    public int? CibaPollingInterval { get; set; }
    public bool? CibaRequireUserCode { get; set; }

    // Collections
    public ICollection<string>? AllowedGrantTypes { get; set; }
    public ICollection<string>? RedirectUris { get; set; }
    public ICollection<string>? PostLogoutRedirectUris { get; set; }
    public ICollection<string>? AllowedScopes { get; set; }
    public ICollection<string>? AllowedCorsOrigins { get; set; }
    public ICollection<ClientClaimDto>? Claims { get; set; }
    public ICollection<ClientPropertyDto>? Properties { get; set; }
    public ICollection<string>? IdentityProviderRestrictions { get; set; }
    public ICollection<string>? AllowedRoles { get; set; }
    public ICollection<AllowedUserDto>? AllowedUsers { get; set; }
}

public class RegenerateSecretResponse
{
    public string ClientId { get; set; } = null!;
    public string ClientSecret { get; set; } = null!;
}

#endregion

using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Oluso.Core.Domain.Entities;

/// <summary>
/// Represents an OAuth 2.0/OpenID Connect client application
/// </summary>
public class Client : TenantEntity
{
    public int Id { get; set; }
    public bool Enabled { get; set; } = true;
    public string ClientId { get; set; } = default!;
    public string ProtocolType { get; set; } = "oidc";
    public ICollection<ClientSecret> ClientSecrets { get; set; } = new List<ClientSecret>();
    public bool RequireClientSecret { get; set; } = true;
    public string? ClientName { get; set; }
    public string? Description { get; set; }
    public string? ClientUri { get; set; }
    public string? LogoUri { get; set; }
    public bool RequireConsent { get; set; } = false;
    public bool AllowRememberConsent { get; set; } = true;
    public bool AlwaysIncludeUserClaimsInIdToken { get; set; } = false;
    public ICollection<ClientGrantType> AllowedGrantTypes { get; set; } = new List<ClientGrantType>();
    public bool RequirePkce { get; set; } = true;
    public bool AllowPlainTextPkce { get; set; } = false;
    public bool RequireRequestObject { get; set; } = false;
    public bool AllowAccessTokensViaBrowser { get; set; } = false;
    public ICollection<ClientRedirectUri> RedirectUris { get; set; } = new List<ClientRedirectUri>();
    public ICollection<ClientPostLogoutRedirectUri> PostLogoutRedirectUris { get; set; } = new List<ClientPostLogoutRedirectUri>();
    public string? FrontChannelLogoutUri { get; set; }
    public bool FrontChannelLogoutSessionRequired { get; set; } = true;
    public string? BackChannelLogoutUri { get; set; }
    public bool BackChannelLogoutSessionRequired { get; set; } = true;
    public bool AllowOfflineAccess { get; set; } = false;
    public ICollection<ClientScope> AllowedScopes { get; set; } = new List<ClientScope>();
    public int IdentityTokenLifetime { get; set; } = 300;
    public string? AllowedIdentityTokenSigningAlgorithms { get; set; }
    public int AccessTokenLifetime { get; set; } = 3600;
    public int AuthorizationCodeLifetime { get; set; } = 300;
    public int? ConsentLifetime { get; set; }
    public int AbsoluteRefreshTokenLifetime { get; set; } = 2592000;
    public int SlidingRefreshTokenLifetime { get; set; } = 1296000;
    public int RefreshTokenUsage { get; set; } = (int)TokenUsage.OneTimeOnly;
    public bool UpdateAccessTokenClaimsOnRefresh { get; set; } = false;
    public int RefreshTokenExpiration { get; set; } = (int)TokenExpiration.Absolute;
    public int AccessTokenType { get; set; } = (int)Entities.AccessTokenType.Jwt;
    public bool EnableLocalLogin { get; set; } = true;
    public ICollection<ClientIdPRestriction> IdentityProviderRestrictions { get; set; } = new List<ClientIdPRestriction>();
    public bool IncludeJwtId { get; set; } = true;
    public ICollection<ClientClaim> Claims { get; set; } = new List<ClientClaim>();
    public bool AlwaysSendClientClaims { get; set; } = false;
    public string ClientClaimsPrefix { get; set; } = "client_";
    public string? PairWiseSubjectSalt { get; set; }
    public int? UserSsoLifetime { get; set; }
    public string? UserCodeType { get; set; }
    public int DeviceCodeLifetime { get; set; } = 300;
    public ICollection<ClientCorsOrigin> AllowedCorsOrigins { get; set; } = new List<ClientCorsOrigin>();
    public ICollection<ClientProperty> Properties { get; set; } = new List<ClientProperty>();
    public DateTime Created { get; set; } = DateTime.UtcNow;
    public DateTime? Updated { get; set; }
    public DateTime? LastAccessed { get; set; }
    public bool NonEditable { get; set; } = false;

    // DPoP settings
    public bool RequireDPoP { get; set; } = false;

    // PAR settings
    public bool RequirePushedAuthorization { get; set; } = false;
    public int PushedAuthorizationLifetime { get; set; } = 60;

    /// <summary>
    /// Whether to use journey-based authentication flow for this client.
    /// null = inherit from tenant setting, true = force journey, false = force standalone
    /// </summary>
    public bool? UseJourneyFlow { get; set; }

    // Access restrictions
    public ICollection<ClientAllowedRole> AllowedRoles { get; set; } = new List<ClientAllowedRole>();
    public ICollection<ClientAllowedUser> AllowedUsers { get; set; } = new List<ClientAllowedUser>();

    // CIBA (Client Initiated Backchannel Authentication) settings
    /// <summary>
    /// Whether CIBA is enabled for this client
    /// </summary>
    public bool CibaEnabled { get; set; } = false;

    /// <summary>
    /// Token delivery mode for CIBA: poll, ping, or push
    /// </summary>
    public string CibaTokenDeliveryMode { get; set; } = "poll";

    /// <summary>
    /// Client notification endpoint for ping/push modes
    /// </summary>
    public string? CibaClientNotificationEndpoint { get; set; }

    /// <summary>
    /// Lifetime of CIBA auth requests in seconds
    /// </summary>
    public int CibaRequestLifetime { get; set; } = 120;

    /// <summary>
    /// Polling interval for CIBA in seconds
    /// </summary>
    public int CibaPollingInterval { get; set; } = 5;

    /// <summary>
    /// Whether user code is required for CIBA
    /// </summary>
    public bool CibaRequireUserCode { get; set; } = false;
}

// Client-related entities
public class ClientSecret
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client Client { get; set; } = default!;
    public string? Description { get; set; }

    /// <summary>
    /// Hashed secret value (SHA-256).
    /// SECURITY: Never expose this in API responses.
    /// </summary>
    [JsonIgnore]
    public string Value { get; set; } = default!;

    public DateTime? Expiration { get; set; }
    public string Type { get; set; } = "SharedSecret";
    public DateTime Created { get; set; } = DateTime.UtcNow;
}

public class ClientGrantType
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client Client { get; set; } = default!;
    public string GrantType { get; set; } = default!;
}

public class ClientRedirectUri
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client Client { get; set; } = default!;
    public string RedirectUri { get; set; } = default!;
}

public class ClientPostLogoutRedirectUri
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client Client { get; set; } = default!;
    public string PostLogoutRedirectUri { get; set; } = default!;
}

public class ClientScope
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client Client { get; set; } = default!;
    public string Scope { get; set; } = default!;
}

public class ClientClaim
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client Client { get; set; } = default!;
    public string Type { get; set; } = default!;
    public string Value { get; set; } = default!;
}

public class ClientCorsOrigin
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client Client { get; set; } = default!;
    public string Origin { get; set; } = default!;
}

public class ClientProperty
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client Client { get; set; } = default!;
    public string Key { get; set; } = default!;
    public string Value { get; set; } = default!;
}

public class ClientIdPRestriction
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client Client { get; set; } = default!;
    public string Provider { get; set; } = default!;
}

public class ClientAllowedRole
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client Client { get; set; } = default!;
    public string Role { get; set; } = default!;
}

public class ClientAllowedUser
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client Client { get; set; } = default!;
    public string SubjectId { get; set; } = default!;
    public string? DisplayName { get; set; }
}

namespace Oluso.Core.Domain.Entities;

/// <summary>
/// Resource (RFC 8707) - represents a protected resource identified by an absolute URI.
/// This replaces the Duende-style ApiResource with a spec-compliant implementation.
///
/// Per RFC 8707:
/// - Resource indicators are absolute URIs without fragment components
/// - Authorization servers MAY validate resources against known/registered resources
/// - Tokens SHOULD be audience-restricted to the requested resource(s)
/// </summary>
public class Resource : TenantEntity
{
    public int Id { get; set; }

    /// <summary>
    /// Whether this resource is enabled. Disabled resources will be rejected.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The absolute URI identifying this resource (RFC 8707).
    /// Must be a valid absolute URI without a fragment component.
    /// Example: https://api.example.com or https://api.example.com/v1
    /// </summary>
    public string Uri { get; set; } = default!;

    /// <summary>
    /// Human-readable display name for the resource.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Description of the resource for admin UI.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Whether to show this resource in discovery document.
    /// </summary>
    public bool ShowInDiscoveryDocument { get; set; } = true;

    /// <summary>
    /// Scopes that are valid for this resource.
    /// If empty, all scopes are allowed for this resource.
    /// </summary>
    public ICollection<ResourceScope> AllowedScopes { get; set; } = new List<ResourceScope>();

    /// <summary>
    /// Additional claims to include in tokens for this resource.
    /// </summary>
    public ICollection<ResourceClaim> UserClaims { get; set; } = new List<ResourceClaim>();

    public DateTime Created { get; set; } = DateTime.UtcNow;
    public DateTime? Updated { get; set; }
    public DateTime? LastAccessed { get; set; }
    public bool NonEditable { get; set; } = false;
}

/// <summary>
/// Scope allowed for a specific resource
/// </summary>
public class ResourceScope
{
    public int Id { get; set; }
    public int ResourceId { get; set; }
    public Resource Resource { get; set; } = default!;
    public string Scope { get; set; } = default!;
}

/// <summary>
/// Additional claim to include in tokens for a resource
/// </summary>
public class ResourceClaim
{
    public int Id { get; set; }
    public int ResourceId { get; set; }
    public Resource Resource { get; set; } = default!;
    public string Type { get; set; } = default!;
}

/// <summary>
/// API Scope - a scope that can be requested by clients
/// </summary>
public class ApiScope : TenantEntity
{
    public int Id { get; set; }
    public bool Enabled { get; set; } = true;
    public string Name { get; set; } = default!;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public bool Required { get; set; } = false;
    public bool Emphasize { get; set; } = false;
    public bool ShowInDiscoveryDocument { get; set; } = true;
    public DateTime Created { get; set; } = DateTime.UtcNow;
    public DateTime? Updated { get; set; }
    public bool NonEditable { get; set; } = false;

    public ICollection<ApiScopeClaim> UserClaims { get; set; } = new List<ApiScopeClaim>();
    public ICollection<ApiScopeProperty> Properties { get; set; } = new List<ApiScopeProperty>();
}

public class ApiScopeClaim
{
    public int Id { get; set; }
    public int ScopeId { get; set; }
    public ApiScope Scope { get; set; } = default!;
    public string Type { get; set; } = default!;
}

public class ApiScopeProperty
{
    public int Id { get; set; }
    public int ScopeId { get; set; }
    public ApiScope Scope { get; set; } = default!;
    public string Key { get; set; } = default!;
    public string Value { get; set; } = default!;
}

/// <summary>
/// Identity Resource - represents identity data like profile, email, etc.
/// </summary>
public class IdentityResource : TenantEntity
{
    public int Id { get; set; }
    public bool Enabled { get; set; } = true;
    public string Name { get; set; } = default!;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public bool Required { get; set; } = false;
    public bool Emphasize { get; set; } = false;
    public bool ShowInDiscoveryDocument { get; set; } = true;
    public DateTime Created { get; set; } = DateTime.UtcNow;
    public DateTime? Updated { get; set; }
    public bool NonEditable { get; set; } = false;

    public ICollection<IdentityResourceClaim> UserClaims { get; set; } = new List<IdentityResourceClaim>();
    public ICollection<IdentityResourceProperty> Properties { get; set; } = new List<IdentityResourceProperty>();
}

public class IdentityResourceClaim
{
    public int Id { get; set; }
    public int IdentityResourceId { get; set; }
    public IdentityResource IdentityResource { get; set; } = default!;
    public string Type { get; set; } = default!;
}

public class IdentityResourceProperty
{
    public int Id { get; set; }
    public int IdentityResourceId { get; set; }
    public IdentityResource IdentityResource { get; set; } = default!;
    public string Key { get; set; } = default!;
    public string Value { get; set; } = default!;
}

/// <summary>
/// External identity provider configuration
/// </summary>
public class IdentityProvider : TenantEntity
{
    public int Id { get; set; }
    public string Scheme { get; set; } = default!;
    public string? DisplayName { get; set; }
    public bool Enabled { get; set; } = true;
    public ExternalProviderType ProviderType { get; set; }
    public string? IconUrl { get; set; }
    public int DisplayOrder { get; set; }
    public string? Properties { get; set; }
    public bool NonEditable { get; set; }
    public List<string> AllowedClientIds { get; set; } = new();
    public DateTime Created { get; set; } = DateTime.UtcNow;
    public DateTime? Updated { get; set; }
    public DateTime? LastAccessed { get; set; }

    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Gets the typed configuration from Properties JSON
    /// </summary>
    public T? GetConfiguration<T>() where T : class
    {
        if (string.IsNullOrEmpty(Properties))
            return null;

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<T>(Properties, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Sets the configuration as Properties JSON
    /// </summary>
    public void SetConfiguration<T>(T configuration) where T : class
    {
        Properties = System.Text.Json.JsonSerializer.Serialize(configuration, JsonOptions);
    }
}

public enum ExternalProviderType
{
    Google,
    Microsoft,
    Facebook,
    Apple,
    GitHub,
    LinkedIn,
    Twitter,
    Oidc,
    OAuth2,
    Saml2,
    Ldap
}



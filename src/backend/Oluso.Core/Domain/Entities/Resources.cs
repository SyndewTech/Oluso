namespace Oluso.Core.Domain.Entities;

/// <summary>
/// API Resource - represents an API being protected
/// </summary>
public class ApiResource : TenantEntity
{
    public int Id { get; set; }
    public bool Enabled { get; set; } = true;
    public string Name { get; set; } = default!;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? AllowedAccessTokenSigningAlgorithms { get; set; }
    public bool ShowInDiscoveryDocument { get; set; } = true;
    public bool RequireResourceIndicator { get; set; } = false;
    public DateTime Created { get; set; } = DateTime.UtcNow;
    public DateTime? Updated { get; set; }
    public DateTime? LastAccessed { get; set; }
    public bool NonEditable { get; set; } = false;

    public ICollection<ApiResourceSecret> Secrets { get; set; } = new List<ApiResourceSecret>();
    public ICollection<ApiResourceScope> Scopes { get; set; } = new List<ApiResourceScope>();
    public ICollection<ApiResourceClaim> UserClaims { get; set; } = new List<ApiResourceClaim>();
    public ICollection<ApiResourceProperty> Properties { get; set; } = new List<ApiResourceProperty>();
}

public class ApiResourceSecret
{
    public int Id { get; set; }
    public int ApiResourceId { get; set; }
    public ApiResource ApiResource { get; set; } = default!;
    public string? Description { get; set; }
    public string Value { get; set; } = default!;
    public DateTime? Expiration { get; set; }
    public string Type { get; set; } = "SharedSecret";
    public DateTime Created { get; set; } = DateTime.UtcNow;
}

public class ApiResourceScope
{
    public int Id { get; set; }
    public int ApiResourceId { get; set; }
    public ApiResource ApiResource { get; set; } = default!;
    public string Scope { get; set; } = default!;
}

public class ApiResourceClaim
{
    public int Id { get; set; }
    public int ApiResourceId { get; set; }
    public ApiResource ApiResource { get; set; } = default!;
    public string Type { get; set; } = default!;
}

public class ApiResourceProperty
{
    public int Id { get; set; }
    public int ApiResourceId { get; set; }
    public ApiResource ApiResource { get; set; } = default!;
    public string Key { get; set; } = default!;
    public string Value { get; set; } = default!;
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



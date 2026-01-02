using Oluso.Core.Domain.Entities;

namespace Oluso.Enterprise.Ldap.Entities;

/// <summary>
/// Represents an LDAP service account for programmatic access to the LDAP server.
/// Service accounts can bind to the LDAP server with specific permissions.
/// Extends TenantEntity for automatic tenant query filtering.
/// </summary>
public class LdapServiceAccount : TenantEntity
{
    /// <summary>
    /// Unique identifier for the service account.
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Human-readable name for the service account.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Description of what this service account is used for.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// The bind DN for this service account.
    /// Format: cn=name,ou=services,o=tenantId,dc=oluso,dc=local
    /// </summary>
    public string BindDn { get; set; } = string.Empty;

    /// <summary>
    /// Hashed password for the service account.
    /// </summary>
    public string PasswordHash { get; set; } = string.Empty;

    /// <summary>
    /// Whether this service account is enabled.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Permission level for this service account.
    /// </summary>
    public LdapServiceAccountPermission Permission { get; set; } = LdapServiceAccountPermission.ReadOnly;

    /// <summary>
    /// Comma-separated list of OUs this service account can access.
    /// Empty/null means all OUs are accessible (within permission level).
    /// </summary>
    public string? AllowedOus { get; set; }

    /// <summary>
    /// Comma-separated list of IP addresses or CIDR ranges allowed to use this service account.
    /// Empty/null means all IPs are allowed.
    /// </summary>
    public string? AllowedIpRanges { get; set; }

    /// <summary>
    /// Maximum number of search results this account can retrieve.
    /// Null/0 means use tenant/global default.
    /// </summary>
    public int MaxSearchResults { get; set; }

    /// <summary>
    /// Rate limit: maximum binds per minute.
    /// 0 means no rate limiting.
    /// </summary>
    public int RateLimitPerMinute { get; set; }

    /// <summary>
    /// When this service account was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this service account was last modified.
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// When this service account was last used for a bind operation.
    /// </summary>
    public DateTime? LastUsedAt { get; set; }

    /// <summary>
    /// Optional expiration date for the service account.
    /// </summary>
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// Check if the service account is expired.
    /// </summary>
    public bool IsExpired => ExpiresAt.HasValue && ExpiresAt.Value < DateTime.UtcNow;

    /// <summary>
    /// Helper to get allowed OUs as a list.
    /// </summary>
    public List<string> GetAllowedOusList()
    {
        if (string.IsNullOrEmpty(AllowedOus)) return new List<string>();
        return AllowedOus.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .ToList();
    }

    /// <summary>
    /// Helper to get allowed IP ranges as a list.
    /// </summary>
    public List<string> GetAllowedIpRangesList()
    {
        if (string.IsNullOrEmpty(AllowedIpRanges)) return new List<string>();
        return AllowedIpRanges.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .ToList();
    }
}

/// <summary>
/// Permission levels for LDAP service accounts.
/// </summary>
[System.Text.Json.Serialization.JsonConverter(typeof(System.Text.Json.Serialization.JsonStringEnumConverter))]
public enum LdapServiceAccountPermission
{
    /// <summary>
    /// Can only search and read entries.
    /// </summary>
    ReadOnly = 0,

    /// <summary>
    /// Can search users and groups.
    /// </summary>
    SearchOnly = 1,

    /// <summary>
    /// Full read access including all attributes.
    /// </summary>
    FullRead = 2
}

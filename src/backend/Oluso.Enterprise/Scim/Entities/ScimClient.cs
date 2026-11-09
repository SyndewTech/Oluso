using Oluso.Core.Domain.Entities;

namespace Oluso.Enterprise.Scim.Entities;

/// <summary>
/// Represents a SCIM client application that can provision users/groups
/// </summary>
public class ScimClient : TenantEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Display name for the client (e.g., "Azure AD", "Okta")
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional description
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Whether this client is enabled
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Hashed API token for authentication
    /// </summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>
    /// Optional OAuth client (ClientId) linked to this SCIM client. When set,
    /// the provisioning endpoints additionally accept a platform-issued JWT
    /// access token (OAuth 2.0 client_credentials) whose <c>client_id</c> claim
    /// matches this value, in addition to the static bearer token above.
    /// </summary>
    public string? OAuthClientId { get; set; }

    /// <summary>
    /// When the token was last rotated
    /// </summary>
    public DateTime TokenCreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the token expires (null = never)
    /// </summary>
    public DateTime? TokenExpiresAt { get; set; }

    /// <summary>
    /// Comma-separated list of allowed IP addresses/CIDR ranges (null = all allowed)
    /// </summary>
    public string? AllowedIpRanges { get; set; }

    /// <summary>
    /// Rate limit (requests per minute, 0 = unlimited)
    /// </summary>
    public int RateLimitPerMinute { get; set; } = 60;

    /// <summary>
    /// Whether this client can create users
    /// </summary>
    public bool CanCreateUsers { get; set; } = true;

    /// <summary>
    /// Whether this client can update users
    /// </summary>
    public bool CanUpdateUsers { get; set; } = true;

    /// <summary>
    /// Whether this client can delete/deactivate users
    /// </summary>
    public bool CanDeleteUsers { get; set; } = true;

    /// <summary>
    /// Whether this client can manage groups
    /// </summary>
    public bool CanManageGroups { get; set; } = true;

    /// <summary>
    /// Custom attribute mappings (JSON)
    /// </summary>
    public string? AttributeMappings { get; set; }

    /// <summary>
    /// Default role to assign to provisioned users (role ID)
    /// </summary>
    public string? DefaultRoleId { get; set; }

    /// <summary>
    /// When this client was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this client was last updated
    /// </summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// When this client last made a request
    /// </summary>
    public DateTime? LastActivityAt { get; set; }

    /// <summary>
    /// Total number of successful provisioning operations
    /// </summary>
    public long SuccessCount { get; set; }

    /// <summary>
    /// Total number of failed provisioning operations
    /// </summary>
    public long ErrorCount { get; set; }
}

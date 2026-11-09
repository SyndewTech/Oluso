using System.Text.Json.Serialization;

namespace Oluso.Core.Domain.Entities;

/// <summary>
/// Role within an organization
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OrganizationRole
{
    /// <summary>
    /// Full control: billing, delete org, manage all settings
    /// </summary>
    Owner = 0,

    /// <summary>
    /// Manage members, tenants, and settings (no billing/delete)
    /// </summary>
    Admin = 1,

    /// <summary>
    /// Access allowed tenants only
    /// </summary>
    Member = 2
}

/// <summary>
/// Status of an organization invitation
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum InvitationStatus
{
    Pending = 0,
    Accepted = 1,
    Declined = 2,
    Expired = 3,
    Revoked = 4
}

/// <summary>
/// Environment type for a tenant (dev, staging, production, etc.)
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TenantEnvironment
{
    /// <summary>
    /// Development environment for testing
    /// </summary>
    Development = 0,

    /// <summary>
    /// Quality assurance / testing environment
    /// </summary>
    QA = 1,

    /// <summary>
    /// Staging environment (pre-production)
    /// </summary>
    Staging = 2,

    /// <summary>
    /// User acceptance testing environment
    /// </summary>
    UAT = 3,

    /// <summary>
    /// Production environment
    /// </summary>
    Production = 4
}

namespace Oluso.Core;

/// <summary>
/// Constants for platform-level resources.
/// </summary>
public static class PlatformConstants
{
    /// <summary>
    /// The reserved tenant ID for platform-level entities.
    /// Platform entities are invisible to regular tenants and only
    /// accessible via explicit queries or protocol fallback.
    /// </summary>
    public const string TenantId = "_platform";

    /// <summary>
    /// Display name for the platform tenant.
    /// </summary>
    public const string TenantName = "Platform";

    /// <summary>
    /// The reserved organization ID for platform-level entities.
    /// </summary>
    public const string OrganizationId = "_platform-org";

    /// <summary>
    /// Display name for the platform organization.
    /// </summary>
    public const string OrganizationName = "Platform";
}

using Oluso.Core.Domain.Entities;

namespace Oluso.Enterprise.Scim.Entities;

/// <summary>
/// Log entry for SCIM provisioning operations
/// </summary>
public class ScimProvisioningLog : TenantEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string ScimClientId { get; set; } = string.Empty;

    /// <summary>
    /// HTTP method (GET, POST, PUT, PATCH, DELETE)
    /// </summary>
    public string Method { get; set; } = string.Empty;

    /// <summary>
    /// Request path
    /// </summary>
    public string Path { get; set; } = string.Empty;

    /// <summary>
    /// Resource type (User, Group)
    /// </summary>
    public string? ResourceType { get; set; }

    /// <summary>
    /// Resource ID if applicable
    /// </summary>
    public string? ResourceId { get; set; }

    /// <summary>
    /// Operation type (Create, Update, Delete, Read, Search)
    /// </summary>
    public string Operation { get; set; } = string.Empty;

    /// <summary>
    /// HTTP status code returned
    /// </summary>
    public int StatusCode { get; set; }

    /// <summary>
    /// Whether the operation was successful
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if failed
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Request body (may be truncated for large payloads)
    /// </summary>
    public string? RequestBody { get; set; }

    /// <summary>
    /// Response body (may be truncated for large payloads)
    /// </summary>
    public string? ResponseBody { get; set; }

    /// <summary>
    /// Client IP address
    /// </summary>
    public string? ClientIp { get; set; }

    /// <summary>
    /// Request duration in milliseconds
    /// </summary>
    public long DurationMs { get; set; }

    /// <summary>
    /// When this operation occurred
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

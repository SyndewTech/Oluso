using Oluso.Core.Domain.Entities;

namespace Oluso.Enterprise.Scim.Entities;

/// <summary>
/// One outbound HTTP call to a SCIM provider.
/// </summary>
public class ScimProviderLog : TenantEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string ScimProviderId { get; set; } = string.Empty;

    /// <summary>
    /// What we were doing: Lookup, Create, Update, Deactivate, Delete, Test, Discover.
    /// </summary>
    public string Operation { get; set; } = string.Empty;

    public string? ResourceType { get; set; }

    public string? InternalId { get; set; }

    public string? RemoteId { get; set; }

    public string Method { get; set; } = string.Empty;

    public string Path { get; set; } = string.Empty;

    public int StatusCode { get; set; }

    public bool Success { get; set; }

    public string? ErrorMessage { get; set; }

    public string? RequestBody { get; set; }

    public string? ResponseBody { get; set; }

    public long DurationMs { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

namespace Oluso.Core.Protocols;

/// <summary>
/// Stores protocol state across authentication redirects
/// </summary>
public interface IProtocolStateStore
{
    /// <summary>
    /// Store protocol state and return correlation ID
    /// </summary>
    Task<string> StoreAsync(
        ProtocolState state,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieve protocol state by correlation ID
    /// </summary>
    Task<ProtocolState?> GetAsync(
        string correlationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove protocol state (after successful callback)
    /// </summary>
    Task RemoveAsync(
        string correlationId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Protocol state stored during authentication
/// </summary>
public class ProtocolState
{
    /// <summary>
    /// Protocol name (oidc, saml, etc.)
    /// </summary>
    public string ProtocolName { get; init; } = null!;

    /// <summary>
    /// Serialized protocol request (JSON)
    /// </summary>
    public string SerializedRequest { get; init; } = null!;

    /// <summary>
    /// Client ID
    /// </summary>
    public string ClientId { get; init; } = null!;

    /// <summary>
    /// Tenant ID (if multi-tenant)
    /// </summary>
    public string? TenantId { get; init; }

    /// <summary>
    /// Creation timestamp
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Endpoint type
    /// </summary>
    public EndpointType EndpointType { get; init; }

    /// <summary>
    /// Additional protocol-specific properties
    /// </summary>
    public IDictionary<string, string> Properties { get; init; } = new Dictionary<string, string>();
}

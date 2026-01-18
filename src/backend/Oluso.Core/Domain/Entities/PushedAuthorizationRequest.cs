namespace Oluso.Core.Domain.Entities;

/// <summary>
/// Represents a Pushed Authorization Request (PAR) - RFC 9126
/// </summary>
public class PushedAuthorizationRequest : TenantEntity
{
    public long Id { get; set; }
    public string RequestUri { get; set; } = default!;
    public string ReferenceValueHash { get; set; } = default!;
    public string ClientId { get; set; } = default!;
    public string Parameters { get; set; } = default!;
    public DateTime CreationTime { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}

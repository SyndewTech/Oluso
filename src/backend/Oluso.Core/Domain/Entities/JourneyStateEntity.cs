namespace Oluso.Core.Domain.Entities;

/// <summary>
/// Persisted journey state for in-progress authentication flows
/// </summary>
public class JourneyStateEntity : TenantEntity
{
    public string Id { get; set; } = default!;
    public string ClientId { get; set; } = default!;
    public string? UserId { get; set; }
    public string PolicyId { get; set; } = default!;
    public string CurrentStepId { get; set; } = default!;
    public string Status { get; set; } = default!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ExpiresAt { get; set; }

    /// <summary>
    /// JSON-serialized journey data
    /// </summary>
    public string? Data { get; set; }

    /// <summary>
    /// JSON-serialized claims bag
    /// </summary>
    public string? ClaimsBag { get; set; }

    public string? SessionId { get; set; }
    public string? AuthenticatedUserId { get; set; }
    public string? CorrelationId { get; set; }
    public string? CallbackUrl { get; set; }
}

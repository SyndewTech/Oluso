namespace Oluso.Core.Domain.Entities;

/// <summary>
/// One previously used password hash for a user, kept so the tenant's password history
/// policy can refuse reuse. Only the Identity password hash is stored — never the password —
/// and a user keeps at most as many rows as their tenant's history count.
/// </summary>
public class PasswordHistoryEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string UserId { get; set; } = null!;

    /// <summary>The hash exactly as Identity's password hasher produced it.</summary>
    public string PasswordHash { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

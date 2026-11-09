namespace Oluso.Account.Dto.Activity;

/// <summary>
/// One entry of the signed-in user's own account activity: an audit row where they
/// are the subject (sign-in, failed sign-in, lockout, password/profile change, consent).
/// </summary>
public class AccountActivityItemDto
{
    public long Id { get; set; }
    public DateTime Timestamp { get; set; }
    public string? Action { get; set; }
    public string? Category { get; set; }
    public string? EventType { get; set; }
    public string? ResourceType { get; set; }
    public string? ResourceName { get; set; }
    public string? ClientId { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public bool Success { get; set; }
}

public class AccountActivityListDto
{
    /// <summary>False when audit logging is not enabled on this deployment; <see cref="Items"/> is then empty.</summary>
    public bool Enabled { get; set; }
    public List<AccountActivityItemDto> Items { get; set; } = new();
}

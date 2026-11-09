namespace Oluso.Admin.Dto.SigningKeys;

public class SigningKeyDto
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string KeyId { get; set; } = null!;
    public string KeyType { get; set; } = null!;
    public string Algorithm { get; set; } = null!;
    public string Use { get; set; } = null!;
    public int KeySize { get; set; }
    public string? ClientId { get; set; }
    public string Status { get; set; } = null!;
    public string StorageProvider { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevocationReason { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public long SignatureCount { get; set; }
    public int Priority { get; set; }
    public bool IncludeInJwks { get; set; }
    public bool CanSign { get; set; }
    public bool CanVerify { get; set; }
    public bool IsExpiringSoon { get; set; }
    public bool IsExpired { get; set; }
}

public class UpdateKeyStatusDto
{
    public string? Status { get; set; }
    public int? Priority { get; set; }
    public bool? IncludeInJwks { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

public class KeyRotationConfigDto
{
    public string? ClientId { get; set; }
    public bool? Enabled { get; set; }
    public string? KeyType { get; set; }
    public string? Algorithm { get; set; }
    public int? KeySize { get; set; }
    public int? KeyLifetimeDays { get; set; }
    public int? RotationLeadDays { get; set; }
    public int? GracePeriodDays { get; set; }
    public int? MaxKeys { get; set; }
    public DateTime? LastRotationAt { get; set; }
    public DateTime? NextRotationAt { get; set; }
}

public class GenerateKeyRequestDto
{
    public string? ClientId { get; set; }
    public string? Name { get; set; }
    public string? KeyType { get; set; }
    public string? Algorithm { get; set; }
    public int? KeySize { get; set; }
    public int? LifetimeDays { get; set; }
    public bool? ActivateImmediately { get; set; }
    public int? Priority { get; set; }
}

public class RevokeKeyRequestDto
{
    public string? Reason { get; set; }
}

namespace Oluso.Account.Dto.Tenants;

/// <summary>
/// Result of switching tenant context. Carries a freshly minted access token
/// scoped to the target tenant — the caller must replace the token it holds,
/// because the previous one still carries the previous tenant's roles.
/// </summary>
public class TenantSwitchDto
{
    public string AccessToken { get; set; } = null!;

    /// <summary>"DPoP" when the token is bound to the caller's DPoP key, as the token it replaces was; else "Bearer".</summary>
    public string TokenType { get; set; } = "Bearer";

    /// <summary>Access token lifetime in seconds.</summary>
    public int ExpiresIn { get; set; }

    public UserTenantDto Tenant { get; set; } = null!;
}

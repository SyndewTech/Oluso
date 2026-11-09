namespace Oluso.Account.Dto.Tenants;

public class TenantValidationDto
{
    public bool IsValid { get; set; }
    public string? Error { get; set; }
    public UserTenantDto? Tenant { get; set; }
}

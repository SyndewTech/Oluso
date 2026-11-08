namespace Oluso.Account.Dto.Profile;

public class UserTenantsDto
{
    public string? CurrentTenantId { get; set; }
    public List<TenantInfoDto> Tenants { get; set; } = new();
    public bool IsMultiTenant { get; set; }
}

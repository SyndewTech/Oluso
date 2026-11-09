namespace Oluso.Account.Dto.Tenants;

public class TenantListDto
{
    public List<UserTenantDto> Tenants { get; set; } = new();
    public string? CurrentTenantId { get; set; }
    public bool IsMultiTenant { get; set; }
}

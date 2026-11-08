namespace Oluso.Account.Dto.Tenants;

public class UserTenantDto
{
    public string Id { get; set; } = null!;
    public string Identifier { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? LogoUrl { get; set; }
    public bool IsCurrent { get; set; }
}

namespace Oluso.Account.Dto.Profile;

public class TenantInfoDto
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? LogoUrl { get; set; }
}

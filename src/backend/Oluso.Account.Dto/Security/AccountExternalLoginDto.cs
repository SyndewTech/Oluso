namespace Oluso.Account.Dto.Security;

public class AccountExternalLoginDto
{
    public string Provider { get; set; } = null!;
    public string ProviderKey { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
}

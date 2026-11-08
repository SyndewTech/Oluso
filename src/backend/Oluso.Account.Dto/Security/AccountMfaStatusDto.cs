namespace Oluso.Account.Dto.Security;

public class AccountMfaStatusDto
{
    public bool Enabled { get; set; }
    public bool TwoFactorEnabled { get; set; }
    public List<string> AvailableMethods { get; set; } = new();
    public List<string> EnabledMethods { get; set; } = new();
    public bool HasTotp { get; set; }
    public bool HasSms { get; set; }
    public bool HasEmail { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
}

namespace Oluso.Account.Dto.Security;

public class SmsSetupDto
{
    public string MaskedPhone { get; set; } = null!;
    public bool CodeSent { get; set; }
}

namespace Oluso.Account.Dto.Security;

public class PhoneVerificationSentDto
{
    public string MaskedPhone { get; set; } = null!;
    public bool CodeSent { get; set; }
}

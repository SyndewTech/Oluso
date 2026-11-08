namespace Oluso.Account.Dto.Security;

public class EmailVerificationSentDto
{
    public string MaskedEmail { get; set; } = null!;
    public bool CodeSent { get; set; }
}

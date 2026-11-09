namespace Oluso.Account.Dto.Security;

public class EmailSetupDto
{
    public string MaskedEmail { get; set; } = null!;
    public bool CodeSent { get; set; }
}

namespace Oluso.Account.Dto.Security;

public class MfaEnableResultDto
{
    public bool Success { get; set; }
    public List<string>? RecoveryCodes { get; set; }
}

namespace Oluso.Account.Dto.Security;

public class TotpSetupDto
{
    public string Secret { get; set; } = null!;
    public string QrCodeUri { get; set; } = null!;

    /// <summary>The QR code for <see cref="QrCodeUri"/> as a PNG data URI, rendered on the server.</summary>
    public string QrCodeImage { get; set; } = null!;
}

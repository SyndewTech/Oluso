using QRCoder;

namespace Oluso.Core.Security;

/// <summary>
/// Renders QR codes on the server. Payloads such as <c>otpauth://</c> URIs carry secrets, so they must
/// never be sent to a third-party image service or a client-side script loaded from a CDN.
/// </summary>
public static class QrCodeImages
{
    /// <summary>A <c>data:image/png;base64,…</c> URI for the payload, or an empty string when there is none.</summary>
    public static string ToPngDataUri(string? payload, int pixelsPerModule = 6)
    {
        if (string.IsNullOrEmpty(payload)) return string.Empty;

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        var png = new PngByteQRCode(data).GetGraphic(pixelsPerModule);
        return "data:image/png;base64," + Convert.ToBase64String(png);
    }
}

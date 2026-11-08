using FluentAssertions;
using Oluso.Core.Security;
using Xunit;

namespace Oluso.Core.Tests.Security;

public class QrCodeImagesTests
{
    [Fact]
    public void Renders_a_png_data_uri_on_the_server()
    {
        var uri = QrCodeImages.ToPngDataUri("otpauth://totp/Oluso:ada%40example.com?secret=JBSWY3DPEHPK3PXP&issuer=Oluso&digits=6");

        uri.Should().StartWith("data:image/png;base64,");
        var png = Convert.FromBase64String(uri["data:image/png;base64,".Length..]);
        png.Take(8).Should().Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void No_payload_gives_no_image(string? payload)
    {
        QrCodeImages.ToPngDataUri(payload).Should().BeEmpty();
    }
}

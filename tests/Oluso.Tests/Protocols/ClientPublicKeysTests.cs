using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using Oluso.Core.Protocols.Models;
using Xunit;

namespace Oluso.Tests.Protocols;

public class ClientPublicKeysTests
{
    [Fact]
    public void A_public_rsa_or_ec_jwk_is_read_with_its_kid()
    {
        using var rsa = RSA.Create(2048);
        var p = rsa.ExportParameters(false);
        var (json, kid, error) = ClientPublicKeys.ReadJwk(JsonSerializer.Serialize(new
        {
            kty = "RSA", kid = "k1", n = Base64UrlEncoder.Encode(p.Modulus), e = Base64UrlEncoder.Encode(p.Exponent)
        }));
        error.Should().BeNull();
        kid.Should().Be("k1");
        json.Should().Contain("\"kty\":\"RSA\"");

        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var q = ec.ExportParameters(false);
        ClientPublicKeys.ReadJwk(JsonSerializer.Serialize(new
        {
            kty = "EC", crv = "P-256", x = Base64UrlEncoder.Encode(q.Q.X), y = Base64UrlEncoder.Encode(q.Q.Y)
        })).Error.Should().BeNull();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("""{"kty":"oct","k":"c2VjcmV0c2VjcmV0c2VjcmV0c2VjcmV0c2VjcmV0"}""")]
    [InlineData("""{"kty":"RSA","e":"AQAB"}""")]
    [InlineData("""{"kty":"EC","crv":"P-256","x":"abc"}""")]
    public void Anything_but_a_complete_public_rsa_or_ec_jwk_is_refused(string json)
    {
        ClientPublicKeys.ReadJwk(json).Error.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void A_jwk_with_any_private_member_is_refused()
    {
        using var rsa = RSA.Create(2048);
        var p = rsa.ExportParameters(true);
        // Only one private member: JsonWebKey.HasPrivateKey alone would not see it.
        ClientPublicKeys.ReadJwk(JsonSerializer.Serialize(new
        {
            kty = "RSA", n = Base64UrlEncoder.Encode(p.Modulus), e = Base64UrlEncoder.Encode(p.Exponent), p = Base64UrlEncoder.Encode(p.P)
        })).Error.Should().Contain("public");
    }

    [Fact]
    public void A_certificate_is_read_from_pem_or_base64_der()
    {
        using var rsa = RSA.Create(2048);
        var certificate = SelfSigned(rsa, DateTimeOffset.UtcNow.AddDays(30));

        var fromPem = ClientPublicKeys.ReadCertificate(certificate.ExportCertificatePem());
        var fromDer = ClientPublicKeys.ReadCertificate(Convert.ToBase64String(certificate.RawData));

        fromPem.Error.Should().BeNull();
        fromPem.Base64Der.Should().Be(Convert.ToBase64String(certificate.RawData));
        fromPem.Thumbprint.Should().Be(Convert.ToHexString(SHA256.HashData(certificate.RawData)));
        fromPem.NotAfter.Should().BeCloseTo(certificate.NotAfter.ToUniversalTime(), TimeSpan.FromSeconds(1));
        fromDer.Base64Der.Should().Be(fromPem.Base64Der);
    }

    [Fact]
    public void A_certificate_with_its_private_key_or_past_its_expiry_is_refused()
    {
        using var rsa = RSA.Create(2048);
        var certificate = SelfSigned(rsa, DateTimeOffset.UtcNow.AddDays(30));

        ClientPublicKeys.ReadCertificate(certificate.ExportCertificatePem() + "\n" + rsa.ExportPkcs8PrivateKeyPem())
            .Error.Should().Contain("private key");
        ClientPublicKeys.ReadCertificate(SelfSigned(rsa, DateTimeOffset.UtcNow.AddDays(-1)).ExportCertificatePem())
            .Error.Should().Contain("expired");
        ClientPublicKeys.ReadCertificate("not a certificate").Error.Should().NotBeNullOrEmpty();
    }

    private static X509Certificate2 SelfSigned(RSA rsa, DateTimeOffset notAfter)
    {
        var request = new CertificateRequest("CN=client.example", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var created = request.CreateSelfSigned(notAfter.AddDays(-60), notAfter);
        return X509CertificateLoader.LoadCertificate(created.Export(X509ContentType.Cert));
    }
}

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Oluso.Core.Protocols;
using Oluso.Protocols.Mtls;
using Xunit;

namespace Oluso.Tests.Protocols.Mtls;

public class ClientCertificateExtractorTests : IDisposable
{
    private readonly X509Certificate2 _testCert;
    private readonly string _certBase64Der;
    private readonly string _certUrlEncodedPem;

    public ClientCertificateExtractorTests()
    {
        _testCert = CreateSelfSignedCertificate("CN=test-client.example.com");

        // Pre-compute common encodings
        _certBase64Der = Convert.ToBase64String(_testCert.RawData);

        var pem = new string(PemEncoding.Write("CERTIFICATE", _testCert.RawData));
        _certUrlEncodedPem = HttpUtility.UrlEncode(pem);
    }

    public void Dispose()
    {
        _testCert.Dispose();
    }

    #region Connection Mode

    [Fact]
    public void Extract_ConnectionMode_ReturnsCertFromConnection()
    {
        var options = new MtlsOptions
        {
            Enabled = true,
            CertificateSource = MtlsCertificateSource.Connection
        };
        var extractor = CreateExtractor(options);
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.ClientCertificate = _testCert;

        var result = extractor.Extract(httpContext);

        result.Should().NotBeNull();
        result!.Thumbprint.Should().Be(_testCert.Thumbprint);
    }

    [Fact]
    public void Extract_ConnectionMode_NoCertOnConnection_ReturnsNull()
    {
        var options = new MtlsOptions
        {
            Enabled = true,
            CertificateSource = MtlsCertificateSource.Connection
        };
        var extractor = CreateExtractor(options);
        var httpContext = new DefaultHttpContext();

        var result = extractor.Extract(httpContext);

        result.Should().BeNull();
    }

    #endregion

    #region Header Mode - Base64 DER

    [Fact]
    public void Extract_HeaderMode_Base64Der_ExtractsCertFromHeader()
    {
        var options = new MtlsOptions
        {
            Enabled = true,
            CertificateSource = MtlsCertificateSource.Header,
            ProxyCertificateHeaderName = "X-Client-Cert",
            ProxyCertificateFormat = MtlsProxyCertificateFormat.Base64Der
        };
        var extractor = CreateExtractor(options);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Client-Cert"] = _certBase64Der;

        var result = extractor.Extract(httpContext);

        result.Should().NotBeNull();
        result!.Thumbprint.Should().Be(_testCert.Thumbprint);
    }

    #endregion

    #region Header Mode - URL-Encoded PEM

    [Fact]
    public void Extract_HeaderMode_UrlEncodedPem_ExtractsCertFromHeader()
    {
        var options = new MtlsOptions
        {
            Enabled = true,
            CertificateSource = MtlsCertificateSource.Header,
            ProxyCertificateHeaderName = "X-SSL-Client-Cert",
            ProxyCertificateFormat = MtlsProxyCertificateFormat.UrlEncodedPem
        };
        var extractor = CreateExtractor(options);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-SSL-Client-Cert"] = _certUrlEncodedPem;

        var result = extractor.Extract(httpContext);

        result.Should().NotBeNull();
        result!.Thumbprint.Should().Be(_testCert.Thumbprint);
    }

    #endregion

    #region Auto Mode

    [Fact]
    public void Extract_AutoMode_ConnectionCertTakesPrecedenceOverHeader()
    {
        using var otherCert = CreateSelfSignedCertificate("CN=other.example.com");
        var otherBase64Der = Convert.ToBase64String(otherCert.RawData);

        var options = new MtlsOptions
        {
            Enabled = true,
            CertificateSource = MtlsCertificateSource.Auto,
            ProxyCertificateHeaderName = "X-Client-Cert",
            ProxyCertificateFormat = MtlsProxyCertificateFormat.Base64Der
        };
        var extractor = CreateExtractor(options);
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.ClientCertificate = _testCert;
        httpContext.Request.Headers["X-Client-Cert"] = otherBase64Der;

        var result = extractor.Extract(httpContext);

        result.Should().NotBeNull();
        result!.Thumbprint.Should().Be(_testCert.Thumbprint, "connection cert should take precedence");
    }

    [Fact]
    public void Extract_AutoMode_FallsBackToHeaderWhenNoConnectionCert()
    {
        var options = new MtlsOptions
        {
            Enabled = true,
            CertificateSource = MtlsCertificateSource.Auto,
            ProxyCertificateHeaderName = "X-Client-Cert",
            ProxyCertificateFormat = MtlsProxyCertificateFormat.Base64Der
        };
        var extractor = CreateExtractor(options);
        var httpContext = new DefaultHttpContext();
        // No connection cert
        httpContext.Request.Headers["X-Client-Cert"] = _certBase64Der;

        var result = extractor.Extract(httpContext);

        result.Should().NotBeNull();
        result!.Thumbprint.Should().Be(_testCert.Thumbprint);
    }

    #endregion

    #region Auto-Detect Well-Known Headers

    [Fact]
    public void Extract_HeaderMode_AutoDetectsCloudflareHeader()
    {
        var options = new MtlsOptions
        {
            Enabled = true,
            CertificateSource = MtlsCertificateSource.Header
            // No explicit header name — should auto-detect
        };
        var extractor = CreateExtractor(options);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["Cf-Client-Cert-Der-Base64"] = _certBase64Der;

        var result = extractor.Extract(httpContext);

        result.Should().NotBeNull();
        result!.Thumbprint.Should().Be(_testCert.Thumbprint);
    }

    [Fact]
    public void Extract_HeaderMode_AutoDetectsAwsAlbHeader()
    {
        var options = new MtlsOptions
        {
            Enabled = true,
            CertificateSource = MtlsCertificateSource.Header
        };
        var extractor = CreateExtractor(options);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Amzn-Mtls-Clientcert"] = _certUrlEncodedPem;

        var result = extractor.Extract(httpContext);

        result.Should().NotBeNull();
        result!.Thumbprint.Should().Be(_testCert.Thumbprint);
    }

    [Fact]
    public void Extract_HeaderMode_AutoDetectsAzureAppServiceHeader()
    {
        var options = new MtlsOptions
        {
            Enabled = true,
            CertificateSource = MtlsCertificateSource.Header
        };
        var extractor = CreateExtractor(options);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-ARR-ClientCert"] = _certBase64Der;

        var result = extractor.Extract(httpContext);

        result.Should().NotBeNull();
        result!.Thumbprint.Should().Be(_testCert.Thumbprint);
    }

    #endregion

    #region Explicit Header Override

    [Fact]
    public void Extract_HeaderMode_ExplicitHeaderOverridesAutoDetect()
    {
        using var otherCert = CreateSelfSignedCertificate("CN=other.example.com");
        var otherBase64Der = Convert.ToBase64String(otherCert.RawData);

        var options = new MtlsOptions
        {
            Enabled = true,
            CertificateSource = MtlsCertificateSource.Header,
            ProxyCertificateHeaderName = "X-My-Custom-Cert",
            ProxyCertificateFormat = MtlsProxyCertificateFormat.Base64Der
        };
        var extractor = CreateExtractor(options);
        var httpContext = new DefaultHttpContext();
        // Cloudflare header has the "other" cert
        httpContext.Request.Headers["Cf-Client-Cert-Der-Base64"] = otherBase64Der;
        // Custom header has the "test" cert
        httpContext.Request.Headers["X-My-Custom-Cert"] = _certBase64Der;

        var result = extractor.Extract(httpContext);

        result.Should().NotBeNull();
        result!.Thumbprint.Should().Be(_testCert.Thumbprint,
            "configured header should take precedence over auto-detected headers");
    }

    #endregion

    #region No Certificate Available

    [Fact]
    public void Extract_NoCertificateAvailable_ReturnsNull()
    {
        var options = new MtlsOptions
        {
            Enabled = true,
            CertificateSource = MtlsCertificateSource.Auto
        };
        var extractor = CreateExtractor(options);
        var httpContext = new DefaultHttpContext();

        var result = extractor.Extract(httpContext);

        result.Should().BeNull();
    }

    [Fact]
    public void Extract_HeaderMode_EmptyHeader_ReturnsNull()
    {
        var options = new MtlsOptions
        {
            Enabled = true,
            CertificateSource = MtlsCertificateSource.Header,
            ProxyCertificateHeaderName = "X-Client-Cert",
            ProxyCertificateFormat = MtlsProxyCertificateFormat.Base64Der
        };
        var extractor = CreateExtractor(options);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Client-Cert"] = "";

        var result = extractor.Extract(httpContext);

        result.Should().BeNull();
    }

    #endregion

    #region Malformed Header

    [Fact]
    public void Extract_HeaderMode_MalformedBase64_ReturnsNull()
    {
        var options = new MtlsOptions
        {
            Enabled = true,
            CertificateSource = MtlsCertificateSource.Header,
            ProxyCertificateHeaderName = "X-Client-Cert",
            ProxyCertificateFormat = MtlsProxyCertificateFormat.Base64Der
        };
        var extractor = CreateExtractor(options);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Client-Cert"] = "not-valid-base64!!!";

        var result = extractor.Extract(httpContext);

        result.Should().BeNull();
    }

    [Fact]
    public void Extract_HeaderMode_ValidBase64ButNotACert_ReturnsNull()
    {
        var options = new MtlsOptions
        {
            Enabled = true,
            CertificateSource = MtlsCertificateSource.Header,
            ProxyCertificateHeaderName = "X-Client-Cert",
            ProxyCertificateFormat = MtlsProxyCertificateFormat.Base64Der
        };
        var extractor = CreateExtractor(options);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Client-Cert"] = Convert.ToBase64String(new byte[] { 1, 2, 3, 4, 5 });

        var result = extractor.Extract(httpContext);

        result.Should().BeNull();
    }

    [Fact]
    public void Extract_HeaderMode_MalformedPem_ReturnsNull()
    {
        var options = new MtlsOptions
        {
            Enabled = true,
            CertificateSource = MtlsCertificateSource.Header,
            ProxyCertificateHeaderName = "X-Client-Cert",
            ProxyCertificateFormat = MtlsProxyCertificateFormat.UrlEncodedPem
        };
        var extractor = CreateExtractor(options);
        var httpContext = new DefaultHttpContext();
        httpContext.Request.Headers["X-Client-Cert"] = HttpUtility.UrlEncode("-----BEGIN CERTIFICATE-----\ngarbage\n-----END CERTIFICATE-----");

        var result = extractor.Extract(httpContext);

        result.Should().BeNull();
    }

    #endregion

    #region Helpers

    private static ClientCertificateExtractor CreateExtractor(MtlsOptions options)
    {
        return new ClientCertificateExtractor(options, NullLogger<ClientCertificateExtractor>.Instance);
    }

    private static X509Certificate2 CreateSelfSignedCertificate(string subjectName)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(subjectName, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddYears(1));
    }

    #endregion
}

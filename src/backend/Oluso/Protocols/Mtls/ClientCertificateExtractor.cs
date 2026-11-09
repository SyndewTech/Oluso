using System.Security.Cryptography.X509Certificates;
using System.Web;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Oluso.Core.Protocols;

namespace Oluso.Protocols.Mtls;

public class ClientCertificateExtractor : IClientCertificateExtractor
{
    private readonly MtlsOptions _options;
    private readonly ILogger<ClientCertificateExtractor> _logger;

    private static readonly (string Header, MtlsProxyCertificateFormat Format)[] WellKnownHeaders =
    [
        ("Cf-Client-Cert-Der-Base64", MtlsProxyCertificateFormat.Base64Der),
        ("X-Amzn-Mtls-Clientcert", MtlsProxyCertificateFormat.UrlEncodedPem),
        ("X-ARR-ClientCert", MtlsProxyCertificateFormat.Base64Der),
        ("X-SSL-Client-Cert", MtlsProxyCertificateFormat.UrlEncodedPem),
        ("X-Client-Cert", MtlsProxyCertificateFormat.Base64Der),
    ];

    public ClientCertificateExtractor(MtlsOptions options, ILogger<ClientCertificateExtractor> logger)
    {
        _options = options;
        _logger = logger;
    }

    public X509Certificate2? Extract(HttpContext httpContext)
    {
        return _options.CertificateSource switch
        {
            MtlsCertificateSource.Connection => ExtractFromConnection(httpContext),
            MtlsCertificateSource.Header => ExtractFromHeaders(httpContext),
            _ => ExtractFromConnection(httpContext) ?? ExtractFromHeaders(httpContext)
        };
    }

    private static X509Certificate2? ExtractFromConnection(HttpContext httpContext)
    {
        return httpContext.Connection.ClientCertificate;
    }

    private X509Certificate2? ExtractFromHeaders(HttpContext httpContext)
    {
        if (!string.IsNullOrEmpty(_options.ProxyCertificateHeaderName))
        {
            var value = httpContext.Request.Headers[_options.ProxyCertificateHeaderName].FirstOrDefault();
            return ParseCertificate(value, _options.ProxyCertificateFormat);
        }

        foreach (var (header, format) in WellKnownHeaders)
        {
            var value = httpContext.Request.Headers[header].FirstOrDefault();
            if (!string.IsNullOrEmpty(value))
            {
                var cert = ParseCertificate(value, format);
                if (cert != null)
                {
                    _logger.LogDebug("Client certificate extracted from auto-detected header {Header}", header);
                    return cert;
                }
            }
        }

        return null;
    }

    private X509Certificate2? ParseCertificate(string? value, MtlsProxyCertificateFormat format)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        try
        {
            var derBytes = format switch
            {
                MtlsProxyCertificateFormat.Base64Der => Convert.FromBase64String(value.Trim()),
                MtlsProxyCertificateFormat.UrlEncodedPem => PemToBytes(HttpUtility.UrlDecode(value)),
                MtlsProxyCertificateFormat.Pem => PemToBytes(value),
                _ => throw new ArgumentOutOfRangeException(nameof(format))
            };

            return new X509Certificate2(derBytes);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to parse client certificate from header");
            return null;
        }
    }

    private static byte[] PemToBytes(string pem)
    {
        var base64 = pem
            .Replace("-----BEGIN CERTIFICATE-----", "")
            .Replace("-----END CERTIFICATE-----", "")
            .Replace("\n", "")
            .Replace("\r", "")
            .Replace(" ", "")
            .Trim();
        return Convert.FromBase64String(base64);
    }
}

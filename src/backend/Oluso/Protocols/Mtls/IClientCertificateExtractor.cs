using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Http;

namespace Oluso.Protocols.Mtls;

public interface IClientCertificateExtractor
{
    X509Certificate2? Extract(HttpContext httpContext);
}

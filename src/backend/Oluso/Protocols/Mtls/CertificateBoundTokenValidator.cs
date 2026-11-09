using Microsoft.AspNetCore.Http;
using Oluso.Protocols.Validation;

namespace Oluso.Protocols.Mtls;

/// <summary>
/// Enforces certificate-bound access tokens where they are used (RFC 8705 §3): a token whose <c>cnf</c>
/// carries <c>x5t#S256</c> is accepted only on a request presenting the client certificate with that
/// SHA-256 thumbprint, so a copied token is useless without the certificate's private key.
/// </summary>
public interface ICertificateBoundTokenValidator
{
    /// <summary>Why the request may not use a token bound to <paramref name="boundThumbprint"/>, or null when it may
    /// (always, for an unbound token).</summary>
    string? Problem(HttpContext context, string? boundThumbprint);
}

public sealed class CertificateBoundTokenValidator : ICertificateBoundTokenValidator
{
    private readonly IClientCertificateExtractor _certificates;

    public CertificateBoundTokenValidator(IClientCertificateExtractor certificates) => _certificates = certificates;

    public string? Problem(HttpContext context, string? boundThumbprint)
    {
        if (string.IsNullOrEmpty(boundThumbprint)) return null;
        var certificate = _certificates.Extract(context);
        if (certificate == null)
            return "The access token is bound to a client certificate; present that certificate on the request.";
        return ClientAuthenticator.ComputeSha256CertThumbprint(certificate) == boundThumbprint
            ? null
            : "The client certificate is not the one the access token is bound to.";
    }
}

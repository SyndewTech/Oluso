namespace Oluso.Core.Protocols;

/// <summary>
/// Configuration for Mutual TLS (mTLS) client authentication per RFC 8705.
/// </summary>
public class MtlsOptions
{
    /// <summary>
    /// Whether mTLS client authentication is enabled.
    /// When false, tls_client_auth and self_signed_tls_client_auth are not
    /// advertised in discovery and the authenticator skips certificate checks.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Where to read the client certificate from.
    /// <list type="bullet">
    ///   <item><see cref="MtlsCertificateSource.Auto"/>: try the TLS connection
    ///     first, then fall back to proxy headers (default).</item>
    ///   <item><see cref="MtlsCertificateSource.Connection"/>: only
    ///     <c>HttpContext.Connection.ClientCertificate</c>.</item>
    ///   <item><see cref="MtlsCertificateSource.Header"/>: only the configured
    ///     (or auto-detected) proxy header.</item>
    /// </list>
    /// </summary>
    public MtlsCertificateSource CertificateSource { get; set; } = MtlsCertificateSource.Auto;

    /// <summary>
    /// Explicit proxy header name that carries the forwarded client certificate.
    /// When set, only this header is checked (auto-detection is skipped).
    /// When null, well-known proxy headers are tried in priority order:
    /// Cf-Client-Cert-Der-Base64, X-Amzn-Mtls-Clientcert, X-ARR-ClientCert,
    /// X-SSL-Client-Cert, X-Client-Cert.
    /// </summary>
    public string? ProxyCertificateHeaderName { get; set; }

    /// <summary>
    /// Format of the certificate value in the proxy header.
    /// Only used when <see cref="ProxyCertificateHeaderName"/> is explicitly set.
    /// For auto-detected headers the format is inferred per proxy.
    /// </summary>
    public MtlsProxyCertificateFormat ProxyCertificateFormat { get; set; } = MtlsProxyCertificateFormat.Base64Der;

    /// <summary>
    /// Whether to bind access tokens to the client certificate by adding
    /// a <c>cnf</c> claim with <c>x5t#S256</c> (SHA-256 certificate thumbprint).
    /// Defaults to true when mTLS is enabled.
    /// </summary>
    public bool AllowCertificateBoundAccessTokens { get; set; } = true;
}

public enum MtlsCertificateSource
{
    Auto,
    Connection,
    Header
}

public enum MtlsProxyCertificateFormat
{
    Base64Der,
    UrlEncodedPem,
    Pem
}

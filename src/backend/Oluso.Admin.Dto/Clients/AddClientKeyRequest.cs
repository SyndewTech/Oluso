namespace Oluso.Admin.Dto.Clients;

/// <summary>
/// A public key for a client to sign its JWTs with (private_key_jwt client assertions, request objects), or a
/// certificate for self-signed mutual TLS. Only public material is accepted.
/// </summary>
public class AddClientKeyRequest
{
    /// <summary><c>jwk</c>: a public RSA or EC JWK as JSON. <c>certificate</c>: an X.509 certificate, PEM or base64 DER.</summary>
    public string Type { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>When the key stops being accepted; a certificate's own expiry applies when it is sooner.</summary>
    public DateTime? Expiration { get; set; }
}

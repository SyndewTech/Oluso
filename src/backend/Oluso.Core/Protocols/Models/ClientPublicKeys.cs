using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace Oluso.Core.Protocols.Models;

/// <summary>
/// The public keys a client can register to sign its JWTs (private_key_jwt client assertions, RFC 7523; request
/// objects, RFC 9101) or, as a certificate, for self-signed mutual TLS (RFC 8705): a public RSA or EC JWK, or an X.509
/// certificate. Each is stored as one of the client's secrets of <see cref="JsonWebKeyType"/> or
/// <see cref="CertificateType"/>. A private or symmetric key is never accepted: the server holds only what verifies.
/// </summary>
public static class ClientPublicKeys
{
    /// <summary>A client secret holding a public JWK (its JSON).</summary>
    public const string JsonWebKeyType = "JsonWebKey";

    /// <summary>A client secret holding an X.509 certificate (base64 DER).</summary>
    public const string CertificateType = "X509CertificateBase64";

    /// <summary>The description of a key a dynamic registration's jwks holds; its updates replace only these.</summary>
    public const string RegisteredDescription = "Registered JWK";

    /// <summary>JWK members that hold private or symmetric key material (RFC 7518 sections 6.2.2, 6.3.2 and 6.4).</summary>
    private static readonly string[] PrivateJwkMembers = { "d", "p", "q", "dp", "dq", "qi", "oth", "k" };

    /// <summary>Why <paramref name="element"/> is not a public RSA or EC JWK, or null when it is one.</summary>
    public static string? PublicJwkError(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return "The key is not a JWK (a JSON object)";
        }

        JsonWebKey jwk;
        try
        {
            jwk = new JsonWebKey(element.GetRawText());
        }
        catch (Exception ex) when (ex is ArgumentException or JsonException)
        {
            return "The key is not a valid JWK";
        }

        if (jwk.Kty is not ("RSA" or "EC"))
        {
            return "The key must be an RSA or EC public key";
        }

        // Any private (or symmetric) member makes it not a public key. JsonWebKey.HasPrivateKey is not enough on its
        // own: it reports an RSA key as private only when every private member is present.
        if (PrivateJwkMembers.Any(member => element.TryGetProperty(member, out _)))
        {
            return "The key must be a public key only";
        }

        var complete = jwk.Kty == "RSA"
            ? !string.IsNullOrEmpty(jwk.N) && !string.IsNullOrEmpty(jwk.E)
            : !string.IsNullOrEmpty(jwk.Crv) && !string.IsNullOrEmpty(jwk.X) && !string.IsNullOrEmpty(jwk.Y);
        return complete ? null : "The key is missing members its type requires";
    }

    /// <summary>
    /// Reads a public JWK from <paramref name="json"/>: its JSON as it will be stored, and its kid, or the reason it is
    /// not a public RSA or EC JWK.
    /// </summary>
    public static (string? Json, string? KeyId, string? Error) ReadJwk(string json)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            return (null, null, "The key is not valid JSON");
        }

        using (document)
        {
            var error = PublicJwkError(document.RootElement);
            if (error != null)
            {
                return (null, null, error);
            }

            var keyId = document.RootElement.TryGetProperty("kid", out var kid) && kid.ValueKind == JsonValueKind.String
                ? kid.GetString()
                : null;
            return (document.RootElement.GetRawText(), keyId, null);
        }
    }

    /// <summary>
    /// Reads an X.509 certificate given as PEM or as base64 DER: its base64 DER as it will be stored, its SHA-256
    /// thumbprint and expiry, or the reason it cannot be used. A certificate carrying a private key is refused.
    /// </summary>
    public static (string? Base64Der, string? Thumbprint, DateTime? NotAfter, string? Error) ReadCertificate(string text)
    {
        var trimmed = text.Trim();
        X509Certificate2 certificate;
        try
        {
            if (trimmed.Contains("PRIVATE KEY", StringComparison.Ordinal))
            {
                return (null, null, null, "Register the certificate only, without its private key");
            }

            certificate = trimmed.StartsWith("-----BEGIN", StringComparison.Ordinal)
                ? X509Certificate2.CreateFromPem(trimmed)
                : X509CertificateLoader.LoadCertificate(Convert.FromBase64String(trimmed));
        }
        catch (Exception ex) when (ex is FormatException or System.Security.Cryptography.CryptographicException or ArgumentException)
        {
            return (null, null, null, "The certificate is not a valid PEM or base64 DER X.509 certificate");
        }

        using (certificate)
        {
            if (certificate.HasPrivateKey)
            {
                return (null, null, null, "Register the certificate only, without its private key");
            }

            if (certificate.NotAfter.ToUniversalTime() <= DateTime.UtcNow)
            {
                return (null, null, null, "The certificate has expired");
            }

            return (Convert.ToBase64String(certificate.RawData),
                certificate.GetCertHashString(System.Security.Cryptography.HashAlgorithmName.SHA256),
                certificate.NotAfter.ToUniversalTime(),
                null);
        }
    }
}

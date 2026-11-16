using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Services;

namespace Oluso.Protocols.Validation;

/// <summary>
/// Finds the key a client signed one of its JWTs with (a client assertion, a request object) among the keys the
/// client registered: a public JWK or certificate for an asymmetric algorithm, the client secret for HMAC. The key
/// must suit the JWT's algorithm, so a key of one kind is never tried with another kind's algorithm.
/// </summary>
internal static class ClientJwtKeys
{
    public const string JsonWebKeySecretType = ClientPublicKeys.JsonWebKeyType;
    public const string CertificateSecretType = ClientPublicKeys.CertificateType;
    public const string SharedSecretType = "SharedSecret";

    /// <summary>
    /// The registered keys that could have signed the JWT (matching its alg, and its kid when present), or null when
    /// there are none. More than one is possible, e.g. while a key or secret is being rotated. For an HMAC algorithm
    /// they are client secrets, usable only when stored recoverable (a hash is never an HMAC key) and long enough
    /// for the algorithm (RFC 7518 section 3.2). <paramref name="publishedKeys"/> are the JWKs the client publishes at
    /// its jwks_uri (<see cref="IClientJwksUriKeys"/>), tried as its registered JWKs are.
    /// </summary>
    public static (IReadOnlyList<SecurityKey> Keys, ClientAuthenticationMethod Method)? Find(
        Client client,
        JwtSecurityToken jwt,
        IClientSecretProtector secrets,
        IReadOnlyList<string>? publishedKeys = null)
    {
        var algorithm = jwt.Header.Alg;
        var kid = jwt.Header.Kid;

        if (IsHmac(algorithm))
        {
            return FindSharedSecret(client, algorithm, secrets);
        }

        var keyType = AsymmetricKeyType(algorithm);
        if (keyType == null)
        {
            return null;
        }

        var keys = new List<SecurityKey>();

        var candidates = client.ClientSecrets
            .Concat((publishedKeys ?? Array.Empty<string>())
                .Select(json => new ClientSecret { Type = JsonWebKeySecretType, Value = json }));
        foreach (var secret in candidates)
        {
            if (secret.Expiration.HasValue && secret.Expiration.Value < DateTime.UtcNow)
                continue;

            if (secret.Type == JsonWebKeySecretType)
            {
                JsonWebKey jwk;
                try
                {
                    jwk = new JsonWebKey(secret.Value);
                }
                catch
                {
                    continue;
                }

                // A key registered for encryption never verifies a signature (RFC 7517 section 4.2), and a key
                // of another type, or pinned to another algorithm, cannot have made this signature.
                if (string.Equals(jwk.Use, JsonWebKeyUseNames.Enc, StringComparison.Ordinal) ||
                    !string.Equals(jwk.Kty, keyType, StringComparison.Ordinal) ||
                    (!string.IsNullOrEmpty(jwk.Alg) && !string.Equals(jwk.Alg, algorithm, StringComparison.Ordinal)))
                    continue;

                // RFC 7517 section 4.4: "alg" is optional on a JWK. When absent, take the JWT header's so the
                // signature provider can be resolved.
                if (string.IsNullOrEmpty(jwk.Alg) && !string.IsNullOrEmpty(algorithm))
                {
                    jwk.Alg = algorithm;
                }

                if (string.IsNullOrEmpty(kid) || jwk.Kid == kid)
                {
                    keys.Add(jwk);
                }
            }
            else if (secret.Type == CertificateSecretType)
            {
                X509SecurityKey certKey;
                try
                {
                    var certBytes = Convert.FromBase64String(secret.Value);
                    var cert = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadCertificate(certBytes);
                    certKey = new X509SecurityKey(cert);
                }
                catch
                {
                    continue;
                }

                var certKeyType = certKey.Certificate.GetRSAPublicKey() != null ? JsonWebAlgorithmsKeyTypes.RSA
                    : certKey.Certificate.GetECDsaPublicKey() != null ? JsonWebAlgorithmsKeyTypes.EllipticCurve
                    : null;
                if (certKeyType == keyType && (string.IsNullOrEmpty(kid) || certKey.KeyId == kid))
                {
                    keys.Add(certKey);
                }
            }
        }

        return keys.Count > 0 ? (keys, ClientAuthenticationMethod.PrivateKeyJwt) : null;
    }

    /// <summary>Whether the algorithm is an HMAC one, keyed with the client secret.</summary>
    public static bool IsHmac(string? algorithm) =>
        algorithm is SecurityAlgorithms.HmacSha256 or SecurityAlgorithms.HmacSha384 or SecurityAlgorithms.HmacSha512;

    /// <summary>
    /// The client secret as the HMAC key: the UTF-8 octets of the secret itself (OpenID Connect Core 1.0 section
    /// 10.1), never padded, and at least as long as the hash output (RFC 7518 section 3.2).
    /// </summary>
    private static (IReadOnlyList<SecurityKey> Keys, ClientAuthenticationMethod Method)? FindSharedSecret(
        Client client,
        string algorithm,
        IClientSecretProtector secrets)
    {
        var minimumBytes = algorithm switch
        {
            SecurityAlgorithms.HmacSha256 => 32,
            SecurityAlgorithms.HmacSha384 => 48,
            _ => 64
        };

        var keys = new List<SecurityKey>();
        foreach (var secret in client.ClientSecrets)
        {
            if (secret.Type != SharedSecretType ||
                (secret.Expiration.HasValue && secret.Expiration.Value < DateTime.UtcNow))
                continue;

            var value = secrets.Recover(secret.Value);
            if (value == null)
                continue;

            var keyBytes = Encoding.UTF8.GetBytes(value);
            if (keyBytes.Length < minimumBytes)
                continue;

            keys.Add(new SymmetricSecurityKey(keyBytes));
        }

        return keys.Count > 0 ? (keys, ClientAuthenticationMethod.ClientSecretJwt) : null;
    }

    /// <summary>The JWK key type an asymmetric algorithm's key has, or null for an algorithm not handled here.</summary>
    private static string? AsymmetricKeyType(string? algorithm) => algorithm switch
    {
        "RS256" or "RS384" or "RS512" or "PS256" or "PS384" or "PS512" => JsonWebAlgorithmsKeyTypes.RSA,
        "ES256" or "ES384" or "ES512" => JsonWebAlgorithmsKeyTypes.EllipticCurve,
        _ => null
    };
}

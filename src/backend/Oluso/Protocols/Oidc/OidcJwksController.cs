using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Protocols.Oidc;

/// <summary>
/// JSON Web Key Set (JWKS) Endpoint.
/// Route is configured via OidcEndpointRouteConvention.
/// </summary>

public class OidcJwksController : OidcControllerBase
{
    private readonly ISigningCredentialStore _signingCredentialStore;

    public OidcJwksController(ISigningCredentialStore signingCredentialStore)
    {
        _signingCredentialStore = signingCredentialStore;
    }

    [HttpGet]
    public async Task<IActionResult> GetJwks(CancellationToken cancellationToken)
    {
        var keys = await _signingCredentialStore.GetValidationKeysAsync(cancellationToken);
        var jwks = new
        {
            // Public keys only: a symmetric key (e.g. a module's HS256 data-protection key) is a secret, never published.
            keys = keys.Where(k => k.Key is RsaSecurityKey or ECDsaSecurityKey or X509SecurityKey)
                .Select(k => GetJwk(k.Key, k.SigningAlgorithm)).ToArray()
        };

        return Ok(jwks);
    }

    private static object GetJwk(SecurityKey key, string algorithm)
    {
        if (key is RsaSecurityKey rsaKey)
        {
            var parameters = rsaKey.Rsa?.ExportParameters(false)
                ?? throw new InvalidOperationException("Unable to export RSA parameters");

            var kid = rsaKey.KeyId ?? ComputeKid(parameters.Modulus!);

            return new
            {
                kty = "RSA",
                use = "sig",
                kid,
                alg = algorithm,
                n = Base64UrlEncode(parameters.Modulus!),
                e = Base64UrlEncode(parameters.Exponent!)
            };
        }

        if (key is ECDsaSecurityKey ecKey)
        {
            var parameters = ecKey.ECDsa.ExportParameters(false);
            var crv = parameters.Curve.Oid?.FriendlyName switch
            {
                "nistP256" or "ECDSA_P256" => "P-256",
                "nistP384" or "ECDSA_P384" => "P-384",
                "nistP521" or "ECDSA_P521" => "P-521",
                _ => throw new InvalidOperationException($"Unsupported curve: {parameters.Curve.Oid?.FriendlyName}")
            };

            var kid = ecKey.KeyId ?? ComputeKid(parameters.Q.X!);

            return new
            {
                kty = "EC",
                use = "sig",
                kid,
                alg = algorithm,
                crv,
                x = Base64UrlEncode(parameters.Q.X!),
                y = Base64UrlEncode(parameters.Q.Y!)
            };
        }

        if (key is X509SecurityKey x509Key)
        {
            var certKid = x509Key.KeyId ?? Base64UrlEncode(x509Key.Certificate.GetCertHash());

            var rsa = x509Key.Certificate.GetRSAPublicKey();
            if (rsa != null)
            {
                var parameters = rsa.ExportParameters(false);
                return new
                {
                    kty = "RSA",
                    use = "sig",
                    kid = certKid,
                    alg = algorithm,
                    n = Base64UrlEncode(parameters.Modulus!),
                    e = Base64UrlEncode(parameters.Exponent!),
                    x5t = Base64UrlEncode(x509Key.Certificate.GetCertHash())
                };
            }

            var ecdsa = x509Key.Certificate.GetECDsaPublicKey();
            if (ecdsa != null)
            {
                var parameters = ecdsa.ExportParameters(false);
                var crv = parameters.Curve.Oid?.FriendlyName switch
                {
                    "nistP256" or "ECDSA_P256" => "P-256",
                    "nistP384" or "ECDSA_P384" => "P-384",
                    "nistP521" or "ECDSA_P521" => "P-521",
                    _ => throw new InvalidOperationException($"Unsupported curve: {parameters.Curve.Oid?.FriendlyName}")
                };

                return new
                {
                    kty = "EC",
                    use = "sig",
                    kid = certKid,
                    alg = algorithm,
                    crv,
                    x = Base64UrlEncode(parameters.Q.X!),
                    y = Base64UrlEncode(parameters.Q.Y!),
                    x5t = Base64UrlEncode(x509Key.Certificate.GetCertHash())
                };
            }
        }

        throw new InvalidOperationException($"Unsupported key type: {key.GetType().Name}");
    }

    private static string ComputeKid(byte[] keyMaterial)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hash = sha256.ComputeHash(keyMaterial);
        return Base64UrlEncode(hash[..16]);
    }

    private static string Base64UrlEncode(byte[] input)
    {
        return Convert.ToBase64String(input)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}

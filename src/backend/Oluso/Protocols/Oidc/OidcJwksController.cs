using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Protocols.Oidc;

/// <summary>
/// JSON Web Key Set (JWKS) Endpoint.
/// Route is configured via OidcEndpointRouteConvention.
/// </summary>

public class OidcJwksController : ControllerBase
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
            keys = keys.Select(k => GetJwk(k.Key, k.SigningAlgorithm)).ToArray()
        };

        return Ok(jwks);
    }

    private static object GetJwk(SecurityKey key, string algorithm)
    {
        if (key is RsaSecurityKey rsaKey)
        {
            var parameters = rsaKey.Rsa?.ExportParameters(false)
                ?? throw new InvalidOperationException("Unable to export RSA parameters");

            return new
            {
                kty = "RSA",
                use = "sig",
                kid = rsaKey.KeyId,
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

            return new
            {
                kty = "EC",
                use = "sig",
                kid = ecKey.KeyId,
                alg = algorithm,
                crv,
                x = Base64UrlEncode(parameters.Q.X!),
                y = Base64UrlEncode(parameters.Q.Y!)
            };
        }

        if (key is X509SecurityKey x509Key)
        {
            var rsa = x509Key.Certificate.GetRSAPublicKey();
            if (rsa != null)
            {
                var parameters = rsa.ExportParameters(false);
                return new
                {
                    kty = "RSA",
                    use = "sig",
                    kid = x509Key.KeyId,
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
                    kid = x509Key.KeyId,
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

    private static string Base64UrlEncode(byte[] input)
    {
        return Convert.ToBase64String(input)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}

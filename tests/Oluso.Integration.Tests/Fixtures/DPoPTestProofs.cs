using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace Oluso.Integration.Tests.Fixtures;

/// <summary>DPoP proofs (RFC 9449) and token bindings for tests that hold a DPoP key.</summary>
public static class DPoPTestProofs
{
    /// <summary>The <c>cnf.jkt</c> an access token is bound to, or null when it is unbound.</summary>
    public static string? TokenJkt(string accessToken)
    {
        var cnf = new JwtSecurityTokenHandler().ReadJwtToken(accessToken).Claims.FirstOrDefault(c => c.Type == "cnf")?.Value;
        return cnf == null ? null : JsonDocument.Parse(cnf).RootElement.GetProperty("jkt").GetString();
    }

    /// <summary>A DPoP proof (RFC 9449 §4.2) signed by <paramref name="key"/> for one request.</summary>
    public static string Proof(ECDsa key, string method, string path, string? accessToken, string? nonce = null)
    {
        var parameters = key.ExportParameters(false);
        var header = new JwtHeader(new SigningCredentials(new ECDsaSecurityKey(key), SecurityAlgorithms.EcdsaSha256))
        {
            ["typ"] = "dpop+jwt",
            ["jwk"] = new Dictionary<string, object>
            {
                ["kty"] = "EC",
                ["crv"] = "P-256",
                ["x"] = Base64UrlEncoder.Encode(parameters.Q.X!),
                ["y"] = Base64UrlEncoder.Encode(parameters.Q.Y!),
            },
        };
        var payload = new JwtPayload
        {
            ["jti"] = Guid.NewGuid().ToString(),
            ["htm"] = method,
            ["htu"] = $"http://localhost{path}",
            ["iat"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
        };
        if (nonce != null)
            payload["nonce"] = nonce;
        if (accessToken != null)
            payload["ath"] = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(accessToken)));
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(header, payload));
    }
}

using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Oluso.Protocols.DPoP;

namespace Oluso.Tests.Protocols.DPoP;

/// <summary>A device's P-256 DPoP key (RFC 9449) that signs proofs, as a client would.</summary>
internal sealed class DPoPTestKey : IDisposable
{
    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    public DPoPTestKey()
    {
        var p = _key.ExportParameters(false);
        X = Base64UrlEncoder.Encode(p.Q.X!);
        Y = Base64UrlEncoder.Encode(p.Q.Y!);
        Jkt = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.UTF8.GetBytes($"{{\"crv\":\"P-256\",\"kty\":\"EC\",\"x\":\"{X}\",\"y\":\"{Y}\"}}")));
    }

    public string X { get; }
    public string Y { get; }

    /// <summary>RFC 7638 thumbprint.</summary>
    public string Jkt { get; }

    public string Proof(string htm, string htu, string? accessToken = null, string? nonce = null, string? jti = null, DateTimeOffset? iat = null)
    {
        var header = new JwtHeader(new SigningCredentials(new ECDsaSecurityKey(_key), SecurityAlgorithms.EcdsaSha256))
        {
            ["typ"] = "dpop+jwt",
            ["jwk"] = new Dictionary<string, object> { ["kty"] = "EC", ["crv"] = "P-256", ["x"] = X, ["y"] = Y },
        };
        var payload = new JwtPayload
        {
            ["jti"] = jti ?? Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(16)),
            ["htm"] = htm,
            ["htu"] = htu,
            ["iat"] = (iat ?? DateTimeOffset.UtcNow).ToUnixTimeSeconds(),
        };
        if (accessToken != null)
            payload["ath"] = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(accessToken)));
        if (nonce != null)
            payload["nonce"] = nonce;
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(header, payload));
    }

    public void Dispose() => _key.Dispose();

    /// <summary>The real proof validator over an in-memory distributed cache (replay cache included).</summary>
    public static DPoPProofValidator NewValidator(out InMemoryDPoPNonceStore nonces)
    {
        IDistributedCache cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
        nonces = new InMemoryDPoPNonceStore(cache, new LocalDPoPReplayCache(cache));
        return new DPoPProofValidator(nonces, new ConfigurationBuilder().Build(), NullLogger<DPoPProofValidator>.Instance);
    }
}

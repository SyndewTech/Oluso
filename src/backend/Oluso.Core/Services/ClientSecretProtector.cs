using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Protocols.Validation;

namespace Oluso.Core.Services;

/// <summary>
/// How client shared secrets are stored and checked.
/// <list type="bullet">
///   <item>By default a secret is kept only as a one-way SHA-256 hash: it can be checked, never recovered.</item>
///   <item>A client that signs JWTs with its secret (HMAC: <c>client_secret_jwt</c>, or an HS*
///     <c>request_object_signing_alg</c>) needs the secret itself to verify them (OpenID Connect Core 1.0
///     section 10.1), so its secret is encrypted at rest with Data Protection instead, as tenant configuration
///     encrypts SMTP and integration credentials. Only those clients get a recoverable secret.</item>
/// </list>
/// A hash is never usable as an HMAC key, and a stored value that cannot be decrypted matches nothing.
/// </summary>
public interface IClientSecretProtector
{
    /// <summary>The value to store for a new secret: encrypted when <paramref name="recoverable"/>, else hashed.</summary>
    string ToStoredValue(string secret, bool recoverable);

    /// <summary>Whether <paramref name="presentedSecret"/> is the secret <paramref name="storedValue"/> holds.</summary>
    bool Matches(string storedValue, string presentedSecret);

    /// <summary>The secret itself when it was stored recoverable; null for a hashed secret or one that fails to decrypt.</summary>
    string? Recover(string storedValue);
}

/// <inheritdoc />
public class ClientSecretProtector : IClientSecretProtector
{
    private const string Envelope = "enc:v1:";

    private readonly IDataProtector _protector;
    private readonly ILogger<ClientSecretProtector> _logger;

    public ClientSecretProtector(IDataProtectionProvider dataProtectionProvider, ILogger<ClientSecretProtector> logger)
    {
        _protector = dataProtectionProvider.CreateProtector("Oluso.ClientSecrets");
        _logger = logger;
    }

    /// <summary>
    /// Whether the client signs JWTs with its shared secret, so its secret must be stored recoverable.
    /// </summary>
    public static bool UsesHmac(Client client) =>
        UsesHmac(
            client.TokenEndpointAuthMethod,
            client.Properties.FirstOrDefault(p => p.Key == RequestObjectSigningAlgorithms.ClientProperty)?.Value);

    /// <summary>
    /// Whether a client with this token endpoint auth method and request_object_signing_alg signs JWTs with its secret.
    /// </summary>
    public static bool UsesHmac(string? tokenEndpointAuthMethod, string? requestObjectSigningAlg) =>
        string.Equals(tokenEndpointAuthMethod, "client_secret_jwt", StringComparison.Ordinal) ||
        requestObjectSigningAlg?.StartsWith("HS", StringComparison.Ordinal) == true;

    /// <summary>
    /// A new random secret. One that will be an HMAC key is long enough for HS512 (RFC 7518 section 3.2: the key
    /// must be at least as long as the hash output).
    /// </summary>
    public static string GenerateSecret(bool hmac)
    {
        var bytes = RandomNumberGenerator.GetBytes(hmac ? 64 : 32);
        return Convert.ToBase64String(bytes);
    }

    public string ToStoredValue(string secret, bool recoverable) =>
        recoverable ? Envelope + _protector.Protect(secret) : Hash(secret);

    public bool Matches(string storedValue, string presentedSecret)
    {
        if (IsProtected(storedValue))
        {
            var secret = Recover(storedValue);
            return secret != null && FixedTimeEquals(secret, presentedSecret);
        }

        return FixedTimeEquals(storedValue, Hash(presentedSecret));
    }

    public string? Recover(string storedValue)
    {
        if (!IsProtected(storedValue))
        {
            return null;
        }

        try
        {
            return _protector.Unprotect(storedValue[Envelope.Length..]);
        }
        catch (CryptographicException ex)
        {
            // The key ring no longer holds the key it was encrypted with. Fail closed; never log the value.
            _logger.LogError(ex, "A stored client secret could not be decrypted");
            return null;
        }
    }

    private static bool IsProtected(string value) => value.StartsWith(Envelope, StringComparison.Ordinal);

    /// <summary>The SHA-256 hash secrets are stored as (base64), as everywhere else client secrets are hashed.</summary>
    private static string Hash(string secret) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}

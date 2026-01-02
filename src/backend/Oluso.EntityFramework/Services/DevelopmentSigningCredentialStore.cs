using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.EntityFramework.Services;

/// <summary>
/// Development signing credential store that generates and caches an RSA key in memory.
/// For production, use a persistent key store or Azure Key Vault.
/// </summary>
public class DevelopmentSigningCredentialStore : ISigningCredentialStore
{
    private static readonly object _lock = new();
    private static RsaSecurityKey? _signingKey;
    private static SigningCredentials? _signingCredentials;

    public Task<SigningCredentials?> GetSigningCredentialsAsync(CancellationToken cancellationToken = default)
    {
        EnsureKeyExists();
        return Task.FromResult<SigningCredentials?>(_signingCredentials);
    }

    public Task<IEnumerable<SecurityKeyInfo>> GetValidationKeysAsync(CancellationToken cancellationToken = default)
    {
        EnsureKeyExists();

        var keys = new List<SecurityKeyInfo>
        {
            new SecurityKeyInfo
            {
                Key = _signingKey!,
                SigningAlgorithm = SecurityAlgorithms.RsaSha256
            }
        };

        return Task.FromResult<IEnumerable<SecurityKeyInfo>>(keys);
    }

    private static void EnsureKeyExists()
    {
        if (_signingKey != null)
            return;

        lock (_lock)
        {
            if (_signingKey != null)
                return;

            // Generate a new RSA key for development
            var rsa = RSA.Create(2048);
            _signingKey = new RsaSecurityKey(rsa)
            {
                KeyId = Guid.NewGuid().ToString("N")[..16]
            };
            _signingCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256);
        }
    }
}

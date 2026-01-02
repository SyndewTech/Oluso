using Microsoft.IdentityModel.Tokens;

namespace Oluso.Core.Domain.Interfaces;

/// <summary>
/// Interface for signing credential management
/// </summary>
public interface ISigningCredentialStore
{
    Task<SigningCredentials?> GetSigningCredentialsAsync(CancellationToken cancellationToken = default);
    Task<IEnumerable<SecurityKeyInfo>> GetValidationKeysAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Contains information about a security key
/// </summary>
public class SecurityKeyInfo
{
    public SecurityKey Key { get; set; } = default!;
    public string SigningAlgorithm { get; set; } = default!;
}

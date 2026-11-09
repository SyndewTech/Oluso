using Microsoft.IdentityModel.Tokens;

namespace Oluso.Core.Domain.Interfaces;

/// <summary>
/// Interface for signing credential management
/// </summary>
public interface ISigningCredentialStore
{
    /// <summary>Signing credentials of the request's tenant (the platform when there is none).</summary>
    Task<SigningCredentials?> GetSigningCredentialsAsync(CancellationToken cancellationToken = default);

    /// <summary>Signing credentials of <paramref name="tenantId"/>, whatever tenant the request was routed to.</summary>
    Task<SigningCredentials?> GetSigningCredentialsAsync(string tenantId, CancellationToken cancellationToken);
    Task<IEnumerable<SecurityKeyInfo>> GetValidationKeysAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns validation keys for the explicit <paramref name="tenantId"/>
    /// (plus the platform tenant), bypassing the request's tenant context.
    /// Used at JWT validation time where the relevant tenant is the token's
    /// own <c>tenant_id</c> claim, not the host the request happened to land on.
    /// </summary>
    Task<IEnumerable<SecurityKeyInfo>> GetValidationKeysAsync(string? tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validation keys, refreshing any cached set once when <paramref name="requiredKeyId"/>
    /// is not among them — the case right after a key was rotated on another instance.
    /// </summary>
    Task<IEnumerable<SecurityKeyInfo>> GetValidationKeysAsync(string? tenantId, string? requiredKeyId, CancellationToken cancellationToken = default)
        => GetValidationKeysAsync(tenantId, cancellationToken);
}

/// <summary>
/// Contains information about a security key
/// </summary>
public class SecurityKeyInfo
{
    public SecurityKey Key { get; set; } = default!;
    public string SigningAlgorithm { get; set; } = default!;
}

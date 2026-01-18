using Oluso.Enterprise.Fido2.Entities;

namespace Oluso.Enterprise.Fido2.Stores;

/// <summary>
/// Store for FIDO2/WebAuthn credentials
/// </summary>
public interface IFido2CredentialStore
{
    /// <summary>
    /// Get a credential by its ID
    /// </summary>
    Task<Fido2CredentialEntity?> GetByIdAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get a credential by its credential ID (from the authenticator)
    /// </summary>
    Task<Fido2CredentialEntity?> GetByCredentialIdAsync(string credentialId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all credentials for a user
    /// </summary>
    Task<IReadOnlyList<Fido2CredentialEntity>> GetByUserIdAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all active credentials for a user
    /// </summary>
    Task<IReadOnlyList<Fido2CredentialEntity>> GetActiveByUserIdAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a credential ID already exists
    /// </summary>
    Task<bool> ExistsAsync(string credentialId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Store a new credential
    /// </summary>
    Task AddAsync(Fido2CredentialEntity credential, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update an existing credential (counter, last used, etc.)
    /// </summary>
    Task UpdateAsync(Fido2CredentialEntity credential, CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove a credential
    /// </summary>
    Task RemoveAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update the signature counter after successful authentication
    /// </summary>
    Task UpdateCounterAsync(string credentialId, uint newCounter, CancellationToken cancellationToken = default);
}

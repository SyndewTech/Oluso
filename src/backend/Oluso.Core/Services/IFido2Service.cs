namespace Oluso.Core.Services;

/// <summary>
/// Service for FIDO2/WebAuthn operations in User Journeys.
/// Implement this interface to enable passkey authentication.
/// </summary>
/// <remarks>
/// The default implementation is provided by the Oluso.Enterprise.Fido2 package.
/// This interface uses a stateful pattern where assertion/registration IDs are
/// returned to track the ceremony state on the server.
/// </remarks>
public interface IFido2Service
{
    /// <summary>
    /// Creates registration options for a new credential
    /// </summary>
    /// <param name="userId">The user ID to register the credential for</param>
    /// <param name="userName">The username</param>
    /// <param name="displayName">Display name shown during registration</param>
    /// <param name="authenticatorType">Optional: "platform" or "cross-platform"</param>
    /// <param name="requireResidentKey">Whether to require discoverable credentials (passkeys)</param>
    Task<Fido2RegistrationOptions> CreateRegistrationOptionsAsync(
        string userId,
        string userName,
        string displayName,
        string? authenticatorType = null,
        bool? requireResidentKey = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies registration response and stores the credential
    /// </summary>
    /// <param name="registrationId">The registration ID from CreateRegistrationOptionsAsync</param>
    /// <param name="attestationResponse">The JSON response from navigator.credentials.create()</param>
    /// <param name="credentialName">Optional: Friendly name for the credential</param>
    Task<Fido2RegistrationResult> VerifyRegistrationAsync(
        string registrationId,
        string attestationResponse,
        string? credentialName = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates assertion options for authentication
    /// </summary>
    /// <param name="username">Username for username-based flow, null for usernameless (discoverable credential) flow</param>
    Task<Fido2AssertionOptions> CreateAssertionOptionsAsync(
        string? username,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies assertion response
    /// </summary>
    /// <param name="assertionId">The assertion ID from CreateAssertionOptionsAsync</param>
    /// <param name="assertionResponse">The JSON response from navigator.credentials.get()</param>
    Task<Fido2AssertionResult> VerifyAssertionAsync(
        string assertionId,
        string assertionResponse,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets credentials for a user
    /// </summary>
    Task<IEnumerable<Fido2Credential>> GetCredentialsAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a credential
    /// </summary>
    Task<bool> DeleteCredentialAsync(
        string userId,
        string credentialId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates a credential's name
    /// </summary>
    /// <param name="userId">The user who owns the credential</param>
    /// <param name="credentialId">The credential ID to update</param>
    /// <param name="name">The new name for the credential</param>
    Task<bool> UpdateCredentialNameAsync(
        string userId,
        string credentialId,
        string name,
        CancellationToken cancellationToken = default);
}

#region FIDO2 Models

/// <summary>
/// Options for creating a new credential
/// </summary>
public class Fido2RegistrationOptions
{
    /// <summary>Server-side ID to track this registration ceremony</summary>
    public required string RegistrationId { get; init; }

    /// <summary>Base64url-encoded challenge</summary>
    public required string Challenge { get; init; }

    /// <summary>User information for the credential</summary>
    public required Fido2UserInfo User { get; init; }

    /// <summary>Relying party information</summary>
    public required Fido2RelyingParty RelyingParty { get; init; }

    /// <summary>Allowed public key algorithms</summary>
    public IList<Fido2PublicKeyCredentialParameters>? PublicKeyCredentialParameters { get; init; }

    /// <summary>Timeout in milliseconds</summary>
    public int? Timeout { get; init; }

    /// <summary>Attestation conveyance preference (none, indirect, direct, enterprise)</summary>
    public string? AttestationConveyance { get; init; }

    /// <summary>Authenticator selection criteria</summary>
    public Fido2AuthenticatorSelection? AuthenticatorSelection { get; init; }

    /// <summary>Credentials to exclude (already registered)</summary>
    public IList<Fido2CredentialDescriptor>? ExcludeCredentials { get; init; }
}

/// <summary>
/// Result of credential registration
/// </summary>
public class Fido2RegistrationResult
{
    public bool Succeeded { get; init; }
    public string? CredentialId { get; init; }
    public string? PublicKey { get; init; }
    public string? Error { get; init; }
    public string? ErrorDescription { get; init; }

    public static Fido2RegistrationResult Success(string credentialId, string publicKey) =>
        new() { Succeeded = true, CredentialId = credentialId, PublicKey = publicKey };

    public static Fido2RegistrationResult Failed(string error, string? description = null) =>
        new() { Succeeded = false, Error = error, ErrorDescription = description };
}

/// <summary>
/// Options for authenticating with a credential
/// </summary>
public class Fido2AssertionOptions
{
    /// <summary>Server-side ID to track this assertion ceremony</summary>
    public required string AssertionId { get; init; }

    /// <summary>Base64url-encoded challenge</summary>
    public required string Challenge { get; init; }

    /// <summary>Timeout in milliseconds</summary>
    public int? Timeout { get; init; }

    /// <summary>Relying party ID</summary>
    public string? RpId { get; init; }

    /// <summary>Allowed credentials (null for usernameless flow)</summary>
    public IList<Fido2CredentialDescriptor>? AllowCredentials { get; init; }

    /// <summary>User verification requirement (preferred, required, discouraged)</summary>
    public string? UserVerification { get; init; }
}

/// <summary>
/// Result of credential assertion (authentication)
/// </summary>
public class Fido2AssertionResult
{
    public bool Succeeded { get; init; }
    public string? UserId { get; init; }
    public string? CredentialId { get; init; }
    public int? SignCount { get; init; }
    public string? Error { get; init; }
    public string? ErrorDescription { get; init; }

    public static Fido2AssertionResult Success(string userId, string credentialId, int signCount) =>
        new() { Succeeded = true, UserId = userId, CredentialId = credentialId, SignCount = signCount };

    public static Fido2AssertionResult Failed(string error, string? description = null) =>
        new() { Succeeded = false, Error = error, ErrorDescription = description };
}

/// <summary>
/// Stored FIDO2 credential
/// </summary>
public class Fido2Credential
{
    public required string Id { get; init; }
    public required string UserId { get; init; }
    public required string CredentialId { get; init; }
    public required string PublicKey { get; init; }
    public int SignCount { get; init; }
    public string? Name { get; init; }
    public string? AuthenticatorType { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? LastUsedAt { get; init; }
    public bool IsResidentKey { get; init; }
}

/// <summary>
/// User information for FIDO2 registration
/// </summary>
public class Fido2UserInfo
{
    /// <summary>Base64url-encoded user handle</summary>
    public required string Id { get; init; }

    /// <summary>Username</summary>
    public required string Name { get; init; }

    /// <summary>Display name shown during registration</summary>
    public required string DisplayName { get; init; }
}

/// <summary>
/// Relying party information
/// </summary>
public class Fido2RelyingParty
{
    /// <summary>Relying party ID (typically the domain)</summary>
    public required string Id { get; init; }

    /// <summary>Relying party display name</summary>
    public required string Name { get; init; }

    /// <summary>Optional icon URL</summary>
    public string? Icon { get; init; }
}

/// <summary>
/// Public key credential algorithm parameters
/// </summary>
public class Fido2PublicKeyCredentialParameters
{
    public required string Type { get; init; }
    public required int Algorithm { get; init; }
}

/// <summary>
/// Authenticator selection criteria
/// </summary>
public class Fido2AuthenticatorSelection
{
    /// <summary>Authenticator attachment (platform, cross-platform)</summary>
    public string? AuthenticatorAttachment { get; init; }

    /// <summary>Whether to require resident key</summary>
    public bool? RequireResidentKey { get; init; }

    /// <summary>Resident key requirement (discouraged, preferred, required)</summary>
    public string? ResidentKey { get; init; }

    /// <summary>User verification requirement</summary>
    public string? UserVerification { get; init; }
}

/// <summary>
/// Credential descriptor for allow/exclude lists
/// </summary>
public class Fido2CredentialDescriptor
{
    public required string Type { get; init; }

    /// <summary>Base64url-encoded credential ID</summary>
    public required string Id { get; init; }

    /// <summary>Allowed transports (usb, nfc, ble, internal)</summary>
    public IList<string>? Transports { get; init; }
}

/// <summary>
/// Exception for FIDO2 operations
/// </summary>
public class Fido2Exception : Exception
{
    public string? ErrorCode { get; }

    public Fido2Exception(string message, string? errorCode = null) : base(message)
    {
        ErrorCode = errorCode;
    }
}

#endregion

#region ViewModels for UI

/// <summary>
/// View model for FIDO2 assertion (authentication) UI
/// </summary>
public class Fido2AssertionViewModel
{
    public required Fido2AssertionOptions Options { get; init; }
    public required string AssertionId { get; init; }
}

/// <summary>
/// View model for FIDO2 registration UI
/// </summary>
public class Fido2RegistrationViewModel
{
    public required Fido2RegistrationOptions Options { get; init; }
    public required string RegistrationId { get; init; }
}

#endregion

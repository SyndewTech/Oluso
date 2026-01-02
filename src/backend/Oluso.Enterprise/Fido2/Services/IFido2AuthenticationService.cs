using Oluso.Core.Domain.Entities;

namespace Oluso.Enterprise.Fido2.Services;

/// <summary>
/// Service for FIDO2/WebAuthn Passkey operations
/// </summary>
public interface IFido2AuthenticationService
{
    /// <summary>
    /// Create options for registering a new credential
    /// </summary>
    /// <param name="user">The user registering the credential</param>
    /// <param name="authenticatorType">Optional: platform or cross-platform</param>
    /// <param name="requireResidentKey">Whether to require discoverable credentials (passkeys)</param>
    /// <returns>Options to pass to navigator.credentials.create()</returns>
    Task<CredentialCreateOptions> CreateRegistrationOptionsAsync(
        OlusoUser user,
        string? authenticatorType = null,
        bool? requireResidentKey = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verify and store a new credential registration
    /// </summary>
    Task<Fido2RegistrationResult> CompleteRegistrationAsync(
        OlusoUser user,
        AuthenticatorAttestationResponse response,
        string challenge,
        string? displayName = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Create options for authenticating with a credential
    /// </summary>
    /// <param name="username">Optional username to get allowed credentials</param>
    /// <returns>Options to pass to navigator.credentials.get()</returns>
    Task<AssertionOptions> CreateAssertionOptionsAsync(
        string? username = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verify an assertion (authentication attempt)
    /// </summary>
    Task<Fido2AssertionResult> VerifyAssertionAsync(
        AuthenticatorAssertionResponse response,
        string challenge,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Get all credentials for a user
    /// </summary>
    Task<IEnumerable<Fido2Credential>> GetUserCredentialsAsync(
        string userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove a credential
    /// </summary>
    Task<bool> RemoveCredentialAsync(
        string userId,
        string credentialId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Update credential display name
    /// </summary>
    Task<bool> UpdateCredentialNameAsync(
        string userId,
        string credentialId,
        string displayName,
        CancellationToken cancellationToken = default);
}

#region DTOs

/// <summary>
/// Options for creating a new credential
/// </summary>
public class CredentialCreateOptions
{
    public string Challenge { get; set; } = null!;
    public RelyingPartyInfo Rp { get; set; } = null!;
    public UserInfo User { get; set; } = null!;
    public List<PubKeyCredParam> PubKeyCredParams { get; set; } = new();
    public int Timeout { get; set; }
    public string Attestation { get; set; } = "none";
    public AuthenticatorSelection? AuthenticatorSelection { get; set; }
    public List<CredentialDescriptor>? ExcludeCredentials { get; set; }
}

public class RelyingPartyInfo
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Icon { get; set; }
}

public class UserInfo
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string? Icon { get; set; }
}

public class PubKeyCredParam
{
    public string Type { get; set; } = "public-key";
    public int Alg { get; set; }
}

public class AuthenticatorSelection
{
    public string? AuthenticatorAttachment { get; set; }
    public string ResidentKey { get; set; } = "preferred";
    public bool RequireResidentKey { get; set; }
    public string UserVerification { get; set; } = "preferred";
}

public class CredentialDescriptor
{
    public string Type { get; set; } = "public-key";
    public string Id { get; set; } = null!;
    public List<string>? Transports { get; set; }
}

/// <summary>
/// Options for authenticating with a credential
/// </summary>
public class AssertionOptions
{
    public string Challenge { get; set; } = null!;
    public int Timeout { get; set; }
    public string RpId { get; set; } = null!;
    public List<CredentialDescriptor>? AllowCredentials { get; set; }
    public string UserVerification { get; set; } = "preferred";
}

/// <summary>
/// Response from navigator.credentials.create()
/// </summary>
public class AuthenticatorAttestationResponse
{
    public string Id { get; set; } = null!;
    public string RawId { get; set; } = null!;
    public string Type { get; set; } = "public-key";
    public string? AuthenticatorAttachment { get; set; }
    public AttestationResponseData Response { get; set; } = null!;
}

public class AttestationResponseData
{
    public string ClientDataJson { get; set; } = null!;
    public string AttestationObject { get; set; } = null!;
    public List<string>? Transports { get; set; }
}

/// <summary>
/// Response from navigator.credentials.get()
/// </summary>
public class AuthenticatorAssertionResponse
{
    public string Id { get; set; } = null!;
    public string RawId { get; set; } = null!;
    public string Type { get; set; } = "public-key";
    public AssertionResponseData Response { get; set; } = null!;
}

public class AssertionResponseData
{
    public string ClientDataJson { get; set; } = null!;
    public string AuthenticatorData { get; set; } = null!;
    public string Signature { get; set; } = null!;
    public string? UserHandle { get; set; }
}

/// <summary>
/// Result of credential registration
/// </summary>
public class Fido2RegistrationResult
{
    public bool Success { get; set; }
    public string? CredentialId { get; set; }
    public string? Error { get; set; }
    public string? ErrorCode { get; set; }
}

/// <summary>
/// Result of credential assertion (authentication)
/// </summary>
public class Fido2AssertionResult
{
    public bool Success { get; set; }
    public string? UserId { get; set; }
    public string? CredentialId { get; set; }
    public uint SignatureCounter { get; set; }
    public string? Error { get; set; }
    public string? ErrorCode { get; set; }
}

/// <summary>
/// Stored FIDO2 credential
/// </summary>
public class Fido2Credential
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = null!;
    public string CredentialId { get; set; } = null!;
    public string PublicKey { get; set; } = null!;
    public string UserHandle { get; set; } = null!;
    public uint SignatureCounter { get; set; }
    public int CredentialType { get; set; }
    public Guid AaGuid { get; set; }
    public string? DisplayName { get; set; }
    public AuthenticatorType AuthenticatorType { get; set; }
    public string? AttestationFormat { get; set; }
    public bool IsDiscoverable { get; set; }
    public string? Transports { get; set; }
    public string? TenantId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? LastUsedAt { get; set; }
}

public enum AuthenticatorType
{
    Platform,
    CrossPlatform
}

#endregion

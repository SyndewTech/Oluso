namespace Oluso.Core.Services;

/// <summary>
/// Service for multi-factor authentication operations.
/// Implement this interface to customize MFA behavior.
/// </summary>
public interface IMfaService
{
    /// <summary>
    /// Generates TOTP setup information for authenticator apps
    /// </summary>
    Task<MfaSetupResult> GenerateTotpSetupAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies a TOTP code from an authenticator app
    /// </summary>
    Task<bool> VerifyTotpCodeAsync(string userId, string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a verification code via email
    /// </summary>
    Task<bool> SendVerificationCodeAsync(string userId, string provider, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies an email verification code
    /// </summary>
    Task<bool> VerifyEmailCodeAsync(string userId, string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies an SMS verification code
    /// </summary>
    Task<bool> VerifySmsCodeAsync(string userId, string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Enables MFA for a user with the specified provider
    /// </summary>
    Task<MfaEnableResult> EnableMfaAsync(string userId, string provider, CancellationToken cancellationToken = default);

    /// <summary>
    /// Disables MFA for a user
    /// </summary>
    Task<bool> DisableMfaAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets available MFA providers for a user
    /// </summary>
    Task<IEnumerable<string>> GetEnabledProvidersAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates new recovery codes for a user
    /// </summary>
    Task<IEnumerable<string>> GenerateRecoveryCodesAsync(string userId, int count = 10, CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies a recovery code
    /// </summary>
    Task<bool> VerifyRecoveryCodeAsync(string userId, string code, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of MFA setup operation
/// </summary>
public class MfaSetupResult
{
    public bool Succeeded { get; init; }
    public string? SharedKey { get; init; }
    public string? AuthenticatorUri { get; init; }
    public string? Error { get; init; }

    public static MfaSetupResult Success(string sharedKey, string authenticatorUri) =>
        new() { Succeeded = true, SharedKey = sharedKey, AuthenticatorUri = authenticatorUri };

    public static MfaSetupResult Failed(string error) =>
        new() { Succeeded = false, Error = error };
}

/// <summary>
/// Result of enabling MFA
/// </summary>
public class MfaEnableResult
{
    public bool Succeeded { get; init; }
    public IEnumerable<string>? RecoveryCodes { get; init; }
    public string? Error { get; init; }

    public static MfaEnableResult Success(IEnumerable<string>? recoveryCodes = null) =>
        new() { Succeeded = true, RecoveryCodes = recoveryCodes };

    public static MfaEnableResult Failed(string error) =>
        new() { Succeeded = false, Error = error };
}

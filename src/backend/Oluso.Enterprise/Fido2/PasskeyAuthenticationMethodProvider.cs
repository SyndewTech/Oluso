using Oluso.Core.Authentication;
using Oluso.Core.Services;

namespace Oluso.Enterprise.Fido2;

/// <summary>
/// Authentication method provider for FIDO2/WebAuthn Passkey authentication.
/// Registers itself with the authentication method registry to enable
/// dynamic discovery of passkey login and account management features.
/// </summary>
public class PasskeyAuthenticationMethodProvider : IAuthenticationMethodProvider
{
    private readonly IFido2Service _fido2Service;

    public PasskeyAuthenticationMethodProvider(IFido2Service fido2Service)
    {
        _fido2Service = fido2Service;
    }

    /// <inheritdoc />
    public string Id => AuthenticationMethodIds.Passkey;

    /// <inheritdoc />
    public string DisplayName => "Passkey";

    /// <inheritdoc />
    public string Category => AuthenticationMethodCategories.Passwordless;

    /// <inheritdoc />
    public int Order => 10;

    /// <inheritdoc />
    public string? Icon => "fingerprint";

    /// <inheritdoc />
    public bool IsAvailable => true; // Always available when FIDO2 is configured

    #region Login Support

    /// <inheritdoc />
    public bool SupportsLogin => true;

    /// <inheritdoc />
    public string? LoginUrl => "/fido2/login";

    /// <inheritdoc />
    public string? LoginButtonText => "Sign in with Passkey";

    /// <inheritdoc />
    public string? LoginButtonIcon => """<svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M21 2l-2 2m-7.61 7.61a5.5 5.5 0 1 1-7.778 7.778 5.5 5.5 0 0 1 7.777-7.777zm0 0L15.5 7.5m0 0l3 3L22 7l-3-3m-3.5 3.5L19 4"></path></svg>""";

    /// <inheritdoc />
    public string? LoginHint => "Use your fingerprint, face, or security key";

    /// <inheritdoc />
    public string? LoginPartialViewName => "_PasskeyAssertion";

    /// <inheritdoc />
    public async Task<object?> GetLoginViewModelAsync(string? username, CancellationToken cancellationToken = default)
    {
        var options = await _fido2Service.CreateAssertionOptionsAsync(username, cancellationToken);
        return new Fido2AssertionViewModel
        {
            Options = options,
            AssertionId = options.AssertionId
        };
    }

    #endregion

    #region Account Management Support

    /// <inheritdoc />
    public bool SupportsManagement => true;

    /// <inheritdoc />
    public string? ManagementPartialViewName => "_PasskeyManagement";

    /// <inheritdoc />
    public async Task<object?> GetManagementViewModelAsync(string userId, CancellationToken cancellationToken = default)
    {
        var credentials = await _fido2Service.GetCredentialsAsync(userId, cancellationToken);
        return new PasskeyManagementViewModel
        {
            UserId = userId,
            Credentials = credentials.ToList()
        };
    }

    /// <inheritdoc />
    public async Task<int> GetCredentialCountAsync(string userId, CancellationToken cancellationToken = default)
    {
        var credentials = await _fido2Service.GetCredentialsAsync(userId, cancellationToken);
        return credentials.Count();
    }

    #endregion

    #region JavaScript Support

    /// <inheritdoc />
    public IEnumerable<string> RequiredScripts => new[] { "js/passkey.js" };

    /// <inheritdoc />
    public string? InlineScript => null;

    #endregion
}

/// <summary>
/// View model for passkey management in account settings
/// </summary>
public class PasskeyManagementViewModel
{
    public required string UserId { get; init; }
    public List<Fido2Credential> Credentials { get; init; } = new();
}

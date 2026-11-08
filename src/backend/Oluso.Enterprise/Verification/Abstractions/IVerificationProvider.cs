namespace Oluso.Enterprise.Verification;

/// <summary>Which of a provider's environments a tenant's credentials belong to.</summary>
public enum VerificationEnvironment
{
    /// <summary>The provider's test environment: synthetic data, not billed.</summary>
    Sandbox = 0,

    /// <summary>The provider's production environment: authoritative sources, billed.</summary>
    Live = 1,
}

/// <summary>
/// A credential a provider needs, described so the admin UI can ask for it without
/// knowing the provider. Secret fields are stored encrypted and never returned.
/// <see cref="Required"/> fields must be saved before the provider can be enabled; the
/// others serve one capability (e.g. hosted sessions) and are demanded when it's used.
/// </summary>
public sealed record VerificationCredentialField(string Name, string Label, bool Secret, string? Description = null, bool Required = true);

/// <summary>
/// A non-secret choice a provider offers (e.g. which lookup tier to buy). <see cref="Default"/>
/// is the value the admin UI preselects; a saved value outside <see cref="Choices"/> is rejected.
/// </summary>
public sealed record VerificationProviderOption(string Name, string Label, IReadOnlyList<string> Choices, string Default, string? Description = null);

/// <summary>A tenant's credentials for one provider, decrypted, ready for a call.</summary>
public sealed record VerificationProviderConnection(
    string TenantId,
    VerificationEnvironment Environment,
    IReadOnlyDictionary<string, string> Credentials,
    IReadOnlyDictionary<string, string> Options)
{
    /// <summary>The named credential; throws when the tenant hasn't saved it, so a call never goes out half-configured.</summary>
    public string Require(string name) =>
        Credentials.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new VerificationConfigurationException($"The '{name}' credential is not configured.");

    /// <summary>The saved value of an option, which the settings service always fills.</summary>
    public string Option(string name) =>
        Options.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new VerificationConfigurationException($"The '{name}' option is not configured.");
}

/// <summary>The tenant's verification configuration can't serve a request.</summary>
public sealed class VerificationConfigurationException(string message) : Exception(message);

/// <summary>Someone else (the provider, the applicant's browser or another reviewer) decided first; nothing was changed.</summary>
public sealed class VerificationDecisionConflictException(string message) : InvalidOperationException(message);

/// <summary>Result of a credential check that doesn't buy a lookup.</summary>
public sealed record VerificationConnectionCheck(bool Success, string Message);

/// <summary>
/// A verification vendor (KYB, and later KYC). One implementation per vendor; each
/// capability is its own interface so a vendor that does both registers once.
/// </summary>
public interface IVerificationProvider
{
    /// <summary>Stable id stored in tenant settings and on records, e.g. <c>dojah</c>.</summary>
    string Id { get; }

    string DisplayName { get; }

    IReadOnlyList<VerificationCredentialField> CredentialFields { get; }

    IReadOnlyList<VerificationProviderOption> Options { get; }

    /// <summary>
    /// Confirms the credentials authenticate against the chosen environment using an
    /// endpoint the vendor doesn't charge for.
    /// </summary>
    Task<VerificationConnectionCheck> CheckConnectionAsync(VerificationProviderConnection connection, CancellationToken cancellationToken = default);
}

/// <summary>Looks businesses up in a company registry.</summary>
public interface IBusinessVerificationProvider : IVerificationProvider
{
    /// <summary>ISO 3166-1 alpha-2 codes of the registries this provider can search.</summary>
    IReadOnlyCollection<string> SupportedBusinessCountries { get; }

    Task<BusinessLookupResult> LookupBusinessAsync(
        VerificationProviderConnection connection,
        BusinessLookupRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>Every registered verification vendor, by id.</summary>
public interface IVerificationProviderRegistry
{
    IReadOnlyList<IVerificationProvider> All { get; }

    IVerificationProvider? Find(string providerId);

    IReadOnlyList<IBusinessVerificationProvider> BusinessProviders { get; }
}

/// <summary>How a provider-hosted session is opened in the applicant's browser.</summary>
public enum HostedSessionLaunchKind
{
    /// <summary>The browser leaves for <see cref="HostedSessionLaunch.RedirectUrl"/> and comes back to the journey's callback.</summary>
    Redirect,

    /// <summary>The journey page loads the provider's script and opens its widget with <see cref="HostedSessionLaunch.WidgetOptions"/>.</summary>
    Widget,
}

public sealed record HostedSessionLaunch
{
    public required HostedSessionLaunchKind Kind { get; init; }
    public string? RedirectUrl { get; init; }
    public string? WidgetScriptUrl { get; init; }

    /// <summary>Options handed to the widget as JSON; never secrets.</summary>
    public IReadOnlyDictionary<string, object>? WidgetOptions { get; init; }
}

/// <summary>What the applicant is being verified for, and where the provider sends them back.</summary>
public sealed record HostedSessionRequest
{
    /// <summary>ISO 3166-1 alpha-2.</summary>
    public required string Country { get; init; }
    public string? RegistrationNumber { get; init; }
    public string? RegistrationType { get; init; }
    public string? BusinessName { get; init; }

    /// <summary>Absolute URL of the journey's callback, which resumes the step.</summary>
    public required string ReturnUrl { get; init; }

    /// <summary>Opaque, non-personal identifiers echoed back by the provider.</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

public sealed record HostedSessionStart(string Reference, HostedSessionLaunch Launch);

/// <summary>Where a hosted session stands with the provider.</summary>
public enum HostedSessionState
{
    /// <summary>The applicant hasn't finished, or the provider is still deciding.</summary>
    Pending,
    Approved,
    Rejected,

    /// <summary>The applicant left the flow without finishing.</summary>
    Abandoned,

    /// <summary>The provider couldn't be asked (unreachable, rate-limited); ask again later.</summary>
    Unavailable,
}

public sealed record HostedSessionResult
{
    public required HostedSessionState State { get; init; }

    /// <summary>The registry details the provider verified, when it returned them.</summary>
    public BusinessRegistryRecord? Record { get; init; }

    public string? ProviderReference { get; init; }

    /// <summary>Operator-facing detail; never shown to the applicant.</summary>
    public string? Message { get; init; }
}

/// <summary>A provider webhook after its signature was checked.</summary>
public sealed record VerificationWebhook(bool Authentic, string? SessionReference, string? Message = null)
{
    public static VerificationWebhook Rejected(string message) => new(false, null, message);
}

/// <summary>
/// A provider that runs the business verification itself in a session the applicant
/// completes in their browser (a hosted page or an embedded widget), and reports the
/// result asynchronously. Sessions are found again by the provider's reference.
/// </summary>
public interface IHostedBusinessVerificationProvider : IBusinessVerificationProvider
{
    /// <summary>ISO 3166-1 alpha-2 codes the hosted flow covers.</summary>
    IReadOnlyCollection<string> HostedBusinessCountries { get; }

    /// <summary>Creates a session. Throws <see cref="VerificationConfigurationException"/> when the hosted credentials are missing.</summary>
    Task<HostedSessionStart> StartBusinessSessionAsync(VerificationProviderConnection connection, HostedSessionRequest request, CancellationToken cancellationToken = default);

    /// <summary>Opens an existing session again, or null when the provider can't reopen one.</summary>
    HostedSessionLaunch? ResumeBusinessSession(VerificationProviderConnection connection, string reference, HostedSessionRequest request);

    /// <summary>Asks the provider for the session's outcome; the authoritative answer, never the browser's.</summary>
    Task<HostedSessionResult> GetBusinessSessionResultAsync(VerificationProviderConnection connection, string reference, CancellationToken cancellationToken = default);

    /// <summary>Checks a webhook's signature with the tenant's webhook secret and reads the session it concerns.</summary>
    VerificationWebhook ReadWebhook(VerificationProviderConnection connection, IReadOnlyDictionary<string, string> headers, byte[] body);
}

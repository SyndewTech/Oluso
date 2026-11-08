namespace Oluso.Authentication.OrganizationSignIn;

/// <summary>
/// Paths, lifetimes and limits of the organisation sign-in hand-off.
/// </summary>
public static class OrganizationSignInDefaults
{
    /// <summary>Protocol name of the pending-request record in the protocol state store.</summary>
    public const string ProtocolName = "org_handoff";

    /// <summary>Action key of the single-use hand-off code (an action token).</summary>
    public const string HandoffAction = "oluso.organization_signin.handoff";

    /// <summary>
    /// How long an expired pending request is kept after it expires, so a late
    /// return can be told "your sign-in took too long" with a link to start over
    /// instead of a bare "not found". The pending request itself expires with the
    /// authorize request it resumes (<c>OidcEndpointConfiguration.StateExpiration</c>
    /// after that request was created).
    /// </summary>
    public static readonly TimeSpan ExpiredRequestRetention = TimeSpan.FromMinutes(15);

    /// <summary>How long the hand-off code minted by the tenant stays redeemable on the platform.</summary>
    public static readonly TimeSpan HandoffCodeLifetime = TimeSpan.FromSeconds(60);

    /// <summary>Failed organisation lookups allowed per client address within <see cref="FailedLookupWindow"/>.</summary>
    public const int MaxFailedLookups = 10;

    public static readonly TimeSpan FailedLookupWindow = TimeSpan.FromMinutes(15);

    public const string EntryPath = "/account/org-signin";
    public const string StartPath = "/account/org-handoff/start";
    public const string CompletePath = "/account/org-handoff/complete";
    public const string CallbackPath = "/account/org-handoff/callback";
    public const string ExpiredPath = "/account/org-handoff/expired";

    /// <summary>Path of the binding cookie: sent to the callback and expired pages only.</summary>
    public const string BindingCookiePath = "/account/org-handoff";

    /// <summary>
    /// Prefix of the host-only binding cookie set on the platform host. The suffix is
    /// derived from the pending request id so parallel sign-ins in separate tabs do
    /// not overwrite each other's binding.
    /// </summary>
    public const string BindingCookiePrefix = "oluso.osi.";
}

/// <summary>Why an organisation sign-in step was refused. Logged and audited, never shown to the user.</summary>
public static class OrganizationSignInErrors
{
    public const string NotOffered = "not_offered";
    public const string InvalidReturnUrl = "invalid_return_url";
    public const string TooManyAttempts = "too_many_attempts";
    public const string UnknownOrganization = "unknown_organization";
    public const string RequestNotFound = "request_not_found";
    public const string RequestExpired = "request_expired";
    public const string TenantMismatch = "tenant_mismatch";
    public const string HostMismatch = "host_mismatch";
    public const string NotAuthenticated = "not_authenticated";
    public const string InvalidCode = "invalid_code";
    public const string BindingMismatch = "binding_mismatch";
    public const string HandoffStateMissing = "handoff_state_missing";
    public const string UserNotFound = "user_not_found";
    public const string UserInactive = "user_inactive";
    public const string UserLockedOut = "user_locked_out";
    public const string NotAMember = "not_a_member";
}

/// <summary>
/// A sign-in the platform has handed to a tenant and is waiting to get back.
/// </summary>
public sealed record OrganizationHandoffRequest
{
    public required string Id { get; init; }
    public required string TenantId { get; init; }

    /// <summary>
    /// Relative URL on the platform host to resume once the user is back: the
    /// authorize callback for a standalone login, the journey for a journey login.
    /// </summary>
    public required string ReturnUrl { get; init; }

    /// <summary>Set only when the platform login was a journey; that journey resumes.</summary>
    public string? JourneyId { get; init; }

    public required DateTime CreatedAt { get; init; }

    /// <summary>
    /// When the request stops being usable: the expiry of the original authorize
    /// request it resumes, since resuming fails once that is gone.
    /// </summary>
    public required DateTime ExpiresAt { get; init; }

    /// <summary>The client of the original authorize request, for audit.</summary>
    public string? ClientId { get; init; }

    /// <summary>
    /// Relative platform URL that starts the original authorize request again, offered
    /// when the sign-in took too long. Null when the original flow cannot be replayed.
    /// </summary>
    public string? RestartUrl { get; init; }

    public string? Prompt { get; init; }
    public int? MaxAge { get; init; }
    public string? LoginHint { get; init; }
    public string? UiLocales { get; init; }

    /// <summary>
    /// The original request demanded a fresh login (<c>prompt=login</c>), so an
    /// existing tenant session must not be reused.
    /// </summary>
    public bool ForceFreshLogin =>
        Prompt?.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Contains("login", StringComparer.Ordinal) == true;
}

/// <summary>A pending request looked up on the tenant host; <see cref="Error"/> is set when it may not be used.</summary>
public sealed record OrganizationHandoffLookup(OrganizationHandoffRequest? Request, string? Error)
{
    public bool Succeeded => Error == null && Request != null;
    public bool IsExpired => Error == OrganizationSignInErrors.RequestExpired;
}

public sealed record OrganizationSignInBeginResult(bool Succeeded, string? RedirectUrl, string? Error)
{
    public static OrganizationSignInBeginResult Success(string redirectUrl) => new(true, redirectUrl, null);
    public static OrganizationSignInBeginResult Failure(string error) => new(false, null, error);
}

public sealed record OrganizationSignInRedeemResult
{
    public required bool Succeeded { get; init; }
    public string? Error { get; init; }
    public OrganizationHandoffRequest? Request { get; init; }
    public string? UserId { get; init; }
    public string? IdentityProvider { get; init; }
    public IReadOnlyList<string> AuthenticationMethods { get; init; } = Array.Empty<string>();

    public static OrganizationSignInRedeemResult Failure(string error, OrganizationHandoffRequest? request = null)
        => new() { Succeeded = false, Error = error, Request = request };
}

/// <summary>How the tenant reported the user authenticated, carried to the platform session.</summary>
public sealed record OrganizationAuthentication(string UserId, string? IdentityProvider, IReadOnlyList<string> AuthenticationMethods);

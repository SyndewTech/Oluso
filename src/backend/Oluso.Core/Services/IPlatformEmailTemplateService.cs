namespace Oluso.Core.Services;

/// <summary>
/// Resolves and renders system-owned email templates with tenant override.
///
/// Lookup cascade:
///   1. Current tenant's template by name (if a tenant has saved an override)
///   2. Platform default template (TenantId == <see cref="PlatformConstants.TenantId"/>)
///   3. Caller-supplied fallback (or null if none)
///
/// Implementations are optional; consumers in the Oluso assembly resolve
/// this as nullable so the package stays usable without one registered. When the service is unavailable, callers
/// keep their hardcoded body as a final fallback.
/// </summary>
public interface IPlatformEmailTemplateService
{
    /// <summary>
    /// Resolves and renders a template by name using the cascade above.
    /// Returns null when no platform default and no tenant override exists,
    /// or when rendering fails — callers should fall back to a hardcoded
    /// body in that case so the user-visible flow doesn't break.
    /// </summary>
    Task<RenderedEmail?> RenderAsync(
        string templateName,
        IDictionary<string, object>? scope = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The same cascade and branding for a named tenant, for mail sent on a tenant's behalf
    /// outside that tenant's requests (e.g. the email relay used by standalone apps).
    /// </summary>
    Task<RenderedEmail?> RenderForTenantAsync(
        string tenantId,
        string templateName,
        IDictionary<string, object>? scope = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of rendering an <see cref="IPlatformEmailTemplateService"/>
/// template — a fully-substituted Subject/Html/Text triple ready to hand
/// to <c>IEmailSender.SendAsync</c>.
/// </summary>
public sealed class RenderedEmail
{
    public required string Subject { get; init; }
    public required string HtmlBody { get; init; }
    public string? TextBody { get; init; }
    public string? FromAddress { get; init; }
    public string? FromName { get; init; }
}

/// <summary>
/// Standard platform template names. Using these constants keeps seeds
/// and call sites in sync — a typo at the call site won't silently miss
/// the seeded row.
/// </summary>
public static class PlatformTemplateNames
{
    public const string PasswordlessOtp = "auth.passwordless.otp";
    public const string PasswordlessMagicLink = "auth.passwordless.magic_link";
    public const string MfaEmail = "auth.mfa.email";
    /// <summary>
    /// Sent to the administrator of a newly provisioned organisation: the platform welcomes the organisation and the
    /// administrator sets their password.
    /// </summary>
    public const string WelcomeInvite = "auth.welcome.invite";

    /// <summary>
    /// Sent when an administrator creates a user in a tenant and asks for an invite: the tenant welcomes them and they
    /// set their own password. Carries the set-password link and its lifetime.
    /// </summary>
    public const string AccountInvite = "auth.account.invite";

    /// <summary>
    /// Password reset. Carries a reset link (forgot-password page) or a reset
    /// code (password-reset journey step).
    /// </summary>
    public const string PasswordReset = "auth.password.reset";

    /// <summary>
    /// Sent when someone is invited to join an organization. Carries the
    /// accept link, which is what redeems the invitation token.
    /// </summary>
    public const string OrgInvitation = "auth.org.invitation";

    /// <summary>
    /// Sent when an application asks the user to approve a sign-in (CIBA) and the user can't be
    /// reached by push. Carries the approve/deny link, the requesting application and, when the
    /// application supplied one, the binding message to compare.
    /// </summary>
    public const string CibaRequest = "auth.ciba.request";

    /// <summary>
    /// Branded "starter" template — opens with a multi-block visual design
    /// in Unlayer (header + hero + body + CTA + footer). Tenants duplicate
    /// this as the base for marketing / announcement emails.
    /// </summary>
    public const string StarterBranded = "starter.branded";
}

/// <summary>
/// Standard placeholder keys available in every platform template. The
/// service auto-populates these from the current tenant context and
/// branding settings; per-template values are merged in on top.
/// </summary>
public static class PlatformTemplateScopeKeys
{
    // Tenant identity
    public const string TenantName = "tenant_name";
    public const string TenantDisplayName = "tenant_display_name";

    // Brand
    public const string LogoUrl = "logo_url";
    public const string PrimaryColor = "primary_color";
    public const string BackgroundColor = "background_color";

    // Common message variables
    public const string OtpCode = "otp_code";
    public const string MagicLink = "magic_link";
    public const string ExpirationMinutes = "expiration_minutes";

    /// <summary>The lifetime as words ("24 hours"), preferred over <see cref="ExpirationMinutes"/> when set.</summary>
    public const string ExpiresIn = "expires_in";
    public const string ResetLink = "reset_link";

    // Recipient / context
    public const string UserEmail = "user_email";
    public const string UserName = "user_name";

    /// <summary>The platform's own name (configuration <c>Oluso:Branding:CompanyName</c>), for mail where the platform speaks.</summary>
    public const string PlatformName = "platform_name";

    // Misc
    public const string Year = "year";
    public const string SupportEmail = "support_email";
}

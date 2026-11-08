using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Microsoft.AspNetCore.RateLimiting;
using Oluso.Core.Security;

namespace Oluso.UI.Pages.Account;

/// <summary>
/// Standalone forgot password page
/// </summary>
[EnableRateLimiting(SignInRateLimitPolicies.SignIn)]
public class ForgotPasswordModel : PageModel
{
    private readonly IOlusoUserService _userService;
    private readonly IProtocolStateStore _protocolStateStore;
    private readonly IMessagingService? _messagingService;
    private readonly IPlatformEmailTemplateService? _templates;
    private readonly ITenantSettingsProvider _tenantSettings;
    private readonly ILogger<ForgotPasswordModel> _logger;

    public ForgotPasswordModel(
        IOlusoUserService userService,
        IProtocolStateStore protocolStateStore,
        ITenantSettingsProvider tenantSettings,
        ILogger<ForgotPasswordModel> logger,
        IMessagingService? messagingService = null,
        IPlatformEmailTemplateService? templates = null)
    {
        _tenantSettings = tenantSettings;
        _userService = userService;
        _protocolStateStore = protocolStateStore;
        _messagingService = messagingService;
        _templates = templates;
        _logger = logger;
    }

    [BindProperty]
    public ForgotPasswordInputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? ErrorMessage { get; set; }
    public Dictionary<string, string> ValidationErrors { get; set; } = new();
    public bool EmailSent { get; set; }
    public JourneyUiConfiguration? UiConfig { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await AuthFlowGate.HasActiveFlowAsync(_protocolStateStore, ReturnUrl))
        {
            _logger.LogWarning("Direct access to /Account/ForgotPassword rejected (no protocol flow in flight). ReturnUrl={ReturnUrl}", ReturnUrl);
            return NotFound();
        }
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        // Validate input
        if (string.IsNullOrWhiteSpace(Input.Email))
        {
            ValidationErrors["email"] = "Email address is required";
            return Page();
        }

        if (!IsValidEmail(Input.Email))
        {
            ValidationErrors["email"] = "Please enter a valid email address";
            return Page();
        }

        _logger.LogDebug("Password reset requested for email {Email}", Input.Email);

        // Find user - but don't reveal if user exists or not
        var user = await _userService.FindByEmailAsync(Input.Email);

        // Defer to the service gate: GeneratePasswordResetTokenAsync refuses to mint a
        // token for federated/passwordless accounts (honouring the tenant's mixed-mode
        // opt-in), so a null token means "not eligible". Either way we show the generic
        // success below — no enumeration, and never disclose existence or account type.
        // Send to the address on the account, never the one typed: lookups are case- and
        // Unicode-normalised, so a different string (e.g. a dotless-i lookalike) can match
        // this account while naming someone else's mailbox.
        var accountEmail = user?.Email;
        var token = user is null || string.IsNullOrWhiteSpace(accountEmail)
            ? null
            : await _userService.GeneratePasswordResetTokenAsync(user.Id);
        if (!string.IsNullOrEmpty(token) && _messagingService != null)
        {
            // Build reset URL
            var request = HttpContext.Request;
            var resetUrl = $"{request.Scheme}://{request.Host}{request.PathBase}/account/reset-password?token={Uri.EscapeDataString(token)}&email={Uri.EscapeDataString(accountEmail!)}";

            // The tenant's password-reset template (or the platform default), branded
            // for the tenant this request came in on.
            var linkLifetime = TimeSpan.FromMinutes(
                (await _tenantSettings.GetAccountEmailSettingsAsync(HttpContext.RequestAborted)).AccountLinkLifetimeMinutes);

            var rendered = _templates == null ? null : await _templates.RenderAsync(
                PlatformTemplateNames.PasswordReset,
                new Dictionary<string, object>
                {
                    [PlatformTemplateScopeKeys.ResetLink] = resetUrl,
                    [PlatformTemplateScopeKeys.UserEmail] = accountEmail!,
                    [PlatformTemplateScopeKeys.UserName] = user!.DisplayName ?? user.Username,
                    [PlatformTemplateScopeKeys.ExpirationMinutes] = (int)linkLifetime.TotalMinutes,
                    [PlatformTemplateScopeKeys.ExpiresIn] = Durations.Describe(linkLifetime),
                },
                HttpContext.RequestAborted);

            await _messagingService.SendEmailAsync(
                accountEmail!,
                rendered?.Subject ?? "Reset your password",
                rendered?.HtmlBody ?? BuildResetEmailBody(resetUrl, Durations.Describe(linkLifetime)),
                HttpContext.RequestAborted);

            _logger.LogInformation("Password reset email sent for user {UserId}", user!.Id);
        }
        else
        {
            _logger.LogDebug("Password reset not sent for {Email} (no eligible local account — missing, federated, or passwordless)", Input.Email);
        }

        // Always show success to prevent email enumeration
        EmailSent = true;
        return Page();
    }

    /// <summary>
    /// The reset email when no template service is wired. The reset URL is the only
    /// dynamic value and is already percent-encoded by the caller.
    /// </summary>
    private static string BuildResetEmailBody(string resetUrl, string expiresIn) => BrandedEmailLayout.Document(
        title: "Reset your password",
        preheader: "Here's how to choose a new password.",
        accent: BrandedEmailLayout.DefaultAccent,
        brand: "",
        eyebrow: "Password reset",
        heading: "Reset your password",
        rows:
            BrandedEmailLayout.Lead("We received a request to reset your password. Click the button below to choose a new one.") +
            BrandedEmailLayout.Button(resetUrl, "Choose a new password &rarr;", BrandedEmailLayout.DefaultAccent) +
            BrandedEmailLayout.Note($"This link expires in <strong>{expiresIn}</strong>.") +
            BrandedEmailLayout.Divider() +
            BrandedEmailLayout.LinkFallback(resetUrl, BrandedEmailLayout.DefaultAccent) +
            BrandedEmailLayout.Paragraph("Didn't ask to reset your password? You can safely ignore this email &mdash; your password won't change."),
        footnote: "This is an automated security email.");

    private static bool IsValidEmail(string email)
    {
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
    }
}

public class ForgotPasswordInputModel
{
    public string? Email { get; set; }
}

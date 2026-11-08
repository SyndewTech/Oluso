using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Oluso.Core.Services;
using Microsoft.AspNetCore.RateLimiting;
using Oluso.Core.Security;

namespace Oluso.UI.Pages.Account;

/// <summary>
/// Standalone reset-password page. This is the landing page for the link emailed
/// by <see cref="ForgotPasswordModel"/> (<c>/account/reset-password?token=...&amp;email=...</c>).
/// It is reached from an email — typically in a fresh browser session with no
/// protocol flow in flight — so, unlike the other Account pages, it is NOT gated
/// behind <see cref="AuthFlowGate"/>. The reset token is the gate: it is bound to
/// the user's security stamp and validated server-side by
/// <see cref="IOlusoUserService.ResetPasswordAsync"/>.
/// <para>
/// With <c>?expired=true</c> it is also where the standalone login page sends a user whose
/// password has expired: the login page has verified the password and left an
/// <see cref="ExpiredPasswordTicket"/> in TempData, so no emailed link is involved and the
/// user is returned to sign in (and to the protocol flow in <see cref="ReturnUrl"/>) afterwards.
/// </para>
/// </summary>
[EnableRateLimiting(SignInRateLimitPolicies.SignIn)]
public class ResetPasswordModel : PageModel
{
    private readonly IOlusoUserService _userService;
    private readonly Oluso.Core.Domain.Interfaces.ITenantSettingsProvider _tenantSettings;
    private readonly ILogger<ResetPasswordModel> _logger;

    public ResetPasswordModel(
        IOlusoUserService userService,
        Oluso.Core.Domain.Interfaces.ITenantSettingsProvider tenantSettings,
        ILogger<ResetPasswordModel> logger)
    {
        _userService = userService;
        _tenantSettings = tenantSettings;
        _logger = logger;
    }

    [BindProperty(SupportsGet = true)]
    public string? Token { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Email { get; set; }

    /// <summary>The user arrived from sign-in because their password expired.</summary>
    [BindProperty(SupportsGet = true)]
    public bool Expired { get; set; }

    /// <summary>The protocol flow to resume by signing in once the expired password is replaced.</summary>
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    [BindProperty]
    public ResetPasswordInputModel Input { get; set; } = new();

    /// <summary>Where "Sign in" goes after the reset — back into the flow when one is in flight.</summary>
    public string SignInUrl => !string.IsNullOrEmpty(ReturnUrl) && Url.IsLocalUrl(ReturnUrl)
        ? $"/account/login?returnUrl={Uri.EscapeDataString(ReturnUrl)}"
        : "/account/login";

    public string? ErrorMessage { get; set; }
    public Dictionary<string, string> ValidationErrors { get; set; } = new();

    /// <summary>True when the token/email are missing — nothing to reset against.</summary>
    public bool LinkInvalid { get; set; }

    /// <summary>True once the password has been reset — show the success state.</summary>
    public bool PasswordReset { get; set; }

    /// <summary>
    /// The login the new password belongs to, for the form's visually hidden username field
    /// (password managers): the address in the reset link, or the name just used to sign in.
    /// </summary>
    public string? AccountName => Expired ? ExpiredPasswordTicket.Peek(TempData)?.Username : Email;

    // Password policy surfaced to the view for the live requirements checklist.
    public int MinLength { get; private set; }
    public bool RequireDigit { get; private set; }
    public bool RequireLowercase { get; private set; }
    public bool RequireUppercase { get; private set; }
    public bool RequireNonAlphanumeric { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        await ApplyPasswordPolicyAsync();

        if (Expired)
        {
            return ExpiredPasswordTicket.Peek(TempData) == null ? ExpiredSessionLost() : Page();
        }

        if (string.IsNullOrEmpty(Token) || string.IsNullOrEmpty(Email))
        {
            _logger.LogWarning("Reset-password page opened without a token/email.");
            LinkInvalid = true;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await ApplyPasswordPolicyAsync();

        if (Expired)
        {
            return await ChangeExpiredPasswordAsync();
        }

        if (string.IsNullOrEmpty(Token) || string.IsNullOrEmpty(Email))
        {
            LinkInvalid = true;
            return Page();
        }

        // Client-friendly validation first so we can give precise feedback without
        // burning the (single-use) token on obviously-bad input.
        var newPassword = Input.NewPassword ?? string.Empty;
        var confirmPassword = Input.ConfirmPassword ?? string.Empty;

        foreach (var error in ValidatePassword(newPassword))
        {
            ValidationErrors.TryAdd("new_password", error);
        }

        if (newPassword != confirmPassword)
        {
            ValidationErrors["confirm_password"] = "Passwords do not match.";
        }

        if (ValidationErrors.Count > 0)
        {
            return Page();
        }

        // Resolve the account from the email carried in the link. We never disclose
        // whether the email maps to an account: a missing user, a mismatched/expired
        // token, and a federated account all collapse to the same generic message.
        var user = await _userService.FindByEmailAsync(Email);
        if (user is null)
        {
            _logger.LogWarning("Reset-password submitted for an email with no local account.");
            ErrorMessage = null;
            LinkInvalid = true;
            return Page();
        }

        var result = await _userService.ResetPasswordAsync(user.Id, newPassword, Token);
        if (result.Succeeded)
        {
            _logger.LogInformation("Password reset completed for user {UserId}", user.Id);
            PasswordReset = true;
            return Page();
        }

        // A bad or expired link shows the invalid-link state; anything else (a password the
        // tenant's policy rejected, an account that signs in elsewhere) says what went wrong.
        if (!string.Equals(result.Error, "invalid_token", StringComparison.Ordinal))
        {
            _logger.LogWarning("Password reset refused for user {UserId}: {Error}", user.Id, result.Error);
            ErrorMessage = result.MessageOr("Your password could not be reset.");
            return Page();
        }

        _logger.LogWarning("Password reset failed for user {UserId}: {Error}", user.Id, result.Error);
        LinkInvalid = true;
        return Page();
    }

    private async Task<IActionResult> ChangeExpiredPasswordAsync()
    {
        var ticket = ExpiredPasswordTicket.Peek(TempData);
        if (ticket == null)
        {
            return ExpiredSessionLost();
        }

        var newPassword = Input.NewPassword ?? string.Empty;
        foreach (var error in ValidatePassword(newPassword))
        {
            ValidationErrors.TryAdd("new_password", error);
        }
        if (newPassword != (Input.ConfirmPassword ?? string.Empty))
        {
            ValidationErrors["confirm_password"] = "Passwords do not match.";
        }
        if (ValidationErrors.Count > 0)
        {
            return Page();
        }

        var result = await _userService.ResetPasswordAsync(ticket.UserId, newPassword, ticket.Token);
        if (result.Succeeded)
        {
            ExpiredPasswordTicket.Clear(TempData);
            _logger.LogInformation("User {UserId} replaced an expired password", ticket.UserId);
            PasswordReset = true;
            return Page();
        }

        if (string.Equals(result.Error, "invalid_token", StringComparison.Ordinal))
        {
            ExpiredPasswordTicket.Clear(TempData);
            return ExpiredSessionLost();
        }

        _logger.LogInformation("Expired password change refused for user {UserId}: {Error}", ticket.UserId, result.Error);
        ErrorMessage = result.MessageOr(Oluso.Core.Locale.UiStringDefaults.Resolve("password_expired_signin_error"));
        return Page();
    }

    /// <summary>The expired-password hand-off is gone (timed out or used): start sign-in again.</summary>
    private IActionResult ExpiredSessionLost()
    {
        var error = Uri.EscapeDataString(Oluso.Core.Locale.UiStringDefaults.Resolve("password_expired_session_lost"));
        return Redirect(SignInUrl + (SignInUrl.Contains('?') ? "&" : "?") + $"error={error}");
    }

    /// <summary>
    /// The policy this tenant enforces when the password is saved (<c>TenantPasswordValidator</c>), so the checklist
    /// shows the rules that apply. ASP.NET Identity's own options are left permissive on purpose and say nothing useful.
    /// </summary>
    private async Task ApplyPasswordPolicyAsync(CancellationToken cancellationToken = default)
    {
        var policy = await _tenantSettings.GetPasswordSettingsAsync(cancellationToken);
        MinLength = policy.MinimumLength;
        RequireDigit = policy.RequireDigit;
        RequireLowercase = policy.RequireLowercase;
        RequireUppercase = policy.RequireUppercase;
        RequireNonAlphanumeric = policy.RequireNonAlphanumeric;
    }

    private IEnumerable<string> ValidatePassword(string password)
    {
        if (string.IsNullOrEmpty(password))
        {
            yield return "Password is required.";
            yield break;
        }

        if (password.Length < MinLength)
            yield return $"Password must be at least {MinLength} characters.";
        if (RequireDigit && !password.Any(char.IsDigit))
            yield return "Password must contain at least one digit.";
        if (RequireLowercase && !password.Any(char.IsLower))
            yield return "Password must contain at least one lowercase letter.";
        if (RequireUppercase && !password.Any(char.IsUpper))
            yield return "Password must contain at least one uppercase letter.";
        if (RequireNonAlphanumeric && password.All(char.IsLetterOrDigit))
            yield return "Password must contain at least one special character.";
    }
}

public class ResetPasswordInputModel
{
    public string? NewPassword { get; set; }
    public string? ConfirmPassword { get; set; }
}

/// <summary>
/// Hand-off from the login page to the reset-password page for a user whose password expired:
/// the account whose password was just verified and a reset token for it. Held in TempData
/// (data-protected, server-issued) so neither value appears in a URL. <paramref name="Username"/>
/// is what the user signed in with, shown to password managers so the new password replaces the
/// saved one for that login.
/// </summary>
public sealed record ExpiredPasswordTicket(string UserId, string Token, string? Username = null)
{
    private const string Key = "expired_password_ticket";

    public static void Store(ITempDataDictionary tempData, ExpiredPasswordTicket ticket) =>
        tempData[Key] = JsonSerializer.Serialize(ticket);

    /// <summary>Reads the ticket without consuming it, so a rejected password can be retried.</summary>
    public static ExpiredPasswordTicket? Peek(ITempDataDictionary tempData) =>
        tempData.Peek(Key) is string json ? JsonSerializer.Deserialize<ExpiredPasswordTicket>(json) : null;

    public static void Clear(ITempDataDictionary tempData) => tempData.Remove(Key);
}

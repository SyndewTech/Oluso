using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Oluso.Core.Services;

namespace Oluso.UI.Pages.Account;

/// <summary>
/// An external sign-in whose email belongs to an existing account the provider can't vouch for: the person proves
/// the account is theirs with a code sent to its address, then the external login is linked to it and the sign-in
/// continues at the external callback as any linked login would.
/// </summary>
public class LinkAccountModel : PageModel
{
    public const int MaxCodeAttempts = 5;
    private static readonly TimeSpan AttemptWindow = TimeSpan.FromMinutes(15);
    private static readonly TimeSpan ResendInterval = TimeSpan.FromSeconds(60);

    private readonly IOlusoUserService _users;
    private readonly IDistributedCache _cache;
    private readonly ILogger<LinkAccountModel> _logger;
    private readonly IExternalAuthService? _externalAuth;
    private readonly IMfaService? _mfa;

    public LinkAccountModel(
        IOlusoUserService users,
        IDistributedCache cache,
        ILogger<LinkAccountModel> logger,
        IExternalAuthService? externalAuth = null,
        IMfaService? mfa = null)
    {
        _users = users;
        _cache = cache;
        _logger = logger;
        _externalAuth = externalAuth;
        _mfa = mfa;
    }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    [BindProperty]
    public string? Code { get; set; }

    public string? MaskedEmail { get; set; }
    public string? ProviderName { get; set; }
    public string? ErrorMessage { get; set; }
    public string? InfoMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        var (pending, redirect) = await ResolveAsync(cancellationToken);
        if (redirect != null) return redirect;

        if (!await SendCodeAsync(pending!, force: false, cancellationToken))
        {
            ErrorMessage = CodeNotSentMessage;
        }
        return Page();
    }

    public async Task<IActionResult> OnPostResendAsync(CancellationToken cancellationToken)
    {
        var (pending, redirect) = await ResolveAsync(cancellationToken);
        if (redirect != null) return redirect;

        if (await SendCodeAsync(pending!, force: true, cancellationToken))
        {
            InfoMessage = "A new code has been sent.";
        }
        else
        {
            ErrorMessage = CodeNotSentMessage;
        }
        return Page();
    }

    public async Task<IActionResult> OnPostCancelAsync()
    {
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        return Redirect($"/account/login?returnUrl={Uri.EscapeDataString(ReturnUrl ?? "/")}");
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var (pending, redirect) = await ResolveAsync(cancellationToken);
        if (redirect != null) return redirect;

        if (_mfa == null || _externalAuth == null)
        {
            ErrorMessage = CodeNotSentMessage;
            return Page();
        }

        if (string.IsNullOrWhiteSpace(Code))
        {
            ErrorMessage = "Enter the code we sent you.";
            return Page();
        }

        var attemptsKey = AttemptsKey(pending!.UserId);
        var attempts = int.TryParse(await _cache.GetStringAsync(attemptsKey, cancellationToken), out var n) ? n : 0;
        if (attempts >= MaxCodeAttempts)
        {
            return await AbandonAsync(pending.UserId, "Too many attempts. Start the sign-in again.", cancellationToken);
        }

        if (!await _mfa.VerifyEmailCodeAsync(pending.UserId, Code.Trim(), cancellationToken))
        {
            attempts++;
            await _cache.SetStringAsync(attemptsKey, attempts.ToString(),
                new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = AttemptWindow }, cancellationToken);
            _logger.LogWarning("Wrong account-link code for user {UserId} ({Attempts}/{Max})", pending.UserId, attempts, MaxCodeAttempts);
            if (attempts >= MaxCodeAttempts)
            {
                return await AbandonAsync(pending.UserId, "Too many attempts. Start the sign-in again.", cancellationToken);
            }

            ErrorMessage = $"That code isn't right. {MaxCodeAttempts - attempts} attempt(s) left.";
            return Page();
        }

        var linked = await _externalAuth.LinkLoginAsync(pending.UserId, pending.Provider, pending.ProviderKey, pending.Provider, cancellationToken);
        if (!linked.Succeeded)
        {
            _logger.LogWarning("Linking {Provider} to user {UserId} failed after the code was confirmed", pending.Provider, pending.UserId);
            ErrorMessage = "This sign-in couldn't be linked to your account. Try again or sign in with your password.";
            return Page();
        }

        // The code reached the account's address: it is the person's.
        await _users.SetEmailVerifiedAsync(pending.UserId, true, cancellationToken);
        await ClearAsync(pending.UserId, cancellationToken);
        _logger.LogInformation("Linked {Provider} to user {UserId} after they confirmed the account by email", pending.Provider, pending.UserId);

        // Linked now, so the callback signs them in as it does any linked login (roles, tokens, audit).
        return Redirect($"/account/external-callback?returnUrl={Uri.EscapeDataString(ReturnUrl ?? "/")}");
    }

    private const string CodeNotSentMessage =
        "We couldn't send a code to this account. Sign in with your password instead, or contact your administrator.";

    private sealed record PendingLink(string Provider, string ProviderKey, string UserId);

    /// <summary>
    /// The external sign-in waiting to be linked and the account it matched, or where to send the person instead.
    /// </summary>
    private async Task<(PendingLink? Pending, IActionResult? Redirect)> ResolveAsync(CancellationToken cancellationToken)
    {
        var returnUrl = ReturnUrl ?? "/";
        var info = await HttpContext.AuthenticateAsync(IdentityConstants.ExternalScheme);
        var provider = info?.Properties?.Items.TryGetValue("LoginProvider", out var p) == true ? p : null;
        var providerKey = info?.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (info?.Principal == null || string.IsNullOrEmpty(provider) || string.IsNullOrEmpty(providerKey))
        {
            return (null, Redirect($"/account/login?error={Uri.EscapeDataString("Your sign-in expired. Please try again.")}&returnUrl={Uri.EscapeDataString(returnUrl)}"));
        }

        // Asked again rather than carried in the URL: the account comes from the sign-in itself.
        var result = await _users.ProcessExternalLoginAsync(provider, info.Principal, createIfNotExists: false, cancellationToken: cancellationToken);
        if (result.Succeeded)
        {
            return (null, Redirect($"/account/external-callback?returnUrl={Uri.EscapeDataString(returnUrl)}"));
        }

        if (result.Error != ProcessExternalLoginResult.AccountLinkRequired || string.IsNullOrEmpty(result.ExistingUserId))
        {
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
            return (null, Redirect($"/account/login?error={Uri.EscapeDataString(result.ErrorDescription ?? "External login failed")}&returnUrl={Uri.EscapeDataString(returnUrl)}"));
        }

        var user = await _users.FindByIdAsync(result.ExistingUserId, cancellationToken);
        MaskedEmail = MaskEmail(user?.Email);
        ProviderName = provider.StartsWith("Saml.", StringComparison.OrdinalIgnoreCase) ? provider[5..] : provider;
        return (new PendingLink(provider, providerKey, result.ExistingUserId), null);
    }

    /// <summary>Sends a code to the account's address, at most once a minute unless asked.</summary>
    private async Task<bool> SendCodeAsync(PendingLink pending, bool force, CancellationToken cancellationToken)
    {
        if (_mfa == null || _externalAuth == null)
        {
            return false;
        }

        var sentKey = SentKey(pending.UserId);
        if (!force && await _cache.GetStringAsync(sentKey, cancellationToken) != null)
        {
            return true;
        }
        if (force && await _cache.GetStringAsync(sentKey, cancellationToken) is { } sentAt
            && DateTime.TryParse(sentAt, null, System.Globalization.DateTimeStyles.RoundtripKind, out var last)
            && DateTime.UtcNow - last < ResendInterval)
        {
            return true;
        }

        if (!await _mfa.SendVerificationCodeAsync(pending.UserId, "email", cancellationToken))
        {
            _logger.LogWarning("Couldn't send an account-link code to user {UserId}", pending.UserId);
            return false;
        }

        await _cache.SetStringAsync(sentKey, DateTime.UtcNow.ToString("O"),
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = AttemptWindow }, cancellationToken);
        await _cache.RemoveAsync(AttemptsKey(pending.UserId), cancellationToken);
        return true;
    }

    private async Task<IActionResult> AbandonAsync(string userId, string message, CancellationToken cancellationToken)
    {
        await ClearAsync(userId, cancellationToken);
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        return Redirect($"/account/login?error={Uri.EscapeDataString(message)}&returnUrl={Uri.EscapeDataString(ReturnUrl ?? "/")}");
    }

    private async Task ClearAsync(string userId, CancellationToken cancellationToken)
    {
        await _cache.RemoveAsync(AttemptsKey(userId), cancellationToken);
        await _cache.RemoveAsync(SentKey(userId), cancellationToken);
    }

    private static string AttemptsKey(string userId) => $"oluso:link-account:attempts:{userId}";
    private static string SentKey(string userId) => $"oluso:link-account:sent:{userId}";

    private static string? MaskEmail(string? email)
    {
        if (string.IsNullOrEmpty(email)) return null;
        var at = email.IndexOf('@');
        if (at <= 1) return email;
        return email[0] + new string('•', Math.Min(at - 1, 6)) + email[at..];
    }
}

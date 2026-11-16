using Oluso.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Oluso.Core.Authentication;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Security;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;

namespace Oluso.UI.Pages.Account;

/// <summary>
/// The second factor of a sign-in the standard pages (or a journey without an MFA step) held back
/// (<see cref="MfaSignInGate"/>): runs the journey's MFA step on this page, verifying the user's method or, for a user
/// who has none on a tenant that requires one, setting one up. The step counts wrong codes against the account's
/// lockout, as wrong passwords are. On success the held session is issued with amr mfa.
/// </summary>
[EnableRateLimiting(SignInRateLimitPolicies.SignIn)]
public class MfaModel : PageModel
{
    private const string StepType = "mfa";

    private readonly MfaSignInGate _gate;
    private readonly IStepHandlerRegistry _steps;
    private readonly UserManager<OlusoUser> _userManager;
    private readonly IOlusoUserService _users;
    private readonly IStringLocalizer _strings;
    private readonly ILogger<MfaModel> _logger;
    private readonly ITenantContext? _tenantContext;
    private readonly ISignInAuditor? _signInAuditor;

    public MfaModel(
        MfaSignInGate gate,
        IStepHandlerRegistry steps,
        UserManager<OlusoUser> userManager,
        IOlusoUserService users,
        IStringLocalizerFactory localizerFactory,
        ILogger<MfaModel> logger,
        ITenantContext? tenantContext = null,
        ISignInAuditor? signInAuditor = null)
    {
        _gate = gate;
        _steps = steps;
        _userManager = userManager;
        _users = users;
        _strings = localizerFactory.Create(typeof(MfaModel));
        _logger = logger;
        _tenantContext = tenantContext;
        _signInAuditor = signInAuditor;
    }

    /// <summary>The MFA step's view (Journey/_Mfa*) and its model.</summary>
    public string? StepView { get; private set; }
    public object? StepViewModel { get; private set; }

    /// <summary>Why the sign-in cannot go on; shown with a link to start it again.</summary>
    public string? ErrorMessage { get; private set; }
    public string? RestartUrl { get; private set; }

    public Task<IActionResult> OnGetAsync(CancellationToken cancellationToken) => RunAsync(input: null, cancellationToken);

    public Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        var input = Request.Form
            .Where(f => !f.Key.StartsWith("__RequestVerification", StringComparison.Ordinal))
            .ToDictionary(f => f.Key, f => (object)f.Value.ToString());
        return RunAsync(input, cancellationToken);
    }

    private async Task<IActionResult> RunAsync(IDictionary<string, object>? input, CancellationToken cancellationToken)
    {
        var held = await _gate.FindAsync(HttpContext, cancellationToken);
        if (held is not { } found)
        {
            return Stop(_strings["mfa_signin_expired"], returnUrl: null);
        }
        var (id, pending) = found;

        var user = await _userManager.FindByIdAsync(pending.UserId);
        if (user == null)
        {
            await _gate.ClearAsync(HttpContext, id, cancellationToken);
            return Stop(_strings["mfa_signin_failed"], pending.ReturnUrl);
        }
        if (await _userManager.IsLockedOutAsync(user))
        {
            await _gate.ClearAsync(HttpContext, id, cancellationToken);
            return Stop(_strings["mfa_signin_locked"], pending.ReturnUrl);
        }

        var handler = _steps.GetHandler(StepType)
            ?? throw new InvalidOperationException("The mfa step handler is not registered");
        var context = new StepExecutionContext
        {
            JourneyId = id,
            StepId = StepType,
            // Only a sign-in that needs a second factor is held, so the step always asks for one.
            Configuration = new JourneyStepConfiguration
            {
                Id = StepType,
                Type = StepType,
                Settings = new Dictionary<string, object> { ["required"] = true }
            },
            UserId = pending.UserId,
            TenantId = _tenantContext?.HasTenant == true ? _tenantContext.TenantId : null,
            ClientId = pending.ClientId,
            JourneyData = pending.StepData,
            Input = input == null ? null : new JourneyStepInput { StepId = StepType, Values = input },
            ServiceProvider = HttpContext.RequestServices,
        };

        var result = await handler.ExecuteAsync(context, cancellationToken);

        // The step counts a wrong code as a failed sign-in (the account lockout); at the limit the held sign-in ends.
        if (result.FailedAttempt)
        {
            if (await _userManager.IsLockedOutAsync(user))
            {
                _logger.LogWarning("User {UserId} locked out after wrong second-factor codes", user.Id);
                await _gate.ClearAsync(HttpContext, id, cancellationToken);
                return Stop(_strings["mfa_signin_locked"], pending.ReturnUrl);
            }
        }

        switch (result.Outcome)
        {
            case StepOutcome.RequireInput when result.StepResult?.ViewName is { Length: > 0 } view:
                await _gate.SaveAsync(HttpContext, pending, id, cancellationToken);
                StepView = view;
                StepViewModel = result.StepResult.ViewModel;
                return Page();

            case StepOutcome.Redirect when !string.IsNullOrEmpty(result.RedirectUrl):
                await _gate.SaveAsync(HttpContext, pending, id, cancellationToken);
                return Redirect(result.RedirectUrl);

            case StepOutcome.Continue or StepOutcome.Complete or StepOutcome.Branch or StepOutcome.Skip
                when SecondFactorRequirement.IsMet(JourneyAuthenticationMethods.Read(context.JourneyData)):
                await _gate.ClearAsync(HttpContext, id, cancellationToken);
                return await CompleteAsync(pending, user);

            default:
                // A failed step, or one that finished without a second factor: no session.
                _logger.LogWarning("Second factor for user {UserId} not completed: {Outcome} {Error}",
                    user.Id, result.Outcome, result.Error);
                await _gate.ClearAsync(HttpContext, id, cancellationToken);
                return Stop(result.ErrorDescription ?? _strings["mfa_signin_failed"], pending.ReturnUrl);
        }
    }

    private async Task<IActionResult> CompleteAsync(PendingMfaSignIn pending, OlusoUser user)
    {
        await _userManager.ResetAccessFailedCountAsync(user);

        // An expired password is changed now that both factors passed; the user then signs in with the new one.
        if (pending.PasswordChangeRequired)
        {
            _logger.LogInformation("User {UserId} passed the second factor; changing the expired password", user.Id);
            return await ExpiredPasswordTicket.BeginAsync(this, _users, user.Id, pending.Username ?? user.UserName, pending.ReturnUrl)
                ?? Stop(_strings["mfa_signin_failed"], pending.ReturnUrl);
        }

        await HttpContext.SignInAsync(IdentityConstants.ApplicationScheme, MfaSignInGate.CompletedPrincipal(pending),
            new AuthenticationProperties { IsPersistent = pending.IsPersistent, ExpiresUtc = pending.ExpiresUtc });
        await _users.RecordLoginAsync(user.Id);

        if (_signInAuditor != null)
        {
            await _signInAuditor.RecordSuccessAsync(new SignInAudit
            {
                SubjectId = user.Id,
                Username = user.UserName ?? user.Email ?? user.Id,
                TenantId = string.IsNullOrEmpty(user.TenantId) ? null : user.TenantId,
                AuthenticationMethod = pending.AuthenticationMethod,
                ClientId = pending.ClientId,
                ReturnUrl = pending.ReturnUrl
            });
        }

        _logger.LogInformation("User {UserId} passed the second factor", user.Id);
        return IsAllowedReturnUrl(pending.ReturnUrl) ? Redirect(pending.ReturnUrl) : Redirect("/");
    }

    /// <summary>Shows why the sign-in stopped, with a link back to the login page for the same request.</summary>
    private IActionResult Stop(string message, string? returnUrl)
    {
        ErrorMessage = message;
        RestartUrl = returnUrl != null && IsAllowedReturnUrl(returnUrl)
            ? $"/account/login?returnUrl={Uri.EscapeDataString(returnUrl)}"
            : null;
        return Page();
    }

    /// <summary>A relative URL or one on this host, as the login page allows.</summary>
    private bool IsAllowedReturnUrl(string url)
    {
        if (Url.IsLocalUrl(url))
        {
            return true;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var request = HttpContext.Request;
            return string.Equals(uri.Host, request.Host.Host, StringComparison.OrdinalIgnoreCase)
                && uri.Port == (request.Host.Port ?? (request.IsHttps ? 443 : 80));
        }

        return false;
    }
}

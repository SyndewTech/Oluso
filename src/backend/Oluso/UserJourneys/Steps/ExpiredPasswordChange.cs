using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Locale;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;

namespace Oluso.UserJourneys.Steps;

/// <summary>
/// The password-change detour a local sign-in step takes when the password was right but has
/// expired (<see cref="UserValidationResult.PasswordExpired"/>). The step does not authenticate
/// the user: it holds the verified account and a fresh reset token in journey state, renders
/// the password-change form, and only once a new password is accepted does the step complete
/// sign-in. Abandoning the form leaves the journey unauthenticated.
/// </summary>
public static class ExpiredPasswordChange
{
    public const string UserIdKey = "password_expired_user_id";
    public const string TokenKey = "password_expired_reset_token";
    public const string RememberMeKey = "password_expired_remember_me";
    public const string UsernameKey = "password_expired_username";

    private const string ViewName = "Journey/_PasswordChange";

    /// <summary>An expired-password change is waiting for the user's new password.</summary>
    public static bool IsPending(StepExecutionContext context) =>
        !string.IsNullOrEmpty(context.GetData<string>(UserIdKey));

    /// <summary>Starts the detour for a user whose correct password has expired.</summary>
    public static async Task<StepHandlerResult> BeginAsync(
        StepExecutionContext context,
        IOlusoUserService userService,
        ValidatedUser user,
        CancellationToken cancellationToken)
    {
        var token = await userService.GeneratePasswordResetTokenAsync(user.Id, cancellationToken);
        // What the user just signed in with, so password managers file the new password under the same login.
        var signedInAs = context.GetInput("username");
        SensitiveInputs.Scrub(context.Input?.Values);
        if (string.IsNullOrEmpty(token))
        {
            return StepHandlerResult.Fail("password_expired", UiStringDefaults.Resolve("password_expired_signin_error"));
        }

        context.SetData(UserIdKey, user.Id);
        context.SetData(TokenKey, token);
        context.SetData(RememberMeKey, context.GetInput("rememberMe") is "true" or "on");
        context.SetData(UsernameKey, string.IsNullOrWhiteSpace(signedInAs) ? user.UserName : signedInAs.Trim());

        return await ShowFormAsync(context, errorMessage: null, cancellationToken);
    }

    /// <summary>
    /// Handles the submitted new password. Returns the user once the password is changed (the
    /// caller then completes sign-in), or the result to render instead.
    /// </summary>
    public static async Task<(OlusoUserInfo? User, StepHandlerResult? Result)> CompleteAsync(
        StepExecutionContext context,
        IOlusoUserService userService,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var userId = context.GetData<string>(UserIdKey)!;
        var token = context.GetData<string>(TokenKey);
        var newPassword = context.GetInput("newPassword");
        var confirmPassword = context.GetInput("confirmPassword");

        if (string.IsNullOrEmpty(token))
        {
            Clear(context);
            return (null, StepHandlerResult.Fail("password_expired", UiStringDefaults.Resolve("password_expired_session_lost")));
        }

        if (string.IsNullOrEmpty(newPassword))
        {
            return (null, await ShowFormAsync(context, errorMessage: null, cancellationToken));
        }

        if (newPassword != confirmPassword)
        {
            SensitiveInputs.Scrub(context.Input?.Values);
            return (null, await ShowFormAsync(context, UiStringDefaults.Resolve("password_change_no_match"), cancellationToken));
        }

        var result = await userService.ResetPasswordAsync(userId, newPassword, token, cancellationToken);
        SensitiveInputs.Scrub(context.Input?.Values);

        if (!result.Succeeded)
        {
            if (string.Equals(result.Error, "invalid_token", StringComparison.Ordinal))
            {
                logger.LogWarning("Expired-password change for user {UserId} outlived its reset token", userId);
                Clear(context);
                return (null, StepHandlerResult.Fail("password_expired", UiStringDefaults.Resolve("password_expired_session_lost")));
            }

            logger.LogInformation("Expired-password change for user {UserId} rejected: {Error}", userId, result.Error);
            return (null, await ShowFormAsync(context, result.MessageOr(UiStringDefaults.Resolve("password_expired_signin_error")), cancellationToken));
        }

        var user = await userService.FindByIdAsync(userId, cancellationToken);
        var rememberMe = context.GetData<bool>(RememberMeKey);
        Clear(context);
        if (user == null)
        {
            return (null, StepHandlerResult.Fail("user_not_found", "User not found"));
        }

        context.SetData("remember_me", rememberMe);
        logger.LogInformation("User {UserId} replaced an expired password during sign-in", userId);

        var eventService = context.ServiceProvider.GetService<IOlusoEventService>();
        if (eventService != null)
        {
            await eventService.RaiseAsync(new UserPasswordChangedEvent
            {
                TenantId = user.TenantId ?? context.ServiceProvider.GetRequiredService<ITenantContext>().TenantId,
                SubjectId = user.Id,
                ChangedByAdmin = false
            }, cancellationToken);
        }

        return (user, null);
    }

    private static async Task<StepHandlerResult> ShowFormAsync(StepExecutionContext context, string? errorMessage, CancellationToken cancellationToken)
    {
        var tenantContext = context.ServiceProvider.GetRequiredService<ITenantContext>();
        var policy = await context.ServiceProvider.GetRequiredService<ITenantSettingsProvider>()
            .GetPasswordSettingsAsync(cancellationToken);
        return StepHandlerResult.ShowUi(ViewName, new PasswordChangeViewModel
        {
            RequireCurrentPassword = false,
            MinPasswordLength = policy.MinimumLength,
            RequireUppercase = policy.RequireUppercase,
            RequireLowercase = policy.RequireLowercase,
            RequireDigit = policy.RequireDigit,
            RequireSpecialChar = policy.RequireNonAlphanumeric,
            NoticeMessage = UiStringDefaults.Resolve("password_expired_notice"),
            ErrorMessage = errorMessage,
            TenantName = tenantContext.Tenant?.Name,
            Username = context.GetData<string>(UsernameKey)
        });
    }

    private static void Clear(StepExecutionContext context)
    {
        context.JourneyData.Remove(UserIdKey);
        context.JourneyData.Remove(TokenKey);
        context.JourneyData.Remove(RememberMeKey);
        context.JourneyData.Remove(UsernameKey);
    }
}

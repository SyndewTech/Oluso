using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Services;

namespace Oluso.Core.UserJourneys;

/// <summary>
/// Completes an <see cref="IPrimaryAuthenticationStep"/> for a user who is already signed
/// in, when a requested policy runs against an existing session. The login is met, so the
/// journey moves on to its remaining steps. No authentication happens here, so the journey
/// keeps the existing session rather than issuing a new one.
/// </summary>
public static class ExistingSessionLogin
{
    /// <summary>
    /// Step setting: the session meets the step only if it signed in within this many
    /// seconds; an older session is asked to sign in again as the same person. 0 (the
    /// default) accepts any live session.
    /// </summary>
    public const string ReauthenticateAfterSetting = "reauthenticateAfter";

    /// <summary>The person signed in by the session the journey runs on, if any.</summary>
    public static string? SessionUserId(StepExecutionContext context) =>
        context.GetDataString(JourneyStartContext.SessionUserIdDataKey) is { Length: > 0 } userId ? userId : null;

    /// <summary>Null when the journey was not started from an existing session.</summary>
    /// <remarks>
    /// Also null when the session is older than the step's <see cref="ReauthenticateAfterSetting"/>:
    /// the step then runs, pre-filled for the session's user, and the orchestrator accepts
    /// only that user.
    /// </remarks>
    public static async Task<StepHandlerResult?> TryCompleteAsync(StepExecutionContext context, CancellationToken cancellationToken)
    {
        var userId = SessionUserId(context);
        if (userId == null) return null;

        var userService = context.ServiceProvider.GetRequiredService<IOlusoUserService>();
        var user = await userService.FindByIdAsync(userId, cancellationToken);
        if (user == null || !user.IsActive)
        {
            context.ServiceProvider.GetService<ILoggerFactory>()?.CreateLogger(typeof(ExistingSessionLogin).FullName!).LogWarning(
                "Session user {UserId} for journey {JourneyId} is missing or inactive", userId, context.JourneyId);
            return StepHandlerResult.Fail("access_denied", "Your account is not available.");
        }

        if (!SignedInRecently(context))
        {
            if (string.IsNullOrEmpty(context.GetDataString("login_hint")))
            {
                context.SetData("login_hint", user.Email ?? user.Username);
            }
            return null;
        }

        context.UserId = user.Id;

        var outputClaims = new Dictionary<string, object>
        {
            ["sub"] = user.Id,
            ["name"] = user.DisplayName ?? user.Username,
            ["email"] = user.Email ?? "",
            ["email_verified"] = user.EmailVerified
        };
        if (!string.IsNullOrEmpty(user.FirstName)) outputClaims["given_name"] = user.FirstName;
        if (!string.IsNullOrEmpty(user.LastName)) outputClaims["family_name"] = user.LastName;

        return StepHandlerResult.Success(outputClaims);
    }

    /// <summary>
    /// Whether the session signed in recently enough for this step. A session with no
    /// recorded sign-in time is treated as too old when the step sets a limit.
    /// </summary>
    private static bool SignedInRecently(StepExecutionContext context)
    {
        var limitSeconds = context.GetConfig(ReauthenticateAfterSetting, 0);
        if (limitSeconds <= 0) return true;

        if (!long.TryParse(context.GetDataString(JourneyStartContext.SessionAuthTimeDataKey), out var signedInUnix)) return false;

        var age = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(signedInUnix);
        return age >= TimeSpan.Zero && age <= TimeSpan.FromSeconds(limitSeconds);
    }
}

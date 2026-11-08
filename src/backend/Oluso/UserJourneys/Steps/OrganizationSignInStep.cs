using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Authentication.OrganizationSignIn;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;

namespace Oluso.UserJourneys.Steps;

/// <summary>
/// Completes a journey login step (local, composite or external) with the result
/// of "Sign in with your organisation", when the user left this journey for their
/// organisation's login and has just come back. The step authenticates the user
/// the way its own methods would, so the journey's remaining steps (MFA, consent,
/// claims) still run and the journey issues the session on completion.
/// </summary>
public static class OrganizationSignInStep
{
    public const string AuthenticationMethod = "organization";

    /// <summary>Null when the journey has no pending organisation sign-in result.</summary>
    public static async Task<StepHandlerResult?> TryCompleteAsync(StepExecutionContext context, CancellationToken cancellationToken)
    {
        var organizationSignIn = context.ServiceProvider.GetService<IOrganizationSignInService>();
        if (organizationSignIn == null) return null;

        var authentication = await organizationSignIn.TakeJourneyResultAsync(context.JourneyId, cancellationToken);
        if (authentication == null) return null;

        var userService = context.ServiceProvider.GetRequiredService<IOlusoUserService>();
        var user = await userService.FindByIdAsync(authentication.UserId, cancellationToken);
        if (user == null || !user.IsActive)
        {
            context.ServiceProvider.GetService<ILogger<OrganizationSignInService>>()?.LogWarning(
                "Organisation sign-in result for journey {JourneyId} refers to a missing or inactive user", context.JourneyId);
            return StepHandlerResult.Fail("access_denied", "Your account is not available.");
        }

        context.SetAuthenticated(user.Id, AuthenticationMethod, authentication.IdentityProvider);
        await userService.RecordLoginAsync(user.Id, cancellationToken);

        var outputClaims = new Dictionary<string, object>
        {
            ["sub"] = user.Id,
            ["name"] = user.DisplayName ?? user.Username,
            ["email"] = user.Email ?? "",
            ["email_verified"] = user.EmailVerified,
            ["amr"] = authentication.AuthenticationMethods.Prepend("external").Distinct(StringComparer.Ordinal).ToArray()
        };
        if (!string.IsNullOrEmpty(authentication.IdentityProvider)) outputClaims["idp"] = authentication.IdentityProvider;
        if (!string.IsNullOrEmpty(user.FirstName)) outputClaims["given_name"] = user.FirstName;
        if (!string.IsNullOrEmpty(user.LastName)) outputClaims["family_name"] = user.LastName;

        return StepHandlerResult.Success(outputClaims);
    }
}

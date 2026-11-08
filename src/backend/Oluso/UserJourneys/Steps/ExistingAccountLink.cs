using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;

namespace Oluso.UserJourneys.Steps;

/// <summary>
/// An external sign-in in a journey whose email belongs to an existing account the provider can't vouch for (see
/// <see cref="ExternalEmailTrust"/>): the person proves the account is theirs with a code sent to its address, and
/// only then is the external login linked to it. The journey counterpart of the standalone /account/link-account.
/// </summary>
public static class ExistingAccountLink
{
    public const int MaxCodeAttempts = 5;

    private const string UserKey = "existing_account_link_user";
    private const string ProviderKey = "existing_account_link_provider";
    private const string ProviderKeyKey = "existing_account_link_provider_key";
    private const string AttemptsKey = "existing_account_link_attempts";

    /// <summary>The outcome of a step run while a link waits: a screen to show, or the account now linked.</summary>
    public sealed record Outcome(StepHandlerResult? Result, string? LinkedUserId);

    /// <summary>The provider of the sign-in waiting for its account to be confirmed, or null.</summary>
    public static string? PendingProvider(StepExecutionContext context) =>
        string.IsNullOrEmpty(context.GetData<string>(UserKey)) ? null : context.GetData<string>(ProviderKey);

    /// <summary>Sends the code to the account and asks for it.</summary>
    public static async Task<StepHandlerResult> BeginAsync(
        StepExecutionContext context,
        string existingUserId,
        string provider,
        string providerKey,
        CancellationToken cancellationToken)
    {
        context.SetData(UserKey, existingUserId);
        context.SetData(ProviderKey, provider);
        context.SetData(ProviderKeyKey, providerKey);
        context.JourneyData.Remove(AttemptsKey);

        var mfa = context.ServiceProvider.GetService<IMfaService>();
        if (mfa == null || !await mfa.SendVerificationCodeAsync(existingUserId, "email", cancellationToken))
        {
            Clear(context);
            return CodeNotSent();
        }

        return await ShowAsync(context, existingUserId, provider, cancellationToken,
            info: $"An account with this email already exists. Enter the code we sent to link your {DisplayName(provider)} sign-in to it.");
    }

    /// <summary>
    /// Handles the code screen: resends, checks the code, and links the login once the code is right.
    /// </summary>
    public static async Task<Outcome> ContinueAsync(StepExecutionContext context, CancellationToken cancellationToken)
    {
        var userId = context.GetData<string>(UserKey)!;
        var provider = context.GetData<string>(ProviderKey) ?? "";
        var providerKey = context.GetData<string>(ProviderKeyKey) ?? "";
        var mfa = context.ServiceProvider.GetService<IMfaService>();
        var externalAuth = context.ServiceProvider.GetService<IExternalAuthService>();
        var users = context.ServiceProvider.GetRequiredService<IOlusoUserService>();
        var logger = context.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ExistingAccountLink));

        if (mfa == null || externalAuth == null || string.IsNullOrEmpty(provider) || string.IsNullOrEmpty(providerKey))
        {
            Clear(context);
            return new Outcome(CodeNotSent(), null);
        }

        if (!string.IsNullOrEmpty(context.GetInput("resend")))
        {
            if (!await mfa.SendVerificationCodeAsync(userId, "email", cancellationToken))
            {
                Clear(context);
                return new Outcome(CodeNotSent(), null);
            }
            context.JourneyData.Remove(AttemptsKey);
            return new Outcome(await ShowAsync(context, userId, provider, cancellationToken, info: "A new code has been sent."), null);
        }

        var code = context.GetInput("code");
        if (string.IsNullOrWhiteSpace(code))
        {
            return new Outcome(await ShowAsync(context, userId, provider, cancellationToken), null);
        }

        if (!await mfa.VerifyEmailCodeAsync(userId, code.Trim(), cancellationToken))
        {
            var attempts = (context.GetData<int?>(AttemptsKey) ?? 0) + 1;
            context.SetData(AttemptsKey, attempts);
            logger.LogWarning("Wrong account-link code for user {UserId} ({Attempts}/{Max})", userId, attempts, MaxCodeAttempts);
            if (attempts >= MaxCodeAttempts)
            {
                Clear(context);
                await externalAuth.SignOutExternalAsync(cancellationToken);
                return new Outcome(StepHandlerResult.Fail("too_many_attempts", "Too many attempts. Start the sign-in again."), null);
            }

            return new Outcome(await ShowAsync(context, userId, provider, cancellationToken,
                error: $"That code isn't right. {MaxCodeAttempts - attempts} attempt(s) left."), null);
        }

        var linked = await externalAuth.LinkLoginAsync(userId, provider, providerKey, provider, cancellationToken);
        if (!linked.Succeeded)
        {
            logger.LogWarning("Linking {Provider} to user {UserId} failed after the code was confirmed", provider, userId);
            Clear(context);
            return new Outcome(StepHandlerResult.Fail("link_failed",
                "This sign-in couldn't be linked to your account. Try again or sign in another way."), null);
        }

        // The code reached the account's address: it is the person's.
        await users.SetEmailVerifiedAsync(userId, true, cancellationToken);
        Clear(context);
        logger.LogInformation("Linked {Provider} to user {UserId} after they confirmed the account by email", provider, userId);
        return new Outcome(null, userId);
    }

    private static void Clear(StepExecutionContext context)
    {
        context.JourneyData.Remove(UserKey);
        context.JourneyData.Remove(ProviderKey);
        context.JourneyData.Remove(ProviderKeyKey);
        context.JourneyData.Remove(AttemptsKey);
    }

    private static StepHandlerResult CodeNotSent() =>
        StepHandlerResult.Fail("account_link_unavailable",
            "An account with this email already exists, and we couldn't send it a code. Sign in with your password instead, or contact your administrator.");

    private static string DisplayName(string provider) =>
        provider.StartsWith("saml:", StringComparison.OrdinalIgnoreCase) ? provider[5..] : provider;

    private static async Task<StepHandlerResult> ShowAsync(
        StepExecutionContext context,
        string userId,
        string provider,
        CancellationToken cancellationToken,
        string? info = null,
        string? error = null)
    {
        var users = context.ServiceProvider.GetRequiredService<IOlusoUserService>();
        var settings = context.ServiceProvider.GetRequiredService<ITenantSettingsProvider>();
        var user = await users.FindByIdAsync(userId, cancellationToken);
        var accountEmails = string.IsNullOrEmpty(user?.TenantId)
            ? await settings.GetAccountEmailSettingsAsync(cancellationToken)
            : await settings.GetAccountEmailSettingsAsync(user!.TenantId, cancellationToken);

        return StepHandlerResult.ShowUi("Journey/_EmailVerificationConfirm", new EmailVerificationConfirmViewModel
        {
            Email = EmailVerificationStepHandler.MaskEmail(user?.Email ?? ""),
            Mode = "otp",
            CanChangeEmail = false,
            ExpirationMinutes = accountEmails.MfaCodeLifetimeMinutes,
            ErrorMessage = error,
            SuccessMessage = info,
            JourneyId = context.JourneyId,
            CurrentStepId = context.StepId
        });
    }
}

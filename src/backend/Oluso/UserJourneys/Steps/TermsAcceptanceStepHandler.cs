using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;

namespace Oluso.UserJourneys.Steps;

/// <summary>
/// Handles Terms of Service and Privacy Policy acceptance during user journeys.
/// Tracks which versions have been accepted and when.
/// </summary>
public class TermsAcceptanceStepHandler : IStepHandler
{
    public string StepType => "terms_acceptance";

    public Task<StepConfigurationValidationResult> ValidateConfigurationAsync(IDictionary<string, object>? configuration)
    {
        var url = StepConfigurationHelpers.GetString(configuration, "termsUrl");
        if (string.IsNullOrWhiteSpace(url))
        {
            return Task.FromResult(StepConfigurationValidationResult.Invalid("Terms URL is required"));
        }
        return Task.FromResult(StepConfigurationValidationResult.Valid());
    }

    public async Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default)
    {
        var userService = context.ServiceProvider.GetRequiredService<IOlusoUserService>();
        var tenantContext = context.ServiceProvider.GetRequiredService<ITenantContext>();
        var logger = context.ServiceProvider.GetRequiredService<ILogger<TermsAcceptanceStepHandler>>();

        // Get terms configuration
        // Note: HTML content is sanitized when the journey policy is saved (UserJourneyPoliciesController)
        // Legacy `version` field is a single string covering both documents — used as fallback
        // when the more granular termsVersion/privacyVersion aren't provided.
        var legacyVersion = context.GetConfig<string?>("version", null);
        var termsVersion = context.GetConfig<string?>("termsVersion", null) ?? legacyVersion ?? "1.0";
        var privacyVersion = context.GetConfig<string?>("privacyVersion", null) ?? legacyVersion ?? "1.0";
        var termsUrl = context.GetConfig<string>("termsUrl", null);
        var privacyUrl = context.GetConfig<string>("privacyUrl", null);
        var termsContent = context.GetConfig<string>("termsContent", null);
        var privacyContent = context.GetConfig<string>("privacyContent", null);
        var requireBoth = context.GetConfig("requireBoth", true);
        var requireCheckbox = context.GetConfig("requireCheckbox", true);
        var canDecline = context.GetConfig("canDecline", false);
        var termsTitle = context.GetLocalizedConfig("termsTitle") ?? Oluso.Core.Locale.UiStringDefaults.Resolve("signup_terms_link");
        var privacyTitle = context.GetLocalizedConfig("privacyTitle") ?? Oluso.Core.Locale.UiStringDefaults.Resolve("signup_privacy_link");

        // Check if user has already accepted current versions
        if (!string.IsNullOrEmpty(context.UserId))
        {
            var user = await userService.FindByIdAsync(context.UserId, cancellationToken);
            if (user != null)
            {
                var acceptedTermsVersion = context.GetData<string>("accepted_terms_version")
                    ?? await GetAcceptedVersionAsync(userService, context.UserId, "terms", cancellationToken);
                var acceptedPrivacyVersion = context.GetData<string>("accepted_privacy_version")
                    ?? await GetAcceptedVersionAsync(userService, context.UserId, "privacy", cancellationToken);

                var termsUpToDate = acceptedTermsVersion == termsVersion;
                var privacyUpToDate = acceptedPrivacyVersion == privacyVersion;

                if (termsUpToDate && (!requireBoth || privacyUpToDate))
                {
                    logger.LogDebug("User {UserId} has already accepted current terms and privacy versions", context.UserId);
                    return StepHandlerResult.Skip();
                }
            }
        }

        // Decline aborts the journey when allowed.
        if (canDecline && context.GetInput("decline") == "true")
        {
            logger.LogInformation("User {UserId} declined terms acceptance", context.UserId ?? "(anonymous)");
            return StepHandlerResult.Fail("terms_declined", "Terms acceptance is required to continue");
        }

        // Check if user submitted acceptance.
        // When requireCheckbox=false, the act of submitting the form
        // (presence of any submit input) is treated as implicit acceptance.
        var submitted = !string.IsNullOrEmpty(context.GetInput("_submitted"))
            || context.GetInput("acceptTerms") != null
            || context.GetInput("continue") == "true";
        var termsAccepted = requireCheckbox
            ? context.GetInput("acceptTerms") == "true"
            : submitted;
        var privacyAccepted = requireCheckbox
            ? (context.GetInput("acceptPrivacy") == "true" || !requireBoth)
            : (submitted || !requireBoth);

        if (!termsAccepted)
        {
            // Show acceptance form
            return StepHandlerResult.ShowUi("Journey/_TermsAcceptance", new TermsAcceptanceViewModel
            {
                TermsTitle = termsTitle,
                PrivacyTitle = privacyTitle,
                TermsVersion = termsVersion,
                PrivacyVersion = privacyVersion,
                TermsUrl = termsUrl,
                PrivacyUrl = privacyUrl,
                TermsContent = termsContent,
                PrivacyContent = privacyContent,
                RequireBoth = requireBoth,
                RequireCheckbox = requireCheckbox,
                ShowTermsInline = context.GetConfig("showTermsInline", false),
                ShowPrivacyInline = context.GetConfig("showPrivacyInline", false),
                TenantName = tenantContext.Tenant?.Name,
                AcceptButtonText = context.GetLocalizedConfig("acceptButtonText") ?? Oluso.Core.Locale.UiStringDefaults.Resolve("terms_acceptance_accept_button"),
                DeclineButtonText = context.GetLocalizedConfig("declineButtonText") ?? Oluso.Core.Locale.UiStringDefaults.Resolve("terms_acceptance_decline_button"),
                CanDecline = canDecline
            });
        }

        if (requireBoth && !privacyAccepted)
        {
            return StepHandlerResult.ShowUi("Journey/_TermsAcceptance", new TermsAcceptanceViewModel
            {
                ErrorMessage = "You must accept both Terms of Service and Privacy Policy to continue",
                TermsTitle = termsTitle,
                PrivacyTitle = privacyTitle,
                TermsVersion = termsVersion,
                PrivacyVersion = privacyVersion,
                TermsUrl = termsUrl,
                PrivacyUrl = privacyUrl,
                RequireBoth = requireBoth,
                RequireCheckbox = requireCheckbox,
                CanDecline = canDecline,
                TenantName = tenantContext.Tenant?.Name
            });
        }

        var acceptedAt = DateTime.UtcNow;

        // Record acceptance for authenticated users
        if (!string.IsNullOrEmpty(context.UserId))
        {
            var recorded = await RecordAcceptanceAsync(userService, context.UserId, "terms", termsVersion, acceptedAt, cancellationToken);
            if (recorded && requireBoth && privacyAccepted)
            {
                recorded = await RecordAcceptanceAsync(userService, context.UserId, "privacy", privacyVersion, acceptedAt, cancellationToken);
            }
            if (!recorded)
            {
                logger.LogError("Recording terms acceptance for user {UserId} failed", context.UserId);
                return StepHandlerResult.ShowUi("Journey/_TermsAcceptance", new TermsAcceptanceViewModel
                {
                    ErrorMessage = "We couldn't record your acceptance. Please try again.",
                    TermsTitle = termsTitle,
                    PrivacyTitle = privacyTitle,
                    TermsVersion = termsVersion,
                    PrivacyVersion = privacyVersion,
                    TermsUrl = termsUrl,
                    PrivacyUrl = privacyUrl,
                    RequireBoth = requireBoth,
                    RequireCheckbox = requireCheckbox,
                    CanDecline = canDecline,
                    TenantName = tenantContext.Tenant?.Name
                });
            }
            logger.LogInformation("User {UserId} accepted terms v{TermsVersion} and privacy v{PrivacyVersion}",
                context.UserId, termsVersion, privacyVersion);
        }
        else
        {
            // No account yet (terms before sign-up): sign-up records this once it creates one.
            logger.LogDebug("Terms accepted before an account exists; kept in the journey for sign-up");
        }

        // Store acceptance in journey data
        context.SetData("terms_accepted", true);
        context.SetData("terms_accepted_at", acceptedAt);
        context.SetData("accepted_terms_version", termsVersion);
        if (requireBoth && privacyAccepted)
        {
            context.SetData("privacy_accepted", true);
            context.SetData("privacy_accepted_at", acceptedAt);
            context.SetData("accepted_privacy_version", privacyVersion);
        }

        return StepHandlerResult.Success(new Dictionary<string, object>
        {
            ["terms_accepted"] = true,
            ["terms_version"] = termsVersion,
            ["terms_accepted_at"] = acceptedAt,
            ["privacy_accepted"] = privacyAccepted,
            ["privacy_version"] = privacyVersion
        });
    }

    private static async Task<string?> GetAcceptedVersionAsync(
        IOlusoUserService userService,
        string userId,
        string documentType,
        CancellationToken cancellationToken)
    {
        // Try to get from user custom properties
        var user = await userService.FindByIdAsync(userId, cancellationToken);
        if (user?.CustomProperties == null) return null;

        var claimType = documentType == "terms" ? "accepted_terms_version" : "accepted_privacy_version";
        return user.CustomProperties.TryGetValue(claimType, out var value) ? value : null;
    }

    /// <summary>
    /// Records terms (and privacy) accepted earlier in the journey, before the account
    /// existed, on the account sign-up just created.
    /// </summary>
    public static async Task<bool> RecordPendingAcceptanceAsync(StepExecutionContext context, IOlusoUserService userService, string userId, CancellationToken cancellationToken)
    {
        var recorded = true;
        foreach (var document in new[] { "terms", "privacy" })
        {
            if (context.GetData<bool>($"{document}_accepted")
                && context.GetData<string>($"accepted_{document}_version") is { Length: > 0 } version)
            {
                var acceptedAt = context.GetData<DateTime?>($"{document}_accepted_at") ?? DateTime.UtcNow;
                recorded &= await RecordAcceptanceAsync(userService, userId, document, version, acceptedAt, cancellationToken);
            }
        }
        return recorded;
    }

    private static async Task<bool> RecordAcceptanceAsync(
        IOlusoUserService userService,
        string userId,
        string documentType,
        string version,
        DateTime acceptedAt,
        CancellationToken cancellationToken)
    {
        // UpdateUserAsync replaces the entire CustomProperties map when
        // present. Fetch existing first and merge so this handler only
        // touches its own keys — otherwise we'd clobber properties set
        // by sign-up claimMappings or admin edits.
        var existing = await userService.FindByIdAsync(userId, cancellationToken);
        var props = existing?.CustomProperties != null
            ? new Dictionary<string, string>(existing.CustomProperties, StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        props[$"accepted_{documentType}_version"] = version;
        props[$"accepted_{documentType}_at"] = acceptedAt.ToString("O");

        var result = await userService.UpdateUserAsync(userId, new UpdateUserRequest
        {
            CustomProperties = props
        }, cancellationToken);
        return result.Succeeded;
    }
}

public class TermsAcceptanceViewModel
{
    public string? ErrorMessage { get; set; }
    public required string TermsTitle { get; init; }
    public required string PrivacyTitle { get; init; }
    public required string TermsVersion { get; init; }
    public required string PrivacyVersion { get; init; }
    public string? TermsUrl { get; init; }
    public string? PrivacyUrl { get; init; }
    public string? TermsContent { get; init; }
    public string? PrivacyContent { get; init; }
    public bool RequireBoth { get; init; } = true;
    public bool RequireCheckbox { get; init; } = true;
    public bool ShowTermsInline { get; init; }
    public bool ShowPrivacyInline { get; init; }
    public string? TenantName { get; init; }
    public string AcceptButtonText { get; init; } = "I Accept";
    public string? DeclineButtonText { get; init; }
    public bool CanDecline { get; init; }
}

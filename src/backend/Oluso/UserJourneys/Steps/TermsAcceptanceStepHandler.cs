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

    public async Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default)
    {
        var userService = context.ServiceProvider.GetRequiredService<IOlusoUserService>();
        var tenantContext = context.ServiceProvider.GetRequiredService<ITenantContext>();
        var logger = context.ServiceProvider.GetRequiredService<ILogger<TermsAcceptanceStepHandler>>();

        // Get terms configuration
        var termsVersion = context.GetConfig("termsVersion", "1.0");
        var privacyVersion = context.GetConfig("privacyVersion", "1.0");
        var termsUrl = context.GetConfig<string>("termsUrl", null);
        var privacyUrl = context.GetConfig<string>("privacyUrl", null);
        var termsContent = context.GetConfig<string>("termsContent", null);
        var privacyContent = context.GetConfig<string>("privacyContent", null);
        var requireBoth = context.GetConfig("requireBoth", true);
        var termsTitle = context.GetConfig("termsTitle", "Terms of Service");
        var privacyTitle = context.GetConfig("privacyTitle", "Privacy Policy");

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

        // Check if user submitted acceptance
        var termsAccepted = context.GetInput("acceptTerms") == "true";
        var privacyAccepted = context.GetInput("acceptPrivacy") == "true" || !requireBoth;

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
                ShowTermsInline = context.GetConfig("showTermsInline", false),
                ShowPrivacyInline = context.GetConfig("showPrivacyInline", false),
                TenantName = tenantContext.Tenant?.Name,
                AcceptButtonText = context.GetConfig("acceptButtonText", "I Accept"),
                DeclineButtonText = context.GetConfig("declineButtonText", "Decline"),
                CanDecline = context.GetConfig("canDecline", false)
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
                TenantName = tenantContext.Tenant?.Name
            });
        }

        var acceptedAt = DateTime.UtcNow;

        // Record acceptance for authenticated users
        if (!string.IsNullOrEmpty(context.UserId))
        {
            await RecordAcceptanceAsync(userService, context.UserId, "terms", termsVersion, acceptedAt, cancellationToken);
            if (requireBoth && privacyAccepted)
            {
                await RecordAcceptanceAsync(userService, context.UserId, "privacy", privacyVersion, acceptedAt, cancellationToken);
            }
            logger.LogInformation("User {UserId} accepted terms v{TermsVersion} and privacy v{PrivacyVersion}",
                context.UserId, termsVersion, privacyVersion);
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

    private static async Task RecordAcceptanceAsync(
        IOlusoUserService userService,
        string userId,
        string documentType,
        string version,
        DateTime acceptedAt,
        CancellationToken cancellationToken)
    {
        var props = new Dictionary<string, string>
        {
            [$"accepted_{documentType}_version"] = version,
            [$"accepted_{documentType}_at"] = acceptedAt.ToString("O")
        };

        await userService.UpdateUserAsync(userId, new UpdateUserRequest
        {
            CustomProperties = props
        }, cancellationToken);
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
    public bool ShowTermsInline { get; init; }
    public bool ShowPrivacyInline { get; init; }
    public string? TenantName { get; init; }
    public string AcceptButtonText { get; init; } = "I Accept";
    public string? DeclineButtonText { get; init; }
    public bool CanDecline { get; init; }
}

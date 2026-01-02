using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;

namespace Oluso.UserJourneys.Steps;

/// <summary>
/// Handles linking external identity provider accounts to a user's local account.
/// Users can link multiple social/enterprise logins to a single account.
/// </summary>
/// <remarks>
/// Configuration options:
/// - allowUnlink: allow users to unlink providers (default: true)
/// - requireConfirmation: require confirmation before linking (default: true)
/// - allowedProviders: list of allowed providers (default: all)
/// </remarks>
public class LinkAccountStepHandler : IStepHandler
{
    public string StepType => "link_account";

    public async Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default)
    {
        var userService = context.ServiceProvider.GetRequiredService<IOlusoUserService>();
        var externalAuthService = context.ServiceProvider.GetService<IExternalAuthService>();
        var logger = context.ServiceProvider.GetRequiredService<ILogger<LinkAccountStepHandler>>();

        var userId = context.UserId;
        if (string.IsNullOrEmpty(userId))
        {
            return StepHandlerResult.Fail("not_authenticated", "User must be authenticated to link accounts");
        }

        var user = await userService.FindByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            return StepHandlerResult.Fail("user_not_found", "User not found");
        }

        var allowUnlink = context.GetConfig("allowUnlink", true);
        var requireConfirmation = context.GetConfig("requireConfirmation", true);
        var allowedProviders = context.GetConfig<List<string>?>("allowedProviders", null);

        // Handle external login callback
        var provider = context.GetInput("_external_provider");
        var providerKey = context.GetInput("_external_provider_key");
        if (!string.IsNullOrEmpty(provider) && !string.IsNullOrEmpty(providerKey))
        {
            // Check if provider is allowed
            if (allowedProviders != null && !allowedProviders.Contains(provider, StringComparer.OrdinalIgnoreCase))
            {
                return StepHandlerResult.ShowUi("Journey/_LinkAccount", new LinkAccountViewModel
                {
                    UserId = user.Id,
                    UserEmail = user.Email,
                    LinkedLogins = await GetLinkedLoginsAsync(user.Id, externalAuthService, cancellationToken),
                    AllowUnlink = allowUnlink,
                    AllowedProviders = allowedProviders,
                    ErrorMessage = $"Linking with {provider} is not allowed."
                });
            }

            // Check if this login is already linked to another account
            if (externalAuthService != null)
            {
                var existingUserId = await externalAuthService.FindUserByLoginAsync(provider, providerKey, cancellationToken);
                if (!string.IsNullOrEmpty(existingUserId) && existingUserId != userId)
                {
                    return StepHandlerResult.ShowUi("Journey/_LinkAccount", new LinkAccountViewModel
                    {
                        UserId = user.Id,
                        UserEmail = user.Email,
                        LinkedLogins = await GetLinkedLoginsAsync(user.Id, externalAuthService, cancellationToken),
                        AllowUnlink = allowUnlink,
                        AllowedProviders = allowedProviders,
                        ErrorMessage = $"This {provider} account is already linked to a different user."
                    });
                }

                if (existingUserId == userId)
                {
                    return StepHandlerResult.ShowUi("Journey/_LinkAccount", new LinkAccountViewModel
                    {
                        UserId = user.Id,
                        UserEmail = user.Email,
                        LinkedLogins = await GetLinkedLoginsAsync(user.Id, externalAuthService, cancellationToken),
                        AllowUnlink = allowUnlink,
                        AllowedProviders = allowedProviders,
                        SuccessMessage = $"Your {provider} account is already linked."
                    });
                }
            }

            // Show confirmation if required
            var providerDisplayName = context.GetInput("_external_provider_name") ?? provider;
            if (requireConfirmation && string.IsNullOrEmpty(context.GetInput("_confirmed")))
            {
                return StepHandlerResult.ShowUi("Journey/_LinkAccountConfirm", new LinkAccountConfirmViewModel
                {
                    Provider = provider,
                    ProviderKey = providerKey,
                    ProviderDisplayName = providerDisplayName
                });
            }

            // Link the account
            if (externalAuthService != null)
            {
                var linkResult = await externalAuthService.LinkLoginAsync(
                    userId, provider, providerKey, providerDisplayName, cancellationToken);

                if (!linkResult.Succeeded)
                {
                    logger.LogWarning("Failed to link {Provider} account for user {UserId}: {Error}",
                        provider, userId, linkResult.Error);

                    return StepHandlerResult.ShowUi("Journey/_LinkAccount", new LinkAccountViewModel
                    {
                        UserId = user.Id,
                        UserEmail = user.Email,
                        LinkedLogins = await GetLinkedLoginsAsync(user.Id, externalAuthService, cancellationToken),
                        AllowUnlink = allowUnlink,
                        AllowedProviders = allowedProviders,
                        ErrorMessage = $"Failed to link account: {linkResult.Error}"
                    });
                }

                logger.LogInformation("User {UserId} linked {Provider} account", userId, provider);
            }

            return StepHandlerResult.ShowUi("Journey/_LinkAccount", new LinkAccountViewModel
            {
                UserId = user.Id,
                UserEmail = user.Email,
                LinkedLogins = await GetLinkedLoginsAsync(user.Id, externalAuthService, cancellationToken),
                AllowUnlink = allowUnlink,
                AllowedProviders = allowedProviders,
                SuccessMessage = $"Successfully linked your {provider} account!"
            });
        }

        // Handle unlink request
        var unlinkProvider = context.GetInput("unlink_provider");
        var unlinkKey = context.GetInput("unlink_key");
        if (!string.IsNullOrEmpty(unlinkProvider) && !string.IsNullOrEmpty(unlinkKey))
        {
            if (!allowUnlink)
            {
                return StepHandlerResult.ShowUi("Journey/_LinkAccount", new LinkAccountViewModel
                {
                    UserId = user.Id,
                    UserEmail = user.Email,
                    LinkedLogins = await GetLinkedLoginsAsync(user.Id, externalAuthService, cancellationToken),
                    AllowUnlink = allowUnlink,
                    AllowedProviders = allowedProviders,
                    ErrorMessage = "Account unlinking is not allowed."
                });
            }

            if (externalAuthService != null)
            {
                // Check if this is the only login method
                var logins = await externalAuthService.GetUserLoginsAsync(userId, cancellationToken);
                var hasPassword = await userService.HasPasswordAsync(userId, cancellationToken);

                if (logins.Count <= 1 && !hasPassword)
                {
                    return StepHandlerResult.ShowUi("Journey/_LinkAccount", new LinkAccountViewModel
                    {
                        UserId = user.Id,
                        UserEmail = user.Email,
                        LinkedLogins = await GetLinkedLoginsAsync(user.Id, externalAuthService, cancellationToken),
                        AllowUnlink = allowUnlink,
                        AllowedProviders = allowedProviders,
                        ErrorMessage = "Cannot unlink your only login method. Please set a password or link another account first."
                    });
                }

                var unlinkResult = await externalAuthService.UnlinkLoginAsync(userId, unlinkProvider, unlinkKey, cancellationToken);

                if (!unlinkResult.Succeeded)
                {
                    return StepHandlerResult.ShowUi("Journey/_LinkAccount", new LinkAccountViewModel
                    {
                        UserId = user.Id,
                        UserEmail = user.Email,
                        LinkedLogins = await GetLinkedLoginsAsync(user.Id, externalAuthService, cancellationToken),
                        AllowUnlink = allowUnlink,
                        AllowedProviders = allowedProviders,
                        ErrorMessage = $"Failed to unlink account: {unlinkResult.Error}"
                    });
                }

                logger.LogInformation("User {UserId} unlinked {Provider} account", userId, unlinkProvider);
            }

            return StepHandlerResult.ShowUi("Journey/_LinkAccount", new LinkAccountViewModel
            {
                UserId = user.Id,
                UserEmail = user.Email,
                LinkedLogins = await GetLinkedLoginsAsync(user.Id, externalAuthService, cancellationToken),
                AllowUnlink = allowUnlink,
                AllowedProviders = allowedProviders,
                SuccessMessage = $"Successfully unlinked your {unlinkProvider} account."
            });
        }

        // Handle "done" to continue journey
        if (!string.IsNullOrEmpty(context.GetInput("done")))
        {
            var linkedLogins = externalAuthService != null
                ? await externalAuthService.GetUserLoginsAsync(userId, cancellationToken)
                : new List<ExternalLoginInfo>();

            return StepHandlerResult.Success(new Dictionary<string, object>
            {
                ["linked_providers"] = string.Join(",", linkedLogins.Select(l => l.Provider)),
                ["linked_count"] = linkedLogins.Count
            });
        }

        // Show link account management UI
        return StepHandlerResult.ShowUi("Journey/_LinkAccount", new LinkAccountViewModel
        {
            UserId = user.Id,
            UserEmail = user.Email,
            LinkedLogins = await GetLinkedLoginsAsync(user.Id, externalAuthService, cancellationToken),
            AllowUnlink = allowUnlink,
            AllowedProviders = allowedProviders
        });
    }

    private static async Task<List<LinkedLoginInfo>> GetLinkedLoginsAsync(
        string userId,
        IExternalAuthService? externalAuthService,
        CancellationToken cancellationToken)
    {
        if (externalAuthService == null)
            return new List<LinkedLoginInfo>();

        var logins = await externalAuthService.GetUserLoginsAsync(userId, cancellationToken);
        return logins.Select(l => new LinkedLoginInfo
        {
            Provider = l.Provider,
            ProviderKey = l.ProviderKey,
            ProviderDisplayName = l.DisplayName ?? l.Provider
        }).ToList();
    }
}

#region ViewModels

public class LinkedLoginInfo
{
    public string Provider { get; set; } = null!;
    public string ProviderKey { get; set; } = null!;
    public string ProviderDisplayName { get; set; } = null!;
}

public class LinkAccountViewModel
{
    public string UserId { get; set; } = null!;
    public string? UserEmail { get; set; }
    public List<LinkedLoginInfo> LinkedLogins { get; set; } = new();
    public bool AllowUnlink { get; set; } = true;
    public List<string>? AllowedProviders { get; set; }
    public string? SuccessMessage { get; set; }
    public string? ErrorMessage { get; set; }
}

public class LinkAccountConfirmViewModel
{
    public string Provider { get; set; } = null!;
    public string ProviderKey { get; set; } = null!;
    public string ProviderDisplayName { get; set; } = null!;
}

#endregion

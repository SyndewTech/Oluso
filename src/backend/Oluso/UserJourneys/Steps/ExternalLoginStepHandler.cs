using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;

namespace Oluso.UserJourneys.Steps;

/// <summary>
/// Handles external identity provider authentication step.
/// Shows available external login providers and handles the OAuth flow.
/// Supports proxy mode for federation broker scenarios.
/// </summary>
public class ExternalLoginStepHandler : IStepHandler
{
    public string StepType => "external_login";

    public async Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default)
    {
        var externalAuthService = context.ServiceProvider.GetService<IExternalAuthService>();
        var tenantContext = context.ServiceProvider.GetRequiredService<ITenantContext>();
        var logger = context.ServiceProvider.GetRequiredService<ILogger<ExternalLoginStepHandler>>();

        if (externalAuthService == null)
        {
            logger.LogWarning("External authentication service not configured");
            return StepHandlerResult.Skip();
        }

        // Check if this is a callback from external provider
        var externalResult = await externalAuthService.GetExternalLoginResultAsync(cancellationToken);
        if (externalResult != null && externalResult.Succeeded)
        {
            return await HandleExternalCallbackAsync(context, externalAuthService, externalResult, logger, cancellationToken);
        }

        // Check if provider was selected
        var selectedProvider = context.GetInput("provider");
        if (!string.IsNullOrEmpty(selectedProvider))
        {
            return await InitiateExternalLoginAsync(context, externalAuthService, selectedProvider, logger, cancellationToken);
        }

        // Get available external providers, filtered by client IdP restrictions
        var providers = !string.IsNullOrEmpty(context.ClientId)
            ? await externalAuthService.GetAvailableProvidersAsync(context.ClientId, cancellationToken)
            : await externalAuthService.GetAvailableProvidersAsync(cancellationToken);

        // Also check configuration for allowed providers (step-level config)
        var allowedProviders = context.GetConfig<List<string>>("providers", null);
        if (allowedProviders != null)
        {
            providers = providers.Where(p => allowedProviders.Contains(p.Scheme)).ToList();
        }

        if (!providers.Any())
        {
            logger.LogDebug("No external providers available");
            return StepHandlerResult.Skip();
        }

        // Check for domain_hint - can be used to auto-select a provider
        // domain_hint maps to provider schemes via naming convention or client properties
        var domainHint = context.GetData<string>("domain_hint");
        if (!string.IsNullOrEmpty(domainHint))
        {
            // Try to find a matching provider by domain hint
            // Common conventions:
            // - "google.com" -> "Google"
            // - "microsoft.com" or "azure" -> "AzureAD" or "Microsoft"
            // - Custom domain like "acme.com" -> look in client properties for mapping
            var matchedProvider = FindProviderByDomainHint(providers, domainHint, context);
            if (matchedProvider != null)
            {
                logger.LogInformation("Domain hint '{DomainHint}' matched provider '{Provider}'", domainHint, matchedProvider);
                return await InitiateExternalLoginAsync(context, externalAuthService, matchedProvider, logger, cancellationToken);
            }
            else
            {
                logger.LogDebug("Domain hint '{DomainHint}' did not match any available provider", domainHint);
            }
        }

        // If only one provider and auto-redirect is enabled, go directly
        var autoRedirect = context.GetConfig("autoRedirect", false);
        if (autoRedirect && providers.Count == 1)
        {
            return await InitiateExternalLoginAsync(context, externalAuthService, providers.First().Scheme, logger, cancellationToken);
        }

        // Show provider selection UI
        return StepHandlerResult.ShowUi("Journey/_ExternalLogin", new ExternalLoginViewModel
        {
            Providers = providers.Select(p => new ExternalProviderViewModel
            {
                Scheme = p.Scheme,
                DisplayName = p.DisplayName ?? p.Scheme,
                IconUrl = p.IconUrl
            }).ToList(),
            TenantName = tenantContext.Tenant?.Name
        });
    }

    private async Task<StepHandlerResult> InitiateExternalLoginAsync(
        StepExecutionContext context,
        IExternalAuthService authService,
        string provider,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // Verify the provider is allowed for this client (IdP restrictions)
        if (!string.IsNullOrEmpty(context.ClientId))
        {
            var allowedProviders = await authService.GetAvailableProvidersAsync(context.ClientId, cancellationToken);
            if (!allowedProviders.Any(p => p.Scheme.Equals(provider, StringComparison.OrdinalIgnoreCase)))
            {
                logger.LogWarning(
                    "Provider {Provider} not allowed for client {ClientId} due to IdP restrictions",
                    provider, context.ClientId);
                return StepHandlerResult.Fail("idp_restricted",
                    $"The identity provider '{provider}' is not allowed for this application");
            }
        }

        logger.LogInformation("Initiating external login with provider {Provider}", provider);

        // Build callback URL with journey ID
        var callbackUrl = $"/journey/{context.JourneyId}/callback";

        var challengeResult = await authService.ChallengeAsync(provider, callbackUrl, cancellationToken);

        if (!challengeResult.Succeeded)
        {
            return StepHandlerResult.Fail("external_auth_failed", challengeResult.Error ?? "Failed to initiate external login");
        }

        // Return redirect to external provider
        return StepHandlerResult.Redirect(challengeResult.RedirectUrl ?? callbackUrl);
    }

    private async Task<StepHandlerResult> HandleExternalCallbackAsync(
        StepExecutionContext context,
        IExternalAuthService authService,
        ExternalLoginResult result,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var userService = context.ServiceProvider.GetRequiredService<IOlusoUserService>();
        var tenantContext = context.ServiceProvider.GetRequiredService<ITenantContext>();

        var provider = result.Provider;
        var providerKey = result.ProviderKey;
        var config = result.ProviderConfig;

        logger.LogInformation("External login callback received from {Provider}", provider);

        // Verify the provider is allowed for this client (IdP restrictions)
        // This prevents circumventing restrictions by directly hitting the callback
        if (!string.IsNullOrEmpty(context.ClientId))
        {
            var allowedProviders = await authService.GetAvailableProvidersAsync(context.ClientId, cancellationToken);
            if (!allowedProviders.Any(p => p.Scheme.Equals(provider, StringComparison.OrdinalIgnoreCase)))
            {
                logger.LogWarning(
                    "Provider {Provider} not allowed for client {ClientId} due to IdP restrictions (callback validation)",
                    provider, context.ClientId);

                // Sign out of external scheme before returning error
                await authService.SignOutExternalAsync(cancellationToken);

                return StepHandlerResult.Fail("idp_restricted",
                    $"The identity provider '{provider}' is not allowed for this application");
            }
        }

        // Check for proxy mode
        var isProxyMode = config?.ProxyMode ?? context.GetConfig("proxyMode", false);
        var storeUserLocally = config?.StoreUserLocally ?? !isProxyMode;

        // Handle proxy mode - pass through external claims without local user
        if (isProxyMode && !storeUserLocally)
        {
            logger.LogInformation("Proxy mode enabled for provider {Provider}, skipping local user storage", provider);
            return await HandleProxyModeAsync(context, authService, result, config, logger, cancellationToken);
        }

        // Standard mode - find or create local user
        var user = await FindUserByExternalLoginAsync(authService, userService, provider, providerKey, result.Email, cancellationToken);

        // Auto-provision if configured and user doesn't exist
        var autoProvision = config?.AutoProvisionUsers ?? context.GetConfig("autoProvision", true);
        if (user == null && autoProvision && !string.IsNullOrEmpty(result.Email))
        {
            var createResult = await userService.CreateUserAsync(new CreateUserRequest
            {
                Email = result.Email,
                Password = Guid.NewGuid().ToString(), // Random password since they'll use external login
                FirstName = result.FirstName,
                LastName = result.LastName,
                TenantId = tenantContext.TenantId,
                RequireEmailVerification = false // Trust external provider's email
            }, cancellationToken);

            if (createResult.Succeeded)
            {
                // Link external login
                await authService.LinkLoginAsync(createResult.UserId!, provider, providerKey, provider, cancellationToken);
                user = createResult.User;
                logger.LogInformation("Created new user {UserId} from external provider {Provider}", user?.Id, provider);
            }
            else
            {
                logger.LogError("Failed to create user from external login: {Error}", createResult.Error);
            }
        }

        // Sign out of external scheme
        await authService.SignOutExternalAsync(cancellationToken);

        if (user == null)
        {
            logger.LogWarning("No user found for external login and auto-provision disabled or failed");
            return StepHandlerResult.Fail("user_not_found", "No account found. Please register first.");
        }

        // Check if user is active
        if (!user.IsActive)
        {
            return StepHandlerResult.Fail("user_deactivated", "Your account has been deactivated");
        }

        // Cache external tokens if configured
        if (config?.CacheExternalTokens == true && !string.IsNullOrEmpty(result.AccessToken))
        {
            var tokenData = new ExternalTokenData
            {
                Provider = provider,
                ExternalSubject = providerKey,
                AccessToken = result.AccessToken,
                RefreshToken = result.RefreshToken,
                IdToken = result.IdToken,
                ExpiresAt = result.TokenExpiresAt
            };
            await authService.CacheExternalTokensAsync(
                user.Id,
                tokenData,
                TimeSpan.FromSeconds(config.TokenCacheDurationSeconds),
                cancellationToken);
        }

        // Update journey state
        context.UserId = user.Id;
        context.SetData("authenticated_at", DateTime.UtcNow);
        context.SetData("auth_method", provider.ToLower());
        context.SetData("idp", provider);

        // Record login
        await userService.RecordLoginAsync(user.Id, cancellationToken);

        logger.LogInformation("User {UserId} authenticated via external provider {Provider}", user.Id, provider);

        // Build output claims
        var outputClaims = new Dictionary<string, object>
        {
            ["sub"] = user.Id,
            ["name"] = user.DisplayName ?? user.Username,
            ["email"] = user.Email ?? "",
            ["email_verified"] = user.EmailVerified,
            ["idp"] = provider,
            ["amr"] = new[] { "external", provider.ToLower() }
        };

        if (!string.IsNullOrEmpty(user.FirstName))
            outputClaims["given_name"] = user.FirstName;
        if (!string.IsNullOrEmpty(user.LastName))
            outputClaims["family_name"] = user.LastName;

        // Include external tokens if configured
        if (config?.IncludeExternalAccessToken == true && !string.IsNullOrEmpty(result.AccessToken))
        {
            outputClaims["external_access_token"] = result.AccessToken;
        }
        if (config?.IncludeExternalIdToken == true && !string.IsNullOrEmpty(result.IdToken))
        {
            outputClaims["external_id_token"] = result.IdToken;
        }

        return StepHandlerResult.Success(outputClaims);
    }

    private async Task<StepHandlerResult> HandleProxyModeAsync(
        StepExecutionContext context,
        IExternalAuthService authService,
        ExternalLoginResult result,
        ExternalProviderConfig? config,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var provider = result.Provider;
        var providerKey = result.ProviderKey;

        // Generate proxy subject ID
        var proxySubject = $"proxy:{provider}:{providerKey}";

        // Cache external tokens if configured
        if (config?.CacheExternalTokens == true && !string.IsNullOrEmpty(result.AccessToken))
        {
            var tokenData = new ExternalTokenData
            {
                Provider = provider,
                ExternalSubject = providerKey,
                AccessToken = result.AccessToken,
                RefreshToken = result.RefreshToken,
                IdToken = result.IdToken,
                ExpiresAt = result.TokenExpiresAt
            };
            await authService.CacheExternalTokensAsync(
                proxySubject,
                tokenData,
                TimeSpan.FromSeconds(config?.TokenCacheDurationSeconds ?? 3600),
                cancellationToken);
        }

        // Sign out of external scheme
        await authService.SignOutExternalAsync(cancellationToken);

        // Update journey state
        context.UserId = proxySubject;
        context.SetData("authenticated_at", DateTime.UtcNow);
        context.SetData("auth_method", provider.ToLower());
        context.SetData("idp", provider);
        context.SetData("proxy_mode", true);
        context.SetData("external_subject", providerKey);

        logger.LogInformation("Proxy mode authentication completed for provider {Provider}, external subject {Subject}",
            provider, providerKey);

        // Build output claims
        var outputClaims = new Dictionary<string, object>
        {
            ["sub"] = proxySubject,
            ["idp"] = provider,
            ["external_sub"] = providerKey,
            ["proxy_mode"] = true,
            ["amr"] = new[] { "external", provider.ToLower(), "proxy" }
        };

        // Pass through external claims with filtering
        if (result.Claims != null)
        {
            var includeClaims = config?.ProxyIncludeClaims ?? Array.Empty<string>();
            var excludeClaims = config?.ProxyExcludeClaims ?? Array.Empty<string>();

            foreach (var claim in result.Claims)
            {
                // Skip if in exclude list
                if (excludeClaims.Contains(claim.Key))
                    continue;

                // If include list is specified, only include those claims
                if (includeClaims.Count > 0 && !includeClaims.Contains(claim.Key))
                    continue;

                // Map common claim types
                var claimKey = claim.Key switch
                {
                    ClaimTypes.Email => "email",
                    ClaimTypes.Name => "name",
                    ClaimTypes.GivenName => "given_name",
                    ClaimTypes.Surname => "family_name",
                    ClaimTypes.NameIdentifier => "sub_original",
                    _ => claim.Key
                };

                // Don't overwrite already set claims
                if (!outputClaims.ContainsKey(claimKey))
                {
                    outputClaims[claimKey] = claim.Value;
                }
            }
        }

        // Add standard claims from result
        if (!outputClaims.ContainsKey("email") && !string.IsNullOrEmpty(result.Email))
            outputClaims["email"] = result.Email;
        if (!outputClaims.ContainsKey("name") && !string.IsNullOrEmpty(result.Name))
            outputClaims["name"] = result.Name;
        if (!outputClaims.ContainsKey("given_name") && !string.IsNullOrEmpty(result.FirstName))
            outputClaims["given_name"] = result.FirstName;
        if (!outputClaims.ContainsKey("family_name") && !string.IsNullOrEmpty(result.LastName))
            outputClaims["family_name"] = result.LastName;

        // Include external tokens if configured
        if (config?.IncludeExternalAccessToken == true && !string.IsNullOrEmpty(result.AccessToken))
        {
            outputClaims["external_access_token"] = result.AccessToken;
        }
        if (config?.IncludeExternalIdToken == true && !string.IsNullOrEmpty(result.IdToken))
        {
            outputClaims["external_id_token"] = result.IdToken;
        }

        return StepHandlerResult.Success(outputClaims);
    }

    private async Task<OlusoUserInfo?> FindUserByExternalLoginAsync(
        IExternalAuthService authService,
        IOlusoUserService userService,
        string provider,
        string providerKey,
        string? email,
        CancellationToken cancellationToken)
    {
        // Try to find by external login first
        var userId = await authService.FindUserByLoginAsync(provider, providerKey, cancellationToken);
        if (userId != null)
        {
            return await userService.FindByIdAsync(userId, cancellationToken);
        }

        // Fall back to finding by email
        if (!string.IsNullOrEmpty(email))
        {
            return await userService.FindByEmailAsync(email, cancellationToken);
        }

        return null;
    }

    /// <summary>
    /// Finds a provider based on domain_hint parameter.
    /// Maps common domains to their corresponding identity providers.
    /// </summary>
    private static string? FindProviderByDomainHint(
        IReadOnlyList<ExternalProviderInfo> providers,
        string domainHint,
        StepExecutionContext context)
    {
        // Check for direct scheme match first (e.g., domain_hint=Google)
        var directMatch = providers.FirstOrDefault(p =>
            p.Scheme.Equals(domainHint, StringComparison.OrdinalIgnoreCase) ||
            p.DisplayName?.Equals(domainHint, StringComparison.OrdinalIgnoreCase) == true);

        if (directMatch != null)
            return directMatch.Scheme;

        // Normalize the domain hint
        var normalizedHint = domainHint.ToLowerInvariant().Trim();

        // Common domain to provider mappings
        var domainMappings = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            // Google
            { "google.com", new[] { "Google", "google" } },
            { "gmail.com", new[] { "Google", "google" } },

            // Microsoft/Azure AD
            { "microsoft.com", new[] { "Microsoft", "AzureAD", "MicrosoftAccount", "azure" } },
            { "outlook.com", new[] { "Microsoft", "MicrosoftAccount" } },
            { "hotmail.com", new[] { "Microsoft", "MicrosoftAccount" } },
            { "live.com", new[] { "Microsoft", "MicrosoftAccount" } },
            { "azure", new[] { "AzureAD", "Microsoft" } },
            { "azuread", new[] { "AzureAD", "Microsoft" } },

            // GitHub
            { "github.com", new[] { "GitHub", "github" } },

            // Facebook
            { "facebook.com", new[] { "Facebook", "facebook" } },

            // Twitter/X
            { "twitter.com", new[] { "Twitter", "twitter", "X" } },

            // LinkedIn
            { "linkedin.com", new[] { "LinkedIn", "linkedin" } },

            // Apple
            { "apple.com", new[] { "Apple", "SignInWithApple" } },
            { "icloud.com", new[] { "Apple", "SignInWithApple" } },

            // Okta
            { "okta.com", new[] { "Okta", "okta" } },

            // Auth0
            { "auth0.com", new[] { "Auth0", "auth0" } }
        };

        // Check if the domain hint matches any known mapping
        if (domainMappings.TryGetValue(normalizedHint, out var possibleSchemes))
        {
            foreach (var scheme in possibleSchemes)
            {
                var match = providers.FirstOrDefault(p =>
                    p.Scheme.Equals(scheme, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                    return match.Scheme;
            }
        }

        // Check if it's an email domain that might match a corporate SAML/OIDC provider
        // This allows enterprises to configure domain_hint mappings in client properties
        // e.g., domain_hint="acme.com" could map to "saml:acme" or "oidc:acme-okta"

        // Look for a provider whose display name or scheme contains the domain
        var domainPart = normalizedHint.Replace(".com", "").Replace(".org", "").Replace(".net", "");
        var fuzzyMatch = providers.FirstOrDefault(p =>
            p.Scheme.Contains(domainPart, StringComparison.OrdinalIgnoreCase) ||
            p.DisplayName?.Contains(domainPart, StringComparison.OrdinalIgnoreCase) == true);

        return fuzzyMatch?.Scheme;
    }
}

#region ViewModels

public class ExternalLoginViewModel
{
    public List<ExternalProviderViewModel> Providers { get; set; } = new();
    public string? TenantName { get; set; }
    public string? ErrorMessage { get; set; }
}

public class ExternalProviderViewModel
{
    public string Scheme { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string? IconUrl { get; set; }
}

#endregion

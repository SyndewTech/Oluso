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
public class ExternalLoginStepHandler : IPrimaryAuthenticationStep, IStepConfigurationTester
{
    public Task<StepConfigurationTestResult> TestConfigurationAsync(StepExecutionContext context, CancellationToken cancellationToken = default) =>
        StepTestSupport.TestExternalProvidersAsync(context, "providers", cancellationToken);

    public string StepType => "external_login";

    public Task<StepConfigurationValidationResult> ValidateConfigurationAsync(IDictionary<string, object>? configuration)
    {
        var warnings = new List<string>();

        // No specific providers configured isn't an error — at runtime the
        // step falls back to all enabled IdPs for the tenant. Worth a
        // warning so the admin knows that's what will happen. Only fire
        // when the key was explicitly set to an empty list; an absent key
        // is the more common "I haven't picked any yet" state and the
        // page already nudges the admin via empty-state UI.
        if (configuration != null
            && configuration.TryGetValue("providers", out var providers)
            && providers != null
            && !StepConfigurationHelpers.IsNonEmptyList(providers))
        {
            warnings.Add("No specific providers configured — the step will offer all enabled identity providers for this tenant.");
        }

        return Task.FromResult(StepConfigurationValidationResult.Valid(warnings.ToArray()));
    }

    public async Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (await OrganizationSignInStep.TryCompleteAsync(context, cancellationToken) is { } organizationResult)
        {
            return organizationResult;
        }

        var externalAuthService = context.ServiceProvider.GetService<IExternalAuthService>();
        var tenantContext = context.ServiceProvider.GetRequiredService<ITenantContext>();
        var logger = context.ServiceProvider.GetRequiredService<ILogger<ExternalLoginStepHandler>>();

        if (externalAuthService == null)
        {
            logger.LogWarning("External authentication service not configured");
            return StepHandlerResult.Skip();
        }

        // A sign-in waiting for the person to confirm the account its email belongs to. Once linked, the external
        // sign-in (still held) finds the account by its login below.
        if (ExistingAccountLink.PendingProvider(context) is { } pendingProvider
            && !pendingProvider.StartsWith("saml:", StringComparison.OrdinalIgnoreCase))
        {
            var outcome = await ExistingAccountLink.ContinueAsync(context, cancellationToken);
            if (outcome.Result != null)
            {
                return outcome.Result;
            }
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
            // On the platform host the step still offers "Sign in with your
            // organisation", which reaches the tenant's own providers.
            if (await IsOrganizationSignInOfferedAsync(context, cancellationToken))
            {
                return StepHandlerResult.ShowUi("Journey/_ExternalLogin", new ExternalLoginViewModel
                {
                    Providers = new List<ExternalProviderViewModel>(),
                    TenantName = tenantContext.Tenant?.Name
                });
            }

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
                IconUrl = p.IconUrl,
                ProviderType = p.ProviderType
            }).ToList(),
            TenantName = tenantContext.Tenant?.Name
        });
    }

    private static async Task<bool> IsOrganizationSignInOfferedAsync(StepExecutionContext context, CancellationToken cancellationToken)
    {
        var http = context.ServiceProvider.GetService<Microsoft.AspNetCore.Http.IHttpContextAccessor>()?.HttpContext;
        var organizationSignIn = context.ServiceProvider.GetService<Authentication.OrganizationSignIn.IOrganizationSignInService>();
        return http != null && organizationSignIn != null
            && await organizationSignIn.GetJourneyEntryUrlAsync(http, context.JourneyId, cancellationToken) != null;
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

        var extraParams = BuildExternalProviderParams(context);
        var challengeResult = await authService.ChallengeAsync(provider, callbackUrl, extraParams, cancellationToken);

        if (!challengeResult.Succeeded)
        {
            return StepHandlerResult.Fail("external_auth_failed", challengeResult.Error ?? "Failed to initiate external login");
        }

        // Return redirect to external provider
        return StepHandlerResult.Redirect(challengeResult.RedirectUrl ?? callbackUrl);
    }

    internal static Dictionary<string, string>? BuildExternalProviderParams(StepExecutionContext context)
    {
        Dictionary<string, string>? p = null;

        void Add(string key, string? value)
        {
            if (string.IsNullOrEmpty(value)) return;
            p ??= new Dictionary<string, string>();
            p[key] = value;
        }

        Add("ui_locales", context.ResolvedLocale);

        Add("domain_hint", context.GetDataString("domain_hint"));
        Add("login_hint", context.GetDataString("login_hint"));

        return p;
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

        // Access-control gate (admin-configured per-IdP). Evaluated before
        // proxy mode and provisioning so federation-broker logins are gated
        // too. Mirrors the standalone ExternalCallback's gate logic.
        if (config?.AccessControl != null)
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                (result.Claims ?? new Dictionary<string, string>())
                    .Select(kv => new Claim(kv.Key, kv.Value))));
            var decision = ExternalAccessControlEvaluator.Evaluate(principal, config.AccessControl);
            if (decision != ExternalAccessControlEvaluator.Decision.Allow)
            {
                logger.LogWarning(
                    "External login denied by IdP access control: provider={Provider} decision={Decision}",
                    provider, decision);
                await authService.SignOutExternalAsync(cancellationToken);
                return StepHandlerResult.Fail("access_denied", "Access denied for this account.");
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

        // Standard mode - find or create local user. The account this login is linked to; else the account with its
        // email, joined only when the provider can vouch for the address and the account's own was proven.
        var emailTrusted = await ExternalEmailTrust.IsTrustedAsync(
            provider,
            type => result.Claims?.FirstOrDefault(c => string.Equals(c.Key, type, StringComparison.Ordinal)).Value,
            result.Email,
            tenantContext.TenantId,
            context.ServiceProvider.GetService<IExternalProviderConfigStore>(),
            context.ServiceProvider.GetRequiredService<ITenantEmailDomainStore>(),
            cancellationToken);

        var linkedUserId = await authService.FindUserByLoginAsync(provider, providerKey, cancellationToken);
        var user = linkedUserId != null ? await userService.FindByIdAsync(linkedUserId, cancellationToken) : null;
        if (user == null && !string.IsNullOrEmpty(result.Email))
        {
            var owner = await userService.FindByEmailAsync(result.Email, cancellationToken);
            if (owner != null)
            {
                if (!emailTrusted || !owner.EmailVerified)
                {
                    logger.LogInformation(
                        "External login from {Provider} matches account {UserId} by email; asking them to confirm it (trusted={Trusted}, accountVerified={Verified})",
                        provider, owner.Id, emailTrusted, owner.EmailVerified);
                    return await ExistingAccountLink.BeginAsync(context, owner.Id, provider, providerKey, cancellationToken);
                }

                var linked = await authService.LinkLoginAsync(owner.Id, provider, providerKey, provider, cancellationToken);
                if (!linked.Succeeded)
                {
                    logger.LogWarning("Failed to link {Provider} to user {UserId}: {Error}", provider, owner.Id, linked.Error);
                    await authService.SignOutExternalAsync(cancellationToken);
                    return StepHandlerResult.Fail("link_failed", "Failed to link external account");
                }
                user = owner;
            }
        }

        // Auto-provision if configured and user doesn't exist
        var autoProvision = config?.AutoProvisionUsers ?? context.GetConfig("autoProvision", true);
        if (user == null && autoProvision && !string.IsNullOrEmpty(result.Email))
        {
            // Mirror the tenant-policy gates the standalone callback
            // applies. AllowSelfRegistration is the tenant-level kill
            // switch — IdP-driven auto-provision still respects it because
            // creating an account is creating an account, regardless of
            // who initiated the redirect. AllowedEmailDomains is a
            // tenant allowlist; enterprise SSO doesn't override it.
            var tenant = tenantContext.Tenant;
            if (tenant?.AllowSelfRegistration == false)
            {
                logger.LogWarning(
                    "Auto-provision blocked for {Provider} — self-registration disabled on tenant {TenantId}",
                    provider, tenantContext.TenantId);
                await authService.SignOutExternalAsync(cancellationToken);
                return StepHandlerResult.Fail("registration_disabled",
                    "Registration is not available for this tenant.");
            }

            if (!IsEmailDomainAllowed(result.Email, tenant?.AllowedEmailDomains))
            {
                logger.LogWarning(
                    "Auto-provision blocked for {Provider} — email domain not in tenant allowlist",
                    provider);
                await authService.SignOutExternalAsync(cancellationToken);
                return StepHandlerResult.Fail("email_domain_not_allowed",
                    "Registrations from this email domain are not allowed.");
            }

            var createResult = await userService.CreateUserAsync(new CreateUserRequest
            {
                Email = result.Email,
                // No password — external-login users authenticate via their IdP. Omitting
                // it makes CreateUserAsync create without one (HasPassword=false → truly
                // federated), matching the standalone external callback and disabling
                // password change/reset. (Was a Guid.NewGuid() junk password.)
                FirstName = result.FirstName,
                LastName = result.LastName,
                TenantId = tenantContext.TenantId,
                // Verified below only when the provider can vouch for the address (ExternalEmailTrust).
                ClientId = context.ClientId
            }, cancellationToken);

            if (createResult.Succeeded)
            {
                // Link external login
                await authService.LinkLoginAsync(createResult.UserId!, provider, providerKey, provider, cancellationToken);
                if (emailTrusted)
                {
                    await userService.SetEmailVerifiedAsync(createResult.UserId!, true, cancellationToken);
                }
                user = await userService.FindByIdAsync(createResult.UserId!, cancellationToken);
                logger.LogInformation("Created new user {UserId} from external provider {Provider}", user?.Id, provider);

                // Apply IdP DefaultRoles on first provision only. Additive —
                // never removed on later logins, so editing the IdP's
                // defaultRoles list won't strip access from existing users.
                if (config?.DefaultRoles != null && config.DefaultRoles.Count > 0)
                {
                    var roleStore = context.ServiceProvider.GetService<IRoleStore>();
                    var appliedRoleNames = new List<string>();
                    if (roleStore != null)
                    {
                        foreach (var roleId in config.DefaultRoles)
                        {
                            var role = await roleStore.GetByIdAsync(roleId, cancellationToken);
                            if (role == null
                                || string.IsNullOrEmpty(role.Name)
                                || (role.TenantId != null && role.TenantId != tenantContext.TenantId))
                            {
                                logger.LogWarning(
                                    "Default role {RoleId} skipped on user {UserId}: not found or not in tenant {TenantId}",
                                    roleId, createResult.UserId, tenantContext.TenantId);
                                continue;
                            }
                            await context.ServiceProvider.GetRequiredService<IRoleGrantService>()
                                .GrantAsync(role.Id, createResult.UserId!, null, RoleGrantActor.System($"External login ({provider})"), cancellationToken);
                            appliedRoleNames.Add(role.Name);
                        }
                    }
                    if (appliedRoleNames.Count > 0)
                    {
                        logger.LogInformation(
                            "Applied IdP default roles {Roles} to new user {UserId} (provider={Provider})",
                            string.Join(",", appliedRoleNames), createResult.UserId, provider);
                    }
                }
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

        // The step's claim mappings carry chosen claims from the provider (external claim
        // type -> claim name), as they do in proxy mode. The local account's identity
        // claims above stay authoritative.
        var claimMappings = context.GetConfig<Dictionary<string, string>>("claimMappings", null);
        if (claimMappings is { Count: > 0 } && result.Claims != null)
        {
            foreach (var (externalType, claimName) in claimMappings)
            {
                if (string.IsNullOrWhiteSpace(claimName) || outputClaims.ContainsKey(claimName)) continue;
                var external = result.Claims.FirstOrDefault(c => string.Equals(c.Key, externalType, StringComparison.OrdinalIgnoreCase));
                if (external.Value is { } mappedValue)
                {
                    outputClaims[claimName] = mappedValue;
                }
            }
        }

        // Include external tokens if configured
        if (config?.IncludeExternalAccessToken == true && !string.IsNullOrEmpty(result.AccessToken))
        {
            outputClaims["external_access_token"] = result.AccessToken;
        }
        if (config?.IncludeExternalIdToken == true && !string.IsNullOrEmpty(result.IdToken))
        {
            outputClaims["external_id_token"] = result.IdToken;
        }

        return StepHandlerResult.SuccessOrBranch(context, outputClaims, $"auth_{provider.ToLowerInvariant()}", "auth_external");
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
        var proxySubject = ExternalProxyClaimsBuilder.ProxySubjectFor(provider, providerKey);

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
                TimeSpan.FromSeconds(config.TokenCacheDurationSeconds),
                cancellationToken);
        }

        await authService.SignOutExternalAsync(cancellationToken);

        context.UserId = proxySubject;
        context.SetData("authenticated_at", DateTime.UtcNow);
        context.SetData("auth_method", provider.ToLowerInvariant());
        context.SetData("idp", provider);
        context.SetData("proxy_mode", true);
        context.SetData("external_subject", providerKey);

        logger.LogInformation("Proxy mode authentication completed for provider {Provider}, external subject {Subject}",
            provider, providerKey);

        var claimMappings = context.GetConfig<Dictionary<string, string>>("claimMappings", null);
        var outputClaims = ExternalProxyClaimsBuilder.Build(
            provider,
            providerKey,
            result.Claims,
            config,
            claimMappings,
            result.Email,
            result.Name,
            result.FirstName,
            result.LastName,
            result.AccessToken,
            result.IdToken);

        return StepHandlerResult.SuccessOrBranch(context, outputClaims, "auth_proxy", $"auth_{provider.ToLowerInvariant()}", "auth_external");
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

    /// <summary>
    /// Mirrors the AllowedEmailDomains check in the standalone Register and
    /// ExternalRegister pages. SSO-via-journey honours the same allowlist —
    /// the IdP doesn't override the tenant's "only listed domains" rule.
    /// </summary>
    private static bool IsEmailDomainAllowed(string email, string? allowedDomainsCsv)
    {
        if (string.IsNullOrWhiteSpace(allowedDomainsCsv)) return true;

        var atIndex = email.IndexOf('@');
        if (atIndex < 0 || atIndex == email.Length - 1) return false;
        var domain = email[(atIndex + 1)..].ToLowerInvariant();

        var domains = allowedDomainsCsv
            .Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(d => d.ToLowerInvariant());

        return domains.Contains(domain);
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
    public string? ProviderType { get; set; }
}

#endregion

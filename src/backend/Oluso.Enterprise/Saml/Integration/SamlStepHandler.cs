using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Oluso.Enterprise.Saml.Configuration;
using Oluso.Enterprise.Saml.ServiceProvider;

#pragma warning disable CS8604 // Possible null reference argument - IdP name validated upstream

namespace Oluso.Enterprise.Saml.Integration;

/// <summary>
/// Journey step handler for SAML 2.0 authentication.
/// Uses Oluso.Core abstractions for user management.
/// </summary>
public class SamlStepHandler : IStepHandler
{
    public string StepType => "saml";

    public async Task<StepHandlerResult> ExecuteAsync(
        StepExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var samlSp = context.ServiceProvider.GetRequiredService<ISamlServiceProvider>();
        var userService = context.ServiceProvider.GetRequiredService<IOlusoUserService>();
        var stateStore = context.ServiceProvider.GetRequiredService<IProtocolStateStore>();
        var logger = context.ServiceProvider.GetRequiredService<ILogger<SamlStepHandler>>();

        // Check if this is a SAML response callback
        var samlResponse = context.GetInput("SAMLResponse");
        if (!string.IsNullOrEmpty(samlResponse))
        {
            return await HandleSamlResponseAsync(context, samlSp, userService, stateStore, logger, cancellationToken);
        }

        // Check if a specific IdP was selected from UI
        var selectedIdp = context.GetInput("saml_idp");
        if (!string.IsNullOrEmpty(selectedIdp))
        {
            return await InitiateSamlLoginAsync(context, samlSp, stateStore, selectedIdp, cancellationToken);
        }

        // Check if IdP is configured in step settings (from x-enumSource selection in journey builder)
        var configuredIdpName = context.GetConfig<string>("idpName");
        if (!string.IsNullOrEmpty(configuredIdpName))
        {
            // Verify the IdP exists and is enabled
            var idps = await samlSp.GetConfiguredIdpsAsync(cancellationToken);
            var targetIdp = idps.FirstOrDefault(i => i.Name.Equals(configuredIdpName, StringComparison.OrdinalIgnoreCase));

            if (targetIdp == null)
            {
                logger.LogWarning("Configured SAML IdP '{IdpName}' not found", configuredIdpName);
                return StepHandlerResult.Failure("idp_not_found", $"SAML Identity Provider '{configuredIdpName}' not found");
            }

            if (!targetIdp.Enabled)
            {
                logger.LogWarning("Configured SAML IdP '{IdpName}' is disabled", configuredIdpName);
                return StepHandlerResult.Failure("idp_disabled", $"SAML Identity Provider '{configuredIdpName}' is disabled");
            }

            return await InitiateSamlLoginAsync(context, samlSp, stateStore, configuredIdpName, cancellationToken);
        }

        // No specific IdP configured - get all available SAML IdPs
        var availableIdps = (await samlSp.GetConfiguredIdpsAsync(cancellationToken)).Where(i => i.Enabled).ToList();

        if (!availableIdps.Any())
        {
            logger.LogWarning("No SAML IdPs configured");
            return StepHandlerResult.Skip();
        }

        // If only one IdP and auto-redirect enabled
        var autoRedirect = context.GetConfig("autoRedirect", false);
        if (autoRedirect && availableIdps.Count == 1)
        {
            return await InitiateSamlLoginAsync(context, samlSp, stateStore, availableIdps[0].Name, cancellationToken);
        }

        // Show IdP selection UI
        return StepHandlerResult.ShowUi("Journey/SamlIdpSelection", new SamlIdpSelectionViewModel
        {
            TenantName = context.TenantId,
            IdentityProviders = availableIdps.Select(i => new SamlIdpDisplayInfo
            {
                Name = i.Name,
                DisplayName = i.DisplayName
            }).ToList()
        });
    }

    private async Task<StepHandlerResult> InitiateSamlLoginAsync(
        StepExecutionContext context,
        ISamlServiceProvider samlSp,
        IProtocolStateStore stateStore,
        string idpName,
        CancellationToken cancellationToken)
    {
        var callbackUrl = $"/journey/{context.JourneyId}/saml-callback";

        // Store journey state for callback using ProtocolState
        var state = new ProtocolState
        {
            ProtocolName = "saml",
            SerializedRequest = System.Text.Json.JsonSerializer.Serialize(new { idpName, callbackUrl }),
            ClientId = context.ClientId ?? "",
            TenantId = context.TenantId,
            EndpointType = EndpointType.Authorize,
            Properties = new Dictionary<string, string>
            {
                ["journey_id"] = context.JourneyId,
                ["step_id"] = context.StepId,
                ["idp_name"] = idpName
            }
        };

        var correlationId = await stateStore.StoreAsync(state, TimeSpan.FromMinutes(10), cancellationToken);

        var request = await samlSp.CreateAuthnRequestAsync(new SamlAuthnRequestParams
        {
            IdpName = idpName,
            ReturnUrl = callbackUrl,
            RelayState = correlationId
        }, cancellationToken);

        // Store IdP name in journey data
        context.SetData("saml_idp", idpName);
        context.SetData("saml_correlation_id", correlationId);

        return StepHandlerResult.Redirect(request.Url);
    }

    private async Task<StepHandlerResult> HandleSamlResponseAsync(
        StepExecutionContext context,
        ISamlServiceProvider samlSp,
        IOlusoUserService userService,
        IProtocolStateStore stateStore,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var samlResponse = context.GetInput("SAMLResponse") ?? "";
        var relayState = context.GetInput("RelayState");

        // Get stored state
        ProtocolState? storedState = null;
        if (!string.IsNullOrEmpty(relayState))
        {
            storedState = await stateStore.GetAsync(relayState, cancellationToken);
        }

        // Get HTTP context from service provider for SAML processing
        var httpContextAccessor = context.ServiceProvider.GetService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
        var httpContext = httpContextAccessor?.HttpContext;

        if (httpContext == null)
        {
            logger.LogError("HTTP context not available for SAML response processing");
            return StepHandlerResult.Failure("saml_error", "Unable to process SAML response");
        }

        var result = await samlSp.ProcessResponseAsync(httpContext, samlResponse, relayState, cancellationToken);

        if (!result.Succeeded || result.Principal == null)
        {
            logger.LogWarning("SAML authentication failed: {Error}", result.Error);
            return StepHandlerResult.Failure("saml_auth_failed", result.Error ?? "SAML authentication failed");
        }

        // Extract claims from SAML principal
        var email = result.Principal.FindFirst(ClaimTypes.Email)?.Value ??
                    result.Principal.FindFirst("email")?.Value;
        var name = result.Principal.FindFirst(ClaimTypes.Name)?.Value ??
                   result.Principal.FindFirst("name")?.Value;
        var subjectId = result.SubjectId ?? result.Principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

        // Get IdP name from stored state or journey data
        var idpName = (storedState?.Properties?.TryGetValue("idp_name", out var storedIdpName) == true ? storedIdpName : null) ??
                      context.GetData<string>("saml_idp") ?? "saml";

        // Get IdP configuration to check for proxy mode
        var idpStore = context.ServiceProvider.GetService<IIdentityProviderStore>();
        var idpConfig = idpStore != null ? await idpStore.GetBySchemeAsync(idpName, cancellationToken) : null;
        var samlConfig = idpConfig?.GetConfiguration<Saml2ProviderConfiguration>();

        // Check if proxy mode is enabled (no local user storage)
        var proxyMode = samlConfig?.ProxyMode ?? context.GetConfig("proxyMode", false);
        var storeUserLocally = samlConfig?.StoreUserLocally ?? !proxyMode;

        string userId;
        OlusoUserInfo? user = null;

        if (storeUserLocally)
        {
            // Normal mode: Create/link local user account
            var loginResult = await userService.ProcessExternalLoginAsync(
                $"saml:{idpName}",
                result.Principal,
                createIfNotExists: samlConfig?.AutoProvisionUsers ?? context.GetConfig("autoProvision", true),
                cancellationToken);

            if (!loginResult.Succeeded)
            {
                logger.LogWarning("Failed to process SAML login: {Error}", loginResult.Error);
                return StepHandlerResult.Failure("user_error", loginResult.Error ?? "Failed to process login");
            }

            userId = loginResult.UserId!;
            user = loginResult.User;

            if (user != null && !user.IsActive)
            {
                return StepHandlerResult.Failure("user_deactivated", "Your account has been deactivated");
            }

            // Record successful login
            await userService.RecordLoginAsync(userId, cancellationToken);
        }
        else
        {
            // Proxy mode: Use external subject ID, don't create local user
            userId = subjectId;
            logger.LogDebug("Proxy mode enabled for SAML IdP {IdP}, using external subject {SubjectId}", idpName, subjectId);
        }

        // Update journey context
        context.UserId = userId;
        context.SetData("authenticated_at", DateTime.UtcNow);
        context.SetData("auth_method", "saml");
        context.SetData("idp", $"saml:{idpName}");

        logger.LogInformation("User {UserId} authenticated via SAML IdP {IdP} (proxy={ProxyMode})", userId, idpName, proxyMode);

        // Clean up stored state
        if (!string.IsNullOrEmpty(relayState))
        {
            await stateStore.RemoveAsync(relayState, cancellationToken);
        }

        // Build output claims
        var outputClaims = new Dictionary<string, object>
        {
            ["sub"] = userId,
            ["idp"] = $"saml:{idpName}",
            ["amr"] = new[] { "external", "saml" }
        };

        if (proxyMode)
        {
            // In proxy mode, pass through all external claims (with optional filtering)
            var includeClaims = samlConfig?.ProxyIncludeClaims ?? new List<string>();
            var excludeClaims = samlConfig?.ProxyExcludeClaims ?? new List<string>();

            foreach (var claim in result.Principal.Claims)
            {
                // Skip if in exclude list
                if (excludeClaims.Contains(claim.Type))
                    continue;

                // If include list is specified, only include those claims
                if (includeClaims.Count > 0 && !includeClaims.Contains(claim.Type))
                    continue;

                // Map common claim types
                var claimName = claim.Type switch
                {
                    ClaimTypes.Email => "email",
                    ClaimTypes.Name => "name",
                    ClaimTypes.GivenName => "given_name",
                    ClaimTypes.Surname => "family_name",
                    ClaimTypes.NameIdentifier => "external_sub",
                    _ => claim.Type
                };

                if (!outputClaims.ContainsKey(claimName))
                {
                    outputClaims[claimName] = claim.Value;
                }
            }

            // Mark as proxy authentication
            outputClaims["proxy_mode"] = true;
            outputClaims["external_sub"] = subjectId;
        }
        else
        {
            // Normal mode: use local user details
            outputClaims["name"] = user?.DisplayName ?? user?.Username ?? name ?? "";
            outputClaims["email"] = user?.Email ?? email ?? "";
            outputClaims["email_verified"] = user?.EmailVerified ?? false;
        }

        if (result.SessionIndex != null)
        {
            outputClaims["saml_session_index"] = result.SessionIndex;
        }

        return StepHandlerResult.Success(outputClaims);
    }

    public Task<StepConfigurationValidationResult> ValidateConfigurationAsync(IDictionary<string, object>? configuration)
    {
        // Validate that at least one SAML IdP is referenced
        return Task.FromResult(StepConfigurationValidationResult.Valid());
    }
}

/// <summary>
/// View model for SAML IdP selection
/// </summary>
public class SamlIdpSelectionViewModel
{
    public string? TenantName { get; set; }
    public List<SamlIdpDisplayInfo> IdentityProviders { get; set; } = new();
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// Display info for a SAML IdP
/// </summary>
public class SamlIdpDisplayInfo
{
    public string Name { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string? IconUrl { get; set; }
}

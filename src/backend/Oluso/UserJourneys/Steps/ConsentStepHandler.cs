using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;

namespace Oluso.UserJourneys.Steps;

/// <summary>
/// Handles OAuth consent step where users approve scopes requested by clients
/// </summary>
public class ConsentStepHandler : IStepHandler
{
    public string StepType => "consent";

    public async Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default)
    {
        var clientStore = context.ServiceProvider.GetRequiredService<IClientStore>();
        var resourceStore = context.ServiceProvider.GetRequiredService<IResourceStore>();
        var consentStore = context.ServiceProvider.GetRequiredService<IConsentStore>();
        var tenantContext = context.ServiceProvider.GetRequiredService<ITenantContext>();
        var logger = context.ServiceProvider.GetRequiredService<ILogger<ConsentStepHandler>>();
        var eventService = context.ServiceProvider.GetService<IOlusoEventService>();
        var httpContextAccessor = context.ServiceProvider.GetService<IHttpContextAccessor>();

        // Step-level consent presentation config. Each defaults to the
        // permissive value so existing journeys that don't set them keep
        // the old behaviour; setting any of them lets the admin tighten
        // or simplify the consent screen per journey.
        var allowRemember = context.GetConfig("allowRemember", true);
        var rememberDays = context.GetConfig("rememberDays", 365);
        var showResourceScopes = context.GetConfig("showResourceScopes", true);
        var showIdentityScopes = context.GetConfig("showIdentityScopes", true);

        // Get user ID from context or from authenticated session
        // This handles consent-only journeys where user is already authenticated
        var userId = context.UserId ?? GetUserIdFromSession(httpContextAccessor);
        if (!string.IsNullOrEmpty(userId) && context.UserId == null)
        {
            // Update context with the user ID from session
            context.UserId = userId;
            logger.LogDebug("Set UserId from authenticated session: {UserId}", userId);
        }

        // Get client ID from context (set when journey started from authorize endpoint)
        var clientId = context.ClientId ?? context.GetData<string>("client_id");
        if (string.IsNullOrEmpty(clientId))
        {
            return StepHandlerResult.Fail("no_client", "No client specified in journey context");
        }

        // Get client
        var client = await clientStore.FindClientByIdAsync(clientId, cancellationToken);
        if (client == null)
        {
            return StepHandlerResult.Fail("client_not_found", "Client not found");
        }

        // Check if consent is required
        if (!client.RequireConsent)
        {
            logger.LogDebug("Consent not required for client {ClientId}", client.ClientId);
            return StepHandlerResult.SkipOrBranch(context, "consent_not_required");
        }

        // Parse requested scopes
        // Consent journeys carry "scope"; a consent step inside a sign-in journey has "scopes".
        var scopeString = context.GetData<string>("scope") ?? context.GetData<string>("scopes") ?? "";
        var requestedScopes = scopeString.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        // Check if user has already consented to all requested scopes
        if (context.UserId != null && requestedScopes.Length > 0)
        {
            var hasConsent = await consentStore.HasConsentAsync(
                context.UserId,
                client.ClientId,
                requestedScopes,
                cancellationToken);

            if (hasConsent)
            {
                logger.LogDebug("User {UserId} already has consent for client {ClientId}, scopes: {Scopes}",
                    context.UserId, client.ClientId, scopeString);
                context.SetData(Oluso.Protocols.ConsentDecisions.ConsentedScopesDataKey, scopeString);
                return StepHandlerResult.SkipOrBranch(context, "consent_already_granted");
            }
        }

        // Get resources for requested scopes
        var identityResources = await resourceStore.FindIdentityResourcesByScopeNameAsync(requestedScopes, cancellationToken);
        // Keyed by the requested value: an instance of a prefix scope (e.g. device:abc of the prefix scope device:) is
        // consented to as requested but shown by the prefix scope's display name, never its client-chosen suffix.
        var defaultSuffixMaxLength = await ApiScopePrefixes.GetDefaultSuffixMaxLengthAsync(
            context.ServiceProvider.GetService<ITenantSettingsProvider>(), cancellationToken);
        var apiScopes = await ApiScopePrefixes.ResolveAsync(resourceStore, requestedScopes, cancellationToken, defaultSuffixMaxLength);

        // Check for consent response
        var consentAction = context.GetInput("consent_action");
        if (!string.IsNullOrEmpty(consentAction))
        {
            if (consentAction == "deny")
            {
                logger.LogInformation("User denied consent for client {ClientId}", client.ClientId);

                // Raise consent denied event
                if (eventService != null && context.UserId != null)
                {
                    await eventService.RaiseAsync(new ConsentDeniedEvent
                    {
                        TenantId = tenantContext.TenantId,
                        SubjectId = context.UserId,
                        ClientId = client.ClientId
                    }, cancellationToken);
                }

                return StepHandlerResult.Fail("access_denied", "User denied consent");
            }

            if (consentAction == "allow")
            {
                // Get consented scopes from form input
                var consentedScopes = new List<string>();

                // Look for scope_* inputs that are checked
                foreach (var kvp in context.UserInput)
                {
                    if (kvp.Key.StartsWith("scope_") && kvp.Value?.ToString() == "true")
                    {
                        consentedScopes.Add(kvp.Key[6..]); // Remove "scope_" prefix
                    }
                }

                // Ensure required scopes are included
                var requiredScopes = new[] { "openid" };
                foreach (var required in requiredScopes)
                {
                    if (requestedScopes.Contains(required) && !consentedScopes.Contains(required))
                    {
                        consentedScopes.Add(required);
                    }
                }

                // Store consent decision. Treat the user's "remember"
                // ticket as honoured only when the step config allows it
                // — admins can disable the remember-consent affordance
                // for sensitive flows by setting allowRemember=false.
                var rememberConsent = allowRemember && context.GetInput("remember_consent") == "true";
                if (rememberConsent && context.UserId != null)
                {
                    // Calculate expiration. Step config wins when set
                    // (rememberDays > 0); otherwise use the client's
                    // ConsentLifetime; otherwise persist indefinitely.
                    DateTime? expiresAt = null;
                    if (rememberDays > 0)
                    {
                        expiresAt = DateTime.UtcNow.AddDays(rememberDays);
                    }
                    else if (client.ConsentLifetime.HasValue && client.ConsentLifetime.Value > 0)
                    {
                        expiresAt = DateTime.UtcNow.AddSeconds(client.ConsentLifetime.Value);
                    }

                    // Store persistent consent in database
                    var consent = new Consent
                    {
                        SubjectId = context.UserId,
                        ClientId = client.ClientId,
                        TenantId = tenantContext.TenantId,
                        CreatedAt = DateTime.UtcNow,
                        ExpiresAt = expiresAt
                    };
                    consent.SetScopes(consentedScopes);

                    await consentStore.StoreConsentAsync(consent, cancellationToken);

                    logger.LogInformation("User granted persistent consent for client {ClientId}, scopes: {Scopes}, expires: {Expires}",
                        client.ClientId, string.Join(", ", consentedScopes), expiresAt?.ToString("o") ?? "never");
                }
                else
                {
                    logger.LogInformation("User granted one-time consent for client {ClientId}, scopes: {Scopes}",
                        client.ClientId, string.Join(", ", consentedScopes));
                }

                context.SetData(Oluso.Protocols.ConsentDecisions.ConsentedScopesDataKey, string.Join(" ", consentedScopes));

                // Raise consent granted event
                if (eventService != null && context.UserId != null)
                {
                    await eventService.RaiseAsync(new ConsentGrantedEvent
                    {
                        TenantId = tenantContext.TenantId,
                        SubjectId = context.UserId,
                        ClientId = client.ClientId,
                        Scopes = consentedScopes,
                        RememberConsent = rememberConsent
                    }, cancellationToken);
                }

                var outputData = new Dictionary<string, object>
                {
                    ["consented_scopes"] = string.Join(" ", consentedScopes)
                };
                return StepHandlerResult.SuccessOrBranch(context, outputData, "consent_granted");
            }
        }

        // Show consent UI. The step config can hide the identity- or
        // API-scope sections entirely (e.g. for an API-only flow that
        // doesn't surface profile scopes) and can suppress the remember
        // checkbox even if the client allows it.
        var viewModel = new ConsentViewModel
        {
            ClientId = client.ClientId,
            ClientName = client.ClientName ?? client.ClientId,
            ClientUri = client.ClientUri,
            ClientLogoUri = client.LogoUri,
            PolicyUri = HttpsProperty(client, "policy_uri"),
            TosUri = HttpsProperty(client, "tos_uri"),
            AllowRememberConsent = allowRemember && client.AllowRememberConsent,
            IdentityScopes = showIdentityScopes
                ? identityResources.Select(r => new ScopeViewModel
                {
                    Name = r.Name,
                    DisplayName = r.DisplayName ?? r.Name,
                    Description = r.Description,
                    Required = r.Required,
                    Emphasize = r.Emphasize,
                    Checked = true
                }).ToList()
                : new List<ScopeViewModel>(),
            ApiScopes = showResourceScopes
                ? requestedScopes.Where(apiScopes.ContainsKey).Distinct(StringComparer.Ordinal).Select(name =>
                {
                    var s = apiScopes[name];
                    return new ScopeViewModel
                    {
                        Name = name,
                        DisplayName = s.DisplayName ?? s.Name,
                        Description = s.Description,
                        Required = s.Required,
                        Emphasize = s.Emphasize,
                        Checked = true
                    };
                }).ToList()
                : new List<ScopeViewModel>()
        };

        return StepHandlerResult.ShowUi("Journey/_Consent", viewModel);
    }

    /// <summary>A registered https link from the client's metadata (policy_uri, tos_uri), else null.</summary>
    private static string? HttpsProperty(Client client, string key)
    {
        var value = client.Properties?.FirstOrDefault(p => p.Key == key)?.Value;
        return Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps ? value : null;
    }

    private static string? GetUserIdFromSession(IHttpContextAccessor? httpContextAccessor)
    {
        var user = httpContextAccessor?.HttpContext?.User;
        if (user?.Identity?.IsAuthenticated != true)
            return null;

        return user.FindFirst("sub")?.Value
            ?? user.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
    }
}

/// <summary>
/// View model for the consent page
/// </summary>
public class ConsentViewModel
{
    public string ClientId { get; set; } = null!;
    public string ClientName { get; set; } = null!;
    public string? ClientUri { get; set; }
    public string? ClientLogoUri { get; set; }
    /// <summary>The client's privacy policy (registered policy_uri, https only).</summary>
    public string? PolicyUri { get; set; }
    /// <summary>The client's terms of service (registered tos_uri, https only).</summary>
    public string? TosUri { get; set; }
    public bool AllowRememberConsent { get; set; }
    public List<ScopeViewModel> IdentityScopes { get; set; } = new();
    public List<ScopeViewModel> ApiScopes { get; set; } = new();
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// View model for individual scopes
/// </summary>
public class ScopeViewModel
{
    public string Name { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string? Description { get; set; }
    public bool Required { get; set; }
    public bool Emphasize { get; set; }
    public bool Checked { get; set; }
}

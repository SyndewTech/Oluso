using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
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
            return StepHandlerResult.Skip();
        }

        // Parse requested scopes
        var scopeString = context.GetData<string>("scope") ?? "";
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
                context.SetData("consented_scopes", scopeString);
                return StepHandlerResult.Skip();
            }
        }

        // Get resources for requested scopes
        var identityResources = await resourceStore.FindIdentityResourcesByScopeNameAsync(requestedScopes, cancellationToken);
        var apiScopes = await resourceStore.FindApiScopesByNameAsync(requestedScopes, cancellationToken);

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

                // Store consent decision
                var rememberConsent = context.GetInput("remember_consent") == "true";
                if (rememberConsent && context.UserId != null)
                {
                    // Calculate expiration based on client settings
                    DateTime? expiresAt = null;
                    if (client.ConsentLifetime.HasValue && client.ConsentLifetime.Value > 0)
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

                context.SetData("consented_scopes", string.Join(" ", consentedScopes));

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

                return StepHandlerResult.Success(new Dictionary<string, object>
                {
                    ["consented_scopes"] = string.Join(" ", consentedScopes)
                });
            }
        }

        // Show consent UI
        var viewModel = new ConsentViewModel
        {
            ClientId = client.ClientId,
            ClientName = client.ClientName ?? client.ClientId,
            ClientUri = client.ClientUri,
            ClientLogoUri = client.LogoUri,
            AllowRememberConsent = client.AllowRememberConsent,
            IdentityScopes = identityResources.Select(r => new ScopeViewModel
            {
                Name = r.Name,
                DisplayName = r.DisplayName ?? r.Name,
                Description = r.Description,
                Required = r.Required,
                Emphasize = r.Emphasize,
                Checked = true
            }).ToList(),
            ApiScopes = apiScopes.Select(s => new ScopeViewModel
            {
                Name = s.Name,
                DisplayName = s.DisplayName ?? s.Name,
                Description = s.Description,
                Required = s.Required,
                Emphasize = s.Emphasize,
                Checked = true
            }).ToList()
        };

        return StepHandlerResult.ShowUi("Journey/_Consent", viewModel);
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

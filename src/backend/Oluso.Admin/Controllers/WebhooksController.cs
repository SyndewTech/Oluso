using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oluso.Admin.Authorization;
using Oluso.Core.Authorization;
using Oluso.Core.Api;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Licensing;
using Oluso.Admin.Dto.Webhooks;

namespace Oluso.Admin.Controllers;

/// <summary>
/// API endpoints for managing webhook endpoints and viewing delivery logs.
/// Tenant admins can create webhook endpoints to receive events.
/// </summary>
[Route("api/admin/webhooks")]
public class WebhooksController : AdminBaseController
{
    private readonly IWebhookStore _store;
    private readonly IWebhookDispatcher _dispatcher;
    private readonly ITenantContext _tenantContext;
    private readonly IOlusoEventService _eventService;
    private readonly IFeatureGate _featureGate;
    private readonly IWebhookEventRegistry _eventRegistry;
    private readonly IClientStore _clients;
    private readonly IAuthorizationService _authorization;
    private readonly ILogger<WebhooksController> _logger;

    public WebhooksController(
        IWebhookStore store,
        IWebhookDispatcher dispatcher,
        ITenantContext tenantContext,
        IOlusoEventService eventService,
        IFeatureGate featureGate,
        IWebhookEventRegistry eventRegistry,
        IClientStore clients,
        IAuthorizationService authorization,
        ILogger<WebhooksController> logger) : base(tenantContext)
    {
        _store = store;
        _dispatcher = dispatcher;
        _tenantContext = tenantContext;
        _eventService = eventService;
        _featureGate = featureGate;
        _eventRegistry = eventRegistry;
        _clients = clients;
        _authorization = authorization;
        _logger = logger;
    }

    #region Available Events

    /// <summary>
    /// Get all available webhook event types that can be subscribed to.
    /// Filters by feature gate — events from modules the tenant doesn't have are excluded.
    /// </summary>
    [HttpGet("events")]
    [RequirePermission(AdminPermissions.WebhooksRead)]
    public async Task<ActionResult<WebhookEventsResponse>> GetAvailableEvents(CancellationToken cancellationToken)
    {
        var filtered = await FilterEventsByFeatureGateAsync(cancellationToken);

        return Ok(new WebhookEventsResponse
        {
            Events = filtered,
            Categories = filtered.Select(e => e.Category).Distinct().OrderBy(c => c).ToList()
        });
    }

    /// <summary>
    /// Get events grouped by webhook provider for
    /// display in UI. Grouping by provider keeps each module's events together,
    /// rather than mixing every module's admin actions into a single Admin
    /// bucket the way Category-based grouping does. Filters by feature gate.
    /// </summary>
    [HttpGet("events/grouped")]
    [RequirePermission(AdminPermissions.WebhooksRead)]
    public async Task<ActionResult<Dictionary<string, List<WebhookEventInfo>>>> GetEventsGrouped(CancellationToken cancellationToken)
    {
        var filtered = await FilterEventsByFeatureGateAsync(cancellationToken);

        var grouped = filtered
            .GroupBy(e => string.IsNullOrEmpty(e.ProviderDisplayName) ? "Other" : e.ProviderDisplayName)
            .OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(e => e.DisplayName, StringComparer.OrdinalIgnoreCase).ToList());

        return Ok(grouped);
    }

    private async Task<List<WebhookEventInfo>> FilterEventsByFeatureGateAsync(CancellationToken cancellationToken)
    {
        var events = _dispatcher.GetAvailableEventTypes();
        var providers = _eventRegistry.GetProviders()
            .ToDictionary(p => p.ProviderId, p => p.DisplayName, StringComparer.OrdinalIgnoreCase);

        var filtered = new List<WebhookEventInfo>();
        var isSuperAdmin = IsSuperAdmin;

        foreach (var e in events)
        {
            // Skip events with no event type or display name — they render
            // as blank rows in the picker. Defensive against event classes that
            // override WebhookEventType but lack a [WebhookEvent(...)] attribute.
            if (string.IsNullOrWhiteSpace(e.EventType)
                || string.IsNullOrWhiteSpace(e.DisplayName))
            {
                continue;
            }

            // Hide Platform-only events from tenant admins. They can't
            // physically receive these (the dispatcher only routes Platform
            // events to platform endpoints) — exposing them in the picker
            // would just be a UX lie.
            if (e.Scope == EventScope.Platform && !isSuperAdmin)
            {
                continue;
            }

            var feature = ModuleFeatures.GetFeatureKeyForProvider(e.ProviderId);
            if (feature != null)
            {
                var gate = await _featureGate.CheckFeatureAsync(feature, cancellationToken: cancellationToken);
                if (!gate.IsAllowed) continue;
            }

            string? providerDisplayName = null;
            if (!string.IsNullOrEmpty(e.ProviderId)
                && providers.TryGetValue(e.ProviderId, out var pdn))
            {
                providerDisplayName = pdn;
            }

            var definition = _eventRegistry.GetEventDefinition(e.EventType);
            filtered.Add(new WebhookEventInfo
            {
                EventType = e.EventType,
                Category = e.Category,
                DisplayName = e.DisplayName,
                Description = e.Description,
                EnabledByDefault = e.EnabledByDefault,
                ProviderId = e.ProviderId,
                ProviderDisplayName = providerDisplayName,
                Scope = e.Scope.ToString(),
                Restricted = definition.IsRestricted(),
                RequiredPermission = definition?.RequiredPermission,
                ClassificationPurpose = definition?.ClassificationPurpose,
                SensitiveProperties = definition.SensitivePropertyNames().OrderBy(n => n, StringComparer.Ordinal).ToList(),
                CanSubscribe = await MayHandleAsync(definition)
            });
        }

        return filtered;
    }

    /// <summary>
    /// Reject Platform-scoped event types for non-SuperAdmin callers.
    /// Returns null if all event types are allowed; otherwise returns the
    /// list of disallowed event types so the controller can return 403.
    /// </summary>
    private List<string>? FindDisallowedPlatformEvents(IEnumerable<string> requestedEventTypes)
    {
        if (IsSuperAdmin) return null;
        var disallowed = requestedEventTypes
            .Where(et => _dispatcher.GetAvailableEventTypes()
                .Any(d => string.Equals(d.EventType, et, StringComparison.Ordinal) && d.Scope == EventScope.Platform))
            .ToList();
        return disallowed.Count == 0 ? null : disallowed;
    }

    /// <summary>
    /// Whether the current administrator holds what a restricted event demands. A restricted event that
    /// names no permission can't be handled by anyone.
    /// </summary>
    private async Task<bool> MayHandleAsync(WebhookEventDefinition? definition)
    {
        if (!definition.IsRestricted()) return true;
        if (string.IsNullOrEmpty(definition!.RequiredPermission)) return false;
        var result = await _authorization.AuthorizeAsync(User, null, PermissionPolicyConstants.PolicyPrefix + definition.RequiredPermission);
        return result.Succeeded;
    }

    /// <summary>The restricted event types among <paramref name="eventTypes"/> the current administrator may not handle.</summary>
    private async Task<List<string>> FindForbiddenRestrictedEventsAsync(IEnumerable<string> eventTypes)
    {
        var forbidden = new List<string>();
        foreach (var eventType in eventTypes.Distinct(StringComparer.Ordinal))
        {
            if (!await MayHandleAsync(_eventRegistry.GetEventDefinition(eventType)))
                forbidden.Add(eventType);
        }
        return forbidden;
    }

    private List<string> RestrictedEventTypes(IEnumerable<string> eventTypes) =>
        eventTypes.Where(et => _eventRegistry.GetEventDefinition(et).IsRestricted()).Distinct(StringComparer.Ordinal).ToList();

    private static ObjectResult RestrictedRefusal(string action, IEnumerable<string> eventTypes) => new(new
    {
        error = $"{action} needs the permission each restricted event declares, which you don't hold for: {string.Join(", ", eventTypes)}"
    })
    { StatusCode = 403 };

    /// <summary>
    /// A linked client must be one of this tenant's clients.
    /// </summary>
    private async Task<string?> ValidateClientAsync(string? clientId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(clientId)) return null;

        var client = await _clients.FindClientByIdAsync(clientId.Trim(), cancellationToken);
        return client == null || client.TenantId != GetTenantId()
            ? $"Unknown client for this tenant: {clientId}"
            : null;
    }

    #endregion

    #region Webhook Endpoints

    /// <summary>
    /// Get all webhook endpoints for the current tenant
    /// </summary>
    [HttpGet("endpoints")]
    [RequirePermission(AdminPermissions.WebhooksRead)]
    public async Task<ActionResult<IEnumerable<WebhookEndpointDto>>> GetEndpoints(
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();
        var endpoints = await _store.GetEndpointsAsync(tenantId, cancellationToken);
        return Ok(endpoints);
    }

    /// <summary>
    /// Get a specific webhook endpoint
    /// </summary>
    [HttpGet("endpoints/{endpointId}")]
    [RequirePermission(AdminPermissions.WebhooksRead)]
    public async Task<ActionResult<WebhookEndpointDto>> GetEndpoint(
        string endpointId,
        CancellationToken cancellationToken)
    {
        var endpoint = await _store.GetEndpointAsync(endpointId, cancellationToken);
        if (endpoint == null)
            return NotFound();

        // Verify tenant ownership
        if (endpoint.TenantId != GetTenantId())
            return Forbid();

        return Ok(endpoint);
    }

    /// <summary>
    /// Create a new webhook endpoint
    /// </summary>
    [HttpPost("endpoints")]
    [RequirePermission(AdminPermissions.WebhooksWrite)]
    public async Task<ActionResult<WebhookEndpointDto>> CreateEndpoint(
        [FromBody] CreateWebhookEndpointRequest request,
        CancellationToken cancellationToken)
    {
        var tenantId = GetTenantId();

        // Validate URL
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri))
        {
            return BadRequest(new { error = "Invalid URL format" });
        }

        // Validate event types
        var availableTypes = _dispatcher.GetAvailableEventTypes().Select(e => e.EventType).ToHashSet();
        var invalidEvents = request.EventTypes
            .Where(e => !availableTypes.Contains(e))
            .ToList();

        if (invalidEvents.Any())
        {
            return BadRequest(new { error = $"Invalid event types: {string.Join(", ", invalidEvents)}" });
        }

        var disallowed = FindDisallowedPlatformEvents(request.EventTypes);
        if (disallowed != null)
        {
            return StatusCode(403, new { error = $"Platform-only event types cannot be subscribed by tenant admins: {string.Join(", ", disallowed)}" });
        }

        var forbidden = await FindForbiddenRestrictedEventsAsync(request.EventTypes);
        if (forbidden.Count > 0)
        {
            return RestrictedRefusal("Subscribing to restricted events", forbidden);
        }

        var clientError = await ValidateClientAsync(request.ClientId, cancellationToken);
        if (clientError != null)
        {
            return BadRequest(new { error = clientError });
        }

        var endpoint = await _store.CreateEndpointAsync(tenantId, new Oluso.Core.Domain.Interfaces.CreateWebhookEndpoint
        {
            Name = request.Name,
            Description = request.Description,
            Url = request.Url,
            Enabled = request.Enabled,
            EventTypes = request.EventTypes,
            Headers = request.Headers,
            AuthType = request.AuthType,
            AuthCredential = request.AuthCredential,
            ApiVersion = request.ApiVersion ?? "2024-01-01",
            ClientId = request.ClientId,
            SkipOwnClientEvents = request.SkipOwnClientEvents,
            AuthorizeSensitiveData = request.AuthorizeSensitiveData,
            SensitiveDataAuthorizedBy = request.AuthorizeSensitiveData ? AdminUserId : null,
            SensitiveDataAuthorizedByName = request.AuthorizeSensitiveData ? AdminUserName : null
        }, cancellationToken);

        _logger.LogInformation(
            "Created webhook endpoint {EndpointId} for tenant {TenantId}",
            endpoint.Id, tenantId);

        if (request.AuthorizeSensitiveData)
        {
            await RaiseSensitiveDataChangedAsync(endpoint, authorized: true, cancellationToken);
        }

        // Raise audit event
        await _eventService.RaiseAsync(new AdminWebhookCreatedEvent
        {
            TenantId = tenantId,
            AdminUserId = AdminUserId!,
            AdminUserName = AdminUserName,
            IpAddress = ClientIp,
            ResourceId = endpoint.Id,
            ResourceName = endpoint.Name,
            EndpointUrl = endpoint.Url
        }, cancellationToken);

        // Return with the secret visible (only time it's shown)
        return CreatedAtAction(nameof(GetEndpoint), new { endpointId = endpoint.Id }, endpoint);
    }

    /// <summary>
    /// Update a webhook endpoint
    /// </summary>
    [HttpPut("endpoints/{endpointId}")]
    [RequirePermission(AdminPermissions.WebhooksWrite)]
    public async Task<ActionResult<WebhookEndpointDto>> UpdateEndpoint(
        string endpointId,
        [FromBody] UpdateWebhookEndpointRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await _store.GetEndpointAsync(endpointId, cancellationToken);
        if (existing == null)
            return NotFound();

        if (existing.TenantId != GetTenantId())
            return Forbid();

        // Validate event types if provided
        if (request.EventTypes != null)
        {
            var availableTypes = _dispatcher.GetAvailableEventTypes().Select(e => e.EventType).ToHashSet();
            var invalidEvents = request.EventTypes
                .Where(e => !availableTypes.Contains(e))
                .ToList();

            if (invalidEvents.Any())
            {
                return BadRequest(new { error = $"Invalid event types: {string.Join(", ", invalidEvents)}" });
            }

            var disallowed = FindDisallowedPlatformEvents(request.EventTypes);
            if (disallowed != null)
            {
                return StatusCode(403, new { error = $"Platform-only event types cannot be subscribed by tenant admins: {string.Join(", ", disallowed)}" });
            }
        }

        var finalEventTypes = request.EventTypes ?? existing.EventTypes;
        if (request.EventTypes != null)
        {
            var added = request.EventTypes.Except(existing.EventTypes, StringComparer.Ordinal);
            var forbidden = await FindForbiddenRestrictedEventsAsync(added);
            if (forbidden.Count > 0)
            {
                return RestrictedRefusal("Subscribing to restricted events", forbidden);
            }
        }

        var wasAuthorized = existing.SensitiveDataAuthorizedAt != null;
        bool? authorizationChange = request.AuthorizeSensitiveData switch
        {
            true when !wasAuthorized => true,
            false when wasAuthorized => false,
            _ => null
        };
        var staysAuthorized = authorizationChange ?? wasAuthorized;
        var urlChanges = request.Url != null && !string.Equals(request.Url, existing.Url, StringComparison.Ordinal);

        if (authorizationChange == true || (staysAuthorized && urlChanges))
        {
            var forbidden = await FindForbiddenRestrictedEventsAsync(finalEventTypes);
            if (forbidden.Count > 0)
            {
                return RestrictedRefusal(
                    authorizationChange == true
                        ? "Authorising an endpoint for sensitive data"
                        : "Changing the URL of an endpoint authorised for sensitive data",
                    forbidden);
            }
        }

        if (!string.IsNullOrWhiteSpace(request.ClientId))
        {
            var clientError = await ValidateClientAsync(request.ClientId, cancellationToken);
            if (clientError != null)
            {
                return BadRequest(new { error = clientError });
            }
        }

        var endpoint = await _store.UpdateEndpointAsync(endpointId, new Oluso.Core.Domain.Interfaces.UpdateWebhookEndpoint
        {
            Name = request.Name,
            Description = request.Description,
            Url = request.Url,
            Enabled = request.Enabled,
            EventTypes = request.EventTypes,
            Headers = request.Headers,
            AuthType = request.AuthType,
            AuthCredential = request.AuthCredential,
            ApiVersion = request.ApiVersion,
            ClientId = request.ClientId,
            SkipOwnClientEvents = request.SkipOwnClientEvents,
            AuthorizeSensitiveData = authorizationChange,
            SensitiveDataAuthorizedBy = authorizationChange == true ? AdminUserId : null,
            SensitiveDataAuthorizedByName = authorizationChange == true ? AdminUserName : null
        }, cancellationToken);

        if (authorizationChange != null)
        {
            await RaiseSensitiveDataChangedAsync(endpoint, authorizationChange.Value, cancellationToken);
        }

        // Raise audit event
        await _eventService.RaiseAsync(new AdminWebhookUpdatedEvent
        {
            TenantId = existing.TenantId,
            AdminUserId = AdminUserId!,
            AdminUserName = AdminUserName,
            IpAddress = ClientIp,
            ResourceId = endpoint.Id,
            ResourceName = endpoint.Name
        }, cancellationToken);

        return Ok(endpoint);
    }

    /// <summary>
    /// Delete a webhook endpoint
    /// </summary>
    [HttpDelete("endpoints/{endpointId}")]
    [RequirePermission(AdminPermissions.WebhooksDelete)]
    public async Task<IActionResult> DeleteEndpoint(
        string endpointId,
        CancellationToken cancellationToken)
    {
        var existing = await _store.GetEndpointAsync(endpointId, cancellationToken);
        if (existing == null)
            return NotFound();

        if (existing.TenantId != GetTenantId())
            return Forbid();

        await _store.DeleteEndpointAsync(endpointId, cancellationToken);

        _logger.LogInformation("Deleted webhook endpoint {EndpointId}", endpointId);

        // Raise audit event
        await _eventService.RaiseAsync(new AdminWebhookDeletedEvent
        {
            TenantId = existing.TenantId,
            AdminUserId = AdminUserId!,
            AdminUserName = AdminUserName,
            IpAddress = ClientIp,
            ResourceId = endpointId,
            ResourceName = existing.Name
        }, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Rotate the secret for a webhook endpoint
    /// </summary>
    [HttpPost("endpoints/{endpointId}/rotate-secret")]
    [RequirePermission(AdminPermissions.WebhooksWrite)]
    public async Task<ActionResult<RotateSecretResponse>> RotateSecret(
        string endpointId,
        CancellationToken cancellationToken)
    {
        var existing = await _store.GetEndpointAsync(endpointId, cancellationToken);
        if (existing == null)
            return NotFound();

        if (existing.TenantId != GetTenantId())
            return Forbid();

        var newSecret = await _store.RotateSecretAsync(endpointId, cancellationToken);

        _logger.LogInformation("Rotated secret for webhook endpoint {EndpointId}", endpointId);

        return Ok(new RotateSecretResponse { Secret = newSecret });
    }

    /// <summary>
    /// Test a webhook endpoint by sending a test event
    /// </summary>
    [HttpPost("endpoints/{endpointId}/test")]
    [RequirePermission(AdminPermissions.WebhooksWrite)]
    public async Task<ActionResult<TestWebhookResponse>> TestEndpoint(
        string endpointId,
        CancellationToken cancellationToken)
    {
        var existing = await _store.GetEndpointAsync(endpointId, cancellationToken);
        if (existing == null)
            return NotFound();

        if (existing.TenantId != GetTenantId())
            return Forbid();

        var tenantId = GetTenantId();

        // Send a test event
        var result = await _dispatcher.DispatchAsync(
            tenantId,
            "test.webhook",
            new
            {
                message = "This is a test webhook event",
                endpoint_id = endpointId,
                timestamp = DateTime.UtcNow
            },
            cancellationToken: cancellationToken);

        return Ok(new TestWebhookResponse
        {
            Success = result.Success,
            DeliveryId = result.DeliveryIds.FirstOrDefault(),
            Error = result.Error
        });
    }

    #endregion

    #region Delivery Logs

    /// <summary>
    /// Get delivery history for a webhook endpoint
    /// </summary>
    [HttpGet("endpoints/{endpointId}/deliveries")]
    [RequirePermission(AdminPermissions.WebhooksRead)]
    public async Task<ActionResult<IEnumerable<WebhookDeliveryDto>>> GetDeliveries(
        string endpointId,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var existing = await _store.GetEndpointAsync(endpointId, cancellationToken);
        if (existing == null)
            return NotFound();

        if (existing.TenantId != GetTenantId())
            return Forbid();

        var deliveries = await _store.GetDeliveryHistoryAsync(endpointId, limit, cancellationToken);
        return Ok(deliveries);
    }

    /// <summary>
    /// Get a specific delivery
    /// </summary>
    [HttpGet("deliveries/{deliveryId}")]
    [RequirePermission(AdminPermissions.WebhooksRead)]
    public async Task<ActionResult<WebhookDeliveryDto>> GetDelivery(
        string deliveryId,
        CancellationToken cancellationToken)
    {
        var delivery = await _store.GetDeliveryAsync(deliveryId, cancellationToken);
        if (delivery == null)
            return NotFound();

        // Verify tenant ownership through endpoint
        var endpoint = await _store.GetEndpointAsync(delivery.EndpointId, cancellationToken);
        if (endpoint?.TenantId != GetTenantId())
            return Forbid();

        return Ok(delivery);
    }

    /// <summary>
    /// Retry a failed delivery
    /// </summary>
    [HttpPost("deliveries/{deliveryId}/retry")]
    [RequirePermission(AdminPermissions.WebhooksWrite)]
    public async Task<ActionResult<RetryDeliveryResponse>> RetryDelivery(
        string deliveryId,
        CancellationToken cancellationToken)
    {
        var delivery = await _store.GetDeliveryAsync(deliveryId, cancellationToken);
        if (delivery == null)
            return NotFound();

        var endpoint = await _store.GetEndpointAsync(delivery.EndpointId, cancellationToken);
        if (endpoint?.TenantId != GetTenantId())
            return Forbid();

        if (delivery.Status == Oluso.Core.Domain.Interfaces.WebhookDeliveryStatus.Success)
        {
            return BadRequest(new { error = "Delivery was already successful" });
        }

        var success = await _dispatcher.RetryDeliveryAsync(deliveryId, cancellationToken);

        return Ok(new RetryDeliveryResponse { Success = success });
    }

    #endregion

    private Task RaiseSensitiveDataChangedAsync(
        Oluso.Core.Domain.Interfaces.WebhookEndpoint endpoint,
        bool authorized,
        CancellationToken cancellationToken) =>
        _eventService.RaiseAsync(new AdminWebhookSensitiveDataChangedEvent
        {
            TenantId = endpoint.TenantId,
            AdminUserId = AdminUserId!,
            AdminUserName = AdminUserName,
            IpAddress = ClientIp,
            ResourceId = endpoint.Id,
            ResourceName = endpoint.Name,
            EndpointUrl = endpoint.Url,
            Authorized = authorized,
            RestrictedEventTypes = RestrictedEventTypes(endpoint.EventTypes)
        }, cancellationToken);

    private string GetTenantId()
    {
        return _tenantContext.TenantId
            ?? User.FindFirstValue("tenant_id")
            ?? throw new InvalidOperationException("Tenant ID not available");
    }
}

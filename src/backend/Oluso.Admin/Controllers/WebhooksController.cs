using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oluso.Core.Api;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;

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
    private readonly ILogger<WebhooksController> _logger;

    public WebhooksController(
        IWebhookStore store,
        IWebhookDispatcher dispatcher,
        ITenantContext tenantContext,
        IOlusoEventService eventService,
        ILogger<WebhooksController> logger) : base(tenantContext)
    {
        _store = store;
        _dispatcher = dispatcher;
        _tenantContext = tenantContext;
        _eventService = eventService;
        _logger = logger;
    }

    #region Available Events

    /// <summary>
    /// Get all available webhook event types that can be subscribed to
    /// </summary>
    [HttpGet("events")]
    public ActionResult<WebhookEventsResponse> GetAvailableEvents()
    {
        var events = _dispatcher.GetAvailableEventTypes();

        return Ok(new WebhookEventsResponse
        {
            Events = events.Select(e => new WebhookEventInfo
            {
                EventType = e.EventType,
                Category = e.Category,
                DisplayName = e.DisplayName,
                Description = e.Description,
                EnabledByDefault = e.EnabledByDefault
            }).ToList(),
            Categories = events.Select(e => e.Category).Distinct().OrderBy(c => c).ToList()
        });
    }

    /// <summary>
    /// Get events grouped by category for display in UI
    /// </summary>
    [HttpGet("events/grouped")]
    public ActionResult<Dictionary<string, List<WebhookEventInfo>>> GetEventsGrouped()
    {
        var events = _dispatcher.GetAvailableEventTypes();

        var grouped = events
            .GroupBy(e => e.Category)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => new WebhookEventInfo
                {
                    EventType = e.EventType,
                    Category = e.Category,
                    DisplayName = e.DisplayName,
                    Description = e.Description,
                    EnabledByDefault = e.EnabledByDefault
                }).ToList());

        return Ok(grouped);
    }

    #endregion

    #region Webhook Endpoints

    /// <summary>
    /// Get all webhook endpoints for the current tenant
    /// </summary>
    [HttpGet("endpoints")]
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

        var endpoint = await _store.CreateEndpointAsync(tenantId, new Oluso.Core.Domain.Interfaces.CreateWebhookEndpoint
        {
            Name = request.Name,
            Description = request.Description,
            Url = request.Url,
            Enabled = request.Enabled,
            EventTypes = request.EventTypes,
            Headers = request.Headers,
            ApiVersion = request.ApiVersion ?? "2024-01-01"
        }, cancellationToken);

        _logger.LogInformation(
            "Created webhook endpoint {EndpointId} for tenant {TenantId}",
            endpoint.Id, tenantId);

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
        }

        var endpoint = await _store.UpdateEndpointAsync(endpointId, new Oluso.Core.Domain.Interfaces.UpdateWebhookEndpoint
        {
            Name = request.Name,
            Description = request.Description,
            Url = request.Url,
            Enabled = request.Enabled,
            EventTypes = request.EventTypes,
            Headers = request.Headers,
            ApiVersion = request.ApiVersion
        }, cancellationToken);

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

    private string GetTenantId()
    {
        return _tenantContext.TenantId
            ?? User.FindFirstValue("tenant_id")
            ?? throw new InvalidOperationException("Tenant ID not available");
    }
}

#region DTOs

public class WebhookEventsResponse
{
    public List<WebhookEventInfo> Events { get; set; } = new();
    public List<string> Categories { get; set; } = new();
}

public class WebhookEventInfo
{
    public string EventType { get; set; } = default!;
    public string Category { get; set; } = default!;
    public string DisplayName { get; set; } = default!;
    public string? Description { get; set; }
    public bool EnabledByDefault { get; set; }
}

public class WebhookEndpointDto
{
    public string Id { get; set; } = default!;
    public string TenantId { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public string Url { get; set; } = default!;
    public bool Enabled { get; set; }
    public List<string> EventTypes { get; set; } = new();
    public Dictionary<string, string>? Headers { get; set; }
    public string? ApiVersion { get; set; }
    public string? Secret { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class CreateWebhookEndpointDto
{
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public string Url { get; set; } = default!;
    public bool Enabled { get; set; } = true;
    public List<string> EventTypes { get; set; } = new();
    public Dictionary<string, string>? Headers { get; set; }
    public string? ApiVersion { get; set; }
}

public class UpdateWebhookEndpointDto
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Url { get; set; }
    public bool? Enabled { get; set; }
    public List<string>? EventTypes { get; set; }
    public Dictionary<string, string>? Headers { get; set; }
    public string? ApiVersion { get; set; }
}

public class CreateWebhookEndpointRequest
{
    public string Name { get; set; } = default!;
    public string? Description { get; set; }
    public string Url { get; set; } = default!;
    public bool Enabled { get; set; } = true;
    public List<string> EventTypes { get; set; } = new();
    public Dictionary<string, string>? Headers { get; set; }
    public string? ApiVersion { get; set; }
}

public class UpdateWebhookEndpointRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public string? Url { get; set; }
    public bool? Enabled { get; set; }
    public List<string>? EventTypes { get; set; }
    public Dictionary<string, string>? Headers { get; set; }
    public string? ApiVersion { get; set; }
}

public class RotateSecretResponse
{
    public string Secret { get; set; } = default!;
}

public class TestWebhookResponse
{
    public bool Success { get; set; }
    public string? DeliveryId { get; set; }
    public string? Error { get; set; }
}

public class RetryDeliveryResponse
{
    public bool Success { get; set; }
}

public class WebhookDeliveryDto
{
    public string Id { get; set; } = default!;
    public string EndpointId { get; set; } = default!;
    public string EventType { get; set; } = default!;
    public Oluso.Core.Domain.Interfaces.WebhookDeliveryStatus Status { get; set; }
    public int HttpStatusCode { get; set; }
    public string? Error { get; set; }
    public int AttemptCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DeliveredAt { get; set; }
}

#endregion

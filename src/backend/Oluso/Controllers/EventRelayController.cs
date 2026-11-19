using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Jobs;

namespace Oluso.Controllers;

/// <summary>
/// Receives forwarded events from standalone apps (e.g., OlusoMarketplace.Web)
/// and re-raises them through the platform's event service so other modules
/// (webhooks, audit, and other modules) can act on them. An event that belongs
/// to a tenant is raised inside a scope set to that tenant, since the relay
/// request itself has none, so every sink acts for the event's tenant.
/// </summary>
[ApiController]
[Route("api/events/relay")]
public class EventRelayController : ControllerBase
{
    public const string RelaySource = "event-relay";

    private readonly IOlusoEventService _eventService;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ITenantStore _tenantStore;
    private readonly EventTypeRegistry _eventTypeRegistry;
    private readonly IConfiguration _configuration;
    private readonly ILogger<EventRelayController> _logger;

    public EventRelayController(
        IOlusoEventService eventService,
        IServiceScopeFactory scopeFactory,
        ITenantStore tenantStore,
        EventTypeRegistry eventTypeRegistry,
        IConfiguration configuration,
        ILogger<EventRelayController> logger)
    {
        _eventService = eventService;
        _scopeFactory = scopeFactory;
        _tenantStore = tenantStore;
        _eventTypeRegistry = eventTypeRegistry;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> Relay(
        [FromBody] EventEnvelope envelope,
        CancellationToken cancellationToken)
    {
        if (RelayKey.Check(this, _configuration, _logger) is { } refused)
            return refused;

        if (string.IsNullOrEmpty(envelope.EventType))
            return BadRequest("EventType is required.");

        // Resolve the concrete event type
        var eventType = _eventTypeRegistry.Resolve(envelope.EventType);
        if (eventType == null)
        {
            _logger.LogWarning("Unknown event type in relay: {EventType}", envelope.EventType);
            return BadRequest($"Unknown event type: {envelope.EventType}");
        }

        // Deserialize to the concrete event type
        OlusoEvent? evt;
        try
        {
            evt = JsonSerializer.Deserialize(envelope.Payload.GetRawText(), eventType) as OlusoEvent;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Failed to deserialize relayed event {EventType}", envelope.EventType);
            return BadRequest("Invalid event payload.");
        }

        if (evt == null)
            return BadRequest("Failed to deserialize event.");

        // The sender's origin travels in the payload. Without one the event came
        // from a platform component, not from this anonymous relay request.
        evt.Origin ??= EventOrigin.System(RelaySource);

        if (string.IsNullOrEmpty(evt.TenantId))
        {
            await _eventService.RaiseAsync(evt, cancellationToken);
        }
        else
        {
            if (await _tenantStore.GetByIdAsync(evt.TenantId, cancellationToken) == null)
            {
                _logger.LogWarning("Relayed event {EventType} names unknown tenant {TenantId}", envelope.EventType, evt.TenantId);
                return BadRequest($"Unknown tenant: {evt.TenantId}");
            }

            await TenantScope.RunAsync(_scopeFactory, evt.TenantId,
                (services, ct) => services.GetRequiredService<IOlusoEventService>().RaiseAsync(evt, ct),
                cancellationToken);
        }

        _logger.LogDebug("Relayed event {EventType} from standalone app", envelope.EventType);
        return Ok();
    }
}

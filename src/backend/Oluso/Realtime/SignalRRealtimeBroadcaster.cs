using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Oluso.Core.Realtime;

namespace Oluso.Realtime;

/// <summary>
/// SignalR-backed default for <see cref="IRealtimeBroadcaster"/>. Pushes
/// to the appropriate group on <see cref="RealtimeHub"/> using the wire
/// shape that the client expects (one event method name, one envelope).
/// </summary>
public sealed class SignalRRealtimeBroadcaster : IRealtimeBroadcaster
{
    private readonly IHubContext<RealtimeHub> _hub;
    private readonly IRealtimeChannelRegistry _channels;
    private readonly ILogger<SignalRRealtimeBroadcaster> _logger;

    public SignalRRealtimeBroadcaster(
        IHubContext<RealtimeHub> hub,
        IRealtimeChannelRegistry channels,
        ILogger<SignalRRealtimeBroadcaster> logger)
    {
        _hub = hub;
        _channels = channels;
        _logger = logger;
    }

    public Task BroadcastToChannelAsync(
        string tenantId, string channel, string eventName, object? payload = null,
        CancellationToken cancellationToken = default)
    {
        // A group channel is delivered ONLY to the connections the hub authorized
        // into its channel group (via Subscribe) — never tenant-wide. An unknown
        // channel is dropped (fail-closed) rather than leaked to the whole tenant.
        var descriptor = _channels.Resolve(channel);
        if (descriptor is not { Scope: RealtimeChannelScope.Group })
        {
            _logger.LogWarning(
                "Realtime broadcast skipped: channel={Channel} is not a registered group channel (tenant={TenantId}).",
                channel, tenantId);
            return Task.CompletedTask;
        }

        var envelope = new RealtimeEvent(channel, eventName, payload, DateTime.UtcNow);
        // Information level so realtime activity is visible in the default log
        // filter without bumping Oluso.* to Debug. Realtime is a diagnosability
        // hotspot — silent broadcasts make every "the page didn't refresh" bug a
        // ten-minute spelunk.
        _logger.LogInformation(
            "Realtime broadcast (channel): channel={Channel} event={Event} tenant={TenantId}",
            channel, eventName, tenantId);
        return _hub.Clients
            .Group(RealtimeHub.ChannelGroup(tenantId, channel))
            .SendAsync(RealtimeHub.EventMethodName, envelope, cancellationToken);
    }

    public Task BroadcastToUserAsync(
        string tenantId, string userId, string channel, string eventName, object? payload = null,
        CancellationToken cancellationToken = default)
    {
        var envelope = new RealtimeEvent(channel, eventName, payload, DateTime.UtcNow);
        _logger.LogInformation(
            "Realtime broadcast (user): channel={Channel} event={Event} tenant={TenantId} user={UserId}",
            channel, eventName, tenantId, userId);
        return _hub.Clients
            .Group(RealtimeHub.UserGroup(tenantId, userId))
            .SendAsync(RealtimeHub.EventMethodName, envelope, cancellationToken);
    }

    public Task BroadcastToEntityAsync(
        string tenantId, string entityType, string entityId, string channel, string eventName,
        object? payload = null, CancellationToken cancellationToken = default)
    {
        // Only connections the hub authorized via Watch are in the entity group.
        var envelope = new RealtimeEvent(channel, eventName, payload, DateTime.UtcNow);
        _logger.LogInformation(
            "Realtime broadcast (entity): channel={Channel} event={Event} tenant={TenantId} entity={EntityType}/{EntityId}",
            channel, eventName, tenantId, entityType, entityId);
        return _hub.Clients
            .Group(RealtimeHub.EntityGroup(tenantId, entityType, entityId))
            .SendAsync(RealtimeHub.EventMethodName, envelope, cancellationToken);
    }
}

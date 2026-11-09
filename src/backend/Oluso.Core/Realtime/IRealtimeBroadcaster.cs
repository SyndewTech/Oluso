namespace Oluso.Core.Realtime;

/// <summary>
/// Pushes events from the server to connected workspace / admin clients
/// in real time. Modules call into this primitive when state changes
/// that any user-facing surface should react to without polling — a new
/// approval task assigned, an invoice approved, a candidate stage moved,
/// etc.
///
/// <para>Channels are dot-namespaced strings (<c>orders.live</c>,
/// <c>orders.42</c>, <c>&lt;module&gt;.&lt;resource&gt;</c>, …) declared in
/// <see cref="IRealtimeChannelRegistry"/>. There are three delivery scopes:</para>
///
/// <list type="bullet">
///   <item><b>Channel (group)</b> — delivered only to connections that have
///     <c>Subscribe</c>d to the channel (the hub authorizes the subscribe
///     against the channel's permission). Use for shared boards; supports
///     per-resource channels like a single resource.</item>
///   <item><b>User</b> — only that user's connections receive it. Use for
///     personal queue updates ("you got a new task").</item>
///   <item><b>Entity</b> — every connection <c>Watch</c>ing one record (the
///     hub authorizes the watch with the type's <see cref="IRealtimeEntityAccess"/>).
///     Use for a page showing that record ("a comment landed on this request").</item>
/// </list>
///
/// <para>Default implementation is SignalR-backed and ships with the
/// platform; tests can substitute a no-op or in-memory recorder.</para>
/// </summary>
public interface IRealtimeBroadcaster
{
    /// <summary>
    /// Push to every connection currently subscribed to a group channel within
    /// the tenant. Channels not registered as <see cref="RealtimeChannelScope.Group"/>
    /// are dropped (fail-closed) — nothing is leaked tenant-wide.
    /// </summary>
    Task BroadcastToChannelAsync(
        string tenantId,
        string channel,
        string eventName,
        object? payload = null,
        CancellationToken cancellationToken = default);

    Task BroadcastToUserAsync(
        string tenantId,
        string userId,
        string channel,
        string eventName,
        object? payload = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Push to every connection watching one entity (the hub's <c>Watch</c>, authorized by
    /// the entity type's <see cref="IRealtimeEntityAccess"/>), whoever they are: the author of
    /// a change included, so a page open elsewhere refreshes when the write came from another
    /// client. Delivered once per connection; <paramref name="channel"/> is the envelope's
    /// channel the client filters on.
    /// </summary>
    Task BroadcastToEntityAsync(
        string tenantId,
        string entityType,
        string entityId,
        string channel,
        string eventName,
        object? payload = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Wire shape pushed to subscribers. The client maps these to typed
/// observables by channel.
/// </summary>
public sealed record RealtimeEvent(
    string Channel,
    string Name,
    object? Payload,
    DateTime At);

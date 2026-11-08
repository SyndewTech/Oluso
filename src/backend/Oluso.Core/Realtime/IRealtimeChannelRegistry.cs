using System.Collections.Concurrent;

namespace Oluso.Core.Realtime;

/// <summary>How a realtime channel is delivered and gated.</summary>
public enum RealtimeChannelScope
{
    /// <summary>
    /// Delivered to a specific user via <see cref="IRealtimeBroadcaster.BroadcastToUserAsync"/>
    /// (the user only ever receives their own events). Subscribing is a no-op
    /// acknowledgement — no group join, no permission check beyond being that
    /// authenticated user. e.g. <c>approval.tasks</c>, <c>conversation</c>.
    /// </summary>
    User,

    /// <summary>
    /// Delivered to every connection that has <see cref="IRealtimeChannelRegistry"/>-authorized
    /// <c>Subscribe</c> to the channel, within the tenant. e.g. a shared board:
    /// <c>orders.live</c>, <c>orders.42</c>, <c>&lt;module&gt;.&lt;resource&gt;</c>.
    /// </summary>
    Group,
}

/// <summary>
/// Declares a realtime channel (or a family of channels via a prefix) — its
/// delivery scope and the permission required to receive it.
/// </summary>
/// <param name="Match">
/// The exact channel string, or — when <paramref name="IsPrefix"/> — the prefix
/// that a concrete channel must start with (e.g. <c>orders.</c> matches
/// <c>orders.42</c>). This is how parameterized/per-resource channels work.
/// </param>
/// <param name="IsPrefix">True when <paramref name="Match"/> is a prefix pattern.</param>
/// <param name="Scope">User- or group-delivered.</param>
/// <param name="RequiredPermission">
/// Permission a subscriber must hold (group channels only); null = any
/// authenticated tenant member may subscribe.
/// </param>
public sealed record RealtimeChannelDescriptor(
    string Match,
    bool IsPrefix,
    RealtimeChannelScope Scope,
    string? RequiredPermission);

/// <summary>
/// Central catalogue of every realtime channel the platform serves. The hub
/// consults it on <c>Subscribe</c> to (a) reject unknown channels, (b) skip
/// group-joining user channels, and (c) authorize group channels. Modules
/// declare their channels at startup via <see cref="IRealtimeChannelGate"/>.
///
/// <para>Channels are explicit by design: a connection receives only what it
/// subscribed to (no accidental cross-feature fan-out), and a permission can be
/// required per channel. Prefix descriptors enable parameterized channels —
/// register <c>orders.</c> once and every <c>orders.{id}</c> is gated the
/// same way.</para>
/// </summary>
public interface IRealtimeChannelRegistry
{
    void Register(RealtimeChannelDescriptor descriptor);

    /// <summary>
    /// Resolve a concrete channel to its descriptor (exact match wins; otherwise
    /// the longest matching prefix). Null when the channel is not registered.
    /// </summary>
    RealtimeChannelDescriptor? Resolve(string channel);
}

/// <summary>
/// Implemented by a module to declare its channels. All registered gates run on
/// registry construction — the same DI-collection pattern used for permission
/// providers and event sinks.
/// </summary>
public interface IRealtimeChannelGate
{
    void Configure(IRealtimeChannelRegistry registry);
}

/// <summary>Default in-memory <see cref="IRealtimeChannelRegistry"/> (singleton).</summary>
public sealed class RealtimeChannelRegistry : IRealtimeChannelRegistry
{
    private readonly ConcurrentDictionary<string, RealtimeChannelDescriptor> _exact =
        new(StringComparer.OrdinalIgnoreCase);
    // Prefix descriptors, kept longest-first so the most specific wins.
    private readonly List<RealtimeChannelDescriptor> _prefixes = new();
    private readonly object _prefixLock = new();

    public RealtimeChannelRegistry() { }

    public RealtimeChannelRegistry(IEnumerable<IRealtimeChannelGate> gates)
    {
        foreach (var gate in gates) gate.Configure(this);
    }

    public void Register(RealtimeChannelDescriptor descriptor)
    {
        if (descriptor is null || string.IsNullOrWhiteSpace(descriptor.Match)) return;
        if (descriptor.IsPrefix)
        {
            lock (_prefixLock)
            {
                _prefixes.RemoveAll(p => string.Equals(p.Match, descriptor.Match, StringComparison.OrdinalIgnoreCase));
                _prefixes.Add(descriptor);
                _prefixes.Sort((a, b) => b.Match.Length.CompareTo(a.Match.Length));
            }
        }
        else
        {
            _exact[descriptor.Match] = descriptor;
        }
    }

    public RealtimeChannelDescriptor? Resolve(string channel)
    {
        if (string.IsNullOrWhiteSpace(channel)) return null;
        if (_exact.TryGetValue(channel, out var exact)) return exact;
        lock (_prefixLock)
        {
            foreach (var p in _prefixes)
            {
                if (channel.StartsWith(p.Match, StringComparison.OrdinalIgnoreCase))
                    return p;
            }
        }
        return null;
    }
}

/// <summary>Fluent helpers for declaring channels from a gate.</summary>
public static class RealtimeChannelRegistryExtensions
{
    /// <summary>A user-delivered channel (events targeted at the recipient).</summary>
    public static void RegisterUserChannel(this IRealtimeChannelRegistry registry, string channel)
        => registry.Register(new RealtimeChannelDescriptor(channel, IsPrefix: false, RealtimeChannelScope.User, null));

    /// <summary>A shared group channel; <paramref name="permission"/> null = any tenant member.</summary>
    public static void RegisterGroupChannel(this IRealtimeChannelRegistry registry, string channel, string? permission = null)
        => registry.Register(new RealtimeChannelDescriptor(channel, IsPrefix: false, RealtimeChannelScope.Group, permission));

    /// <summary>A family of group channels sharing a prefix (e.g. <c>orders.</c>).</summary>
    public static void RegisterGroupPrefix(this IRealtimeChannelRegistry registry, string prefix, string? permission = null)
        => registry.Register(new RealtimeChannelDescriptor(prefix, IsPrefix: true, RealtimeChannelScope.Group, permission));
}

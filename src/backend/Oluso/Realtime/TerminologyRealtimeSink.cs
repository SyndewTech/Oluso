using Oluso.Core.Events;
using Oluso.Core.Realtime;
using Oluso.Core.Terminology;

namespace Oluso.Realtime;

/// <summary>
/// Bridges <see cref="TerminologyUpdatedEvent"/> (an override set/reset in the
/// Terminology Center, or an industry change) to the platform realtime
/// <c>terminology</c> channel so open UIs re-fetch their label bundle and
/// relabel live. Platform-owned — terminology is not any module's concern.
/// Best-effort: a broadcast failure never breaks the underlying event flow.
/// </summary>
public sealed class TerminologyRealtimeSink : IOlusoEventSink
{
    /// <summary>Channel open UIs subscribe to for live terminology relabel.</summary>
    public const string Channel = "terminology";

    private readonly IRealtimeBroadcaster _broadcaster;

    public TerminologyRealtimeSink(IRealtimeBroadcaster broadcaster) => _broadcaster = broadcaster;

    public string Name => "terminology-realtime";

    public async Task HandleAsync(OlusoEvent evt, CancellationToken cancellationToken = default)
    {
        if (evt is not TerminologyUpdatedEvent updated) return;
        if (string.IsNullOrEmpty(updated.TenantId)) return;

        try
        {
            // Payload-less hint (plus the key/locale for targeted refreshes) —
            // clients just re-fetch the bundle for the affected locale.
            await _broadcaster.BroadcastToChannelAsync(
                updated.TenantId, Channel, "terminology.updated",
                new { key = updated.Key, locale = updated.Locale }, cancellationToken);
        }
        catch
        {
            // Realtime is best-effort; never propagate.
        }
    }
}

/// <summary>
/// Declares the platform-owned realtime channels. <c>terminology</c> is a
/// group channel any authenticated tenant member may subscribe to — labels are
/// not sensitive, and every app needs the relabel signal. <c>ciba</c> is a user
/// channel: each user receives only their own sign-in approval requests.
/// </summary>
public sealed class PlatformChannelGate : IRealtimeChannelGate
{
    public void Configure(IRealtimeChannelRegistry registry)
    {
        registry.RegisterGroupChannel(TerminologyRealtimeSink.Channel);
        registry.RegisterUserChannel(Oluso.Core.Services.CibaNotifications.RealtimeChannel);
    }
}

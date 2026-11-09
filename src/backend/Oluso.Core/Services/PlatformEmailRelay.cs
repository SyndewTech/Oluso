namespace Oluso.Core.Services;

/// <summary>
/// An email a standalone app (e.g. the careers site) asks the platform to send on a
/// tenant's behalf, posted to <see cref="Route"/> with the relay key. The platform renders
/// the named template for that tenant (its override, else the platform default, with the
/// tenant's branding) and sends it through the tenant's sender, so the app holds neither
/// mail credentials nor email copy.
/// </summary>
public sealed class PlatformEmailRelayRequest
{
    public const string Route = "api/messaging/relay/email";

    public required string TenantId { get; init; }
    public required string To { get; init; }
    public required string TemplateName { get; init; }

    /// <summary>The template's placeholder values.</summary>
    public Dictionary<string, string> Values { get; init; } = new();
}

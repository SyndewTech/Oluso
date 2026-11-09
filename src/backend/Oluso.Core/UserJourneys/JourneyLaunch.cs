using System.Text.Json;

namespace Oluso.Core.UserJourneys;

/// <summary>
/// A signed instruction to start a journey for a calling application: the context the
/// application wants back on the submission, and where to send the person when they finish.
/// Carried as the <c>launch</c> query parameter of <c>/journey/start/{slug}</c>.
/// </summary>
/// <param name="Module">The module starting the journey; a run of a module's journey belongs to it (see
/// <see cref="JourneyRunOwnership"/>).</param>
public sealed record JourneyLaunchTicket(
    string TenantId,
    string PolicyId,
    IReadOnlyDictionary<string, string> Context,
    string? ReturnUrl,
    DateTimeOffset ExpiresAt,
    string? Module = null);

public enum JourneyLaunchReadStatus
{
    Valid,
    Invalid,
    Expired,
}

public sealed record JourneyLaunchReadResult(JourneyLaunchReadStatus Status, JourneyLaunchTicket? Ticket)
{
    public static JourneyLaunchReadResult Invalid { get; } = new(JourneyLaunchReadStatus.Invalid, null);
    public static JourneyLaunchReadResult Expired { get; } = new(JourneyLaunchReadStatus.Expired, null);
}

/// <summary>
/// Mints and reads journey launch tickets. Tickets are protected with ASP.NET Data Protection,
/// so only this deployment can issue them and any change to one makes it unreadable.
/// </summary>
public interface IJourneyLaunchTokenService
{
    /// <summary>Protects a ticket into an opaque URL-safe token.</summary>
    string Protect(JourneyLaunchTicket ticket);

    /// <summary>Reads a token; a tampered, foreign or expired token is never returned as valid.</summary>
    JourneyLaunchReadResult Read(string token);

    /// <summary>
    /// Whether <paramref name="returnUrl"/> may receive people at the end of a journey for the
    /// tenant: a local path, or an absolute address on the tenant's own host or on an origin
    /// registered by one of its applications.
    /// </summary>
    Task<bool> IsAllowedReturnUrlAsync(string tenantId, string returnUrl, CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds the tenant-host start URL of an enabled journey with a launch ticket carrying
    /// <paramref name="context"/>. Throws when the journey isn't one the tenant can use or the
    /// return URL isn't allowed.
    /// </summary>
    /// <param name="module">The module starting the journey. Required for a module's journey, which the module may
    /// start only when it owns it or the journey is shared.</param>
    Task<string> CreateLaunchUrlAsync(
        string tenantId,
        string policyId,
        IDictionary<string, string> context,
        string? returnUrl,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default,
        string? module = null);
}

/// <summary>The person a launch link was sent to: the addresses the link went out on.</summary>
public sealed record JourneyLinkRecipient(string? Email, string? Phone);

/// <summary>Where a launch ticket's context lives in journey state.</summary>
public static class JourneyLaunchData
{
    public const string QueryParameter = "launch";

    /// <summary>Reserved journey-data key; the leading underscore keeps it out of submitted data.</summary>
    public const string ContextDataKey = "_launch_context";

    /// <summary>Context key for the email address a link was sent to.</summary>
    public const string RecipientEmailKey = "recipient.email";

    /// <summary>Context key for the phone number a link was sent to.</summary>
    public const string RecipientPhoneKey = "recipient.phone";

    /// <summary>
    /// Who the run's launch link was sent to, or null when it wasn't sent to anyone (a page started it for a
    /// signed-in user, or it was opened by its slug). A link sent to someone names at least one of their addresses.
    /// </summary>
    public static JourneyLinkRecipient? ReadRecipient(IDictionary<string, object>? data)
    {
        var context = ReadContext(data);
        if (context == null) return null;
        var email = context.TryGetValue(RecipientEmailKey, out var e) && !string.IsNullOrWhiteSpace(e) ? e.Trim() : null;
        var phone = context.TryGetValue(RecipientPhoneKey, out var p) && !string.IsNullOrWhiteSpace(p) ? p.Trim() : null;
        return email == null && phone == null ? null : new JourneyLinkRecipient(email, phone);
    }

    public static void Write(IDictionary<string, object> data, JourneyLaunchTicket ticket)
    {
        data[ContextDataKey] = JsonSerializer.Serialize(ticket.Context);
        if (!string.IsNullOrEmpty(ticket.Module))
        {
            data[JourneyRunOwnership.LaunchingModuleDataKey] = ticket.Module;
        }
    }

    public static IReadOnlyDictionary<string, string>? ReadContext(IDictionary<string, object>? data)
    {
        if (data == null || !data.TryGetValue(ContextDataKey, out var raw)) return null;
        var json = raw switch
        {
            string s => s,
            JsonElement { ValueKind: JsonValueKind.String } e => e.GetString(),
            _ => raw?.ToString(),
        };
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

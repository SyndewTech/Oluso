using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Oluso.Core.Events;

/// <summary>
/// The kinds of actor an <see cref="EventOrigin"/> names.
/// </summary>
public static class EventOriginKinds
{
    /// <summary>A user acting through the admin API. Id is their user id.</summary>
    public const string Admin = "admin";

    /// <summary>A machine client calling the Service API with a client_credentials token. Id is the client id.</summary>
    public const string ServiceClient = "service_client";

    /// <summary>An inbound SCIM client provisioning into the tenant. Id is the SCIM client id.</summary>
    public const string Scim = "scim";

    /// <summary>
    /// A message received from an external system over an inbound integration feed.
    /// Id names the interface it arrived on, prefixed with the feed's protocol.
    /// </summary>
    public const string Integration = "integration";

    /// <summary>A user journey step. Id is the journey policy id.</summary>
    public const string Journey = "journey";

    /// <summary>A signed-in user acting on their own account. Id is their user id.</summary>
    public const string User = "user";

    /// <summary>An unauthenticated request.</summary>
    public const string Anonymous = "anonymous";

    /// <summary>The platform itself: background jobs, startup, timers. Id names the job when known.</summary>
    public const string System = "system";
}

/// <summary>
/// What caused an event: the kind of actor, its id within that kind, an optional
/// display name, and the IP address of the request when there was one. Webhook
/// subscribers, the audit log, workflows and outbound provisioning read it to
/// tell who made a change and to avoid echoing a change back to its source.
/// </summary>
public sealed record EventOrigin
{
    /// <summary>One of <see cref="EventOriginKinds"/>.</summary>
    public required string Kind { get; init; }

    /// <summary>The actor's id within <see cref="Kind"/>: user id, client id, SCIM client id, policy id, integration interface.</summary>
    public string? Id { get; init; }

    public string? Name { get; init; }

    public string? IpAddress { get; init; }

    public static EventOrigin Admin(string userId, string? name = null, string? ipAddress = null) =>
        new() { Kind = EventOriginKinds.Admin, Id = userId, Name = name, IpAddress = ipAddress };

    public static EventOrigin ServiceClient(string clientId, string? ipAddress = null, string? name = null) =>
        new() { Kind = EventOriginKinds.ServiceClient, Id = clientId, Name = name, IpAddress = ipAddress };

    public static EventOrigin Scim(string scimClientId, string? name = null, string? ipAddress = null) =>
        new() { Kind = EventOriginKinds.Scim, Id = scimClientId, Name = name, IpAddress = ipAddress };

    public static EventOrigin Integration(string interfaceId, string? name = null, string? ipAddress = null) =>
        new() { Kind = EventOriginKinds.Integration, Id = interfaceId, Name = name, IpAddress = ipAddress };

    public static EventOrigin Journey(string policyId, string? name = null, string? ipAddress = null) =>
        new() { Kind = EventOriginKinds.Journey, Id = policyId, Name = name, IpAddress = ipAddress };

    public static EventOrigin User(string userId, string? name = null, string? ipAddress = null) =>
        new() { Kind = EventOriginKinds.User, Id = userId, Name = name, IpAddress = ipAddress };

    public static EventOrigin Anonymous(string? ipAddress = null) =>
        new() { Kind = EventOriginKinds.Anonymous, IpAddress = ipAddress };

    public static EventOrigin System(string? source = null) =>
        new() { Kind = EventOriginKinds.System, Id = source };

    public bool Is(string kind, string? id) =>
        string.Equals(Kind, kind, StringComparison.Ordinal)
        && !string.IsNullOrEmpty(id)
        && string.Equals(Id, id, StringComparison.Ordinal);
}

/// <summary>
/// The origin of events raised in the current async flow, for raisers that know
/// better than the request does (a SCIM client, a journey, a background job).
/// Events raised inside the scope without an explicit origin take it, including
/// events raised by services the raiser calls.
/// </summary>
public static class EventOriginScope
{
    private static readonly AsyncLocal<EventOrigin?> CurrentOrigin = new();

    public static EventOrigin? Current => CurrentOrigin.Value;

    public static IDisposable Begin(EventOrigin origin)
    {
        var previous = CurrentOrigin.Value;
        CurrentOrigin.Value = origin;
        return new Restore(previous);
    }

    private sealed class Restore(EventOrigin? previous) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            CurrentOrigin.Value = previous;
        }
    }
}

/// <summary>
/// Derives an event's origin from the request being handled.
/// </summary>
public interface IEventOriginResolver
{
    /// <summary>The request's actor, or null outside a request.</summary>
    EventOrigin? ResolveFromRequest();

    /// <summary>The request's client IP address, or null outside a request.</summary>
    string? RequestIpAddress { get; }
}

/// <summary>
/// A client_credentials token (client_id, no subject) is a service client; a user
/// token on the admin API is an admin, anywhere else the user themselves; an
/// unauthenticated request is anonymous.
/// </summary>
public sealed class HttpContextEventOriginResolver : IEventOriginResolver
{
    private const string AdminApiPrefix = "/api/admin";

    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextEventOriginResolver(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public string? RequestIpAddress => _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public EventOrigin? ResolveFromRequest()
    {
        var context = _httpContextAccessor.HttpContext;
        if (context == null) return null;

        var ip = context.Connection.RemoteIpAddress?.ToString();
        var user = context.User;
        if (user.Identity?.IsAuthenticated != true)
            return EventOrigin.Anonymous(ip);

        var subject = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var clientId = user.FindFirst("client_id")?.Value;

        if (string.IsNullOrEmpty(subject))
        {
            return string.IsNullOrEmpty(clientId)
                ? EventOrigin.Anonymous(ip)
                : EventOrigin.ServiceClient(clientId, ip);
        }

        var name = user.FindFirst("name")?.Value
            ?? user.FindFirst(ClaimTypes.Name)?.Value
            ?? user.FindFirst("email")?.Value
            ?? user.FindFirst(ClaimTypes.Email)?.Value;

        return context.Request.Path.StartsWithSegments(AdminApiPrefix, StringComparison.OrdinalIgnoreCase)
            ? EventOrigin.Admin(subject, name, ip)
            : EventOrigin.User(subject, name, ip);
    }
}

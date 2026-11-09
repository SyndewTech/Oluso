using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Oluso.Core.Events;

/// <summary>
/// Interface for handling Oluso events.
/// Implement this interface to receive and process events.
/// Enterprise packages can register their own sinks.
/// </summary>
/// <example>
/// <code>
/// public class MyEventSink : IOlusoEventSink
/// {
///     public Task HandleAsync(OlusoEvent evt, CancellationToken cancellationToken)
///     {
///         switch (evt)
///         {
///             case UserSignedInEvent signIn:
///                 _logger.LogInformation("User {User} signed in", signIn.Username);
///                 break;
///             case UserSignInFailedEvent failed:
///                 _alertService.NotifySecurityTeam(failed);
///                 break;
///         }
///         return Task.CompletedTask;
///     }
/// }
/// </code>
/// </example>
public interface IOlusoEventSink
{
    /// <summary>
    /// Display name for this sink (for logging/diagnostics)
    /// </summary>
    string Name => GetType().Name;

    /// <summary>
    /// Handles an Oluso event
    /// </summary>
    Task HandleAsync(OlusoEvent evt, CancellationToken cancellationToken = default);
}

/// <summary>
/// Service for raising events. Events are dispatched to all registered IOlusoEventSink implementations.
/// This is the single entry point for all event publishing in the system.
/// </summary>
public interface IOlusoEventService
{
    /// <summary>
    /// Raises an event to all registered sinks
    /// </summary>
    Task RaiseAsync(OlusoEvent evt, CancellationToken cancellationToken = default);
}

/// <summary>
/// Configuration options for the event service
/// </summary>
public class OlusoEventOptions
{
    /// <summary>
    /// Whether to continue processing other sinks if one fails
    /// </summary>
    public bool ContinueOnSinkError { get; set; } = true;

    /// <summary>
    /// Categories to include. Empty means all categories.
    /// </summary>
    public HashSet<string> IncludeCategories { get; set; } = new();

    /// <summary>
    /// Categories to exclude.
    /// </summary>
    public HashSet<string> ExcludeCategories { get; set; } = new();

    /// <summary>
    /// Whether to capture activity ID for distributed tracing
    /// </summary>
    public bool CaptureActivityId { get; set; } = true;
}

/// <summary>
/// Fills in event data only the host handling the event can know (e.g. a URL configured
/// on the platform but not on a Functions host that raised the event). Runs in
/// <see cref="IOlusoEventService.RaiseAsync"/> before the sinks, including when the
/// platform re-raises an event relayed from another host.
/// </summary>
public interface IOlusoEventEnricher
{
    Task EnrichAsync(OlusoEvent evt, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default implementation of IOlusoEventService
/// </summary>
public class OlusoEventService : IOlusoEventService
{
    private readonly IEnumerable<IOlusoEventSink> _sinks;
    private readonly IEnumerable<IOlusoEventEnricher> _enrichers;
    private readonly ILogger<OlusoEventService> _logger;
    private readonly OlusoEventOptions _options;
    private readonly IEventOriginResolver? _originResolver;

    public OlusoEventService(
        IEnumerable<IOlusoEventSink> sinks,
        ILogger<OlusoEventService> logger,
        IOptions<OlusoEventOptions>? options = null,
        IEnumerable<IOlusoEventEnricher>? enrichers = null,
        IEventOriginResolver? originResolver = null)
    {
        _enrichers = enrichers ?? Array.Empty<IOlusoEventEnricher>();
        _sinks = sinks;
        _logger = logger;
        _options = options?.Value ?? new OlusoEventOptions();
        _originResolver = originResolver;
    }

    /// <summary>
    /// The raiser's origin, else the ambient scope, else an authenticated request,
    /// else what the event's own fields imply, else an anonymous request, else the
    /// system. A request IP fills an origin that has none.
    /// </summary>
    private EventOrigin ResolveOrigin(OlusoEvent evt)
    {
        var origin = evt.Origin ?? EventOriginScope.Current;
        if (origin == null)
        {
            var fromRequest = _originResolver?.ResolveFromRequest();
            origin = fromRequest is { Kind: not EventOriginKinds.Anonymous }
                ? fromRequest
                : evt.ImpliedOrigin() ?? fromRequest ?? EventOrigin.System();
        }

        if (origin.IpAddress == null && origin.Kind != EventOriginKinds.System && _originResolver?.RequestIpAddress is { } ip)
            origin = origin with { IpAddress = ip };

        return origin;
    }

    public async Task RaiseAsync(OlusoEvent evt, CancellationToken cancellationToken = default)
    {
        // Apply category filtering
        if (_options.ExcludeCategories.Contains(evt.Category))
            return;

        if (_options.IncludeCategories.Count > 0 && !_options.IncludeCategories.Contains(evt.Category))
            return;

        evt.Origin = ResolveOrigin(evt);

        // Capture activity ID for distributed tracing
        if (_options.CaptureActivityId && string.IsNullOrEmpty(evt.ActivityId))
        {
            evt.ActivityId = Activity.Current?.Id;
        }

        foreach (var enricher in _enrichers)
        {
            try
            {
                await enricher.EnrichAsync(evt, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Event enricher {Enricher} failed for event {EventType}", enricher.GetType().Name, evt.EventType);
            }
        }

        foreach (var sink in _sinks)
        {
            try
            {
                await sink.HandleAsync(evt, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Event sink {SinkName} failed to handle event {EventType}",
                    sink.Name, evt.EventType);

                if (!_options.ContinueOnSinkError)
                    throw;
            }
        }
    }
}

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
///     public Task HandleAsync(OlusoEvent evt, CancellationToken ct)
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
/// Default implementation of IOlusoEventService
/// </summary>
public class OlusoEventService : IOlusoEventService
{
    private readonly IEnumerable<IOlusoEventSink> _sinks;
    private readonly ILogger<OlusoEventService> _logger;
    private readonly OlusoEventOptions _options;

    public OlusoEventService(
        IEnumerable<IOlusoEventSink> sinks,
        ILogger<OlusoEventService> logger,
        IOptions<OlusoEventOptions>? options = null)
    {
        _sinks = sinks;
        _logger = logger;
        _options = options?.Value ?? new OlusoEventOptions();
    }

    public async Task RaiseAsync(OlusoEvent evt, CancellationToken cancellationToken = default)
    {
        // Apply category filtering
        if (_options.ExcludeCategories.Contains(evt.Category))
            return;

        if (_options.IncludeCategories.Count > 0 && !_options.IncludeCategories.Contains(evt.Category))
            return;

        // Capture activity ID for distributed tracing
        if (_options.CaptureActivityId && string.IsNullOrEmpty(evt.ActivityId))
        {
            evt.ActivityId = Activity.Current?.Id;
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

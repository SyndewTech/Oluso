using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Oluso.Core.Events;

/// <summary>
/// Event sink that logs events to ILogger.
/// Useful for development and debugging.
/// </summary>
public class LoggerEventSink : IOlusoEventSink
{
    private readonly ILogger<LoggerEventSink> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public string Name => "Logger";

    public LoggerEventSink(ILogger<LoggerEventSink> logger)
    {
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public Task HandleAsync(OlusoEvent evt, CancellationToken cancellationToken = default)
    {
        var logLevel = evt.IsFailure ? LogLevel.Warning : LogLevel.Information;

        _logger.Log(
            logLevel,
            "[{Category}] {EventType}: {EventData}",
            evt.Category,
            evt.EventType,
            JsonSerializer.Serialize(evt, evt.GetType(), _jsonOptions));

        return Task.CompletedTask;
    }
}

/// <summary>
/// Event sink that invokes a callback for each event.
/// Useful for custom processing without implementing IOlusoEventSink.
/// </summary>
public class CallbackEventSink : IOlusoEventSink
{
    private readonly Func<OlusoEvent, CancellationToken, Task> _callback;
    private readonly string _name;

    public string Name => _name;

    public CallbackEventSink(Func<OlusoEvent, CancellationToken, Task> callback, string? name = null)
    {
        _callback = callback;
        _name = name ?? "Callback";
    }

    public CallbackEventSink(Func<OlusoEvent, Task> callback, string? name = null)
        : this((evt, _) => callback(evt), name)
    {
    }

    public Task HandleAsync(OlusoEvent evt, CancellationToken cancellationToken = default)
    {
        return _callback(evt, cancellationToken);
    }
}

/// <summary>
/// Event sink that filters events before forwarding to another sink.
/// </summary>
public class FilteredEventSink : IOlusoEventSink
{
    private readonly IOlusoEventSink _inner;
    private readonly Func<OlusoEvent, bool> _filter;

    public string Name => $"Filtered({_inner.Name})";

    public FilteredEventSink(IOlusoEventSink inner, Func<OlusoEvent, bool> filter)
    {
        _inner = inner;
        _filter = filter;
    }

    /// <summary>
    /// Creates a sink that only handles events of specified categories
    /// </summary>
    public static FilteredEventSink ForCategories(IOlusoEventSink inner, params string[] categories)
    {
        var categorySet = new HashSet<string>(categories, StringComparer.OrdinalIgnoreCase);
        return new FilteredEventSink(inner, evt => categorySet.Contains(evt.Category));
    }

    /// <summary>
    /// Creates a sink that excludes events of specified categories
    /// </summary>
    public static FilteredEventSink ExcludeCategories(IOlusoEventSink inner, params string[] categories)
    {
        var categorySet = new HashSet<string>(categories, StringComparer.OrdinalIgnoreCase);
        return new FilteredEventSink(inner, evt => !categorySet.Contains(evt.Category));
    }

    /// <summary>
    /// Creates a sink that only handles failure events
    /// </summary>
    public static FilteredEventSink FailuresOnly(IOlusoEventSink inner)
    {
        return new FilteredEventSink(inner, evt => evt.IsFailure);
    }

    public Task HandleAsync(OlusoEvent evt, CancellationToken cancellationToken = default)
    {
        if (_filter(evt))
        {
            return _inner.HandleAsync(evt, cancellationToken);
        }

        return Task.CompletedTask;
    }
}

/// <summary>
/// Event sink that batches events and flushes periodically.
/// Useful for high-throughput scenarios.
/// </summary>
public class BatchingEventSink : IOlusoEventSink, IDisposable
{
    private readonly Func<IReadOnlyList<OlusoEvent>, CancellationToken, Task> _flushCallback;
    private readonly List<OlusoEvent> _buffer = new();
    private readonly int _batchSize;
    private readonly TimeSpan _flushInterval;
    private readonly Timer _timer;
    private readonly object _lock = new();

    public string Name => "Batching";

    public BatchingEventSink(
        Func<IReadOnlyList<OlusoEvent>, CancellationToken, Task> flushCallback,
        int batchSize = 100,
        TimeSpan? flushInterval = null)
    {
        _flushCallback = flushCallback;
        _batchSize = batchSize;
        _flushInterval = flushInterval ?? TimeSpan.FromSeconds(5);
        _timer = new Timer(async _ => await FlushAsync(), null, _flushInterval, _flushInterval);
    }

    public async Task HandleAsync(OlusoEvent evt, CancellationToken cancellationToken = default)
    {
        List<OlusoEvent>? toFlush = null;

        lock (_lock)
        {
            _buffer.Add(evt);

            if (_buffer.Count >= _batchSize)
            {
                toFlush = new List<OlusoEvent>(_buffer);
                _buffer.Clear();
            }
        }

        if (toFlush != null)
        {
            await _flushCallback(toFlush, cancellationToken);
        }
    }

    public async Task FlushAsync(CancellationToken cancellationToken = default)
    {
        List<OlusoEvent>? toFlush = null;

        lock (_lock)
        {
            if (_buffer.Count > 0)
            {
                toFlush = new List<OlusoEvent>(_buffer);
                _buffer.Clear();
            }
        }

        if (toFlush != null)
        {
            await _flushCallback(toFlush, cancellationToken);
        }
    }

    public void Dispose()
    {
        _timer.Dispose();
        FlushAsync().GetAwaiter().GetResult();
    }
}

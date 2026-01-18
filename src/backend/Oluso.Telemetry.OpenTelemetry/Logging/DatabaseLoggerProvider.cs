using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Telemetry.OpenTelemetry.Logging;

/// <summary>
/// Options for the database logger provider
/// </summary>
public class DatabaseLoggerOptions
{
    /// <summary>
    /// Minimum log level to capture (default: Information)
    /// </summary>
    public LogLevel MinimumLevel { get; set; } = LogLevel.Information;

    /// <summary>
    /// Categories to include (empty = all categories)
    /// </summary>
    public HashSet<string> IncludeCategories { get; set; } = new();

    /// <summary>
    /// Categories to exclude
    /// </summary>
    public HashSet<string> ExcludeCategories { get; set; } = new()
    {
        "Microsoft.EntityFrameworkCore", // Exclude EF Core to prevent recursion
        "Microsoft.AspNetCore.Routing",
        "Microsoft.AspNetCore.StaticFiles",
        "Microsoft.AspNetCore.Cors"
    };

    /// <summary>
    /// Whether to include stack traces for errors (default: true)
    /// </summary>
    public bool IncludeStackTrace { get; set; } = true;

    /// <summary>
    /// Batch size for writing logs (default: 10)
    /// </summary>
    public int BatchSize { get; set; } = 10;

    /// <summary>
    /// Maximum time to wait before flushing logs (default: 5 seconds)
    /// </summary>
    public TimeSpan FlushInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Maximum message length (default: 4000)
    /// </summary>
    public int MaxMessageLength { get; set; } = 4000;
}

/// <summary>
/// Logger provider that writes logs to the database via ITelemetryLogStore
/// </summary>
[ProviderAlias("Database")]
public class DatabaseLoggerProvider : ILoggerProvider, IAsyncDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly DatabaseLoggerOptions _options;
    private readonly ConcurrentDictionary<string, DatabaseLogger> _loggers = new();
    private readonly ConcurrentQueue<TelemetryLog> _logQueue = new();
    private readonly Timer _flushTimer;
    private readonly SemaphoreSlim _flushSemaphore = new(1, 1);
    private bool _disposed;

    public DatabaseLoggerProvider(
        IServiceProvider serviceProvider,
        IOptions<DatabaseLoggerOptions> options)
    {
        _serviceProvider = serviceProvider;
        _options = options.Value;
        _flushTimer = new Timer(async _ => await FlushAsync(), null, _options.FlushInterval, _options.FlushInterval);
    }

    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, name => new DatabaseLogger(name, this, _options));
    }

    internal void EnqueueLog(TelemetryLog log)
    {
        _logQueue.Enqueue(log);

        // Flush immediately if batch size reached
        if (_logQueue.Count >= _options.BatchSize)
        {
            _ = Task.Run(async () => await FlushAsync());
        }
    }

    private async Task FlushAsync()
    {
        if (_disposed || _logQueue.IsEmpty)
            return;

        // Only allow one flush at a time
        if (!await _flushSemaphore.WaitAsync(0))
            return;

        try
        {
            var logs = new List<TelemetryLog>();
            while (logs.Count < _options.BatchSize * 2 && _logQueue.TryDequeue(out var log))
            {
                logs.Add(log);
            }

            if (logs.Count == 0)
                return;

            // Create a new scope to get the store
            using var scope = _serviceProvider.CreateScope();
            var store = scope.ServiceProvider.GetService<ITelemetryLogStore>();
            if (store != null)
            {
                await store.WriteBatchAsync(logs);
            }
        }
        catch
        {
            // Swallow exceptions to prevent logging from breaking the application
        }
        finally
        {
            _flushSemaphore.Release();
        }
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().Wait();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;

        _disposed = true;
        await _flushTimer.DisposeAsync();

        // Final flush
        await FlushAsync();

        _flushSemaphore.Dispose();
    }
}

/// <summary>
/// Individual logger instance for a specific category
/// </summary>
internal class DatabaseLogger : ILogger
{
    private readonly string _categoryName;
    private readonly DatabaseLoggerProvider _provider;
    private readonly DatabaseLoggerOptions _options;

    public DatabaseLogger(string categoryName, DatabaseLoggerProvider provider, DatabaseLoggerOptions options)
    {
        _categoryName = categoryName;
        _provider = provider;
        _options = options;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel)
    {
        if (logLevel == LogLevel.None)
            return false;

        if (logLevel < _options.MinimumLevel)
            return false;

        // Check excluded categories
        foreach (var excluded in _options.ExcludeCategories)
        {
            if (_categoryName.StartsWith(excluded, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        // Check included categories (if specified)
        if (_options.IncludeCategories.Count > 0)
        {
            foreach (var included in _options.IncludeCategories)
            {
                if (_categoryName.StartsWith(included, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        return true;
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        var message = formatter(state, exception);
        if (string.IsNullOrEmpty(message) && exception == null)
            return;

        // Truncate message if too long
        if (message.Length > _options.MaxMessageLength)
            message = message[.._options.MaxMessageLength] + "...";

        // Get trace context
        var activity = Activity.Current;

        var log = new TelemetryLog
        {
            Timestamp = DateTime.UtcNow,
            Level = logLevel.ToString(),
            Message = message,
            Category = _categoryName,
            TraceId = activity?.TraceId.ToString(),
            SpanId = activity?.SpanId.ToString(),
            MachineName = Environment.MachineName
        };

        if (exception != null)
        {
            log.Exception = exception.Message;
            log.ExceptionType = exception.GetType().FullName;
            if (_options.IncludeStackTrace)
            {
                log.StackTrace = exception.StackTrace;
            }
        }

        // Extract properties from state if available
        if (state is IReadOnlyList<KeyValuePair<string, object?>> stateProperties)
        {
            var props = new Dictionary<string, object?>();
            foreach (var prop in stateProperties)
            {
                if (prop.Key != "{OriginalFormat}")
                {
                    props[prop.Key] = prop.Value;
                }
            }
            if (props.Count > 0)
            {
                try
                {
                    log.Properties = JsonSerializer.Serialize(props);
                }
                catch
                {
                    // Ignore serialization errors
                }
            }
        }

        _provider.EnqueueLog(log);
    }
}

/// <summary>
/// Extension methods for registering the database logger
/// </summary>
public static class DatabaseLoggerExtensions
{
    /// <summary>
    /// Adds database logging to the logging pipeline.
    /// Logs will be written to the TelemetryLogs table via ITelemetryLogStore.
    /// </summary>
    public static ILoggingBuilder AddDatabaseLogger(
        this ILoggingBuilder builder,
        Action<DatabaseLoggerOptions>? configure = null)
    {
        builder.Services.Configure<DatabaseLoggerOptions>(options =>
        {
            configure?.Invoke(options);
        });

        builder.Services.AddSingleton<ILoggerProvider, DatabaseLoggerProvider>();

        return builder;
    }
}

using Microsoft.Extensions.Logging;

namespace Oluso.UserJourneys.Plugins;

/// <summary>
/// Watches plugin directory for changes (hot-reload)
/// </summary>
public interface IPluginWatcher : IDisposable
{
    /// <summary>
    /// Event raised when a plugin file changes
    /// </summary>
    event EventHandler<PluginChangedEventArgs>? OnPluginChanged;

    /// <summary>
    /// Start watching the specified directory for plugin changes
    /// </summary>
    void StartWatching(string directory);

    /// <summary>
    /// Stop watching for changes
    /// </summary>
    void StopWatching();

    /// <summary>
    /// Whether the watcher is currently active
    /// </summary>
    bool IsWatching { get; }
}

/// <summary>
/// Event args for plugin file changes
/// </summary>
public class PluginChangedEventArgs : EventArgs
{
    /// <summary>
    /// Name of the plugin (filename without extension)
    /// </summary>
    public string PluginName { get; init; } = null!;

    /// <summary>
    /// Full path to the plugin file
    /// </summary>
    public string FilePath { get; init; } = null!;

    /// <summary>
    /// Type of change that occurred
    /// </summary>
    public PluginChangeType ChangeType { get; init; }
}

/// <summary>
/// Type of plugin file change
/// </summary>
public enum PluginChangeType
{
    Created,
    Modified,
    Deleted
}

/// <summary>
/// File system watcher implementation for plugins
/// </summary>
public class FileSystemPluginWatcher : IPluginWatcher
{
    private FileSystemWatcher? _watcher;
    private readonly ILogger<FileSystemPluginWatcher> _logger;
    private readonly object _lock = new();
    private bool _disposed;

    public event EventHandler<PluginChangedEventArgs>? OnPluginChanged;

    public bool IsWatching => _watcher != null;

    public FileSystemPluginWatcher(ILogger<FileSystemPluginWatcher> logger)
    {
        _logger = logger;
    }

    public void StartWatching(string directory)
    {
        lock (_lock)
        {
            if (_watcher != null)
            {
                _logger.LogWarning("Plugin watcher already active, stopping previous watcher");
                StopWatching();
            }

            if (!Directory.Exists(directory))
            {
                _logger.LogInformation("Creating plugin directory: {Directory}", directory);
                Directory.CreateDirectory(directory);
            }

            _watcher = new FileSystemWatcher(directory, "*.wasm")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.CreationTime,
                EnableRaisingEvents = true,
                IncludeSubdirectories = false
            };

            _watcher.Created += OnFileCreated;
            _watcher.Changed += OnFileChanged;
            _watcher.Deleted += OnFileDeleted;
            _watcher.Renamed += OnFileRenamed;
            _watcher.Error += OnWatcherError;

            _logger.LogInformation("Started watching plugin directory: {Directory}", directory);
        }
    }

    public void StopWatching()
    {
        lock (_lock)
        {
            if (_watcher != null)
            {
                _watcher.Created -= OnFileCreated;
                _watcher.Changed -= OnFileChanged;
                _watcher.Deleted -= OnFileDeleted;
                _watcher.Renamed -= OnFileRenamed;
                _watcher.Error -= OnWatcherError;
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
                _watcher = null;

                _logger.LogInformation("Stopped watching plugin directory");
            }
        }
    }

    private void OnFileCreated(object sender, FileSystemEventArgs e)
    {
        RaisePluginChanged(e.Name, e.FullPath, PluginChangeType.Created);
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        RaisePluginChanged(e.Name, e.FullPath, PluginChangeType.Modified);
    }

    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        RaisePluginChanged(e.Name, e.FullPath, PluginChangeType.Deleted);
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        // Treat rename as delete of old + create of new
        if (e.OldName != null)
        {
            RaisePluginChanged(e.OldName, e.OldFullPath, PluginChangeType.Deleted);
        }
        RaisePluginChanged(e.Name, e.FullPath, PluginChangeType.Created);
    }

    private void OnWatcherError(object sender, ErrorEventArgs e)
    {
        _logger.LogError(e.GetException(), "File system watcher error");
    }

    private void RaisePluginChanged(string? fileName, string filePath, PluginChangeType changeType)
    {
        if (string.IsNullOrEmpty(fileName)) return;

        var pluginName = Path.GetFileNameWithoutExtension(fileName);
        _logger.LogDebug("Plugin file {ChangeType}: {PluginName} ({FilePath})",
            changeType, pluginName, filePath);

        OnPluginChanged?.Invoke(this, new PluginChangedEventArgs
        {
            PluginName = pluginName,
            FilePath = filePath,
            ChangeType = changeType
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StopWatching();
    }
}

/// <summary>
/// No-op plugin watcher for when hot-reload is disabled
/// </summary>
public class NullPluginWatcher : IPluginWatcher
{
    public event EventHandler<PluginChangedEventArgs>? OnPluginChanged;

    public bool IsWatching => false;

    public void StartWatching(string directory)
    {
        // No-op
    }

    public void StopWatching()
    {
        // No-op
    }

    public void Dispose()
    {
        // No-op
    }
}

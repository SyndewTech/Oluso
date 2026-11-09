using Microsoft.Extensions.Logging;
using Oluso.EntityFramework.DataMigrations;

namespace Oluso.Testing.DataMigrations;

/// <summary>
/// The data migrations the runner starts, in order, read from its log. Registered as the runner's logger, it shows a
/// deploy what ran, so an upgrade can prove migrations already recorded as applied weren't run again.
/// </summary>
public sealed class DataMigrationRunLog : ILogger<DataMigrationRunner>
{
    private const string Started = "{ContextType}: Running data migration '{MigrationId}' - {Description}";

    private readonly List<string> _ran = [];

    public IReadOnlyList<string> Ran => _ran;

    public void Clear() => _ran.Clear();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        if (state is not IReadOnlyList<KeyValuePair<string, object?>> values) return;
        if (!values.Any(v => v.Key == "{OriginalFormat}" && Equals(v.Value, Started))) return;
        if (values.FirstOrDefault(v => v.Key == "MigrationId").Value is string id) _ran.Add(id);
    }
}

using System.Collections.Concurrent;

namespace Oluso.Core.UserJourneys;

/// <summary>
/// Default implementation of managed plugin registry
/// </summary>
public class DefaultManagedPluginRegistry : IManagedPluginRegistry
{
    private readonly ConcurrentDictionary<string, IManagedPlugin> _plugins = new(StringComparer.OrdinalIgnoreCase);

    public void Register(string name, IManagedPlugin plugin)
    {
        _plugins[name] = plugin;
    }

    public IManagedPlugin? Get(string name)
    {
        _plugins.TryGetValue(name, out var plugin);
        return plugin;
    }

    public IEnumerable<(string Name, IManagedPlugin Plugin)> GetAll()
    {
        return _plugins.Select(kvp => (kvp.Key, kvp.Value));
    }
}

namespace Oluso.Core.Licensing;

public class ModuleRegistry
{
    private readonly List<IModuleDescriptor> _modules = new();
    private readonly object _lock = new();
    private static ModuleRegistry? _instance;

    public static ModuleRegistry Instance => _instance ??= new ModuleRegistry();

    public void Register(IModuleDescriptor descriptor)
    {
        lock (_lock)
        {
            if (!_modules.Any(m => m.FeatureKey.Equals(descriptor.FeatureKey, StringComparison.OrdinalIgnoreCase)))
                _modules.Add(descriptor);
        }
    }

    public IReadOnlyList<IModuleDescriptor> GetRegisteredModules()
    {
        lock (_lock)
        {
            return _modules.ToList().AsReadOnly();
        }
    }

    public IModuleDescriptor? GetModule(string featureKey)
    {
        lock (_lock)
        {
            return _modules.FirstOrDefault(m => m.FeatureKey.Equals(featureKey, StringComparison.OrdinalIgnoreCase));
        }
    }

    public bool IsRegistered(string featureKey)
    {
        lock (_lock)
        {
            return _modules.Any(m => m.FeatureKey.Equals(featureKey, StringComparison.OrdinalIgnoreCase));
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _modules.Clear();
        }
    }
}

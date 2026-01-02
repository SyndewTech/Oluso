using Oluso.Core.Domain.Entities;
using Oluso.Core.Services;

namespace Oluso.Keys;

/// <summary>
/// Registry for managing multiple key material providers
/// </summary>
public class KeyMaterialProviderRegistry : IKeyMaterialProviderRegistry
{
    private readonly IEnumerable<IKeyMaterialProvider> _providers;
    private readonly KeyStorageProvider _defaultProvider;

    public KeyMaterialProviderRegistry(
        IEnumerable<IKeyMaterialProvider> providers,
        KeyStorageProvider defaultProvider = KeyStorageProvider.Local)
    {
        _providers = providers;
        _defaultProvider = defaultProvider;
    }

    public IKeyMaterialProvider? GetProvider(KeyStorageProvider providerType)
    {
        return _providers.FirstOrDefault(p => p.ProviderType == providerType);
    }

    public IKeyMaterialProvider GetDefaultProvider()
    {
        return GetProvider(_defaultProvider)
            ?? _providers.FirstOrDefault()
            ?? throw new InvalidOperationException("No key material providers registered");
    }

    public IEnumerable<IKeyMaterialProvider> GetAvailableProviders()
    {
        return _providers.Where(p => p.IsAvailableAsync().GetAwaiter().GetResult());
    }
}

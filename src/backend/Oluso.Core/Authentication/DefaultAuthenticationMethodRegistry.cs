namespace Oluso.Core.Authentication;

/// <summary>
/// Default implementation of IAuthenticationMethodRegistry that discovers
/// providers registered via dependency injection.
/// </summary>
public class DefaultAuthenticationMethodRegistry : IAuthenticationMethodRegistry
{
    private readonly IEnumerable<IAuthenticationMethodProvider> _providers;

    public DefaultAuthenticationMethodRegistry(IEnumerable<IAuthenticationMethodProvider> providers)
    {
        _providers = providers;
    }

    /// <inheritdoc />
    public IEnumerable<IAuthenticationMethodProvider> GetAll()
    {
        return _providers.OrderBy(p => p.Order);
    }

    /// <inheritdoc />
    public IAuthenticationMethodProvider? Get(string id)
    {
        return _providers.FirstOrDefault(p => p.Id == id);
    }

    /// <inheritdoc />
    public IEnumerable<IAuthenticationMethodProvider> GetByCategory(string category)
    {
        return _providers
            .Where(p => p.Category == category)
            .OrderBy(p => p.Order);
    }

    /// <inheritdoc />
    public IEnumerable<IAuthenticationMethodProvider> GetLoginProviders()
    {
        return _providers
            .Where(p => p.SupportsLogin)
            .OrderBy(p => p.Order);
    }

    /// <inheritdoc />
    public IEnumerable<IAuthenticationMethodProvider> GetManagementProviders()
    {
        return _providers
            .Where(p => p.SupportsManagement)
            .OrderBy(p => p.Order);
    }

    /// <inheritdoc />
    public IEnumerable<IAuthenticationMethodProvider> GetAvailableLoginProviders()
    {
        return _providers
            .Where(p => p.IsAvailable && p.SupportsLogin)
            .OrderBy(p => p.Order);
    }

    /// <inheritdoc />
    public IEnumerable<IAuthenticationMethodProvider> GetAvailableManagementProviders()
    {
        return _providers
            .Where(p => p.IsAvailable && p.SupportsManagement)
            .OrderBy(p => p.Order);
    }
}


namespace Oluso.Enterprise.Verification.Services;

public sealed class VerificationProviderRegistry : IVerificationProviderRegistry
{
    private readonly Dictionary<string, IVerificationProvider> _byId;

    public VerificationProviderRegistry(IEnumerable<IVerificationProvider> providers)
    {
        All = providers.ToList();
        _byId = new Dictionary<string, IVerificationProvider>(StringComparer.OrdinalIgnoreCase);
        foreach (var provider in All)
        {
            if (!_byId.TryAdd(provider.Id, provider))
                throw new InvalidOperationException($"Two verification providers are registered with the id '{provider.Id}'.");
        }
        BusinessProviders = All.OfType<IBusinessVerificationProvider>().ToList();
    }

    public IReadOnlyList<IVerificationProvider> All { get; }

    public IReadOnlyList<IBusinessVerificationProvider> BusinessProviders { get; }

    public IVerificationProvider? Find(string providerId) => _byId.GetValueOrDefault(providerId);
}

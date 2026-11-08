using Microsoft.Extensions.Logging;

namespace Oluso.Enterprise.Verification.Services;

public sealed class BusinessVerificationRouter : IBusinessVerificationRouter
{
    private readonly IIdentityVerificationSettingsService _settings;
    private readonly IVerificationProviderRegistry _providers;
    private readonly ILogger<BusinessVerificationRouter> _logger;

    public BusinessVerificationRouter(
        IIdentityVerificationSettingsService settings,
        IVerificationProviderRegistry providers,
        ILogger<BusinessVerificationRouter> logger)
    {
        _settings = settings;
        _providers = providers;
        _logger = logger;
    }

    public async Task<BusinessVerificationRoute> ResolveAsync(string tenantId, string country, CancellationToken cancellationToken = default)
    {
        var code = country.Trim().ToUpperInvariant();
        var settings = await _settings.GetAsync(tenantId, cancellationToken);

        if (!settings.BusinessRouting.TryGetValue(code, out var providerId) || string.IsNullOrWhiteSpace(providerId))
            throw Fail(tenantId, code, $"No business verification provider is routed for {code}.");

        if (_providers.Find(providerId) is not IBusinessVerificationProvider provider)
            throw Fail(tenantId, code, $"The provider '{providerId}' routed for {code} isn't available for business verification.");

        if (!provider.SupportedBusinessCountries.Contains(code, StringComparer.OrdinalIgnoreCase))
            throw Fail(tenantId, code, $"{provider.DisplayName} doesn't cover business registries in {code}.");

        if (!settings.Providers.TryGetValue(provider.Id, out var configured) || !configured.Enabled)
            throw Fail(tenantId, code, $"{provider.DisplayName} is routed for {code} but isn't enabled.");

        return new BusinessVerificationRoute(provider, Connect(tenantId, code, provider, configured));
    }

    public async Task<BusinessVerificationRoute> ResolveProviderAsync(string tenantId, string providerId, CancellationToken cancellationToken = default)
    {
        var settings = await _settings.GetAsync(tenantId, cancellationToken);
        if (_providers.Find(providerId) is not IBusinessVerificationProvider provider)
            throw Fail(tenantId, "-", $"'{providerId}' isn't a business verification provider.");
        if (!settings.Providers.TryGetValue(provider.Id, out var configured) || !configured.Enabled)
            throw Fail(tenantId, "-", $"{provider.DisplayName} isn't enabled.");
        return new BusinessVerificationRoute(provider, Connect(tenantId, "-", provider, configured));
    }

    private VerificationProviderConnection Connect(string tenantId, string code, IBusinessVerificationProvider provider, VerificationProviderSettings configured)
    {
        var missing = provider.CredentialFields
            .Where(f => f.Required && (!configured.Credentials.TryGetValue(f.Name, out var value) || string.IsNullOrWhiteSpace(value)))
            .Select(f => f.Label)
            .ToList();
        if (missing.Count > 0)
            throw Fail(tenantId, code, $"{provider.DisplayName} is missing {string.Join(", ", missing)}.");

        var options = provider.Options.ToDictionary(
            o => o.Name,
            o => configured.Options.GetValueOrDefault(o.Name) ?? string.Empty,
            StringComparer.OrdinalIgnoreCase);
        var invalid = provider.Options.Where(o => !o.Choices.Contains(options[o.Name], StringComparer.OrdinalIgnoreCase)).Select(o => o.Label).ToList();
        if (invalid.Count > 0)
            throw Fail(tenantId, code, $"{provider.DisplayName} has no valid {string.Join(", ", invalid)} saved.");

        return new VerificationProviderConnection(
            tenantId,
            configured.Environment,
            new Dictionary<string, string>(configured.Credentials, StringComparer.OrdinalIgnoreCase),
            options);
    }

    private VerificationConfigurationException Fail(string tenantId, string country, string message)
    {
        _logger.LogError("Business verification for tenant {TenantId} in {Country} can't be routed: {Reason}", tenantId, country, message);
        return new VerificationConfigurationException(message);
    }
}

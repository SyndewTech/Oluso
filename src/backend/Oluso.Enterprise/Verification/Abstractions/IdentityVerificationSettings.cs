namespace Oluso.Enterprise.Verification;

/// <summary>
/// An organisation's verification vendors and how requests are routed to them. Stored
/// as the encrypted "IdentityVerification" tenant configuration section, because it
/// holds vendor secrets. Vendor credentials are shared by every capability (business
/// now, person checks later); each capability has its own country routing.
/// </summary>
public class IdentityVerificationSettings
{
    public const string SectionKey = "IdentityVerification";

    /// <summary>Configured vendors, keyed by <see cref="IVerificationProvider.Id"/>.</summary>
    public Dictionary<string, VerificationProviderSettings> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Country (ISO 3166-1 alpha-2) → provider id used for business lookups there.</summary>
    public Dictionary<string, string> BusinessRouting { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// How long, in days, a verified result for a registration number is reused instead
    /// of buying a new lookup. 0 always looks up again.
    /// </summary>
    public int BusinessReuseMaxAgeDays { get; set; }
}

public class VerificationProviderSettings
{
    public bool Enabled { get; set; }

    public VerificationEnvironment Environment { get; set; } = VerificationEnvironment.Sandbox;

    /// <summary>Credential values keyed by <see cref="VerificationCredentialField.Name"/>.</summary>
    public Dictionary<string, string> Credentials { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Provider-specific options keyed by <see cref="VerificationProviderOption.Name"/>.</summary>
    public Dictionary<string, string> Options { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public interface IIdentityVerificationSettingsService
{
    /// <summary>The tenant's saved settings, decrypted; an empty object when none are saved.</summary>
    Task<IdentityVerificationSettings> GetAsync(string tenantId, CancellationToken cancellationToken = default);

    Task SaveAsync(string tenantId, IdentityVerificationSettings settings, CancellationToken cancellationToken = default);
}

/// <summary>A business lookup's provider and the tenant's credentials for it.</summary>
public sealed record BusinessVerificationRoute(IBusinessVerificationProvider Provider, VerificationProviderConnection Connection);

/// <summary>
/// Picks the provider for a country from the tenant's routing. A country with no route,
/// a route to a disabled or unknown provider, or a provider that doesn't cover the
/// country is a configuration error, never a fallback to another provider.
/// </summary>
public interface IBusinessVerificationRouter
{
    /// <exception cref="VerificationConfigurationException">The tenant can't verify businesses in <paramref name="country"/>.</exception>
    Task<BusinessVerificationRoute> ResolveAsync(string tenantId, string country, CancellationToken cancellationToken = default);

    /// <summary>A provider the tenant has enabled, whatever it's routed for (webhooks, reopening sessions).</summary>
    /// <exception cref="VerificationConfigurationException">The provider isn't enabled or lacks required credentials.</exception>
    Task<BusinessVerificationRoute> ResolveProviderAsync(string tenantId, string providerId, CancellationToken cancellationToken = default);
}

using Oluso.Core.Domain.Interfaces;

namespace Oluso.Core.Terminology;

/// <summary>
/// The canonical, platform-owned list of tenant industries/types. Defined in
/// Core so the tenant "type" is a first-class platform setting — no module
/// owns or leaks this list. Modules map these values
/// to their own vocabulary bundles; the value itself is platform data.
/// </summary>
public static class IndustryCatalog
{
    public static readonly IReadOnlyList<IndustryOption> All = new[]
    {
        new IndustryOption("Healthcare", "Healthcare"),
        new IndustryOption("SocialCare", "Social Care (Care Home)"),
        new IndustryOption("Hospitality", "Hospitality"),
        new IndustryOption("Coworking", "Coworking"),
        new IndustryOption("CommercialOffice", "Commercial Office"),
        new IndustryOption("Education", "Education"),
        new IndustryOption("Retail", "Retail"),
        new IndustryOption("Manufacturing", "Manufacturing"),
        new IndustryOption("Residential", "Residential"),
        new IndustryOption("PropertyManagement", "Property Management"),
    };
}

/// <summary>
/// Reads/writes the tenant's industry/type — a central platform setting stored
/// in tenant configuration (<see cref="TerminologySections.TenantProfile"/>).
/// This is the single source of truth the terminology layer consumes; there is
/// no per-module fallback.
/// </summary>
public interface ITenantProfileService
{
    /// <summary>The tenant's industry plus the available choices.</summary>
    Task<TenantTerminologyProfile> GetAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>The tenant's industry value (null = generic).</summary>
    Task<string?> GetIndustryAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>Set the tenant's industry/type.</summary>
    Task SetIndustryAsync(string tenantId, string? industry, CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="ITenantProfileService"/> backed by
/// <see cref="ITenantConfigurationService"/>. Pure Core — depends only on Core
/// abstractions, so it carries no module coupling.
/// </summary>
public sealed class TenantProfileService : ITenantProfileService
{
    private readonly ITenantConfigurationService _config;

    public TenantProfileService(ITenantConfigurationService config)
    {
        _config = config;
    }

    public async Task<string?> GetIndustryAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        var section = await _config.GetSectionAsync<TenantProfileSettings>(
            tenantId, TerminologySections.TenantProfile, cancellationToken);
        return string.IsNullOrWhiteSpace(section?.Industry) ? null : section!.Industry;
    }

    public async Task<TenantTerminologyProfile> GetAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        var industry = await GetIndustryAsync(tenantId, cancellationToken);
        return new TenantTerminologyProfile(industry, IndustryCatalog.All);
    }

    public Task SetIndustryAsync(string tenantId, string? industry, CancellationToken cancellationToken = default)
    {
        var value = string.IsNullOrWhiteSpace(industry) ? null : industry.Trim();
        return _config.SetSectionAsync(
            tenantId, TerminologySections.TenantProfile,
            new TenantProfileSettings { Industry = value }, cancellationToken);
    }
}

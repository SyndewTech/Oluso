using Oluso.Core.Domain.Interfaces;

namespace Oluso.Enterprise.Verification.Services;

public sealed class IdentityVerificationSettingsService : IIdentityVerificationSettingsService
{
    private readonly ITenantConfigurationService _configuration;

    public IdentityVerificationSettingsService(ITenantConfigurationService configuration) => _configuration = configuration;

    public async Task<IdentityVerificationSettings> GetAsync(string tenantId, CancellationToken cancellationToken = default) =>
        await _configuration.GetSectionAsync<IdentityVerificationSettings>(tenantId, IdentityVerificationSettings.SectionKey, cancellationToken)
        ?? new IdentityVerificationSettings();

    public Task SaveAsync(string tenantId, IdentityVerificationSettings settings, CancellationToken cancellationToken = default) =>
        _configuration.SetSectionAsync(tenantId, IdentityVerificationSettings.SectionKey, settings, cancellationToken);
}

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Services;

/// <summary>
/// Resolves the issuer URI based on tenant context, configuration, and request.
/// Priority: Tenant IssuerUri > Tenant CustomDomain > Server config > Request host
/// </summary>
public class IssuerResolver : IIssuerResolver
{
    private readonly ITenantContext _tenantContext;
    private readonly ITenantSettingsProvider _tenantSettings;
    private readonly IConfiguration _configuration;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public IssuerResolver(
        ITenantContext tenantContext,
        ITenantSettingsProvider tenantSettings,
        IConfiguration configuration,
        IHttpContextAccessor httpContextAccessor)
    {
        _tenantContext = tenantContext;
        _tenantSettings = tenantSettings;
        _configuration = configuration;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<string> GetIssuerAsync(CancellationToken cancellationToken = default)
    {
        // 1. Check tenant-specific issuer override
        if (_tenantContext.HasTenant)
        {
            var tokenSettings = await _tenantSettings.GetTokenSettingsAsync(cancellationToken);
            if (!string.IsNullOrEmpty(tokenSettings.IssuerUri))
            {
                return tokenSettings.IssuerUri.TrimEnd('/');
            }

            // 2. Use tenant's custom domain if configured
            var tenant = _tenantContext.Tenant;
            if (!string.IsNullOrEmpty(tenant?.CustomDomain))
            {
                return $"https://{tenant.CustomDomain.TrimEnd('/')}";
            }
        }

        // 3. Use configured issuer URI
        var configuredIssuer = _configuration["Oluso:IssuerUri"];
        if (!string.IsNullOrEmpty(configuredIssuer))
        {
            return configuredIssuer.TrimEnd('/');
        }

        // 4. Fallback to request host
        var request = _httpContextAccessor.HttpContext?.Request;
        if (request != null)
        {
            return $"{request.Scheme}://{request.Host}".TrimEnd('/');
        }

        // Last resort - should not happen in normal operation
        return "https://localhost";
    }
}

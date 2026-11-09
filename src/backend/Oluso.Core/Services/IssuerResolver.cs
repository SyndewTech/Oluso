using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Core.Services;

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

        // 3. Construct subdomain-based issuer for the current tenant.
        if (_tenantContext.HasTenant)
        {
            var auto = ComputeAutoSubdomain(_tenantContext.Tenant);
            if (auto != null) return auto;
        }

        // 4. Use configured issuer URI (platform/no tenant)
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

    public async Task<bool> IsOwnIssuerAsync(string? issuer, Core.Domain.Entities.Tenant? tenant, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(issuer)) return false;
        var configuredBase = _configuration["Oluso:IssuerUri"]?.TrimEnd('/');
        var trimmed = issuer.TrimEnd('/');

        if (SameIssuer(trimmed, configuredBase) || IsSubdomainOf(trimmed, configuredBase)) return true;
        if (tenant is not { Enabled: true }) return false;

        if (SameIssuer(trimmed, await GetIssuerForTenantAsync(tenant, cancellationToken))) return true;

        // The issuer override is a tenant setting, readable only for the tenant in context.
        return _tenantContext.TenantId == tenant.Id
            && SameIssuer(trimmed, await GetIssuerAsync(cancellationToken));
    }

    private static bool SameIssuer(string issuer, string? other) =>
        other != null && string.Equals(issuer, other.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    /// <summary>A tenant subdomain of the configured base issuer, on the same scheme and port.</summary>
    private static bool IsSubdomainOf(string issuer, string? configuredBase)
    {
        if (string.IsNullOrEmpty(configuredBase)
            || !Uri.TryCreate(configuredBase, UriKind.Absolute, out var baseUri)
            || !Uri.TryCreate(issuer, UriKind.Absolute, out var issUri))
        {
            return false;
        }

        return string.Equals(issUri.Scheme, baseUri.Scheme, StringComparison.OrdinalIgnoreCase)
            && issUri.Port == baseUri.Port
            && issUri.Host.EndsWith("." + baseUri.Host, StringComparison.OrdinalIgnoreCase);
    }

    public Task<string> GetIssuerForTenantAsync(Core.Domain.Entities.Tenant tenant, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        if (!string.IsNullOrEmpty(tenant.CustomDomain))
        {
            return Task.FromResult($"https://{tenant.CustomDomain.TrimEnd('/')}");
        }

        var auto = ComputeAutoSubdomain(tenant);
        if (auto != null)
        {
            return Task.FromResult(auto);
        }

        var configuredIssuer = _configuration["Oluso:IssuerUri"];
        if (!string.IsNullOrEmpty(configuredIssuer))
        {
            return Task.FromResult(configuredIssuer.TrimEnd('/'));
        }

        var request = _httpContextAccessor.HttpContext?.Request;
        if (request != null)
        {
            return Task.FromResult($"{request.Scheme}://{request.Host}".TrimEnd('/'));
        }

        return Task.FromResult("https://localhost");
    }

    public async Task<ResolvedIssuer> GetResolvedIssuerAsync(CancellationToken cancellationToken = default)
    {
        var issuer = await GetIssuerAsync(cancellationToken);
        return new ResolvedIssuer
        {
            IssuerUri = issuer,
            AutoSubdomain = _tenantContext.HasTenant ? ComputeAutoSubdomain(_tenantContext.Tenant) : null
        };
    }

    public async Task<ResolvedIssuer> GetResolvedIssuerForTenantAsync(Core.Domain.Entities.Tenant tenant, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        var issuer = await GetIssuerForTenantAsync(tenant, cancellationToken);
        return new ResolvedIssuer
        {
            IssuerUri = issuer,
            AutoSubdomain = ComputeAutoSubdomain(tenant)
        };
    }

    public string BuildTokenIssuer(string? tenantId)
    {
        var configuredBase = _configuration["Oluso:IssuerUri"]?.TrimEnd('/');
        if (string.IsNullOrEmpty(configuredBase))
        {
            throw new InvalidOperationException("Oluso:IssuerUri is not configured");
        }
        if (string.IsNullOrEmpty(tenantId)
            || string.Equals(tenantId, Core.PlatformConstants.TenantId, StringComparison.OrdinalIgnoreCase))
        {
            return configuredBase;
        }
        return $"{configuredBase}/{tenantId}";
    }

    /// <summary>
    /// Builds the canonical "auto" subdomain URL for a tenant, ignoring
    /// CustomDomain. Used for hosted UIs, discovery doc URL/issuer field,
    /// callback URLs, and anywhere else where the tenant's public-facing
    /// host is needed.
    /// <list type="bullet">
    ///   <item>Non-platform tenant → <c>{scheme}://{identifier}.{base-host}</c></item>
    ///   <item>Platform tenant → the configured base IssuerUri itself</item>
    /// </list>
    /// Returns null when the tenant has no identifier or
    /// <c>Oluso:IssuerUri</c> isn't configured / doesn't parse.
    /// </summary>
    private string? ComputeAutoSubdomain(Core.Domain.Entities.Tenant? tenant)
    {
        if (tenant == null) return null;
        if (string.IsNullOrEmpty(tenant.Identifier)) return null;

        var configuredIssuer = _configuration["Oluso:IssuerUri"];
        if (string.IsNullOrEmpty(configuredIssuer)) return null;
        if (!Uri.TryCreate(configuredIssuer, UriKind.Absolute, out var issuerUri)) return null;

        if (tenant.Identifier == Core.PlatformConstants.TenantId)
        {
            return configuredIssuer.TrimEnd('/');
        }

        var hostWithPort = issuerUri.IsDefaultPort
            ? issuerUri.Host
            : $"{issuerUri.Host}:{issuerUri.Port}";

        return $"{issuerUri.Scheme}://{tenant.Identifier}.{hostWithPort}";
    }
}

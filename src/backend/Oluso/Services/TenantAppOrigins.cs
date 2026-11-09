using Microsoft.EntityFrameworkCore;
using Oluso.Core;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.EntityFramework;

namespace Oluso.Services;

public sealed class TenantAppOrigins : ITenantAppOrigins
{
    private readonly IOlusoDbContext _db;
    private readonly ITenantConfigurationService _configuration;

    public TenantAppOrigins(IOlusoDbContext db, ITenantConfigurationService configuration)
    {
        _db = db;
        _configuration = configuration;
    }

    public async Task<IReadOnlySet<string>> GetOriginsAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        var clients = _db.Clients.IgnoreQueryFilters()
            .Where(c => c.Enabled && (c.TenantId == tenantId || c.TenantId == PlatformConstants.TenantId));

        var addresses = new List<string?>();
        addresses.AddRange(await clients.Select(c => c.ClientUri).ToListAsync(cancellationToken));
        addresses.AddRange(await clients.SelectMany(c => c.RedirectUris).Select(r => r.RedirectUri).ToListAsync(cancellationToken));
        addresses.AddRange(await clients.SelectMany(c => c.PostLogoutRedirectUris).Select(r => r.PostLogoutRedirectUri).ToListAsync(cancellationToken));
        addresses.AddRange(await clients.SelectMany(c => c.AllowedCorsOrigins).Select(o => o.Origin).ToListAsync(cancellationToken));

        var cors = await _configuration.GetSectionAsync<TenantCorsSettings>(tenantId, TenantCorsSettings.SectionKey, cancellationToken);
        if (cors != null) addresses.AddRange(cors.AllowedOrigins);

        var origins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var address in addresses)
        {
            if (!string.IsNullOrWhiteSpace(address)
                && Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri)
                && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
            {
                origins.Add(uri.GetLeftPart(UriPartial.Authority));
            }
        }
        return origins;
    }
}

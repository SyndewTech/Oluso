using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Core.Services;

/// <summary>
/// AsyncLocal-based tenant context for request-scoped tenant isolation
/// </summary>
public class TenantContextAccessor : ITenantContextAccessor
{
    private static readonly AsyncLocal<TenantHolder> _tenantHolder = new();

    public string? TenantId => _tenantHolder.Value?.Tenant?.Id;

    public Tenant? Tenant => _tenantHolder.Value?.Tenant;

    public bool HasTenant => _tenantHolder.Value?.Tenant != null;

    public void SetTenant(Tenant tenant)
    {
        var holder = _tenantHolder.Value;
        if (holder != null)
        {
            holder.Tenant = null;
        }

        _tenantHolder.Value = new TenantHolder { Tenant = tenant };
    }

    public void ClearTenant()
    {
        var holder = _tenantHolder.Value;
        if (holder != null)
        {
            holder.Tenant = null;
        }
    }

    private class TenantHolder
    {
        public Tenant? Tenant { get; set; }
    }
}

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
        // A fresh holder for this async flow only. Nulling the previous holder
        // here would also clear it for every flow that shares it, so a
        // workflow step setting its tenant inside an HTTP request used to
        // strip the request's own tenant for the rest of the call.
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

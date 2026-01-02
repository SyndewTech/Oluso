using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Oluso.Core.Api;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Account.Controllers;

/// <summary>
/// Account API for multi-tenant user tenant management.
/// Allows users who belong to multiple tenants to view and switch contexts.
/// </summary>
[Route("api/account/tenants")]
public class TenantsController : AccountBaseController
{
    private readonly ITenantStore _tenantStore;
    private readonly ILogger<TenantsController> _logger;

    public TenantsController(
        ITenantContext tenantContext,
        ITenantStore tenantStore,
        ILogger<TenantsController> logger) : base(tenantContext)
    {
        _tenantStore = tenantStore;
        _logger = logger;
    }

    /// <summary>
    /// Get all tenants the current user belongs to
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<TenantListDto>> GetUserTenants(CancellationToken cancellationToken)
    {
        var tenantIds = GetUserTenantIds();
        var tenants = new List<UserTenantDto>();

        foreach (var tenantId in tenantIds)
        {
            var tenant = await _tenantStore.GetByIdAsync(tenantId, cancellationToken);
            if (tenant != null && tenant.Enabled)
            {
                tenants.Add(new UserTenantDto
                {
                    Id = tenant.Id,
                    Identifier = tenant.Identifier,
                    Name = tenant.Name,
                    DisplayName = tenant.DisplayName ?? tenant.Name,
                    LogoUrl = tenant.Branding?.LogoUrl,
                    IsCurrent = tenant.Id == TenantId
                });
            }
        }

        return Ok(new TenantListDto
        {
            Tenants = tenants,
            CurrentTenantId = TenantId,
            IsMultiTenant = tenants.Count > 1
        });
    }

    /// <summary>
    /// Get current tenant details
    /// </summary>
    [HttpGet("current")]
    public async Task<ActionResult<UserTenantDto>> GetCurrentTenant(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(TenantId))
        {
            return Ok(new UserTenantDto
            {
                Id = "default",
                Identifier = "default",
                Name = "Default",
                DisplayName = "Default Organization",
                IsCurrent = true
            });
        }

        var tenant = await _tenantStore.GetByIdAsync(TenantId, cancellationToken);
        if (tenant == null)
        {
            return NotFound(new { error = "Tenant not found" });
        }

        return Ok(new UserTenantDto
        {
            Id = tenant.Id,
            Identifier = tenant.Identifier,
            Name = tenant.Name,
            DisplayName = tenant.DisplayName ?? tenant.Name,
            LogoUrl = tenant.Branding?.LogoUrl,
            IsCurrent = true
        });
    }

    /// <summary>
    /// Validate that user can access a specific tenant.
    /// Used by frontend before switching tenant context.
    /// </summary>
    [HttpGet("{tenantId}/validate")]
    public async Task<ActionResult<TenantValidationDto>> ValidateTenantAccess(
        string tenantId,
        CancellationToken cancellationToken)
    {
        var userTenants = GetUserTenantIds();

        if (!userTenants.Contains(tenantId))
        {
            return Ok(new TenantValidationDto
            {
                IsValid = false,
                Error = "You do not have access to this organization"
            });
        }

        var tenant = await _tenantStore.GetByIdAsync(tenantId, cancellationToken);
        if (tenant == null)
        {
            return Ok(new TenantValidationDto
            {
                IsValid = false,
                Error = "Organization not found"
            });
        }

        if (!tenant.Enabled)
        {
            return Ok(new TenantValidationDto
            {
                IsValid = false,
                Error = "Organization is disabled"
            });
        }

        return Ok(new TenantValidationDto
        {
            IsValid = true,
            Tenant = new UserTenantDto
            {
                Id = tenant.Id,
                Identifier = tenant.Identifier,
                Name = tenant.Name,
                DisplayName = tenant.DisplayName ?? tenant.Name,
                LogoUrl = tenant.Branding?.LogoUrl,
                IsCurrent = tenant.Id == TenantId
            }
        });
    }
}

#region DTOs

public class TenantListDto
{
    public List<UserTenantDto> Tenants { get; set; } = new();
    public string? CurrentTenantId { get; set; }
    public bool IsMultiTenant { get; set; }
}

public class UserTenantDto
{
    public string Id { get; set; } = null!;
    public string Identifier { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? LogoUrl { get; set; }
    public bool IsCurrent { get; set; }
}

public class TenantValidationDto
{
    public bool IsValid { get; set; }
    public string? Error { get; set; }
    public UserTenantDto? Tenant { get; set; }
}

#endregion

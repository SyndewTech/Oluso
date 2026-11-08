using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;

namespace Oluso.Jobs;

/// <summary>
/// Rechecks active custom domains every 30 days to confirm TXT record still exists.
/// If TXT is missing, starts a 7-day grace period. If still missing after grace, deactivates.
/// Runs once per hour (checks are cheap — only processes domains past their 30-day window).
/// </summary>
public class DomainRevalidationBackgroundService : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    private static readonly TimeSpan RevalidationPeriod = TimeSpan.FromDays(30);
    private static readonly TimeSpan GracePeriod = TimeSpan.FromDays(7);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DomainRevalidationBackgroundService> _logger;
    private readonly IOptions<OlusoOptions> _options;

    public DomainRevalidationBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<DomainRevalidationBackgroundService> logger,
        IOptions<OlusoOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.RunDomainChecksInProcess)
        {
            _logger.LogInformation("In-process domain checks are off (Oluso:RunDomainChecksInProcess); another instance runs them");
            return;
        }

        _logger.LogInformation("Domain revalidation background service started (30-day cycle, 7-day grace)");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RevalidateActiveDomainsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in domain revalidation background service");
            }

            try
            {
                await RevalidateEmailDomainsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error revalidating email domains");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task RevalidateActiveDomainsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ICustomDomainVerificationStore>();
        var dnsService = scope.ServiceProvider.GetRequiredService<IDnsVerificationService>();
        var tenantStore = scope.ServiceProvider.GetRequiredService<ITenantStore>();
        var tenantAccessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
        var cacheInvalidator = scope.ServiceProvider.GetService<IHostValidationCacheInvalidator>();
        var provisioning = scope.ServiceProvider.GetService<ICustomDomainProvisioningService>();

        var cutoff = DateTime.UtcNow - RevalidationPeriod;
        var domains = await store.GetActiveDomainsForRevalidationAsync(cutoff, ct);
        var tenantCache = new Dictionary<string, Tenant?>(StringComparer.Ordinal);

        foreach (var domain in domains)
        {
            if (ct.IsCancellationRequested) break;

            // Establish tenant scope so audit logs and downstream writes are
            // attributed to the domain's tenant rather than landing as null
            // / _platform.
            if (!tenantCache.TryGetValue(domain.TenantId, out var tenant))
            {
                tenant = await tenantStore.GetByIdAsync(domain.TenantId);
                tenantCache[domain.TenantId] = tenant;
            }
            if (tenant != null)
            {
                tenantAccessor.SetTenant(tenant);
            }

            try
            {
                var result = await dnsService.VerifyDomainAsync(domain, ct);
                domain.LastRevalidatedAt = DateTime.UtcNow;

                if (result.Success)
                {
                    // TXT still present — clear any grace period
                    if (domain.GracePeriodStartedAt != null)
                    {
                        _logger.LogInformation("Domain {Domain} TXT record restored, grace period cleared", domain.Domain);
                        domain.GracePeriodStartedAt = null;
                    }
                    domain.FailureReason = null;
                }
                else if (domain.GracePeriodStartedAt == null)
                {
                    // First failure — start grace period
                    domain.GracePeriodStartedAt = DateTime.UtcNow;
                    domain.FailureReason = $"TXT record missing during revalidation. Grace period ends {DateTime.UtcNow.Add(GracePeriod):yyyy-MM-dd}";
                    _logger.LogWarning("Domain {Domain} TXT record missing — 7-day grace period started for tenant {TenantId}",
                        domain.Domain, domain.TenantId);
                }
                else if (DateTime.UtcNow - domain.GracePeriodStartedAt > GracePeriod)
                {
                    // Grace period expired — deactivate
                    _logger.LogWarning("Domain {Domain} deactivated — TXT record missing for 7+ days, tenant {TenantId}",
                        domain.Domain, domain.TenantId);

                    if (provisioning != null)
                    {
                        await provisioning.DeprovisionAsync(domain.Domain, ct);
                    }

                    domain.Status = CustomDomainStatus.Failed;
                    domain.FailureReason = "Deactivated: TXT record not found during 30-day revalidation after 7-day grace period";

                    if (tenant != null && tenant.CustomDomain == domain.Domain)
                    {
                        tenant.CustomDomain = null;
                        tenant.Updated = DateTime.UtcNow;
                        await tenantStore.UpdateAsync(tenant);
                        if (cacheInvalidator != null) await cacheInvalidator.InvalidateCacheAsync(ct);
                    }
                }

                await store.UpdateAsync(domain, ct);
            }
            finally
            {
                tenantAccessor.ClearTenant();
            }
        }
    }

    /// <summary>
    /// Verified email domains, on the same cycle: a TXT record missing past the grace period fails the domain, and
    /// addresses there are no longer trusted on the domain's word.
    /// </summary>
    private async Task RevalidateEmailDomainsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ITenantEmailDomainStore>();
        var dnsService = scope.ServiceProvider.GetRequiredService<IDnsVerificationService>();
        var tenantStore = scope.ServiceProvider.GetRequiredService<ITenantStore>();
        var tenantAccessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();

        var domains = await store.GetVerifiedForRevalidationAsync(DateTime.UtcNow - RevalidationPeriod, ct);
        var tenantCache = new Dictionary<string, Tenant?>(StringComparer.Ordinal);

        foreach (var domain in domains)
        {
            if (ct.IsCancellationRequested) break;

            if (!tenantCache.TryGetValue(domain.TenantId, out var tenant))
            {
                tenant = await tenantStore.GetByIdAsync(domain.TenantId);
                tenantCache[domain.TenantId] = tenant;
            }
            if (tenant != null)
            {
                tenantAccessor.SetTenant(tenant);
            }

            try
            {
                var result = await dnsService.VerifyTxtRecordAsync(domain.TxtRecordName, domain.TxtRecordValue, ct);
                domain.LastRevalidatedAt = DateTime.UtcNow;

                if (result.Success)
                {
                    domain.GracePeriodStartedAt = null;
                    domain.FailureReason = null;
                }
                else if (domain.GracePeriodStartedAt == null)
                {
                    domain.GracePeriodStartedAt = DateTime.UtcNow;
                    domain.FailureReason = $"TXT record missing during revalidation. Grace period ends {DateTime.UtcNow.Add(GracePeriod):yyyy-MM-dd}";
                    _logger.LogWarning("Email domain {Domain} TXT record missing; grace period started for tenant {TenantId}",
                        domain.Domain, domain.TenantId);
                }
                else if (DateTime.UtcNow - domain.GracePeriodStartedAt > GracePeriod)
                {
                    domain.Status = TenantEmailDomainStatus.Failed;
                    domain.FailureReason = "No longer verified: TXT record missing after the grace period";
                    _logger.LogWarning("Email domain {Domain} no longer verified for tenant {TenantId}", domain.Domain, domain.TenantId);
                }

                await store.UpdateAsync(domain, ct);
            }
            finally
            {
                tenantAccessor.ClearTenant();
            }
        }
    }
}

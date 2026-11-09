using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;

namespace Oluso.Jobs;

/// <summary>
/// Background service that periodically checks pending domain verifications.
/// Runs every 5 minutes. Auto-activates verified domains.
/// Marks as failed after 288 attempts (24 hours at 5-minute intervals).
/// </summary>
public class DomainVerificationBackgroundService : BackgroundService
{
    private const int MaxAttempts = 288; // 24 hours at 5-minute intervals
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<DomainVerificationBackgroundService> _logger;
    private readonly IOptions<OlusoOptions> _options;

    public DomainVerificationBackgroundService(
        IServiceScopeFactory scopeFactory,
        ILogger<DomainVerificationBackgroundService> logger,
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

        _logger.LogInformation("Domain verification background service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingVerificationsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in domain verification background service");
            }

            try
            {
                await ProcessPendingEmailDomainsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking pending email domains");
            }

            await Task.Delay(Interval, stoppingToken);
        }
    }

    private async Task ProcessPendingVerificationsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ICustomDomainVerificationStore>();
        var dnsService = scope.ServiceProvider.GetRequiredService<IDnsVerificationService>();
        var tenantStore = scope.ServiceProvider.GetRequiredService<ITenantStore>();
        var tenantAccessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
        var activation = scope.ServiceProvider.GetRequiredService<ICustomDomainActivationService>();

        var pending = await store.GetPendingVerificationsAsync(ct);
        var count = 0;
        var tenantCache = new Dictionary<string, Tenant?>(StringComparer.Ordinal);

        foreach (var verification in pending)
        {
            if (ct.IsCancellationRequested) break;

            // Establish tenant scope for this iteration so audit logs and any
            // downstream writes get attributed to the verification's tenant.
            if (!tenantCache.TryGetValue(verification.TenantId, out var tenant))
            {
                tenant = await tenantStore.GetByIdAsync(verification.TenantId);
                tenantCache[verification.TenantId] = tenant;
            }
            if (tenant != null)
            {
                tenantAccessor.SetTenant(tenant);
            }

            try
            {
                verification.LastCheckedAt = DateTime.UtcNow;
                verification.CheckAttempts++;

                // Already-verified records are waiting on the edge provider,
                // not on DNS. Re-checking TXT would only re-prove ownership
                // that's already established — poll the edge instead, which
                // is what actually finalises them.
                if (verification.Status == CustomDomainStatus.Verified)
                {
                    var outcome = await activation.TryActivateAsync(verification, tenant, ct);
                    if (outcome == CustomDomainActivationOutcome.Activated)
                    {
                        _logger.LogInformation("Domain {Domain} activated for tenant {TenantId}",
                            verification.Domain, verification.TenantId);
                        count++;
                    }
                    else if (verification.CheckAttempts >= MaxAttempts)
                    {
                        verification.Status = CustomDomainStatus.Failed;
                        verification.FailureReason =
                            $"Edge provisioning did not complete after {MaxAttempts} attempts. " +
                            $"Last state: {verification.FailureReason}";
                        _logger.LogWarning(
                            "Domain {Domain} failed after {Attempts} edge attempts for tenant {TenantId}",
                            verification.Domain, verification.CheckAttempts, verification.TenantId);
                    }

                    await store.UpdateAsync(verification, ct);
                    continue;
                }

                var result = await dnsService.VerifyDomainAsync(verification, ct);

                if (result.Success)
                {
                    verification.Status = CustomDomainStatus.Verified;
                    verification.VerifiedAt = DateTime.UtcNow;
                    verification.FailureReason = null;

                    var outcome = await activation.TryActivateAsync(verification, tenant, ct);
                    if (outcome == CustomDomainActivationOutcome.Activated)
                    {
                        _logger.LogInformation("Domain {Domain} auto-verified and activated for tenant {TenantId}",
                            verification.Domain, verification.TenantId);
                        count++;
                    }
                    else
                    {
                        _logger.LogInformation(
                            "Domain {Domain} verified for tenant {TenantId}; awaiting edge ({Reason})",
                            verification.Domain, verification.TenantId, verification.FailureReason);
                    }
                }
                else if (verification.CheckAttempts >= MaxAttempts)
                {
                    verification.Status = CustomDomainStatus.Failed;
                    verification.FailureReason = $"Verification timed out after {MaxAttempts} attempts. Last error: {result.FailureReason}";
                    _logger.LogWarning("Domain {Domain} verification failed after {Attempts} attempts for tenant {TenantId}",
                        verification.Domain, verification.CheckAttempts, verification.TenantId);
                }
                else
                {
                    verification.FailureReason = result.FailureReason;
                }

                await store.UpdateAsync(verification, ct);
            }
            finally
            {
                tenantAccessor.ClearTenant();
            }
        }

        if (count > 0)
        {
            _logger.LogInformation("Domain verification job: {Activated} domains activated, {Total} checked",
                count, pending.Count());
        }
    }

    /// <summary>
    /// Pending email domains: verified once their TXT record is found, failed after <see
    /// cref="TenantEmailDomainVerification.MaxAttempts"/> checks.
    /// </summary>
    private async Task ProcessPendingEmailDomainsAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<ITenantEmailDomainStore>();
        var dnsService = scope.ServiceProvider.GetRequiredService<IDnsVerificationService>();
        var tenantStore = scope.ServiceProvider.GetRequiredService<ITenantStore>();
        var tenantAccessor = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();

        var pending = await store.GetPendingAsync(ct);
        var tenantCache = new Dictionary<string, Tenant?>(StringComparer.Ordinal);

        foreach (var claim in pending)
        {
            if (ct.IsCancellationRequested) break;

            if (!tenantCache.TryGetValue(claim.TenantId, out var tenant))
            {
                tenant = await tenantStore.GetByIdAsync(claim.TenantId);
                tenantCache[claim.TenantId] = tenant;
            }
            if (tenant != null)
            {
                tenantAccessor.SetTenant(tenant);
            }

            try
            {
                await TenantEmailDomainVerification.CheckAsync(claim, store, dnsService, ct);
                if (claim.Status == TenantEmailDomainStatus.Verified)
                {
                    _logger.LogInformation("Email domain {Domain} verified for tenant {TenantId}", claim.Domain, claim.TenantId);
                }
                else if (claim.CheckAttempts >= TenantEmailDomainVerification.MaxAttempts)
                {
                    claim.Status = TenantEmailDomainStatus.Failed;
                    claim.FailureReason =
                        $"Verification timed out after {TenantEmailDomainVerification.MaxAttempts} attempts. Last error: {claim.FailureReason}";
                    _logger.LogWarning("Email domain {Domain} verification failed for tenant {TenantId}", claim.Domain, claim.TenantId);
                }

                await store.UpdateAsync(claim, ct);
            }
            finally
            {
                tenantAccessor.ClearTenant();
            }
        }
    }
}

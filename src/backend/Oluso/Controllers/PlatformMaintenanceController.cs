using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Oluso.Core.Events;
using Oluso.Core.Services;

namespace Oluso.Controllers;

/// <summary>
/// Maintenance passes that must run inside the platform host because they use
/// its secrets and key material (Key Vault, data protection). A scheduler
/// without that access — the Azure Functions timer in
/// <c>Oluso.Functions.Common</c> — calls back in here instead of doing the work
/// itself. Authenticated with the shared callback key
/// (<c>Oluso:Maintenance:ApiKey</c>, falling back to <c>EventRelay:ApiKey</c>);
/// requests are refused until one is configured.
/// </summary>
[ApiController]
[Route("api/system/maintenance")]
public class PlatformMaintenanceController : ControllerBase
{
    public const string ApiKeyHeader = "X-Api-Key";
    public const string ApiKeyConfigKey = "Oluso:Maintenance:ApiKey";
    public const string FallbackApiKeyConfigKey = "EventRelay:ApiKey";

    private readonly ISigningKeyService _signingKeyService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<PlatformMaintenanceController> _logger;
    private readonly Oluso.Core.Services.IExpiredRecordsPurger? _purger;
    private readonly IWebhookSubscriptionStore? _webhookStore;
    private readonly IWebhookDispatcher? _webhookDispatcher;
    private readonly Oluso.Core.Domain.Interfaces.ITenantContextAccessor? _tenantAccessor;
    private readonly Oluso.Core.Domain.Interfaces.ITenantStore? _tenantStore;

    public PlatformMaintenanceController(
        ISigningKeyService signingKeyService,
        IConfiguration configuration,
        ILogger<PlatformMaintenanceController> logger,
        Oluso.Core.Services.IExpiredRecordsPurger? purger = null,
        IWebhookSubscriptionStore? webhookStore = null,
        IWebhookDispatcher? webhookDispatcher = null,
        Oluso.Core.Domain.Interfaces.ITenantContextAccessor? tenantAccessor = null,
        Oluso.Core.Domain.Interfaces.ITenantStore? tenantStore = null)
    {
        _signingKeyService = signingKeyService;
        _configuration = configuration;
        _logger = logger;
        _purger = purger;
        _webhookStore = webhookStore;
        _webhookDispatcher = webhookDispatcher;
        _tenantAccessor = tenantAccessor;
        _tenantStore = tenantStore;
    }

    private IActionResult? CheckCallbackKey()
    {
        var expectedKey = _configuration[ApiKeyConfigKey] ?? _configuration[FallbackApiKeyConfigKey];
        if (string.IsNullOrWhiteSpace(expectedKey))
        {
            _logger.LogWarning("Maintenance callback refused: no {ConfigKey} (or {Fallback}) configured", ApiKeyConfigKey, FallbackApiKeyConfigKey);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Maintenance callback key is not configured" });
        }

        var providedKey = Request.Headers[ApiKeyHeader].FirstOrDefault()
            ?? Request.Headers["X-Event-Relay-Key"].FirstOrDefault();
        return string.Equals(providedKey, expectedKey, StringComparison.Ordinal) ? null : Unauthorized();
    }

    /// <summary>
    /// Sweep expired persisted grants, server-side sessions and pushed authorization
    /// requests. Also runs as part of the signing-key pass, so the existing hourly
    /// callback covers it; this endpoint exists for an explicit or ad-hoc sweep.
    /// </summary>
    [HttpPost("expired-records")]
    public async Task<IActionResult> PurgeExpiredRecords(CancellationToken cancellationToken)
    {
        if (CheckCallbackKey() is { } refused) return refused;
        if (_purger is null) return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Purger is not registered" });

        var result = await _purger.PurgeAsync(cancellationToken);
        return Ok(new { ranAt = DateTimeOffset.UtcNow, purged = result });
    }

    /// <summary>
    /// Retry a queued webhook delivery. Endpoint secrets and credentials are decrypted
    /// here, so the function app draining the retry queue hands each message back
    /// rather than sending it itself. A delivery that already succeeded, ran out of
    /// retries or was cancelled is left alone, so a repeated message sends nothing.
    /// A failed attempt is queued again by the dispatcher.
    /// </summary>
    [HttpPost("webhook-retries")]
    public async Task<IActionResult> RetryWebhookDelivery([FromBody] WebhookRetryMessage message, CancellationToken cancellationToken)
    {
        if (CheckCallbackKey() is { } refused) return refused;
        if (_webhookStore is null || _webhookDispatcher is null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Webhooks are not registered" });

        // Deliveries of every tenant come through here; the host may have resolved one.
        _tenantAccessor?.ClearTenant();
        var delivery = await _webhookStore.GetDeliveryAsync(message.DeliveryId, cancellationToken);
        if (delivery is null) return Ok(new { retried = false, reason = "not_found" });
        if (delivery.Status != WebhookDeliveryStatus.Failed)
            return Ok(new { retried = false, reason = delivery.Status.ToString() });

        if (_tenantAccessor != null && _tenantStore != null && !string.IsNullOrEmpty(delivery.TenantId)
            && await _tenantStore.GetByIdAsync(delivery.TenantId, cancellationToken) is { } tenant)
        {
            _tenantAccessor.SetTenant(tenant);
        }

        var success = await _webhookDispatcher.RetryDeliveryAsync(message.DeliveryId, cancellationToken);
        return Ok(new { retried = true, success });
    }

    /// <summary>
    /// Run the signing key maintenance pass: mark keys past expiry, rotate ahead
    /// of expiry per tenant configuration, and clean up keys past their grace
    /// period. Idempotent, so overlapping callers are harmless.
    /// </summary>
    [HttpPost("signing-keys")]
    public async Task<IActionResult> MaintainSigningKeys(CancellationToken cancellationToken)
    {
        var expectedKey = _configuration[ApiKeyConfigKey] ?? _configuration[FallbackApiKeyConfigKey];
        if (string.IsNullOrWhiteSpace(expectedKey))
        {
            _logger.LogWarning("Maintenance callback refused: no {ConfigKey} (or {Fallback}) configured", ApiKeyConfigKey, FallbackApiKeyConfigKey);
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Maintenance callback key is not configured" });
        }

        var providedKey = Request.Headers[ApiKeyHeader].FirstOrDefault()
            ?? Request.Headers["X-Event-Relay-Key"].FirstOrDefault();
        if (!string.Equals(providedKey, expectedKey, StringComparison.Ordinal))
        {
            return Unauthorized();
        }

        var stopwatch = Stopwatch.StartNew();
        await _signingKeyService.ProcessScheduledRotationsAsync(cancellationToken);
        var purged = _purger is null ? null : await _purger.PurgeAsync(cancellationToken);
        stopwatch.Stop();

        _logger.LogInformation("Signing key maintenance pass completed via callback in {ElapsedMs} ms", stopwatch.ElapsedMilliseconds);
        return Ok(new { ranAt = DateTimeOffset.UtcNow, durationMs = stopwatch.ElapsedMilliseconds, purged });
    }
}

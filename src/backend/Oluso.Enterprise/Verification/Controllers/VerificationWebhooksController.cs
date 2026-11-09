using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;

namespace Oluso.Enterprise.Verification.Controllers;

/// <summary>
/// Verification providers' webhooks. An organisation gives its provider
/// <c>https://{its host}/api/verification/webhooks/{provider}</c> (its subdomain or custom
/// domain); the tenant middleware resolves the organisation from that host. A request is
/// accepted only when the provider's signature verifies with that organisation's webhook
/// secret. The webhook only names the session: its outcome is then read from the
/// provider's API, and a journey waiting on it is told to refresh.
/// </summary>
[ApiController]
[Route(RoutePrefix + "/{providerId}")]
[AllowAnonymous]
public class VerificationWebhooksController : ControllerBase
{
    public const string RoutePrefix = "/api/verification/webhooks";
    private const int MaxBodyBytes = 1024 * 1024;

    private readonly IBusinessVerificationRouter _router;
    private readonly IBusinessVerificationService _verifications;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<VerificationWebhooksController> _logger;

    public VerificationWebhooksController(
        IBusinessVerificationRouter router,
        IBusinessVerificationService verifications,
        ITenantContext tenantContext,
        ILogger<VerificationWebhooksController> logger)
    {
        _router = router;
        _verifications = verifications;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    [HttpPost]
    [RequestSizeLimit(MaxBodyBytes)]
    public async Task<IActionResult> Receive(string providerId, CancellationToken cancellationToken)
    {
        var tenantId = AddressedTenantId();
        if (tenantId == null) return NotFound();

        BusinessVerificationRoute route;
        try
        {
            route = await _router.ResolveProviderAsync(tenantId, providerId, cancellationToken);
        }
        catch (VerificationConfigurationException ex)
        {
            _logger.LogWarning("Verification webhook for {Provider} on tenant {TenantId} refused: {Reason}", providerId, tenantId, ex.Message);
            return NotFound();
        }
        if (route.Provider is not IHostedBusinessVerificationProvider hosted) return NotFound();

        byte[] body;
        using (var buffer = new MemoryStream())
        {
            await Request.Body.CopyToAsync(buffer, cancellationToken);
            body = buffer.ToArray();
        }
        if (body.Length == 0) return BadRequest();

        var headers = Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString(), StringComparer.OrdinalIgnoreCase);
        var webhook = hosted.ReadWebhook(route.Connection, headers, body);
        if (!webhook.Authentic)
        {
            _logger.LogWarning("Verification webhook for {Provider} on tenant {TenantId} rejected: {Reason}", providerId, tenantId, webhook.Message);
            return Unauthorized();
        }
        if (webhook.SessionReference == null)
        {
            _logger.LogInformation("Verification webhook for {Provider} on tenant {TenantId} names no session: {Reason}", providerId, tenantId, webhook.Message);
            return Ok();
        }

        using var origin = EventOriginScope.Begin(EventOrigin.Integration($"verification:{route.Provider.Id}", route.Provider.DisplayName,
            HttpContext.Connection.RemoteIpAddress?.ToString()));
        var view = await _verifications.CompleteHostedSessionAsync(tenantId, route.Provider.Id, webhook.SessionReference, cancellationToken);
        if (view == null)
            _logger.LogInformation("Verification webhook for {Provider} on tenant {TenantId} names unknown session {Reference}", providerId, tenantId, webhook.SessionReference);
        return Ok();
    }

    /// <summary>The tenant the host resolved, or null when the request landed on the platform host.</summary>
    private string? AddressedTenantId()
    {
        if (!_tenantContext.HasTenant || string.IsNullOrEmpty(_tenantContext.TenantId)) return null;
        return string.Equals(_tenantContext.TenantId, Oluso.Core.PlatformConstants.TenantId, StringComparison.Ordinal)
            ? null
            : _tenantContext.TenantId;
    }
}

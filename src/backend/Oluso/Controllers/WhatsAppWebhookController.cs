using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Messaging.WhatsApp;

namespace Oluso.Controllers;

/// <summary>
/// Meta webhook for the WhatsApp product. A tenant registers
/// <c>https://{slug}.{base-domain}/api/webhooks/whatsapp</c> (or its custom
/// domain); the tenant middleware resolves the tenant from that host like it
/// does for every other request, and the request is accepted only when the
/// X-Hub-Signature-256 HMAC verifies with that tenant's app secret. A request
/// that reaches the platform host, where the middleware falls back to the
/// platform tenant, is not addressed to anyone and is refused.
/// </summary>
[ApiController]
[Route("api/webhooks/whatsapp")]
[AllowAnonymous]
public class WhatsAppWebhookController : ControllerBase
{
    private readonly IWhatsAppSettingsResolver _settings;
    private readonly IWhatsAppWebhookProcessor _processor;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<WhatsAppWebhookController> _logger;

    public WhatsAppWebhookController(
        IWhatsAppSettingsResolver settings,
        IWhatsAppWebhookProcessor processor,
        ITenantContext tenantContext,
        ILogger<WhatsAppWebhookController> logger)
    {
        _settings = settings;
        _processor = processor;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>Subscription handshake: echo hub.challenge when the verify token matches the tenant's.</summary>
    [HttpGet]
    public async Task<IActionResult> Verify(
        [FromQuery(Name = "hub.mode")] string? mode,
        [FromQuery(Name = "hub.verify_token")] string? verifyToken,
        [FromQuery(Name = "hub.challenge")] string? challenge,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(mode, "subscribe", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(challenge))
            return BadRequest();

        var tenantId = AddressedTenantId();
        if (tenantId == null) return NotFound();

        var settings = await _settings.ResolveAsync(tenantId, cancellationToken);
        var expected = settings?.WebhookVerifyToken;
        if (string.IsNullOrEmpty(expected) || string.IsNullOrEmpty(verifyToken) || !FixedTimeEquals(expected, verifyToken))
        {
            _logger.LogWarning("WhatsApp webhook verification rejected for tenant {TenantId}", tenantId);
            return Forbid();
        }

        return Content(challenge, "text/plain");
    }

    [HttpPost]
    public async Task<IActionResult> Receive(CancellationToken cancellationToken)
    {
        var tenantId = AddressedTenantId();
        if (tenantId == null) return NotFound();

        byte[] bodyBytes;
        using (var buffer = new MemoryStream())
        {
            await Request.Body.CopyToAsync(buffer, cancellationToken);
            bodyBytes = buffer.ToArray();
        }
        if (bodyBytes.Length == 0) return BadRequest();

        var settings = await _settings.ResolveAsync(tenantId, cancellationToken);
        var signature = Request.Headers["X-Hub-Signature-256"].FirstOrDefault();
        if (!IWhatsAppWebhookProcessor.SignatureIsValid(settings?.AppSecret ?? string.Empty, bodyBytes, signature))
        {
            _logger.LogWarning("WhatsApp webhook signature invalid for tenant {TenantId}", tenantId);
            return Unauthorized();
        }

        JsonDocument doc;
        try { doc = JsonDocument.Parse(bodyBytes); }
        catch (JsonException) { return BadRequest(); }

        using (doc)
        {
            var root = doc.RootElement;
            var totalErrors = 0;

            // Account-level notices (template status changes) carry no phone
            // number id; only message and status changes are processed.
            foreach (var phoneNumberId in IWhatsAppWebhookProcessor.ExtractPhoneNumberIds(root))
            {
                if (settings != null && !string.Equals(phoneNumberId, settings.PhoneNumberId, StringComparison.Ordinal))
                {
                    _logger.LogWarning("WhatsApp webhook for tenant {TenantId} carried phone number id {PhoneNumberId}, not the tenant's configured number; ignored",
                        tenantId, phoneNumberId);
                    continue;
                }

                var result = await _processor.ProcessAsync(tenantId, phoneNumberId, root, cancellationToken);
                totalErrors += result.Errors;
                _logger.LogInformation(
                    "WhatsApp webhook for tenant {TenantId}: {Messages} message(s), {Statuses} status(es), {Duplicates} duplicate(s), {Errors} error(s)",
                    tenantId, result.Messages, result.Statuses, result.Duplicates, result.Errors);
            }

            // A processing error means Meta should redeliver; already-stored
            // message ids are skipped on the retry.
            return totalErrors > 0 ? StatusCode(500) : Ok();
        }
    }

    /// <summary>The tenant the host resolved, or null when the request landed on the platform host.</summary>
    private string? AddressedTenantId()
    {
        if (!_tenantContext.HasTenant || string.IsNullOrEmpty(_tenantContext.TenantId)) return null;
        return string.Equals(_tenantContext.TenantId, Oluso.Core.PlatformConstants.TenantId, StringComparison.Ordinal)
            ? null
            : _tenantContext.TenantId;
    }

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}

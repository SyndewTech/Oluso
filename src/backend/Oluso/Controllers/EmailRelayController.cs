using System.Net.Mail;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;

namespace Oluso.Controllers;

/// <summary>
/// Renders a named email template for a tenant and sends it through the tenant's sender,
/// for standalone apps that hold no mail credentials or email copy. Guarded by the relay key.
/// </summary>
[ApiController]
[Route(PlatformEmailRelayRequest.Route)]
public class EmailRelayController : ControllerBase
{
    private readonly IEmailSender _emailSender;
    private readonly IPlatformEmailTemplateService? _templates;
    private readonly ITenantStore _tenantStore;
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailRelayController> _logger;

    public EmailRelayController(
        IEmailSender emailSender,
        ITenantStore tenantStore,
        IConfiguration configuration,
        ILogger<EmailRelayController> logger,
        IPlatformEmailTemplateService? templates = null)
    {
        _templates = templates;
        _emailSender = emailSender;
        _tenantStore = tenantStore;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> Send([FromBody] PlatformEmailRelayRequest request, CancellationToken cancellationToken)
    {
        if (RelayKey.Check(this, _configuration, _logger) is { } refused)
            return refused;

        if (!MailAddress.TryCreate(request.To, out _))
            return BadRequest(new { message = "To is not a valid email address" });
        if (string.IsNullOrWhiteSpace(request.TemplateName))
            return BadRequest(new { message = "TemplateName is required" });
        if (await _tenantStore.GetByIdAsync(request.TenantId, cancellationToken) is null)
            return BadRequest(new { message = "Unknown tenant" });
        if (_templates == null)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { message = "Email templates are not available on this host" });

        var rendered = await _templates.RenderForTenantAsync(
            request.TenantId, request.TemplateName, request.Values.ToDictionary(kv => kv.Key, kv => (object)kv.Value), cancellationToken);
        if (rendered == null)
            return NotFound(new { message = $"No active email template '{request.TemplateName}'" });

        var result = await _emailSender.SendAsync(request.TenantId, new EmailMessage
        {
            To = new[] { request.To },
            Subject = rendered.Subject,
            HtmlBody = rendered.HtmlBody,
            TextBody = rendered.TextBody,
        }, cancellationToken);

        if (!result.Success)
        {
            _logger.LogWarning("Relayed email for tenant {TenantId} failed: {Error}", request.TenantId, result.Error);
            return StatusCode(StatusCodes.Status502BadGateway, new { message = result.Error });
        }

        return Ok(new { messageId = result.MessageId });
    }
}

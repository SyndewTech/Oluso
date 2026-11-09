using System.Text.Json;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Messaging.WhatsApp;

namespace Oluso.Messaging;

/// <summary>
/// WhatsApp Business (Meta Cloud API) sender. Tenant-explicit: credentials are
/// resolved per call from the tenant's encrypted settings (with legacy and
/// host fallbacks), the recipient is normalised to E.164, opted-out contacts
/// are refused, free-form text is refused outside the 24-hour window, and
/// Graph calls retry on transient failures with Meta's error decoded.
/// </summary>
public class MetaWhatsAppSender : IWhatsAppSender
{
    private readonly MetaGraphClient _graph;
    private readonly IWhatsAppSettingsResolver _settings;
    private readonly IWhatsAppContactService _contacts;
    private readonly IWhatsAppContactStore _contactStore;
    private readonly IWhatsAppTemplateStore _templates;
    private readonly ILogger<MetaWhatsAppSender> _logger;

    public MetaWhatsAppSender(
        MetaGraphClient graph,
        IWhatsAppSettingsResolver settings,
        IWhatsAppContactService contacts,
        IWhatsAppContactStore contactStore,
        IWhatsAppTemplateStore templates,
        ILogger<MetaWhatsAppSender> logger)
    {
        _graph = graph;
        _settings = settings;
        _contacts = contacts;
        _contactStore = contactStore;
        _templates = templates;
        _logger = logger;
    }

    public async Task<bool> IsConfiguredAsync(string tenantId, CancellationToken cancellationToken = default) =>
        await _settings.ResolveAsync(tenantId, cancellationToken) != null;

    public async Task<bool> CanSendFreeFormAsync(string tenantId, string phoneNumber, CancellationToken cancellationToken = default)
    {
        var settings = await _settings.ResolveAsync(tenantId, cancellationToken);
        if (settings == null) return false;
        if (!_contacts.TryNormalize(phoneNumber, settings.DefaultCountry, out var e164, out _)) return false;

        var contact = await _contactStore.GetByPhoneAsync(tenantId, e164, cancellationToken);
        return contact != null
            && contact.ConsentStatus != WhatsAppConsentStatus.OptedOut
            && contact.IsSessionOpen(DateTime.UtcNow);
    }

    public async Task<WhatsAppResult> SendTemplateAsync(
        string tenantId,
        string phoneNumber,
        string templateName,
        string languageCode,
        IReadOnlyList<string>? bodyParameters = null,
        CancellationToken cancellationToken = default)
    {
        var settings = await _settings.ResolveAsync(tenantId, cancellationToken);
        if (settings == null) return WhatsAppResult.NotConfigured();

        if (string.IsNullOrWhiteSpace(templateName))
            return WhatsAppResult.Failed("Template name is required");
        var language = string.IsNullOrWhiteSpace(languageCode) ? "en" : languageCode.Trim();

        var gate = await GateAsync(tenantId, phoneNumber, settings, requireSession: false, cancellationToken);
        if (gate.Failure != null) return gate.Failure;

        // Warn (do not block) when the local template registry disagrees;
        // the registry may simply be stale, and Meta is the authority.
        var known = await _templates.GetAsync(tenantId, templateName.Trim(), language, cancellationToken);
        if (known != null && !known.IsApproved)
            return WhatsAppResult.Failed($"Template '{templateName}' ({language}) is {known.Status}, not APPROVED", retryable: false, normalizedPhone: gate.E164);
        if (known != null && known.BodyParameterCount != (bodyParameters?.Count ?? 0))
        {
            _logger.LogWarning(
                "Template {Template} expects {Expected} body parameters but {Actual} were supplied",
                templateName, known.BodyParameterCount, bodyParameters?.Count ?? 0);
        }

        var payload = new
        {
            messaging_product = "whatsapp",
            recipient_type = "individual",
            to = gate.E164.TrimStart('+'),
            type = "template",
            template = new
            {
                name = templateName.Trim(),
                language = new { code = language },
                components = bodyParameters is { Count: > 0 }
                    ? new object[]
                    {
                        new
                        {
                            type = "body",
                            parameters = bodyParameters.Select(p => new { type = "text", text = p ?? string.Empty }).ToArray(),
                        },
                    }
                    : null,
            },
        };

        return await DispatchAsync(tenantId, settings, payload, gate.E164, cancellationToken);
    }

    public async Task<WhatsAppResult> SendTextAsync(
        string tenantId,
        string phoneNumber,
        string message,
        CancellationToken cancellationToken = default)
    {
        var settings = await _settings.ResolveAsync(tenantId, cancellationToken);
        if (settings == null) return WhatsAppResult.NotConfigured();
        if (string.IsNullOrWhiteSpace(message))
            return WhatsAppResult.Failed("Message body is required");

        var gate = await GateAsync(tenantId, phoneNumber, settings, requireSession: true, cancellationToken);
        if (gate.Failure != null) return gate.Failure;

        var payload = new
        {
            messaging_product = "whatsapp",
            recipient_type = "individual",
            to = gate.E164.TrimStart('+'),
            type = "text",
            text = new { preview_url = false, body = message },
        };

        return await DispatchAsync(tenantId, settings, payload, gate.E164, cancellationToken);
    }

    private async Task<(string E164, WhatsAppResult? Failure)> GateAsync(
        string tenantId, string phoneNumber, ResolvedWhatsAppSettings settings, bool requireSession, CancellationToken cancellationToken)
    {
        if (!_contacts.TryNormalize(phoneNumber, settings.DefaultCountry, out var e164, out var phoneError))
            return (string.Empty, WhatsAppResult.InvalidPhone(phoneError));

        var contact = await _contactStore.GetByPhoneAsync(tenantId, e164, cancellationToken);
        if (contact?.ConsentStatus == WhatsAppConsentStatus.OptedOut)
            return (e164, WhatsAppResult.OptedOut(e164));

        if (requireSession && (contact == null || !contact.IsSessionOpen(DateTime.UtcNow)))
            return (e164, WhatsAppResult.SessionClosed(e164));

        return (e164, null);
    }

    private async Task<WhatsAppResult> DispatchAsync(
        string tenantId, ResolvedWhatsAppSettings settings, object payload, string e164, CancellationToken cancellationToken)
    {
        var response = await _graph.SendMessageAsync(settings, payload, cancellationToken);
        if (!response.Success || response.Body == null)
        {
            _logger.LogWarning("WhatsApp send to {Phone} failed for tenant {TenantId}: {Error}", Mask(e164), tenantId, response.Error);
            return WhatsAppResult.Failed(response.Error ?? "WhatsApp send failed", response.ErrorCode, response.Retryable, e164);
        }

        string? messageId = null;
        using (var body = response.Body)
        {
            if (body.RootElement.TryGetProperty("messages", out var messages)
                && messages.ValueKind == JsonValueKind.Array
                && messages.GetArrayLength() > 0
                && messages[0].TryGetProperty("id", out var id))
            {
                messageId = id.GetString();
            }
        }

        try
        {
            await _contacts.MarkOutboundAsync(tenantId, e164, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sent WhatsApp {MessageId} but could not record the outbound on the contact", messageId);
        }

        _logger.LogInformation("WhatsApp message {MessageId} sent to {Phone} for tenant {TenantId} via {Source} credentials",
            messageId, Mask(e164), tenantId, settings.Source);
        return WhatsAppResult.Succeeded(messageId, e164);
    }

    private static string Mask(string e164) =>
        e164.Length <= 6 ? "***" : $"{e164[..4]}***{e164[^3..]}";
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;

namespace Oluso.Messaging.WhatsApp;

public sealed record WebhookProcessingResult(int Messages, int Statuses, int Duplicates, int Errors);

public interface IWhatsAppWebhookProcessor
{
    /// <summary>Constant-time check of Meta's sha256= HMAC over the raw body.</summary>
    static bool SignatureIsValid(string appSecret, byte[] body, string? providedSignature)
    {
        if (string.IsNullOrWhiteSpace(appSecret) || string.IsNullOrWhiteSpace(providedSignature)) return false;
        var provided = providedSignature.Trim();
        if (provided.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
            provided = provided["sha256=".Length..];

        byte[] providedBytes;
        try { providedBytes = Convert.FromHexString(provided); }
        catch (FormatException) { return false; }

        var computed = HMACSHA256.HashData(Encoding.UTF8.GetBytes(appSecret), body);
        return CryptographicOperations.FixedTimeEquals(computed, providedBytes);
    }

    /// <summary>The phone number ids referenced by a payload, so the caller can route it to tenants.</summary>
    static IReadOnlyList<string> ExtractPhoneNumberIds(JsonElement root)
    {
        var ids = new List<string>();
        if (!root.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array) return ids;
        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array) continue;
            foreach (var change in changes.EnumerateArray())
            {
                if (change.TryGetProperty("value", out var value)
                    && value.TryGetProperty("metadata", out var metadata)
                    && metadata.TryGetProperty("phone_number_id", out var pid)
                    && pid.ValueKind == JsonValueKind.String)
                {
                    var id = pid.GetString();
                    if (!string.IsNullOrEmpty(id) && !ids.Contains(id)) ids.Add(id);
                }
            }
        }
        return ids;
    }

    /// <summary>Processes every change in the payload that belongs to <paramref name="phoneNumberId"/> for the given tenant.</summary>
    Task<WebhookProcessingResult> ProcessAsync(string tenantId, string phoneNumberId, JsonElement root, CancellationToken cancellationToken = default);
}

/// <summary>
/// Turns Meta's webhook payload into contact updates, stored inbound messages
/// and platform events. Idempotent per message id, so Meta's redelivery on a
/// non-2xx response cannot double-process.
/// </summary>
public sealed class WhatsAppWebhookProcessor : IWhatsAppWebhookProcessor
{
    private readonly IWhatsAppContactService _contacts;
    private readonly IWhatsAppInboundMessageStore _inbound;
    private readonly IOlusoEventService? _events;
    private readonly ILogger<WhatsAppWebhookProcessor> _logger;

    public WhatsAppWebhookProcessor(
        IWhatsAppContactService contacts,
        IWhatsAppInboundMessageStore inbound,
        IServiceProvider services,
        ILogger<WhatsAppWebhookProcessor> logger)
    {
        _contacts = contacts;
        _inbound = inbound;
        _events = services.GetService(typeof(IOlusoEventService)) as IOlusoEventService;
        _logger = logger;
    }

    public async Task<WebhookProcessingResult> ProcessAsync(string tenantId, string phoneNumberId, JsonElement root, CancellationToken cancellationToken = default)
    {
        int messages = 0, statuses = 0, duplicates = 0, errors = 0;
        if (!root.TryGetProperty("entry", out var entries) || entries.ValueKind != JsonValueKind.Array)
            return new WebhookProcessingResult(0, 0, 0, 0);

        foreach (var entry in entries.EnumerateArray())
        {
            if (!entry.TryGetProperty("changes", out var changes) || changes.ValueKind != JsonValueKind.Array) continue;
            foreach (var change in changes.EnumerateArray())
            {
                if (!change.TryGetProperty("value", out var value)) continue;
                if (!value.TryGetProperty("metadata", out var metadata)
                    || !metadata.TryGetProperty("phone_number_id", out var pid)
                    || pid.GetString() != phoneNumberId)
                    continue;

                var profiles = ReadProfiles(value);

                if (value.TryGetProperty("messages", out var msgs) && msgs.ValueKind == JsonValueKind.Array)
                {
                    foreach (var msg in msgs.EnumerateArray())
                    {
                        try
                        {
                            var outcome = await ProcessMessageAsync(tenantId, phoneNumberId, msg, profiles, cancellationToken);
                            if (outcome == Outcome.Duplicate) duplicates++; else messages++;
                        }
                        catch (Exception ex)
                        {
                            errors++;
                            _logger.LogError(ex, "Failed to process inbound WhatsApp message for tenant {TenantId}", tenantId);
                        }
                    }
                }

                if (value.TryGetProperty("statuses", out var sts) && sts.ValueKind == JsonValueKind.Array)
                {
                    foreach (var st in sts.EnumerateArray())
                    {
                        try
                        {
                            await ProcessStatusAsync(tenantId, phoneNumberId, st, cancellationToken);
                            statuses++;
                        }
                        catch (Exception ex)
                        {
                            errors++;
                            _logger.LogError(ex, "Failed to process WhatsApp status for tenant {TenantId}", tenantId);
                        }
                    }
                }
            }
        }

        return new WebhookProcessingResult(messages, statuses, duplicates, errors);
    }

    private enum Outcome { Processed, Duplicate }

    private async Task<Outcome> ProcessMessageAsync(
        string tenantId, string phoneNumberId, JsonElement msg, IReadOnlyDictionary<string, string> profiles, CancellationToken cancellationToken)
    {
        var messageId = GetString(msg, "id") ?? throw new InvalidOperationException("message without id");
        if (await _inbound.ExistsAsync(tenantId, messageId, cancellationToken))
            return Outcome.Duplicate;

        var from = GetString(msg, "from") ?? throw new InvalidOperationException("message without sender");
        var e164 = "+" + new string(from.Where(char.IsDigit).ToArray());
        var type = GetString(msg, "type") ?? "unknown";
        var receivedAt = ReadTimestamp(msg);
        var text = ExtractText(msg, type);
        profiles.TryGetValue(from, out var profileName);
        string? inReplyTo = null;
        if (msg.TryGetProperty("context", out var ctx) && ctx.ValueKind == JsonValueKind.Object)
            inReplyTo = GetString(ctx, "id");

        var contact = await _contacts.RecordInboundAsync(tenantId, e164, profileName, messageId, type == "text" || type == "button" || type == "interactive" ? text : null, receivedAt, cancellationToken);
        var keyword = IWhatsAppContactService.DetectKeyword(type == "text" ? text : null);

        await _inbound.AddAsync(new WhatsAppInboundMessage
        {
            TenantId = tenantId,
            ProviderMessageId = messageId,
            ContactId = contact.Id,
            From = e164,
            MessageType = type,
            Text = text,
            Keyword = keyword,
            InReplyToMessageId = inReplyTo,
            ReceivedAt = receivedAt,
            RawJson = msg.GetRawText(),
        }, cancellationToken);

        await RaiseAsync(new WhatsAppMessageReceivedEvent
        {
            TenantId = tenantId,
            MessageId = messageId,
            ContactId = contact.Id,
            From = e164,
            ProfileName = profileName,
            MessageType = type,
            Text = text,
            Keyword = keyword,
            InReplyToMessageId = inReplyTo,
            ReceivedAt = receivedAt,
            PhoneNumberId = phoneNumberId,
        }, cancellationToken);

        return Outcome.Processed;
    }

    private async Task ProcessStatusAsync(string tenantId, string phoneNumberId, JsonElement st, CancellationToken cancellationToken)
    {
        var messageId = GetString(st, "id") ?? throw new InvalidOperationException("status without message id");
        var status = GetString(st, "status") ?? "unknown";
        var recipient = GetString(st, "recipient_id");
        var e164 = string.IsNullOrEmpty(recipient) ? string.Empty : "+" + new string(recipient.Where(char.IsDigit).ToArray());

        int? errorCode = null;
        string? errorTitle = null, errorDetail = null;
        if (st.TryGetProperty("errors", out var errs) && errs.ValueKind == JsonValueKind.Array && errs.GetArrayLength() > 0)
        {
            var first = errs[0];
            if (first.TryGetProperty("code", out var c) && c.TryGetInt32(out var ci)) errorCode = ci;
            errorTitle = GetString(first, "title") ?? GetString(first, "message");
            if (first.TryGetProperty("error_data", out var ed)) errorDetail = GetString(ed, "details");
        }

        string? category = null;
        if (st.TryGetProperty("conversation", out var conv) && conv.TryGetProperty("origin", out var origin))
            category = GetString(origin, "type");

        await RaiseAsync(new WhatsAppMessageStatusEvent
        {
            TenantId = tenantId,
            MessageId = messageId,
            Recipient = e164,
            Status = status,
            StatusAt = ReadTimestamp(st),
            ErrorCode = errorCode,
            ErrorTitle = errorTitle,
            ErrorDetail = errorDetail,
            ConversationCategory = category,
            PhoneNumberId = phoneNumberId,
        }, cancellationToken);
    }

    private async Task RaiseAsync(OlusoEvent evt, CancellationToken cancellationToken)
    {
        if (_events == null) return;
        await _events.RaiseAsync(evt, cancellationToken);
    }

    private static IReadOnlyDictionary<string, string> ReadProfiles(JsonElement value)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!value.TryGetProperty("contacts", out var contacts) || contacts.ValueKind != JsonValueKind.Array) return map;
        foreach (var c in contacts.EnumerateArray())
        {
            var waId = GetString(c, "wa_id");
            if (waId == null) continue;
            if (c.TryGetProperty("profile", out var profile) && GetString(profile, "name") is { } name)
                map[waId] = name;
        }
        return map;
    }

    private static string? ExtractText(JsonElement msg, string type)
    {
        switch (type)
        {
            case "text":
                return msg.TryGetProperty("text", out var t) ? GetString(t, "body") : null;
            case "button":
                return msg.TryGetProperty("button", out var b) ? GetString(b, "text") ?? GetString(b, "payload") : null;
            case "interactive":
                if (!msg.TryGetProperty("interactive", out var i)) return null;
                if (i.TryGetProperty("button_reply", out var br)) return GetString(br, "title") ?? GetString(br, "id");
                if (i.TryGetProperty("list_reply", out var lr)) return GetString(lr, "title") ?? GetString(lr, "id");
                return null;
            case "image":
            case "document":
            case "video":
            case "audio":
                return msg.TryGetProperty(type, out var media) ? GetString(media, "caption") : null;
            case "location":
                if (!msg.TryGetProperty("location", out var loc)) return null;
                return $"{GetString(loc, "name")} {GetString(loc, "address")}".Trim();
            case "reaction":
                return msg.TryGetProperty("reaction", out var r) ? GetString(r, "emoji") : null;
            default:
                return null;
        }
    }

    private static DateTime ReadTimestamp(JsonElement el)
    {
        if (el.TryGetProperty("timestamp", out var ts))
        {
            var raw = ts.ValueKind == JsonValueKind.Number ? ts.GetInt64().ToString() : ts.GetString();
            if (long.TryParse(raw, out var seconds))
                return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
        }
        return DateTime.UtcNow;
    }

    private static string? GetString(JsonElement el, string name) =>
        el.ValueKind == JsonValueKind.Object && el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
}

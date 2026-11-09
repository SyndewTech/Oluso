namespace Oluso.Core.Services;

/// <summary>
/// Unified messaging service for sending emails and SMS
/// </summary>
public interface IMessagingService
{
    /// <summary>
    /// Sends an email
    /// </summary>
    Task SendEmailAsync(
        string to,
        string subject,
        string body,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an SMS
    /// </summary>
    Task SendSmsAsync(
        string phoneNumber,
        string message,
        CancellationToken cancellationToken = default);
}

#region Email Interfaces

/// <summary>
/// Service for sending emails
/// </summary>
public interface IEmailSender
{
    /// <summary>
    /// Sends an email with HTML content. Uses the ambient tenant context
    /// (HTTP request scope) to resolve the tenant's default SenderProfile,
    /// falling back to global EmailOptions when no tenant is in scope.
    /// Background workers / workflow handlers should prefer the
    /// <see cref="SendAsync(string, string, string, string, CancellationToken)"/>
    /// overload that takes an explicit tenant id.
    /// </summary>
    Task<EmailResult> SendAsync(
        string email,
        string subject,
        string htmlMessage,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tenant-explicit send. Resolves the SenderProfile and credentials for
    /// the supplied <paramref name="tenantId"/> regardless of ambient
    /// context, so callers running outside an HTTP request (background jobs,
    /// workflow action handlers, event sinks) deliver via the tenant's own
    /// configured provider instead of the global fallback.
    /// </summary>
    Task<EmailResult> SendAsync(
        string tenantId,
        string email,
        string subject,
        string htmlMessage,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tenant-explicit send with file attachments. Used for transactional mail
    /// that must carry a payload — e.g. an iCalendar invite
    /// (<c>text/calendar; method=REQUEST</c>) so the recipient's mail client
    /// renders Accept/Decline and adds the event to their calendar.
    ///
    /// Default implementation delegates to the no-attachment overload when no
    /// attachments are supplied, and otherwise throws — implementations that
    /// genuinely support attachments override this. The throw is deliberate:
    /// silently dropping an attachment would deliver an invite-less email that
    /// looks sent but does nothing.
    /// </summary>
    Task<EmailResult> SendAsync(
        string tenantId,
        string email,
        string subject,
        string htmlMessage,
        IReadOnlyList<EmailAttachment>? attachments,
        CancellationToken cancellationToken = default)
        => attachments is null or { Count: 0 }
            ? SendAsync(tenantId, email, subject, htmlMessage, cancellationToken)
            : throw new NotSupportedException(
                $"{GetType().Name} does not support email attachments.");

    /// <summary>
    /// Tenant-explicit send of a full message: several To recipients, CC, BCC,
    /// reply-to and attachments in one email, so recipients see each other the
    /// way they expect. Default implementation covers the single-recipient
    /// case and refuses CC/BCC; providers that support them override this.
    /// </summary>
    Task<EmailResult> SendAsync(
        string tenantId,
        EmailMessage message,
        CancellationToken cancellationToken = default)
    {
        if (message.To.Count == 1 && message.Cc.Count == 0 && message.Bcc.Count == 0 && message.Headers is null or { Count: 0 })
            return SendAsync(tenantId, message.To[0], message.Subject, message.HtmlBody, message.Attachments, cancellationToken);
        throw new NotSupportedException($"{GetType().Name} does not support multiple recipients, CC, BCC or custom headers.");
    }

    /// <summary>
    /// Sends an email using a provider template
    /// </summary>
    Task<EmailResult> SendTemplateAsync(
        string email,
        string templateName,
        IDictionary<string, object> templateData,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// One outgoing email with its full recipient set.
/// </summary>
public sealed class EmailMessage
{
    public required IReadOnlyList<string> To { get; init; }
    public IReadOnlyList<string> Cc { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Bcc { get; init; } = Array.Empty<string>();
    public string? ReplyTo { get; init; }
    public required string Subject { get; init; }
    public required string HtmlBody { get; init; }
    public string? TextBody { get; init; }
    public IReadOnlyList<EmailAttachment>? Attachments { get; init; }

    /// <summary>
    /// Extra MIME headers, such as List-Unsubscribe and List-Unsubscribe-Post (RFC 8058) on marketing mail. Senders
    /// that can't carry custom headers refuse a message that has any rather than drop them, since a marketing email
    /// without its unsubscribe header breaks bulk-sender rules.
    /// </summary>
    public IReadOnlyDictionary<string, string>? Headers { get; init; }

    /// <summary>Every address the message goes to, in To, Cc, Bcc order, de-duplicated.</summary>
    public IEnumerable<string> AllRecipients =>
        To.Concat(Cc).Concat(Bcc).Distinct(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// A file attached to an outgoing email. <see cref="ContentType"/> carries the
/// full MIME type <em>including parameters</em> — e.g.
/// <c>text/calendar; method=REQUEST; charset=utf-8</c> for an iCalendar invite —
/// so the receiving client renders it correctly rather than as a plain download.
/// </summary>
public sealed class EmailAttachment
{
    public required string FileName { get; init; }
    public required byte[] Content { get; init; }
    public string ContentType { get; init; } = "application/octet-stream";

    /// <summary>Set for inline parts referenced from the HTML body via <c>cid:</c>.</summary>
    public string? ContentId { get; init; }

    public AttachmentDisposition Disposition { get; init; } = AttachmentDisposition.Attachment;
}

public enum AttachmentDisposition
{
    Attachment,
    Inline,
}

/// <summary>
/// Result of sending an email
/// </summary>
public class EmailResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string? MessageId { get; init; }

    /// <summary>Nothing was sent because no recipient can receive mail (see <see cref="UndeliverableEmail"/>); counts as success.</summary>
    public bool Skipped { get; init; }

    public static EmailResult Succeeded(string? messageId = null) =>
        new() { Success = true, MessageId = messageId };

    public static EmailResult Failed(string error) =>
        new() { Success = false, Error = error };

    public static EmailResult SkippedUndeliverable() =>
        new() { Success = true, Skipped = true };
}

/// <summary>
/// Addresses that can never receive mail: domains reserved for tests and local networks (RFC 2606, RFC 6761,
/// RFC 6762's <c>.local</c>) and domains without a dot, such as seeded accounts like <c>admin@default.local</c>.
/// Sending to them only earns a provider error (and, with some providers, a mark against the sender).
/// </summary>
public static class UndeliverableEmail
{
    private static readonly string[] ReservedSuffixes = [".local", ".localhost", ".test", ".example", ".invalid", ".internal"];
    private static readonly string[] ReservedDomains = ["example.com", "example.net", "example.org", "localhost"];

    public static bool IsUndeliverable(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return true;
        var at = address.LastIndexOf('@');
        if (at <= 0 || at == address.Length - 1) return true;
        var domain = address[(at + 1)..].Trim().TrimEnd('.').ToLowerInvariant();
        if (!domain.Contains('.')) return true;
        if (ReservedDomains.Any(d => domain == d || domain.EndsWith("." + d, StringComparison.Ordinal))) return true;
        return ReservedSuffixes.Any(s => domain.EndsWith(s, StringComparison.Ordinal));
    }
}

/// <summary>
/// Alias for IEmailSender for backward compatibility
/// </summary>
public interface IEmailService : IEmailSender;

/// <summary>
/// Sender that delivers mail through a specific tenant SenderProfile rather
/// than the resolved default. Used by the admin test-send endpoint and by
/// campaign sends. Never falls back to platform / global config — callers
/// pass the exact profile to use, and the implementation refuses to send
/// if the profile or its credentials are missing/invalid.
/// </summary>
public interface IProfileEmailSender
{
    Task<EmailResult> SendThroughProfileAsync(
        string tenantId,
        string profileId,
        string toEmail,
        string subject,
        string htmlMessage,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a full message (text body, reply-to, headers) through the profile. The default covers a single
    /// recipient with no headers; implementations that carry headers override it.
    /// </summary>
    Task<EmailResult> SendThroughProfileAsync(
        string tenantId,
        string profileId,
        EmailMessage message,
        CancellationToken cancellationToken = default)
    {
        if (message.To.Count == 1 && message.Cc.Count == 0 && message.Bcc.Count == 0 && message.Headers is null or { Count: 0 })
            return SendThroughProfileAsync(tenantId, profileId, message.To[0], message.Subject, message.HtmlBody, cancellationToken);
        throw new NotSupportedException($"{GetType().Name} does not support multiple recipients, CC, BCC or custom headers.");
    }
}

#endregion

#region SMS Interfaces

/// <summary>
/// Service for sending SMS messages
/// </summary>
public interface ISmsSender
{
    /// <summary>
    /// Sends an SMS to the specified phone number
    /// </summary>
    Task<SmsResult> SendAsync(
        string phoneNumber,
        string message,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of sending an SMS
/// </summary>
public class SmsResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string? MessageId { get; init; }

    public static SmsResult Succeeded(string? messageId = null) =>
        new() { Success = true, MessageId = messageId };

    public static SmsResult Failed(string error) =>
        new() { Success = false, Error = error };
}

/// <summary>
/// Alias for ISmsSender for backward compatibility
/// </summary>
public interface ISmsService : ISmsSender;

#endregion

#region WhatsApp Interfaces

/// <summary>
/// Sends WhatsApp messages through the tenant's WhatsApp Business account.
/// Free-form text is only accepted by Meta inside the 24-hour window that
/// follows the recipient's last inbound message; outside it a pre-approved
/// template must be used. Every send is refused for recipients who opted out.
/// </summary>
public interface IWhatsAppSender
{
    /// <summary>
    /// Sends a pre-approved template message. Parameters fill the template's
    /// positional body placeholders ({{1}}, {{2}}, ...) in order.
    /// </summary>
    Task<WhatsAppResult> SendTemplateAsync(
        string tenantId,
        string phoneNumber,
        string templateName,
        string languageCode,
        IReadOnlyList<string>? bodyParameters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends a free-form text message. Fails with
    /// <see cref="WhatsAppResult.SessionClosedReason"/> when the recipient has
    /// not written in within the last 24 hours.
    /// </summary>
    Task<WhatsAppResult> SendTextAsync(
        string tenantId,
        string phoneNumber,
        string message,
        CancellationToken cancellationToken = default);

    /// <summary>Whether the tenant has usable WhatsApp credentials.</summary>
    Task<bool> IsConfiguredAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether a free-form text to this number would be accepted right now
    /// (the recipient wrote in within the last 24 hours and has not opted out).
    /// </summary>
    Task<bool> CanSendFreeFormAsync(string tenantId, string phoneNumber, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of sending a WhatsApp message
/// </summary>
public class WhatsAppResult
{
    /// <summary>Error sentinel for "this tenant has no WhatsApp configured" —
    /// distinct from a real send failure so callers can skip gracefully rather
    /// than cascade-fail a workflow.</summary>
    public const string NotConfiguredReason = "whatsapp_not_configured";

    /// <summary>The recipient replied STOP (or was opted out by an administrator).</summary>
    public const string OptedOutReason = "recipient_opted_out";

    /// <summary>Free-form text refused because the 24-hour customer-service window is closed.</summary>
    public const string SessionClosedReason = "session_window_closed";

    /// <summary>The phone number could not be normalised to E.164.</summary>
    public const string InvalidPhoneReason = "invalid_phone_number";

    public bool Success { get; init; }
    public string? Error { get; init; }
    public string? MessageId { get; init; }

    /// <summary>Meta's numeric error code, when the failure came from the Graph API.</summary>
    public int? ErrorCode { get; init; }

    /// <summary>True when the failure is transient (rate limit, 5xx, network) and a later retry may succeed.</summary>
    public bool Retryable { get; init; }

    /// <summary>The E.164 number the message was addressed to, after normalisation.</summary>
    public string? NormalizedPhone { get; init; }

    public static WhatsAppResult Succeeded(string? messageId = null, string? normalizedPhone = null) =>
        new() { Success = true, MessageId = messageId, NormalizedPhone = normalizedPhone };

    public static WhatsAppResult Failed(string error, int? errorCode = null, bool retryable = false, string? normalizedPhone = null) =>
        new() { Success = false, Error = error, ErrorCode = errorCode, Retryable = retryable, NormalizedPhone = normalizedPhone };

    public static WhatsAppResult NotConfigured() =>
        new() { Success = false, Error = NotConfiguredReason };

    public static WhatsAppResult OptedOut(string normalizedPhone) =>
        new() { Success = false, Error = OptedOutReason, NormalizedPhone = normalizedPhone };

    public static WhatsAppResult SessionClosed(string normalizedPhone) =>
        new() { Success = false, Error = SessionClosedReason, NormalizedPhone = normalizedPhone };

    public static WhatsAppResult InvalidPhone(string detail) =>
        new() { Success = false, Error = $"{InvalidPhoneReason}: {detail}" };
}

#endregion

/// <summary>
/// Generic result for message sending operations.
/// Use EmailResult or SmsResult for specific operations.
/// </summary>
public class MessageSendResult
{
    public bool Success { get; init; }
    public string? MessageId { get; init; }
    public string? Error { get; init; }
    public string? ErrorCode { get; init; }

    public static MessageSendResult Succeeded(string? messageId = null) =>
        new() { Success = true, MessageId = messageId };

    public static MessageSendResult Failed(string error, string? errorCode = null) =>
        new() { Success = false, Error = error, ErrorCode = errorCode };

    public static implicit operator EmailResult(MessageSendResult result) =>
        result.Success ? EmailResult.Succeeded(result.MessageId) : EmailResult.Failed(result.Error ?? "Unknown error");

    public static implicit operator SmsResult(MessageSendResult result) =>
        result.Success ? SmsResult.Succeeded(result.MessageId) : SmsResult.Failed(result.Error ?? "Unknown error");
}

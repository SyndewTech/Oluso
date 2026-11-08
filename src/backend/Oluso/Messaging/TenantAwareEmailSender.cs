using System.Net;
using System.Net.Http.Json;
using System.Net.Mail;
using System.Net.Mime;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;

namespace Oluso.Messaging;

/// <summary>
/// Transactional email sender. Resolves the tenant's default SenderProfile
/// for system / transactional mail (auth flows, password reset, workflow
/// notifications). When a tenant has no default profile, falls back to the
/// global EmailOptions from appsettings — the platform's own credentials —
/// so core flows like password reset never break.
///
/// Campaign sends are NOT routed through here as a default — they call
/// IProfileEmailSender with an explicit profile id (implemented by this same
/// type) and the profile-targeted path refuses to fall back to anything.
/// </summary>
public class TenantAwareEmailSender : IEmailService, IProfileEmailSender
{
    private readonly HttpClient _httpClient;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantConfigurationService _configService;
    private readonly ISenderProfileStore _profileStore;
    private readonly ITenantStore _tenantStore;
    private readonly EmailOptions _globalOptions;
    private readonly ILogger<TenantAwareEmailSender> _logger;

    private const string SendGridApiUrl = "https://api.sendgrid.com/v3/mail/send";
    private const string BrevoApiUrl = "https://api.brevo.com/v3/smtp/email";
    private const string CloudflareApiBase = "https://api.cloudflare.com/client/v4/accounts";

    private static readonly JsonSerializerOptions SendGridJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    // Preserves anonymous-type property names as written (Brevo expects
    // camelCase) but drops nulls so an absent attachment array is omitted.
    private static readonly JsonSerializerOptions OmitNullJsonOptions = new()
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public TenantAwareEmailSender(
        HttpClient httpClient,
        ITenantContext tenantContext,
        ITenantConfigurationService configService,
        ISenderProfileStore profileStore,
        ITenantStore tenantStore,
        IOptions<EmailOptions> globalOptions,
        ILogger<TenantAwareEmailSender> logger)
    {
        _httpClient = httpClient;
        _tenantContext = tenantContext;
        _configService = configService;
        _profileStore = profileStore;
        _tenantStore = tenantStore;
        _globalOptions = globalOptions.Value;
        _logger = logger;
    }

    public async Task<EmailResult> SendAsync(
        string email,
        string subject,
        string htmlMessage,
        CancellationToken cancellationToken = default)
    {
        var settings = await ResolveTenantSettingsAsync(null, cancellationToken);
        return await DispatchAsync(settings, email, subject, htmlMessage, cancellationToken);
    }

    public async Task<EmailResult> SendAsync(
        string tenantId,
        string email,
        string subject,
        string htmlMessage,
        CancellationToken cancellationToken = default)
    {
        var settings = await ResolveTenantSettingsAsync(tenantId, cancellationToken);
        return await DispatchAsync(settings, email, subject, htmlMessage, cancellationToken);
    }

    public async Task<EmailResult> SendAsync(
        string tenantId,
        string email,
        string subject,
        string htmlMessage,
        IReadOnlyList<EmailAttachment>? attachments,
        CancellationToken cancellationToken = default)
    {
        var settings = await ResolveTenantSettingsAsync(tenantId, cancellationToken);
        return await DispatchAsync(settings, email, subject, htmlMessage, cancellationToken, attachments);
    }

    public async Task<EmailResult> SendAsync(
        string tenantId,
        EmailMessage message,
        CancellationToken cancellationToken = default)
    {
        if (message.To.Count == 0)
            return EmailResult.Failed("Email needs at least one To recipient");

        var settings = await ResolveTenantSettingsAsync(tenantId, cancellationToken);
        return await DispatchAsync(settings, message.To[0], message.Subject, message.HtmlBody, cancellationToken, message.Attachments, Envelope.Of(message, settings));
    }

    public async Task<EmailResult> SendTemplateAsync(
        string email,
        string templateName,
        IDictionary<string, object> templateData,
        CancellationToken cancellationToken = default)
    {
        if (UndeliverableEmail.IsUndeliverable(email))
            return EmailResult.SkippedUndeliverable();

        var settings = await ResolveTenantSettingsAsync(null, cancellationToken);

        return settings.Provider.ToLowerInvariant() switch
        {
            "brevo" => await SendViaBrevoTemplateAsync(settings, email, templateName, templateData, cancellationToken),
            "sendgrid" => await SendViaSendGridTemplateAsync(settings, email, templateName, templateData, cancellationToken),
            _ => EmailResult.Failed($"Template emails are not supported for provider {settings.Provider}"),
        };
    }

    public async Task<EmailResult> SendThroughProfileAsync(
        string tenantId,
        string profileId,
        string toEmail,
        string subject,
        string htmlMessage,
        CancellationToken cancellationToken = default)
    {
        var (settings, error) = await ResolveProfileSettingsAsync(tenantId, profileId, cancellationToken);
        return settings == null ? EmailResult.Failed(error!) : await DispatchAsync(settings, toEmail, subject, htmlMessage, cancellationToken);
    }

    public async Task<EmailResult> SendThroughProfileAsync(
        string tenantId,
        string profileId,
        EmailMessage message,
        CancellationToken cancellationToken = default)
    {
        if (message.To.Count == 0)
            return EmailResult.Failed("Email needs at least one To recipient");

        var (settings, error) = await ResolveProfileSettingsAsync(tenantId, profileId, cancellationToken);
        if (settings == null) return EmailResult.Failed(error!);
        return await DispatchAsync(settings, message.To[0], message.Subject, message.HtmlBody, cancellationToken, message.Attachments, Envelope.Of(message, settings));
    }

    /// <summary>The profile's sender and credentials, or why it can't send; never falls back to another sender.</summary>
    private async Task<(ResolvedEmailSettings? Settings, string? Error)> ResolveProfileSettingsAsync(string tenantId, string profileId, CancellationToken cancellationToken)
    {
        var profile = await _profileStore.GetAsync(tenantId, profileId, cancellationToken);
        if (profile == null)
            return (null, $"SenderProfile {profileId} not found.");

        var credentials = await _configService.GetSectionAsync<SenderCredentials>(
            tenantId,
            SenderCredentials.BuildSectionKey(profile.Id),
            cancellationToken);

        if (credentials == null)
            return (null, $"SenderProfile {profile.Name} has no credentials configured.");

        var settings = new ResolvedEmailSettings
        {
            Provider = profile.Provider.ToString(),
            FromAddress = profile.FromAddress,
            FromName = profile.FromName,
            ReplyToAddress = profile.ReplyToAddress,
            ApiKey = credentials.ApiKey,
            CloudflareAccountId = credentials.CloudflareAccountId,
            SmtpHost = credentials.SmtpHost,
            SmtpPort = credentials.SmtpPort,
            SmtpUsername = credentials.SmtpUsername,
            SmtpPassword = credentials.SmtpPassword,
            SmtpUseSsl = credentials.SmtpUseSsl,
        };
        await ApplyTenantReplyToAsync(settings, tenantId, sharedSender: false, cancellationToken);
        return (settings, null);
    }

    /// <summary>Full recipient set for one send; the single-address overloads build one with just To.</summary>
    private sealed record Envelope(
        IReadOnlyList<string> To,
        IReadOnlyList<string> Cc,
        IReadOnlyList<string> Bcc,
        string? ReplyTo,
        string? TextBody,
        IReadOnlyDictionary<string, string>? Headers = null)
    {
        public static Envelope Single(string email, ResolvedEmailSettings settings) =>
            new(new[] { email }, Array.Empty<string>(), Array.Empty<string>(), settings.ReplyToAddress, null);

        public static Envelope Of(EmailMessage message, ResolvedEmailSettings settings) =>
            new(message.To, message.Cc, message.Bcc, message.ReplyTo ?? settings.ReplyToAddress, message.TextBody,
                message.Headers is { Count: > 0 } ? message.Headers : null);

        public string Summary => string.Join(",", To) + (Cc.Count > 0 ? $" cc:{Cc.Count}" : "") + (Bcc.Count > 0 ? $" bcc:{Bcc.Count}" : "");
    }

    private Task<EmailResult> DispatchAsync(
        ResolvedEmailSettings settings,
        string email,
        string subject,
        string htmlMessage,
        CancellationToken cancellationToken,
        IReadOnlyList<EmailAttachment>? attachments = null,
        Envelope? envelope = null)
    {
        envelope ??= Envelope.Single(email, settings);

        // Seeded and test accounts (admin@default.local) can't receive mail: drop them instead of asking the provider.
        if (envelope.To.Concat(envelope.Cc).Concat(envelope.Bcc).Any(UndeliverableEmail.IsUndeliverable))
        {
            envelope = envelope with
            {
                To = envelope.To.Where(a => !UndeliverableEmail.IsUndeliverable(a)).ToList(),
                Cc = envelope.Cc.Where(a => !UndeliverableEmail.IsUndeliverable(a)).ToList(),
                Bcc = envelope.Bcc.Where(a => !UndeliverableEmail.IsUndeliverable(a)).ToList(),
            };
            if (envelope.To.Count == 0)
            {
                _logger.LogDebug("Email '{Subject}' not sent: no recipient can receive mail", subject);
                return Task.FromResult(EmailResult.SkippedUndeliverable());
            }
        }

        return settings.Provider.ToLowerInvariant() switch
        {
            "smtp" => SendViaSmtpAsync(settings, envelope, subject, htmlMessage, cancellationToken, attachments),
            "brevo" => SendViaBrevoAsync(settings, envelope, subject, htmlMessage, cancellationToken, attachments),
            "cloudflare" => SendViaCloudflareAsync(settings, envelope, subject, htmlMessage, cancellationToken, attachments),
            _ => SendViaSendGridAsync(settings, envelope, subject, htmlMessage, cancellationToken, attachments),
        };
    }

    /// <summary>
    /// Resolves the effective sender for a transactional send: tenant's
    /// default SenderProfile if one exists, otherwise the platform's global
    /// EmailOptions from appsettings. The global fallback is intentional —
    /// system mail (password reset, auth) must work even for tenants that
    /// haven't configured their own profile.
    /// </summary>
    /// <summary>
    /// Fills the reply-to when the sender profile has none: the tenant's confirmed
    /// reply-to inbox, otherwise the platform default for tenants sending from the
    /// platform address. A reply-to set on the message itself still wins at send time.
    /// </summary>
    private async Task ApplyTenantReplyToAsync(ResolvedEmailSettings settings, string? tenantId, bool sharedSender, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(settings.ReplyToAddress))
            return;

        var replyTo = string.IsNullOrEmpty(tenantId)
            ? null
            : (await _configService.GetSectionAsync<TenantEmailSettings>(tenantId, TenantEmailSettings.SectionKey, cancellationToken))?.VerifiedReplyTo;
        if (string.IsNullOrWhiteSpace(replyTo) && sharedSender)
            replyTo = _globalOptions.ReplyToAddress;

        if (!string.IsNullOrWhiteSpace(replyTo))
            settings.ReplyToAddress = replyTo.Trim();
    }

    /// <summary>
    /// Display name on the shared platform sender: "Tenant via Platform", so the
    /// recipient sees who the mail is from while the address stays the platform's.
    /// Platform mail and unnamed tenants keep the platform name.
    /// </summary>
    private async Task<string?> SharedSenderNameAsync(string? tenantId, CancellationToken cancellationToken)
    {
        var platformName = _globalOptions.FromName;
        if (string.IsNullOrEmpty(tenantId) || tenantId == Oluso.Core.PlatformConstants.TenantId)
            return platformName;

        var tenant = await _tenantStore.GetByIdAsync(tenantId, cancellationToken);
        var tenantName = tenant == null ? null : (string.IsNullOrWhiteSpace(tenant.DisplayName) ? tenant.Name : tenant.DisplayName);
        if (string.IsNullOrWhiteSpace(tenantName))
            return platformName;

        return string.IsNullOrWhiteSpace(platformName) ? tenantName : $"{tenantName} via {platformName}";
    }

    private async Task<ResolvedEmailSettings> ResolveTenantSettingsAsync(string? explicitTenantId, CancellationToken cancellationToken)
    {
        // Explicit tenant id wins over ambient context — used by background
        // / workflow / event-sink paths where ITenantContext isn't populated.
        var tenantId = !string.IsNullOrEmpty(explicitTenantId)
            ? explicitTenantId
            : (_tenantContext.HasTenant ? _tenantContext.TenantId : null);

        if (!string.IsNullOrEmpty(tenantId))
        {
            var profile = await _profileStore.GetDefaultAsync(tenantId, cancellationToken);

            if (profile != null)
            {
                var credentials = await _configService.GetSectionAsync<SenderCredentials>(
                    tenantId,
                    SenderCredentials.BuildSectionKey(profile.Id),
                    cancellationToken) ?? new SenderCredentials();

                var resolved = new ResolvedEmailSettings
                {
                    Provider = profile.Provider.ToString(),
                    FromAddress = profile.FromAddress,
                    FromName = profile.FromName,
                    ReplyToAddress = profile.ReplyToAddress,
                    ApiKey = credentials.ApiKey,
                    CloudflareAccountId = credentials.CloudflareAccountId,
                    SmtpHost = credentials.SmtpHost,
                    SmtpPort = credentials.SmtpPort,
                    SmtpUsername = credentials.SmtpUsername,
                    SmtpPassword = credentials.SmtpPassword,
                    SmtpUseSsl = credentials.SmtpUseSsl,
                };
                await ApplyTenantReplyToAsync(resolved, tenantId, sharedSender: false, cancellationToken);
                return resolved;
            }
        }

        var globalApiKey = _globalOptions.Provider.ToLowerInvariant() switch
        {
            "cloudflare" => _globalOptions.Cloudflare.ApiToken,
            _            => _globalOptions.SendGrid.ApiKey,
        };

        var shared = new ResolvedEmailSettings
        {
            Provider = _globalOptions.Provider,
            FromAddress = _globalOptions.FromAddress ?? "noreply@oluso.io",
            FromName = await SharedSenderNameAsync(tenantId, cancellationToken),
            ApiKey = globalApiKey,
            CloudflareAccountId = _globalOptions.Cloudflare.AccountId,
            SmtpHost = _globalOptions.Smtp.Host,
            SmtpPort = _globalOptions.Smtp.Port,
            SmtpUsername = _globalOptions.Smtp.Username,
            SmtpPassword = _globalOptions.Smtp.Password,
            SmtpUseSsl = _globalOptions.Smtp.UseSsl,
        };
        await ApplyTenantReplyToAsync(shared, tenantId, sharedSender: true, cancellationToken);
        return shared;
    }

    private async Task<EmailResult> SendViaSendGridAsync(
        ResolvedEmailSettings settings, Envelope envelope, string subject, string htmlMessage,
        CancellationToken cancellationToken, IReadOnlyList<EmailAttachment>? attachments = null)
    {
        var email = envelope.Summary;
        if (string.IsNullOrEmpty(settings.ApiKey))
        {
            _logger.LogError("SendGrid API key is not configured");
            return EmailResult.Failed("SendGrid API key is not configured");
        }

        try
        {
            // SendGrid's per-attachment `type` accepts the full content-type
            // string, so the iCalendar method parameter survives end-to-end.
            // Null when there are none so WhenWritingNull drops the field.
            var payload = new
            {
                personalizations = new[]
                {
                    new
                    {
                        to = envelope.To.Select(a => new { email = a }).ToArray(),
                        cc = envelope.Cc.Count > 0 ? envelope.Cc.Select(a => new { email = a }).ToArray() : null,
                        bcc = envelope.Bcc.Count > 0 ? envelope.Bcc.Select(a => new { email = a }).ToArray() : null,
                    },
                },
                // SendGrid takes custom headers as a string map, List-Unsubscribe included.
                headers = envelope.Headers,
                from = new { email = settings.FromAddress, name = settings.FromName },
                reply_to = string.IsNullOrWhiteSpace(envelope.ReplyTo) ? null : new { email = envelope.ReplyTo },
                subject,
                content = string.IsNullOrEmpty(envelope.TextBody)
                    ? new[] { new { type = "text/html", value = htmlMessage } }
                    : new[] { new { type = "text/plain", value = envelope.TextBody }, new { type = "text/html", value = htmlMessage } },
                attachments = attachments is { Count: > 0 }
                    ? attachments.Select(a => new
                    {
                        content = Convert.ToBase64String(a.Content),
                        type = a.ContentType,
                        filename = a.FileName,
                        disposition = a.Disposition == AttachmentDisposition.Inline ? "inline" : "attachment",
                        content_id = a.ContentId,
                    }).ToArray()
                    : null,
            };

            var request = new HttpRequestMessage(HttpMethod.Post, SendGridApiUrl);
            request.Headers.Add("Authorization", $"Bearer {settings.ApiKey}");
            request.Content = JsonContent.Create(payload, options: SendGridJsonOptions);

            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var messageId = response.Headers.TryGetValues("X-Message-Id", out var values)
                    ? values.FirstOrDefault() : null;
                _logger.LogInformation("Email sent to {To} via SendGrid, MessageId: {MessageId}", email, messageId);
                return EmailResult.Succeeded(messageId);
            }

            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("SendGrid API error: {StatusCode} - {Error}", response.StatusCode, errorContent);
            return EmailResult.Failed($"SendGrid API error: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending email via SendGrid to {To}", email);
            return EmailResult.Failed(ex.Message);
        }
    }

    private async Task<EmailResult> SendViaSendGridTemplateAsync(
        ResolvedEmailSettings settings, string email, string templateName,
        IDictionary<string, object> templateData, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(settings.ApiKey))
        {
            return EmailResult.Failed("SendGrid API key is not configured");
        }

        try
        {
            var payload = new
            {
                personalizations = new[] { new { to = new[] { new { email } }, dynamic_template_data = templateData } },
                from = new { email = settings.FromAddress, name = settings.FromName },
                template_id = templateName
            };

            var request = new HttpRequestMessage(HttpMethod.Post, SendGridApiUrl);
            request.Headers.Add("Authorization", $"Bearer {settings.ApiKey}");
            request.Content = JsonContent.Create(payload, options: SendGridJsonOptions);

            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var messageId = response.Headers.TryGetValues("X-Message-Id", out var values)
                    ? values.FirstOrDefault() : null;
                return EmailResult.Succeeded(messageId);
            }

            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("SendGrid template API error: {StatusCode} - {Error}", response.StatusCode, errorContent);
            return EmailResult.Failed($"SendGrid API error: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending templated email via SendGrid to {To}", email);
            return EmailResult.Failed(ex.Message);
        }
    }

    private async Task<EmailResult> SendViaBrevoAsync(
        ResolvedEmailSettings settings, Envelope envelope, string subject, string htmlMessage,
        CancellationToken cancellationToken, IReadOnlyList<EmailAttachment>? attachments = null)
    {
        var email = envelope.Summary;
        if (string.IsNullOrEmpty(settings.ApiKey))
        {
            _logger.LogError("Brevo API key is not configured");
            return EmailResult.Failed("Brevo API key is not configured");
        }

        try
        {
            // Brevo derives an attachment's MIME type from its filename and
            // exposes no way to set content-type parameters, so an iCalendar
            // part loses its `method=REQUEST` — the .ics still delivers as a
            // file but won't render inline Accept/Decline. Warn rather than
            // drop: the recipient can still open the invite.
            if (attachments is { Count: > 0 } &&
                attachments.Any(a => a.ContentType.Contains("method=", StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogWarning(
                    "Brevo cannot preserve content-type parameters on attachments; " +
                    "iCalendar invites to {To} will attach as a file without inline RSVP.", email);
            }

            var payload = new
            {
                sender = new { email = settings.FromAddress, name = settings.FromName },
                to = envelope.To.Select(a => new { email = a }).ToArray(),
                cc = envelope.Cc.Count > 0 ? envelope.Cc.Select(a => new { email = a }).ToArray() : null,
                bcc = envelope.Bcc.Count > 0 ? envelope.Bcc.Select(a => new { email = a }).ToArray() : null,
                replyTo = string.IsNullOrWhiteSpace(envelope.ReplyTo) ? null : new { email = envelope.ReplyTo },
                subject,
                htmlContent = htmlMessage,
                textContent = string.IsNullOrEmpty(envelope.TextBody) ? null : envelope.TextBody,
                headers = envelope.Headers,
                attachment = attachments is { Count: > 0 }
                    ? attachments.Select(a => new
                    {
                        content = Convert.ToBase64String(a.Content),
                        name = a.FileName,
                    }).ToArray()
                    : null,
            };

            var request = new HttpRequestMessage(HttpMethod.Post, BrevoApiUrl);
            request.Headers.Add("api-key", settings.ApiKey);
            request.Content = JsonContent.Create(payload, options: OmitNullJsonOptions);

            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadFromJsonAsync<BrevoSendResponse>(cancellationToken: cancellationToken);
                var messageId = body?.MessageId;
                _logger.LogInformation("Email sent to {To} via Brevo, MessageId: {MessageId}", email, messageId);
                return EmailResult.Succeeded(messageId);
            }

            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Brevo API error: {StatusCode} - {Error}", response.StatusCode, errorContent);
            return EmailResult.Failed($"Brevo API error: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending email via Brevo to {To}", email);
            return EmailResult.Failed(ex.Message);
        }
    }

    private async Task<EmailResult> SendViaBrevoTemplateAsync(
        ResolvedEmailSettings settings, string email, string templateName,
        IDictionary<string, object> templateData, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(settings.ApiKey))
        {
            return EmailResult.Failed("Brevo API key is not configured");
        }

        try
        {
            // Brevo uses numeric template IDs
            if (!long.TryParse(templateName, out var templateId))
            {
                return EmailResult.Failed($"Brevo template ID must be numeric, got: {templateName}");
            }

            var payload = new
            {
                sender = new { email = settings.FromAddress, name = settings.FromName },
                to = new[] { new { email } },
                templateId,
                @params = templateData
            };

            var request = new HttpRequestMessage(HttpMethod.Post, BrevoApiUrl);
            request.Headers.Add("api-key", settings.ApiKey);
            request.Content = JsonContent.Create(payload);

            var response = await _httpClient.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadFromJsonAsync<BrevoSendResponse>(cancellationToken: cancellationToken);
                return EmailResult.Succeeded(body?.MessageId);
            }

            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Brevo template API error: {StatusCode} - {Error}", response.StatusCode, errorContent);
            return EmailResult.Failed($"Brevo API error: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending templated email via Brevo to {To}", email);
            return EmailResult.Failed(ex.Message);
        }
    }

    private async Task<EmailResult> SendViaCloudflareAsync(
        ResolvedEmailSettings settings, Envelope envelope, string subject, string htmlMessage,
        CancellationToken cancellationToken, IReadOnlyList<EmailAttachment>? attachments = null)
    {
        var email = envelope.Summary;
        if (string.IsNullOrEmpty(settings.ApiKey))
        {
            _logger.LogError("Cloudflare API token is not configured");
            return EmailResult.Failed("Cloudflare API token is not configured");
        }

        if (string.IsNullOrEmpty(settings.CloudflareAccountId))
        {
            _logger.LogError("Cloudflare account ID is not configured");
            return EmailResult.Failed("Cloudflare account ID is not configured");
        }

        // The send schema this sender follows has no custom headers field; sending marketing mail without its
        // List-Unsubscribe header would quietly break bulk-sender rules, so such a message is refused instead.
        if (envelope.Headers is { Count: > 0 })
        {
            _logger.LogWarning("Cloudflare Email Sending can't carry custom headers; refusing a message with {Headers}", string.Join(", ", envelope.Headers.Keys));
            return EmailResult.Failed("Cloudflare Email Sending can't carry custom headers (such as List-Unsubscribe); send this message through another provider.");
        }

        try
        {
            // Cloudflare's REST send endpoint takes a flat payload. The sender
            // domain must already be onboarded in the dashboard (Email →
            // Sending → Onboard Domain) or the API returns 400 with a
            // domain-not-verified error.
            var fromValue = string.IsNullOrEmpty(settings.FromName)
                ? settings.FromAddress
                : $"{settings.FromName} <{settings.FromAddress}>";

            // Attachments follow the documented send schema: base64 content,
            // MIME type, and disposition. An attachment with a ContentId is an
            // inline part referenced from the HTML as cid:{ContentId}.
            var cloudflareAttachments = attachments is { Count: > 0 }
                ? attachments.Select(a => new
                {
                    filename = a.FileName,
                    content = Convert.ToBase64String(a.Content),
                    type = string.IsNullOrWhiteSpace(a.ContentType) ? "application/octet-stream" : a.ContentType,
                    disposition = string.IsNullOrEmpty(a.ContentId) ? "attachment" : "inline",
                    content_id = string.IsNullOrEmpty(a.ContentId) ? null : a.ContentId,
                }).ToArray()
                : null;

            var payload = new
            {
                to = envelope.To.ToArray(),
                cc = envelope.Cc.Count > 0 ? envelope.Cc.ToArray() : null,
                bcc = envelope.Bcc.Count > 0 ? envelope.Bcc.ToArray() : null,
                from = fromValue,
                reply_to = string.IsNullOrWhiteSpace(envelope.ReplyTo) ? null : envelope.ReplyTo,
                subject,
                html = htmlMessage,
                text = string.IsNullOrEmpty(envelope.TextBody) ? null : envelope.TextBody,
                attachments = cloudflareAttachments,
            };

            var url = $"{CloudflareApiBase}/{Uri.EscapeDataString(settings.CloudflareAccountId)}/email/sending/send";
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Add("Authorization", $"Bearer {settings.ApiKey}");
            request.Content = JsonContent.Create(payload, options: OmitNullJsonOptions);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                // { success, result: { message_id, delivered[], queued[], permanent_bounces[], suppressed_recipients[] } }
                string? messageId = null;
                var suppressedOrBounced = false;
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("result", out var result))
                    {
                        if (result.TryGetProperty("message_id", out var id) && id.ValueKind == System.Text.Json.JsonValueKind.String)
                            messageId = id.GetString();
                        suppressedOrBounced =
                            (result.TryGetProperty("permanent_bounces", out var pb) && pb.ValueKind == System.Text.Json.JsonValueKind.Array && pb.GetArrayLength() > 0)
                            || (result.TryGetProperty("suppressed_recipients", out var sr) && sr.ValueKind == System.Text.Json.JsonValueKind.Array && sr.GetArrayLength() > 0);
                    }
                }
                catch (System.Text.Json.JsonException)
                {
                    // Non-JSON 2xx body: treat as accepted without an id.
                }

                if (suppressedOrBounced)
                {
                    _logger.LogWarning("Cloudflare Email Sending did not deliver to {To}: recipient bounced or is suppressed", email);
                    return EmailResult.Failed("Recipient address is suppressed or permanently bounced at Cloudflare.");
                }

                _logger.LogInformation("Email sent to {To} via Cloudflare Email Sending ({MessageId})", email, messageId ?? "no id");
                return EmailResult.Succeeded(messageId);
            }

            _logger.LogError("Cloudflare Email Sending API error: {StatusCode} - {Body}", response.StatusCode, body);
            return EmailResult.Failed($"Cloudflare Email Sending API error: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending email via Cloudflare Email Sending to {To}", email);
            return EmailResult.Failed(ex.Message);
        }
    }

    private async Task<EmailResult> SendViaSmtpAsync(
        ResolvedEmailSettings settings, Envelope envelope, string subject, string htmlMessage,
        CancellationToken cancellationToken, IReadOnlyList<EmailAttachment>? attachments = null)
    {
        var email = envelope.Summary;
        if (string.IsNullOrEmpty(settings.SmtpHost))
        {
            _logger.LogError("SMTP host is not configured");
            return EmailResult.Failed("SMTP host is not configured");
        }

        try
        {
            using var client = new SmtpClient(settings.SmtpHost, settings.SmtpPort)
            {
                EnableSsl = settings.SmtpUseSsl,
                Credentials = !string.IsNullOrEmpty(settings.SmtpUsername)
                    ? new NetworkCredential(settings.SmtpUsername, settings.SmtpPassword)
                    : null
            };

            var from = new MailAddress(settings.FromAddress, settings.FromName);

            using var message = new MailMessage
            {
                From = from,
                Subject = subject,
                Body = htmlMessage,
                IsBodyHtml = true
            };
            foreach (var a in envelope.To) message.To.Add(new MailAddress(a));
            foreach (var a in envelope.Cc) message.CC.Add(new MailAddress(a));
            foreach (var a in envelope.Bcc) message.Bcc.Add(new MailAddress(a));
            if (!string.IsNullOrWhiteSpace(envelope.ReplyTo)) message.ReplyToList.Add(new MailAddress(envelope.ReplyTo));
            foreach (var (name, value) in envelope.Headers ?? new Dictionary<string, string>()) message.Headers.Add(name, value);
            if (!string.IsNullOrEmpty(envelope.TextBody))
                message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(envelope.TextBody, null, "text/plain"));

            // SMTP gives full control of the part's content-type, so the
            // iCalendar method parameter is preserved verbatim — the most
            // faithful invite rendering of all the providers.
            if (attachments is { Count: > 0 })
            {
                foreach (var att in attachments)
                {
                    var attachment = new Attachment(
                        new MemoryStream(att.Content),
                        new ContentType(att.ContentType)) { Name = att.FileName };

                    if (att.Disposition == AttachmentDisposition.Inline)
                    {
                        attachment.ContentDisposition!.Inline = true;
                        attachment.ContentId = att.ContentId;
                    }

                    message.Attachments.Add(attachment);
                }
            }

            await client.SendMailAsync(message, cancellationToken);

            _logger.LogInformation("Email sent to {To} via SMTP ({Host}:{Port})", email, settings.SmtpHost, settings.SmtpPort);
            return EmailResult.Succeeded();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending email via SMTP to {To}", email);
            return EmailResult.Failed(ex.Message);
        }
    }

    private class ResolvedEmailSettings
    {
        public string Provider { get; init; } = "SendGrid";
        public string FromAddress { get; init; } = "noreply@oluso.io";
        public string? FromName { get; init; }
        public string? ReplyToAddress { get; set; }
        public string? ApiKey { get; init; }
        public string? CloudflareAccountId { get; init; }
        public string? SmtpHost { get; init; }
        public int SmtpPort { get; init; } = 587;
        public string? SmtpUsername { get; init; }
        public string? SmtpPassword { get; init; }
        public bool SmtpUseSsl { get; init; } = true;
    }

    private class BrevoSendResponse
    {
        public string? MessageId { get; set; }
    }
}

using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Locale;

namespace Oluso.Messaging.WhatsApp;

public interface IWhatsAppContactService
{
    /// <summary>
    /// Normalises a typed number to E.164 using the tenant's default country
    /// for numbers without a calling code. Returns false with a reason when it
    /// cannot.
    /// </summary>
    bool TryNormalize(string? input, string? defaultCountry, out string e164, out string error);

    /// <summary>Contact row for a number, created on first sight.</summary>
    Task<WhatsAppContact> EnsureAsync(string tenantId, string e164, string? displayName, CancellationToken cancellationToken = default);

    /// <summary>Records an inbound message: refreshes the session window and applies STOP/START keywords.</summary>
    Task<WhatsAppContact> RecordInboundAsync(
        string tenantId, string e164, string? profileName, string messageId, string? text, DateTime receivedAt,
        CancellationToken cancellationToken = default);

    Task MarkOutboundAsync(string tenantId, string e164, CancellationToken cancellationToken = default);

    Task<WhatsAppContact> SetConsentAsync(
        string tenantId, string e164, WhatsAppConsentStatus status, string source, string? changedByUserId,
        CancellationToken cancellationToken = default);

    /// <summary>Which keyword, if any, the text represents: "STOP" or "START".</summary>
    static string? DetectKeyword(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var word = text.Trim().TrimEnd('.', '!').ToUpperInvariant();
        if (OptOutKeywords.Contains(word)) return "STOP";
        if (OptInKeywords.Contains(word)) return "START";
        return null;
    }

    static readonly HashSet<string> OptOutKeywords = new(StringComparer.OrdinalIgnoreCase)
        { "STOP", "UNSUBSCRIBE", "CANCEL", "END", "QUIT", "STOPALL", "OPT OUT", "OPTOUT" };

    static readonly HashSet<string> OptInKeywords = new(StringComparer.OrdinalIgnoreCase)
        { "START", "UNSTOP", "SUBSCRIBE", "OPT IN", "OPTIN", "YES", "RESUME" };
}

public sealed class WhatsAppContactService : IWhatsAppContactService
{
    private readonly IWhatsAppContactStore _contacts;
    private readonly IServiceProvider _services;
    private readonly ILogger<WhatsAppContactService> _logger;

    public WhatsAppContactService(IWhatsAppContactStore contacts, IServiceProvider services, ILogger<WhatsAppContactService> logger)
    {
        _contacts = contacts;
        _services = services;
        _logger = logger;
    }

    public bool TryNormalize(string? input, string? defaultCountry, out string e164, out string error)
    {
        e164 = string.Empty;
        error = string.Empty;
        var raw = (input ?? string.Empty).Trim();
        if (raw.Length == 0)
        {
            error = "no phone number";
            return false;
        }

        // Meta and many address books hand back bare digits with the country
        // code already present ("2348031234567"). Treat a long digit-only
        // string that does not start with a trunk zero as international.
        var digitsOnly = raw.All(ch => char.IsDigit(ch) || ch is ' ' or '-' or '(' or ')' or '.');
        var digits = new string(raw.Where(char.IsDigit).ToArray());
        if (digitsOnly && digits.Length >= 10 && digits[0] != '0' && PhoneNumberInput.IsValidE164("+" + digits)
            && (string.IsNullOrWhiteSpace(defaultCountry) || !LooksNational(digits, defaultCountry)))
        {
            e164 = "+" + digits;
            return true;
        }

        if (PhoneNumberInput.TryCompose(defaultCountry, raw, out e164, out var composeError))
            return true;

        error = string.IsNullOrWhiteSpace(defaultCountry)
            ? $"'{raw}' is not in international format and the tenant has no default country configured"
            : $"'{raw}': {composeError}";
        return false;
    }

    private static bool LooksNational(string digits, string countryCode)
    {
        // A digit string that already begins with the default country's
        // calling code is international; anything else of national length is
        // treated as national so "8031234567" in NG becomes +2348031234567.
        var country = SupportedCountries.GetByCode(countryCode);
        if (country == null) return false;
        return !digits.StartsWith(country.DialCode, StringComparison.Ordinal) && digits.Length <= 11;
    }

    public async Task<WhatsAppContact> EnsureAsync(string tenantId, string e164, string? displayName, CancellationToken cancellationToken = default)
    {
        var existing = await _contacts.GetByPhoneAsync(tenantId, e164, cancellationToken);
        if (existing != null)
        {
            if (!string.IsNullOrWhiteSpace(displayName) && existing.DisplayName != displayName)
            {
                existing.DisplayName = displayName;
                await _contacts.UpsertAsync(existing, cancellationToken);
            }
            return existing;
        }

        return await _contacts.UpsertAsync(new WhatsAppContact
        {
            TenantId = tenantId,
            PhoneNumber = e164,
            DisplayName = displayName,
        }, cancellationToken);
    }

    public async Task<WhatsAppContact> RecordInboundAsync(
        string tenantId, string e164, string? profileName, string messageId, string? text, DateTime receivedAt,
        CancellationToken cancellationToken = default)
    {
        var contact = await EnsureAsync(tenantId, e164, profileName, cancellationToken);
        if (contact.LastInboundAt == null || receivedAt >= contact.LastInboundAt.Value)
        {
            contact.LastInboundAt = receivedAt;
            contact.LastInboundMessageId = messageId;
        }

        var keyword = IWhatsAppContactService.DetectKeyword(text);
        var consentChanged = false;
        if (keyword == "STOP" && contact.ConsentStatus != WhatsAppConsentStatus.OptedOut)
        {
            contact.ConsentStatus = WhatsAppConsentStatus.OptedOut;
            contact.ConsentSource = "keyword";
            contact.ConsentAt = receivedAt;
            contact.OptedOutAt = receivedAt;
            consentChanged = true;
        }
        else if (keyword == "START" && contact.ConsentStatus != WhatsAppConsentStatus.OptedIn)
        {
            contact.ConsentStatus = WhatsAppConsentStatus.OptedIn;
            contact.ConsentSource = "keyword";
            contact.ConsentAt = receivedAt;
            contact.OptedOutAt = null;
            consentChanged = true;
        }
        else if (contact.ConsentStatus == WhatsAppConsentStatus.Unknown && keyword == null)
        {
            // Writing to the business number is the clearest opt-in there is
            // for service conversations; record it so the tenant has a source.
            contact.ConsentStatus = WhatsAppConsentStatus.OptedIn;
            contact.ConsentSource = "inbound_message";
            contact.ConsentAt = receivedAt;
            consentChanged = true;
        }

        contact = await _contacts.UpsertAsync(contact, cancellationToken);

        if (consentChanged)
            await RaiseConsentChangedAsync(contact, contact.ConsentSource!, null, cancellationToken);

        return contact;
    }

    public async Task MarkOutboundAsync(string tenantId, string e164, CancellationToken cancellationToken = default)
    {
        var contact = await EnsureAsync(tenantId, e164, null, cancellationToken);
        contact.LastOutboundAt = DateTime.UtcNow;
        await _contacts.UpsertAsync(contact, cancellationToken);
    }

    public async Task<WhatsAppContact> SetConsentAsync(
        string tenantId, string e164, WhatsAppConsentStatus status, string source, string? changedByUserId,
        CancellationToken cancellationToken = default)
    {
        var contact = await EnsureAsync(tenantId, e164, null, cancellationToken);
        var now = DateTime.UtcNow;
        contact.ConsentStatus = status;
        contact.ConsentSource = source;
        contact.ConsentAt = now;
        contact.OptedOutAt = status == WhatsAppConsentStatus.OptedOut ? now : null;
        contact = await _contacts.UpsertAsync(contact, cancellationToken);
        await RaiseConsentChangedAsync(contact, source, changedByUserId, cancellationToken);
        return contact;
    }

    private async Task RaiseConsentChangedAsync(WhatsAppContact contact, string source, string? changedByUserId, CancellationToken cancellationToken)
    {
        var events = _services.GetService(typeof(IOlusoEventService)) as IOlusoEventService;
        if (events == null) return;
        try
        {
            await events.RaiseAsync(new WhatsAppConsentChangedEvent
            {
                TenantId = contact.TenantId,
                ContactId = contact.Id,
                PhoneNumber = contact.PhoneNumber,
                ConsentStatus = contact.ConsentStatus.ToString(),
                Source = source,
                ChangedByUserId = changedByUserId,
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to raise WhatsApp consent event for {Phone}", contact.PhoneNumber);
        }
    }
}

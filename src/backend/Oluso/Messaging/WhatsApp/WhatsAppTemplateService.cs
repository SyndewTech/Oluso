using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Messaging.WhatsApp;

public sealed record WhatsAppAccountCheck(bool Success, string? Error, WhatsAppAccount? Account);

public interface IWhatsAppTemplateService
{
    /// <summary>Pulls the tenant's templates from Meta and replaces the local registry.</summary>
    Task<(bool Success, string? Error, int Count)> SyncAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Calls Meta with the tenant's credentials to prove they work, and records
    /// the phone number → tenant mapping the webhook relies on.
    /// </summary>
    Task<WhatsAppAccountCheck> VerifyAccountAsync(string tenantId, CancellationToken cancellationToken = default);
}

public sealed partial class WhatsAppTemplateService : IWhatsAppTemplateService
{
    private readonly MetaGraphClient _graph;
    private readonly IWhatsAppSettingsResolver _settings;
    private readonly IWhatsAppTemplateStore _templates;
    private readonly IWhatsAppAccountStore _accounts;
    private readonly ILogger<WhatsAppTemplateService> _logger;

    public WhatsAppTemplateService(
        MetaGraphClient graph,
        IWhatsAppSettingsResolver settings,
        IWhatsAppTemplateStore templates,
        IWhatsAppAccountStore accounts,
        ILogger<WhatsAppTemplateService> logger)
    {
        _graph = graph;
        _settings = settings;
        _templates = templates;
        _accounts = accounts;
        _logger = logger;
    }

    public async Task<(bool Success, string? Error, int Count)> SyncAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        var settings = await _settings.ResolveAsync(tenantId, cancellationToken);
        if (settings == null) return (false, "WhatsApp is not configured for this tenant.", 0);

        var (ok, error, items) = await _graph.ListTemplatesAsync(settings, cancellationToken);
        if (!ok) return (false, error, 0);

        var rows = items.Select(t => Map(tenantId, t)).Where(t => t != null).Select(t => t!).ToList();
        await _templates.ReplaceAllAsync(tenantId, rows, cancellationToken);
        _logger.LogInformation("Synced {Count} WhatsApp templates for tenant {TenantId}", rows.Count, tenantId);
        return (true, null, rows.Count);
    }

    public async Task<WhatsAppAccountCheck> VerifyAccountAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        var settings = await _settings.ResolveAsync(tenantId, cancellationToken);
        if (settings == null) return new WhatsAppAccountCheck(false, "WhatsApp is not configured for this tenant.", null);

        var response = await _graph.GetPhoneNumberAsync(settings, cancellationToken);
        if (!response.Success || response.Body == null)
            return new WhatsAppAccountCheck(false, response.Error, null);

        string? display = null, verifiedName = null, quality = null;
        using (var body = response.Body)
        {
            var root = body.RootElement;
            if (root.TryGetProperty("display_phone_number", out var d)) display = d.GetString();
            if (root.TryGetProperty("verified_name", out var v)) verifiedName = v.GetString();
            if (root.TryGetProperty("quality_rating", out var q)) quality = q.GetString();
        }

        var account = await _accounts.UpsertAsync(new WhatsAppAccount
        {
            TenantId = tenantId,
            PhoneNumberId = settings.PhoneNumberId,
            BusinessAccountId = settings.BusinessAccountId,
            DisplayPhoneNumber = display,
            VerifiedName = verifiedName,
            QualityRating = quality,
            LastVerifiedAt = DateTime.UtcNow,
        }, cancellationToken);

        return new WhatsAppAccountCheck(true, null, account);
    }

    private static WhatsAppTemplate? Map(string tenantId, JsonElement t)
    {
        string? Get(string name) => t.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        var name = Get("name");
        var language = Get("language");
        var id = Get("id");
        if (name == null || language == null || id == null) return null;

        string? bodyText = null, headerFormat = null, componentsJson = null;
        var bodyParams = 0;
        if (t.TryGetProperty("components", out var components) && components.ValueKind == JsonValueKind.Array)
        {
            componentsJson = components.GetRawText();
            foreach (var c in components.EnumerateArray())
            {
                var type = c.TryGetProperty("type", out var ct) ? ct.GetString() : null;
                if (string.Equals(type, "BODY", StringComparison.OrdinalIgnoreCase) && c.TryGetProperty("text", out var text))
                {
                    bodyText = text.GetString();
                    bodyParams = CountPlaceholders(bodyText);
                }
                else if (string.Equals(type, "HEADER", StringComparison.OrdinalIgnoreCase) && c.TryGetProperty("format", out var fmt))
                {
                    headerFormat = fmt.GetString();
                }
            }
        }

        return new WhatsAppTemplate
        {
            TenantId = tenantId,
            ProviderTemplateId = id,
            Name = name,
            Language = language,
            Category = Get("category"),
            Status = Get("status") ?? "UNKNOWN",
            BodyText = bodyText,
            BodyParameterCount = bodyParams,
            HeaderFormat = headerFormat,
            ComponentsJson = componentsJson,
        };
    }

    /// <summary>Highest positional placeholder index in "{{1}} ... {{n}}", or 0.</summary>
    public static int CountPlaceholders(string? bodyText)
    {
        if (string.IsNullOrEmpty(bodyText)) return 0;
        var max = 0;
        foreach (Match m in Placeholder().Matches(bodyText))
        {
            if (int.TryParse(m.Groups[1].Value, out var n) && n > max) max = n;
        }
        return max;
    }

    [GeneratedRegex(@"\{\{\s*(\d+)\s*\}\}")]
    private static partial Regex Placeholder();
}

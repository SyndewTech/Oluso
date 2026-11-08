using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oluso.Core.Authentication;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;

namespace Oluso.Messaging;

/// <summary>
/// Confirms a tenant's reply-to inbox by mailing a magic link to it. The address
/// only becomes the tenant's reply-to when that link is used, and the previous
/// confirmed address stays in use until then.
/// </summary>
public sealed class ReplyToVerificationService : IReplyToVerificationService
{
    public static readonly TimeSpan LinkLifetime = TimeSpan.FromHours(24);
    public static readonly TimeSpan ResendCooldown = TimeSpan.FromMinutes(1);

    private readonly ITenantConfigurationService _config;
    private readonly IActionTokenService _tokens;
    private readonly IEmailSender _email;
    private readonly ITenantStore _tenants;
    private readonly OlusoOptions _options;
    private readonly ILogger<ReplyToVerificationService> _logger;

    public ReplyToVerificationService(
        ITenantConfigurationService config,
        IActionTokenService tokens,
        IEmailSender email,
        ITenantStore tenants,
        IOptions<OlusoOptions> options,
        ILogger<ReplyToVerificationService> logger)
    {
        _config = config;
        _tokens = tokens;
        _email = email;
        _tenants = tenants;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ReplyToStatus> GetStatusAsync(string tenantId, CancellationToken cancellationToken = default)
        => ToStatus(await LoadAsync(tenantId, cancellationToken));

    public async Task<ReplyToRequestResult> RequestAsync(string tenantId, string address, CancellationToken cancellationToken = default)
    {
        var settings = await LoadAsync(tenantId, cancellationToken);
        var normalized = address?.Trim() ?? "";
        if (!MailAddress.TryCreate(normalized, out var parsed) || !string.Equals(parsed.Address, normalized, StringComparison.OrdinalIgnoreCase))
            return new ReplyToRequestResult(false, "Reply-to must be a single valid email address.", ToStatus(settings));

        if (string.Equals(settings.VerifiedReplyTo, normalized, StringComparison.OrdinalIgnoreCase))
        {
            settings.PendingReplyToAddress = null;
            settings.PendingReplyToSentAt = null;
            await SaveAsync(tenantId, settings, cancellationToken);
            return new ReplyToRequestResult(false, null, ToStatus(settings));
        }

        return await SendConfirmationAsync(tenantId, settings, normalized, cancellationToken);
    }

    public async Task<ReplyToRequestResult> ResendAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        var settings = await LoadAsync(tenantId, cancellationToken);
        if (string.IsNullOrWhiteSpace(settings.PendingReplyToAddress))
            return new ReplyToRequestResult(false, "There is no reply-to address waiting for confirmation.", ToStatus(settings));
        return await SendConfirmationAsync(tenantId, settings, settings.PendingReplyToAddress, cancellationToken);
    }

    public async Task<ReplyToStatus> CancelPendingAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        var settings = await LoadAsync(tenantId, cancellationToken);
        settings.PendingReplyToAddress = null;
        settings.PendingReplyToSentAt = null;
        await SaveAsync(tenantId, settings, cancellationToken);
        return ToStatus(settings);
    }

    public async Task<ReplyToStatus> RemoveAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        var settings = await LoadAsync(tenantId, cancellationToken);
        settings.ReplyToAddress = null;
        settings.ReplyToVerifiedAt = null;
        settings.PendingReplyToAddress = null;
        settings.PendingReplyToSentAt = null;
        await SaveAsync(tenantId, settings, cancellationToken);
        return ToStatus(settings);
    }

    private async Task<ReplyToRequestResult> SendConfirmationAsync(string tenantId, TenantEmailSettings settings, string address, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        if (string.Equals(settings.PendingReplyToAddress, address, StringComparison.OrdinalIgnoreCase)
            && settings.PendingReplyToSentAt is { } sentAt && now - sentAt < ResendCooldown)
        {
            return new ReplyToRequestResult(false, "A confirmation email was sent less than a minute ago.", ToStatus(settings));
        }

        var baseUrl = (_options.WorkspaceBaseUrl ?? _options.IssuerUri)?.TrimEnd('/');
        if (string.IsNullOrEmpty(baseUrl))
            return new ReplyToRequestResult(false, "No workspace URL is configured, so a confirmation link cannot be sent.", ToStatus(settings));

        var token = await _tokens.IssueAsync(tenantId, ReplyToVerificationTokenHandler.RedeemerFor(address),
            ReplyToVerificationTokenHandler.ActionKey, tenantId, LinkLifetime, cancellationToken);
        var link = $"{baseUrl}/m#tk={token}";
        var tenantName = await TenantNameAsync(tenantId, cancellationToken);

        var result = await _email.SendAsync(tenantId, new EmailMessage
        {
            To = new[] { address },
            Subject = $"Confirm the reply-to inbox for {tenantName}",
            HtmlBody = $"""
                <p>Someone asked for replies to email from <strong>{WebUtility.HtmlEncode(tenantName)}</strong> to be delivered to this inbox.</p>
                <p><a href="{WebUtility.HtmlEncode(link)}">Confirm this inbox</a></p>
                <p>The link expires in 24 hours. If you did not expect this, ignore it and nothing changes.</p>
                """,
            TextBody = $"Someone asked for replies to email from {tenantName} to be delivered to this inbox.\n\nConfirm: {link}\n\nThe link expires in 24 hours. If you did not expect this, ignore it and nothing changes.",
        }, cancellationToken);

        if (!result.Success)
        {
            _logger.LogWarning("Could not send the reply-to confirmation for tenant {TenantId}: {Error}", tenantId, result.Error);
            return new ReplyToRequestResult(false, "The confirmation email could not be sent. Check the sender settings and try again.", ToStatus(settings));
        }

        settings.PendingReplyToAddress = address;
        settings.PendingReplyToSentAt = now;
        await SaveAsync(tenantId, settings, cancellationToken);
        return new ReplyToRequestResult(true, null, ToStatus(settings));
    }

    private async Task<string> TenantNameAsync(string tenantId, CancellationToken cancellationToken)
    {
        var tenant = await _tenants.GetByIdAsync(tenantId, cancellationToken);
        return tenant == null ? tenantId : (string.IsNullOrWhiteSpace(tenant.DisplayName) ? tenant.Name : tenant.DisplayName!);
    }

    private async Task<TenantEmailSettings> LoadAsync(string tenantId, CancellationToken cancellationToken)
        => await _config.GetSectionAsync<TenantEmailSettings>(tenantId, TenantEmailSettings.SectionKey, cancellationToken) ?? new TenantEmailSettings();

    private Task SaveAsync(string tenantId, TenantEmailSettings settings, CancellationToken cancellationToken)
        => _config.SetSectionAsync(tenantId, TenantEmailSettings.SectionKey, settings, cancellationToken);

    private static ReplyToStatus ToStatus(TenantEmailSettings s)
        => new(s.VerifiedReplyTo, s.VerifiedReplyTo == null ? null : s.ReplyToVerifiedAt, s.PendingReplyToAddress, s.PendingReplyToSentAt);
}

/// <summary>
/// The confirmation link mailed to a proposed reply-to inbox. Confirming makes
/// the address the tenant's reply-to, provided it is still the one awaiting
/// confirmation.
/// </summary>
public sealed class ReplyToVerificationTokenHandler : IActionTokenHandler
{
    public const string ActionKey = "email.reply-to.verify";
    public const string ConfirmOption = "confirm";
    private const string RedeemerPrefix = "reply-to:";

    private readonly ITenantConfigurationService _config;
    private readonly ITenantStore _tenants;

    public ReplyToVerificationTokenHandler(ITenantConfigurationService config, ITenantStore tenants)
    {
        _config = config;
        _tenants = tenants;
    }

    public string Action => ActionKey;

    public static string RedeemerFor(string address) => RedeemerPrefix + address.Trim().ToLowerInvariant();

    public async Task<ActionTokenPreview?> PreviewAsync(ActionTokenClaim claim, CancellationToken cancellationToken = default)
    {
        var address = AddressOf(claim);
        if (address == null) return null;

        var settings = await LoadAsync(claim.TenantId, cancellationToken);
        var tenantName = await TenantNameAsync(claim.TenantId, cancellationToken);
        var preview = new ActionTokenPreview
        {
            Title = "Confirm reply-to inbox",
            Summary = $"Replies to email from {tenantName} will be delivered to {address}.",
            Description = tenantName,
        };

        if (string.Equals(settings.VerifiedReplyTo, address, StringComparison.OrdinalIgnoreCase))
        {
            preview.AlreadyActioned = true;
            preview.AlreadyActionedReason = "This inbox is already confirmed.";
        }
        else if (!string.Equals(settings.PendingReplyToAddress, address, StringComparison.OrdinalIgnoreCase))
        {
            preview.AlreadyActioned = true;
            preview.AlreadyActionedReason = "This confirmation link is no longer current; a different address was requested since.";
        }
        else
        {
            preview.Options.Add(new ActionTokenOption { Key = ConfirmOption, Label = "Confirm this inbox", Variant = "primary" });
        }
        return preview;
    }

    public async Task<ActionTokenExecuteResult> ExecuteAsync(ActionTokenClaim claim, string option, string? comments, CancellationToken cancellationToken = default)
    {
        if (option != ConfirmOption)
            throw new InvalidOperationException("Unknown option.");

        var address = AddressOf(claim) ?? throw new InvalidOperationException("This confirmation link is not valid.");
        var settings = await LoadAsync(claim.TenantId, cancellationToken);
        if (!string.Equals(settings.PendingReplyToAddress, address, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("This confirmation link is no longer current; a different address was requested since.");

        settings.ReplyToAddress = settings.PendingReplyToAddress;
        settings.ReplyToVerifiedAt = DateTime.UtcNow;
        settings.PendingReplyToAddress = null;
        settings.PendingReplyToSentAt = null;
        await _config.SetSectionAsync(claim.TenantId, TenantEmailSettings.SectionKey, settings, cancellationToken);

        var tenantName = await TenantNameAsync(claim.TenantId, cancellationToken);
        return new ActionTokenExecuteResult { Message = $"Confirmed. Replies to email from {tenantName} will now reach {address}." };
    }

    private static string? AddressOf(ActionTokenClaim claim)
        => claim.UserId.StartsWith(RedeemerPrefix, StringComparison.Ordinal) ? claim.UserId[RedeemerPrefix.Length..] : null;

    private async Task<TenantEmailSettings> LoadAsync(string tenantId, CancellationToken cancellationToken)
        => await _config.GetSectionAsync<TenantEmailSettings>(tenantId, TenantEmailSettings.SectionKey, cancellationToken) ?? new TenantEmailSettings();

    private async Task<string> TenantNameAsync(string tenantId, CancellationToken cancellationToken)
    {
        var tenant = await _tenants.GetByIdAsync(tenantId, cancellationToken);
        return tenant == null ? tenantId : (string.IsNullOrWhiteSpace(tenant.DisplayName) ? tenant.Name : tenant.DisplayName!);
    }
}

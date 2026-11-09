using Microsoft.Extensions.DependencyInjection;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oluso.Core.Authentication;
using Oluso.Core.Locale;
using Oluso.Core.Notifications;
using Oluso.Core.Realtime;
using Oluso.Core.Services;

namespace Oluso.Protocols.Ciba;

/// <summary>
/// Pushes the request to the user's signed-in apps over the realtime hub on the user-scoped
/// <see cref="CibaNotifications.RealtimeChannel"/> channel, which the mobile CIBA screen listens
/// to so a pending request appears without polling.
/// </summary>
public sealed class CibaRealtimeNotificationChannel : ICibaNotificationChannel
{
    public const string ChannelName = "realtime";

    private readonly IRealtimeBroadcaster _realtime;

    public CibaRealtimeNotificationChannel(IRealtimeBroadcaster realtime)
    {
        _realtime = realtime;
    }

    public string Name => ChannelName;

    public async Task<bool> NotifyAsync(CibaUserNotification notification, CancellationToken cancellationToken = default)
    {
        var request = notification.Request;
        await _realtime.BroadcastToUserAsync(
            notification.TenantId,
            notification.User.Id,
            CibaNotifications.RealtimeChannel,
            CibaNotifications.RequestedEvent,
            new
            {
                authReqId = request.AuthReqId,
                clientId = request.ClientId,
                clientName = notification.ClientName,
                bindingMessage = request.BindingMessage,
                expiresAt = request.ExpiresAt
            },
            cancellationToken);
        return true;
    }
}

/// <summary>
/// Sends a mobile push through the platform <see cref="IPushSender"/> (OneSignal, targeted by
/// the user's id as external id). Tapping it opens the request in the app's CIBA module.
/// Does nothing when push isn't configured, leaving the email channel to reach the user.
/// </summary>
public sealed class CibaPushNotificationChannel : ICibaNotificationChannel
{
    public const string ChannelName = "push";

    private readonly IPushSender? _push;

    public CibaPushNotificationChannel(IPushSender? push = null)
    {
        _push = push;
    }

    public string Name => ChannelName;

    public async Task<bool> NotifyAsync(CibaUserNotification notification, CancellationToken cancellationToken = default)
    {
        if (_push is not { IsEnabled: true })
        {
            return false;
        }

        var request = notification.Request;
        var body = string.IsNullOrWhiteSpace(request.BindingMessage)
            ? UiStringDefaults.Resolve("ciba_push_body", [notification.ClientName])
            : UiStringDefaults.Resolve("ciba_push_body_binding", notification.ClientName, request.BindingMessage);

        await _push.SendAsync(
            notification.User.Id,
            UiStringDefaults.Resolve("ciba_push_title", [notification.ClientName]),
            body,
            CibaNotifications.RequestedEvent,
            new Dictionary<string, string>
            {
                ["type"] = CibaNotifications.PushType,
                ["channel"] = CibaNotifications.PushType,
                ["authReqId"] = request.AuthReqId
            },
            cancellationToken);
        return true;
    }
}

/// <summary>
/// Emails the user an approve/deny link when the request wasn't pushed to their phone. The link
/// is a single-use action token (<see cref="CibaNotifications.DecideAction"/>) bound to the user
/// and the request, valid only until the request expires, opened on the workspace's magic-link
/// page. Only sent to a verified address, since the link can approve a sign-in.
/// </summary>
public sealed class CibaEmailNotificationChannel : ICibaNotificationChannel
{
    public const string ChannelName = "email";

    private readonly IActionTokenService _tokens;
    private readonly IServiceProvider _services;
    private readonly IOptions<OlusoOptions> _options;
    private readonly ILogger<CibaEmailNotificationChannel> _logger;
    private readonly IEmailSender? _email;
    private readonly IPlatformEmailTemplateService? _templates;

    public CibaEmailNotificationChannel(
        IActionTokenService tokens,
        IServiceProvider services,
        IOptions<OlusoOptions> options,
        ILogger<CibaEmailNotificationChannel> logger,
        IEmailSender? email = null,
        IPlatformEmailTemplateService? templates = null)
    {
        _tokens = tokens;
        _services = services;
        _options = options;
        _logger = logger;
        _email = email;
        _templates = templates;
    }

    public string Name => ChannelName;

    public async Task<bool> NotifyAsync(CibaUserNotification notification, CancellationToken cancellationToken = default)
    {
        if (notification.DeliveredVia.Contains(CibaPushNotificationChannel.ChannelName))
        {
            return false;
        }

        var user = notification.User;
        var request = notification.Request;

        if (_email == null)
        {
            _logger.LogWarning("CIBA email to user {UserId} not sent: no email sender is configured", user.Id);
            return false;
        }
        if (string.IsNullOrWhiteSpace(user.Email) || !user.EmailVerified)
        {
            _logger.LogInformation("CIBA email to user {UserId} not sent: no verified email address", user.Id);
            return false;
        }
        // Resolved per send: the decide handler depends on ICibaService, which depends on this channel.
        if (!_services.GetServices<IActionTokenHandler>().Any(h => h.Action == CibaNotifications.DecideAction))
        {
            _logger.LogWarning("CIBA email to user {UserId} not sent: no handler serves the approval link (is the account API registered?)", user.Id);
            return false;
        }

        var baseUrl = (_options.Value.WorkspaceBaseUrl ?? _options.Value.IssuerUri)?.TrimEnd('/');
        if (string.IsNullOrEmpty(baseUrl) || !Uri.TryCreate(baseUrl, UriKind.Absolute, out _))
        {
            _logger.LogWarning("CIBA email to user {UserId} not sent: no absolute WorkspaceBaseUrl or IssuerUri is configured for the approval link", user.Id);
            return false;
        }

        var lifetime = request.ExpiresAt - DateTime.UtcNow;
        if (lifetime <= TimeSpan.Zero)
        {
            return false;
        }

        var token = await _tokens.IssueAsync(
            notification.TenantId, user.Id, CibaNotifications.DecideAction,
            request.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), lifetime, cancellationToken);
        var link = $"{baseUrl}/m#tk={token}";
        var expiresIn = Durations.Describe(lifetime);

        var rendered = _templates == null ? null : await _templates.RenderForTenantAsync(
            notification.TenantId,
            PlatformTemplateNames.CibaRequest,
            new Dictionary<string, object>
            {
                ["approval_link"] = link,
                ["client_name"] = notification.ClientName,
                ["binding_message"] = request.BindingMessage ?? string.Empty,
                [PlatformTemplateScopeKeys.UserEmail] = user.Email,
                [PlatformTemplateScopeKeys.UserName] = user.DisplayName ?? user.Username,
                [PlatformTemplateScopeKeys.ExpiresIn] = expiresIn,
            },
            cancellationToken);

        var result = await _email.SendAsync(
            notification.TenantId,
            user.Email,
            rendered?.Subject ?? UiStringDefaults.Resolve("ciba_email_subject", [notification.ClientName]),
            rendered?.HtmlBody ?? BuildBody(notification.ClientName, request.BindingMessage, link, expiresIn),
            cancellationToken);

        if (!result.Success)
        {
            _logger.LogWarning("CIBA email to user {UserId} failed: {Error}", user.Id, result.Error);
            return false;
        }
        return true;
    }

    private static string BuildBody(string clientName, string? bindingMessage, string link, string expiresIn)
    {
        var client = WebUtility.HtmlEncode(clientName);
        var heading = UiStringDefaults.Resolve("ciba_email_subject", [client]);
        var rows = BrandedEmailLayout.Lead(UiStringDefaults.Resolve("ciba_email_lead", [client]));
        if (!string.IsNullOrWhiteSpace(bindingMessage))
        {
            rows += BrandedEmailLayout.Details(BrandedEmailLayout.Detail(
                UiStringDefaults.Resolve("ciba_email_binding_label"), WebUtility.HtmlEncode(bindingMessage)));
        }
        rows += BrandedEmailLayout.Button(link, UiStringDefaults.Resolve("ciba_email_button"), BrandedEmailLayout.DefaultAccent)
            + BrandedEmailLayout.Note(UiStringDefaults.Resolve("ciba_email_expires", [$"<strong>{WebUtility.HtmlEncode(expiresIn)}</strong>"]))
            + BrandedEmailLayout.Divider()
            + BrandedEmailLayout.LinkFallback(link, BrandedEmailLayout.DefaultAccent)
            + BrandedEmailLayout.Paragraph(UiStringDefaults.Resolve("ciba_email_ignore"));

        return BrandedEmailLayout.Document(
            title: heading,
            preheader: UiStringDefaults.Resolve("ciba_email_lead", [client]),
            accent: BrandedEmailLayout.DefaultAccent,
            brand: "",
            eyebrow: UiStringDefaults.Resolve("ciba_email_eyebrow"),
            heading: heading,
            rows: rows,
            footnote: UiStringDefaults.Resolve("ciba_email_footnote"));
    }
}

using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;

namespace Oluso.Protocols.Ciba;

/// <summary>
/// Tells the user about a pending CIBA request on every registered
/// <see cref="ICibaNotificationChannel"/>, in registration order: realtime to their signed-in
/// devices, push to the mobile app when push is configured, and email when push didn't go out.
/// Each channel is isolated, so a tenant without push still gets realtime and email, and one
/// failing channel never blocks the rest.
/// </summary>
public sealed class CibaUserNotificationService : ICibaUserNotificationService
{
    private readonly IEnumerable<ICibaNotificationChannel> _channels;
    private readonly IOlusoUserService _users;
    private readonly IClientStore _clients;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<CibaUserNotificationService> _logger;

    public CibaUserNotificationService(
        IEnumerable<ICibaNotificationChannel> channels,
        IOlusoUserService users,
        IClientStore clients,
        ITenantContext tenantContext,
        ILogger<CibaUserNotificationService> logger)
    {
        _channels = channels;
        _users = users;
        _clients = clients;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task NotifyUserAsync(CibaRequest request, CancellationToken cancellationToken = default)
    {
        if (request.ExpiresAt <= DateTime.UtcNow)
        {
            _logger.LogInformation("CIBA request for user {SubjectId} expired before it could be delivered", request.SubjectId);
            return;
        }

        var user = await _users.FindByIdAsync(request.SubjectId, cancellationToken);
        if (user == null)
        {
            _logger.LogWarning("CIBA notification skipped: user {SubjectId} not found", request.SubjectId);
            return;
        }

        var tenantId = request.TenantId ?? user.TenantId ?? _tenantContext.TenantId;
        if (string.IsNullOrEmpty(tenantId))
        {
            _logger.LogWarning("CIBA notification skipped: no tenant for user {SubjectId}", request.SubjectId);
            return;
        }

        var client = await _clients.FindClientByIdAsync(request.ClientId, cancellationToken);
        var notification = new CibaUserNotification
        {
            Request = request,
            TenantId = tenantId,
            User = user,
            ClientName = client?.ClientName is { } name && !string.IsNullOrWhiteSpace(name) ? name : request.ClientId
        };

        foreach (var channel in _channels)
        {
            try
            {
                if (await channel.NotifyAsync(notification, cancellationToken))
                {
                    notification.DeliveredVia.Add(channel.Name);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "CIBA {Channel} notification to user {SubjectId} failed", channel.Name, request.SubjectId);
            }
        }

        _logger.LogInformation(
            "CIBA request from client {ClientId} notified to user {SubjectId} via [{Channels}]",
            request.ClientId, request.SubjectId, string.Join(", ", notification.DeliveredVia));
    }
}

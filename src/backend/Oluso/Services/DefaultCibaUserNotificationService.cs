using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;

namespace Oluso.Services;

/// <summary>
/// Default implementation of ICibaUserNotificationService that logs notifications.
/// Applications should provide their own implementation to send actual notifications
/// via push notifications, email, SMS, or other channels.
/// </summary>
public class DefaultCibaUserNotificationService : ICibaUserNotificationService
{
    private readonly IClientStore _clientStore;
    private readonly ILogger<DefaultCibaUserNotificationService> _logger;

    public DefaultCibaUserNotificationService(
        IClientStore clientStore,
        ILogger<DefaultCibaUserNotificationService> logger)
    {
        _clientStore = clientStore;
        _logger = logger;
    }

    public async Task NotifyUserAsync(CibaRequest request, CancellationToken cancellationToken = default)
    {
        var client = await _clientStore.FindClientByIdAsync(request.ClientId, cancellationToken);
        var clientName = client?.ClientName ?? request.ClientId;

        _logger.LogInformation(
            "CIBA notification for user {SubjectId}: {ClientName} is requesting authentication. " +
            "AuthReqId: {AuthReqId}, BindingMessage: {BindingMessage}, " +
            "Scopes: {Scopes}, Expires: {ExpiresAt}",
            request.SubjectId,
            clientName,
            request.AuthReqId,
            request.BindingMessage ?? "(none)",
            request.RequestedScopes,
            request.ExpiresAt);

        // In a real implementation, you would:
        // 1. Send a push notification to the user's registered devices
        // 2. Send an email notification
        // 3. Send an SMS notification
        // 4. Trigger a webhook to a notification service
        //
        // The notification should include:
        // - The client name/logo
        // - The binding message (if provided)
        // - The requested scopes
        // - A link/action to approve or deny the request
        //
        // Example:
        // await _pushNotificationService.SendAsync(
        //     request.SubjectId,
        //     title: $"{clientName} wants to sign you in",
        //     body: request.BindingMessage ?? "Approve this authentication request?",
        //     data: new { authReqId = request.AuthReqId });
    }
}

using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Protocols.Services;

/// <summary>
/// Records when a client was last used to sign someone in or get tokens (the client's
/// "Last accessed"). Written at most once an hour per client, so busy clients don't add a
/// database write to every request; a failure is logged and never fails the request.
/// </summary>
public interface IClientUsageRecorder
{
    Task RecordAsync(string clientId, CancellationToken cancellationToken = default);
}

public sealed class ClientUsageRecorder : IClientUsageRecorder
{
    /// <summary>How stale LastAccessed may get before it is written again.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    private readonly IClientStore _clients;
    private readonly ILogger<ClientUsageRecorder> _logger;
    private readonly TimeProvider _clock;

    public ClientUsageRecorder(IClientStore clients, ILogger<ClientUsageRecorder> logger, TimeProvider? clock = null)
    {
        _clients = clients;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task RecordAsync(string clientId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(clientId)) return;
        try
        {
            await _clients.RecordAccessAsync(clientId, _clock.GetUtcNow().UtcDateTime, Interval, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Could not record last access for client {ClientId}", clientId);
        }
    }
}

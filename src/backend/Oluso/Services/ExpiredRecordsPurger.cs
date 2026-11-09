using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;

namespace Oluso.Services;

public sealed class ExpiredRecordsPurger : IExpiredRecordsPurger
{
    private readonly IPersistedGrantStore _grants;
    private readonly IServerSideSessionStore? _sessions;
    private readonly IPushedAuthorizationStore? _pushedAuthorization;
    private readonly IActionTokenStore? _actionTokens;
    private readonly ILogger<ExpiredRecordsPurger> _logger;

    public ExpiredRecordsPurger(
        IPersistedGrantStore grants,
        ILogger<ExpiredRecordsPurger> logger,
        IServerSideSessionStore? sessions = null,
        IPushedAuthorizationStore? pushedAuthorization = null,
        IActionTokenStore? actionTokens = null)
    {
        _grants = grants;
        _sessions = sessions;
        _pushedAuthorization = pushedAuthorization;
        _actionTokens = actionTokens;
        _logger = logger;
    }

    public async Task<ExpiredRecordsPurgeResult> PurgeAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var grants = await _grants.RemoveExpiredAsync(cancellationToken);
        var sessions = _sessions is null ? 0 : await _sessions.RemoveExpiredSessionsAsync(cancellationToken);
        var pushed = 0;
        if (_pushedAuthorization is not null)
        {
            await _pushedAuthorization.RemoveExpiredAsync(cancellationToken);
        }
        var actionTokens = _actionTokens is null
            ? 0
            : await _actionTokens.RemoveExpiredAsync(DateTime.UtcNow, cancellationToken);
        stopwatch.Stop();

        if (grants > 0 || sessions > 0 || actionTokens > 0)
        {
            _logger.LogInformation(
                "Expired records purged in {ElapsedMs} ms: {Grants} grants, {Sessions} server-side sessions, {ActionTokens} action tokens",
                stopwatch.ElapsedMilliseconds, grants, sessions, actionTokens);
        }
        return new ExpiredRecordsPurgeResult(grants, sessions, pushed, actionTokens);
    }
}

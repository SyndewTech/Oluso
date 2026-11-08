namespace Oluso.Core.Services;

/// <summary>
/// Removes protocol records that have outlived their expiry: persisted grants
/// (refresh and reference tokens), server-side sessions, pushed authorization
/// requests and single-use action tokens (magic links, organisation sign-in
/// hand-off codes). Without it those tables only shrink when someone revokes by
/// hand, and every "sessions" list has to filter dead rows on read.
/// </summary>
public interface IExpiredRecordsPurger
{
    Task<ExpiredRecordsPurgeResult> PurgeAsync(CancellationToken cancellationToken = default);
}

public sealed record ExpiredRecordsPurgeResult(int PersistedGrants, int ServerSideSessions, int PushedAuthorizationRequests, int ActionTokens);

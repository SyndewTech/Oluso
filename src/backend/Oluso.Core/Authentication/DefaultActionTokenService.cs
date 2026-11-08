using System.Security.Cryptography;
using System.Text;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Core.Authentication;

/// <summary>
/// Default <see cref="IActionTokenService"/>. Tokens are 256 random bits
/// rendered as URL-safe base64 (~43 chars), stored hashed via SHA-256.
/// Redeem is a two-step lookup-then-update that uses the store's atomic
/// <c>TryConsumeAsync</c> so concurrent redeems of the same raw value can
/// only succeed once.
/// </summary>
public sealed class DefaultActionTokenService : IActionTokenService
{
    private readonly IActionTokenStore _store;
    private readonly TimeProvider _clock;

    public DefaultActionTokenService(IActionTokenStore store, TimeProvider? clock = null)
    {
        _store = store;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<string> IssueAsync(
        string tenantId,
        string userId,
        string action,
        string subjectId,
        TimeSpan ttl,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(tenantId)) throw new ArgumentException("tenantId required", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("userId required", nameof(userId));
        if (string.IsNullOrWhiteSpace(action)) throw new ArgumentException("action required", nameof(action));
        if (string.IsNullOrWhiteSpace(subjectId)) throw new ArgumentException("subjectId required", nameof(subjectId));
        if (ttl <= TimeSpan.Zero) throw new ArgumentException("ttl must be positive", nameof(ttl));

        var rawBytes = RandomNumberGenerator.GetBytes(32);
        var raw = ToUrlBase64(rawBytes);

        var token = new ActionToken
        {
            TenantId = tenantId,
            TokenHash = HashRaw(raw),
            UserId = userId,
            Action = action,
            SubjectId = subjectId,
            ExpiresAt = _clock.GetUtcNow().UtcDateTime + ttl,
        };

        await _store.CreateAsync(token, cancellationToken);
        return raw;
    }

    public async Task<ActionTokenClaim?> RedeemAsync(string rawToken, CancellationToken cancellationToken = default)
    {
        var existing = await LoadValidAsync(rawToken, cancellationToken);
        if (existing == null) return null;

        var consumed = await _store.TryConsumeAsync(
            existing.Id, _clock.GetUtcNow().UtcDateTime, cancellationToken);
        if (!consumed)
        {
            // Lost a race against a concurrent redeem of the same raw token.
            return null;
        }

        return ToClaim(existing);
    }

    public async Task<ActionTokenClaim?> PeekAsync(string rawToken, CancellationToken cancellationToken = default)
    {
        var existing = await LoadValidAsync(rawToken, cancellationToken);
        return existing == null ? null : ToClaim(existing);
    }

    public async Task ReleaseAsync(string rawToken, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawToken)) return;
        var existing = await _store.GetByHashAsync(HashRaw(rawToken), cancellationToken);
        if (existing != null) await _store.ReleaseAsync(existing.Id, cancellationToken);
    }

    private async Task<ActionToken?> LoadValidAsync(string rawToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken)) return null;

        var hash = HashRaw(rawToken);
        var existing = await _store.GetByHashAsync(hash, cancellationToken);
        if (existing == null) return null;
        if (existing.ConsumedAt != null) return null;
        if (existing.ExpiresAt <= _clock.GetUtcNow().UtcDateTime) return null;
        return existing;
    }

    private static ActionTokenClaim ToClaim(ActionToken t)
        => new(t.TenantId ?? "", t.UserId, t.Action, t.SubjectId);

    private static string HashRaw(string raw)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes);
    }

    private static string ToUrlBase64(byte[] bytes)
    {
        // Standard base64 with URL-safe substitutions and no padding so
        // tokens flow through query strings cleanly.
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}

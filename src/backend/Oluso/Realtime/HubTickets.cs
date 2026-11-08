using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;

namespace Oluso.Realtime;

/// <summary>
/// Short-lived tickets that let a browser open the realtime hub without putting its access token in a URL.
///
/// A WebSocket upgrade cannot carry an <c>Authorization</c> header, so SignalR passes its credential in the
/// <c>access_token</c> query string. A sender-constrained access token (DPoP, mTLS) cannot be used that way —
/// there is no proof — and any token in a URL can end up in logs. Instead the client asks for a ticket over a
/// normal authenticated call and hands the hub the ticket: an opaque random value, valid only on the hub for
/// <see cref="Lifetime"/>, that stands for the caller's identity at the time it was issued.
/// </summary>
public static class HubTicket
{
    public const string Prefix = "rt_";

    /// <summary>Long enough for SignalR's negotiate and connect, which both present it.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromSeconds(60);

    public static bool IsTicket(string? value) => value != null && value.StartsWith(Prefix, StringComparison.Ordinal);

    internal static string NewValue() => Prefix + Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

    internal static string Serialize(ClaimsPrincipal user)
    {
        var identity = user.Identities.First(i => i.IsAuthenticated);
        return JsonSerializer.Serialize(new StoredIdentity(
            identity.NameClaimType,
            identity.RoleClaimType,
            identity.Claims.Select(c => new StoredClaim(c.Type, c.Value, c.ValueType)).ToList()));
    }

    internal static ClaimsPrincipal Deserialize(string stored, string authenticationType)
    {
        var identity = JsonSerializer.Deserialize<StoredIdentity>(stored)!;
        return new ClaimsPrincipal(new ClaimsIdentity(
            identity.Claims.Select(c => new Claim(c.Type, c.Value, c.ValueType)),
            authenticationType,
            identity.NameClaimType,
            identity.RoleClaimType));
    }

    private sealed record StoredIdentity(string NameClaimType, string RoleClaimType, List<StoredClaim> Claims);

    private sealed record StoredClaim(string Type, string Value, string ValueType);
}

/// <summary>Issues and redeems <see cref="HubTicket"/>s.</summary>
public interface IHubTicketStore
{
    /// <summary>A new ticket standing for <paramref name="user"/> for <see cref="HubTicket.Lifetime"/>.</summary>
    Task<string> IssueAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default);

    /// <summary>The identity a live ticket stands for, or null when it is unknown or has expired.</summary>
    Task<ClaimsPrincipal?> RedeemAsync(string ticket, string authenticationType, CancellationToken cancellationToken = default);
}

/// <summary>Tickets held by this server: for a single instance, as with the in-memory realtime backplane.</summary>
public sealed class MemoryHubTicketStore : IHubTicketStore
{
    private readonly IMemoryCache _cache;

    public MemoryHubTicketStore(IMemoryCache cache) => _cache = cache;

    public Task<string> IssueAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        var ticket = HubTicket.NewValue();
        _cache.Set(Key(ticket), HubTicket.Serialize(user), HubTicket.Lifetime);
        return Task.FromResult(ticket);
    }

    public Task<ClaimsPrincipal?> RedeemAsync(string ticket, string authenticationType, CancellationToken cancellationToken = default) =>
        Task.FromResult(_cache.TryGetValue(Key(ticket), out string? stored) && stored != null
            ? HubTicket.Deserialize(stored, authenticationType)
            : null);

    private static string Key(string ticket) => "oluso:hub-ticket:" + ticket;
}

/// <summary>Tickets held in Redis, so any instance behind the load balancer can redeem one another issued —
/// used whenever the realtime Redis backplane is configured.</summary>
public sealed class RedisHubTicketStore : IHubTicketStore
{
    private readonly Lazy<Task<ConnectionMultiplexer>> _redis;

    public RedisHubTicketStore(string connectionString) =>
        _redis = new Lazy<Task<ConnectionMultiplexer>>(() => ConnectionMultiplexer.ConnectAsync(connectionString));

    public async Task<string> IssueAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        var ticket = HubTicket.NewValue();
        var db = (await _redis.Value).GetDatabase();
        await db.StringSetAsync(Key(ticket), HubTicket.Serialize(user), HubTicket.Lifetime);
        return ticket;
    }

    public async Task<ClaimsPrincipal?> RedeemAsync(string ticket, string authenticationType, CancellationToken cancellationToken = default)
    {
        var stored = await (await _redis.Value).GetDatabase().StringGetAsync(Key(ticket));
        return stored.HasValue ? HubTicket.Deserialize(stored.ToString(), authenticationType) : null;
    }

    private static RedisKey Key(string ticket) => "oluso:hub-ticket:" + ticket;
}

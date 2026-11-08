using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Oluso.Core.Authorization;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Realtime;

namespace Oluso.Realtime;

/// <summary>
/// Single SignalR hub the platform exposes for module realtime updates.
/// Authenticated clients connect, the hub auto-joins them to a
/// per-tenant and per-user group, and modules push events through the
/// <see cref="Core.Realtime.IRealtimeBroadcaster"/> primitive — they
/// don't talk to the hub directly.
///
/// <para>One hub keeps the wire surface small: every module shares the
/// same authenticated connection and just filters by event channel on
/// the client.</para>
///
/// <para><b>Permission-gated channels.</b> A channel registered in
/// <see cref="IRealtimeChannelRegistry"/> (e.g. live asset GPS) is NOT
/// delivered tenant-wide. A client must call <see cref="Subscribe"/>; the
/// hub checks the channel's required permission against the connection's
/// principal and only then joins the channel group the broadcaster targets.
/// Ungated channels need no subscribe — they stay tenant-wide and are
/// filtered on the client, exactly as before.</para>
///
/// <para><b>Watched entities.</b> A page showing one record calls <see cref="Watch"/>;
/// the entity type's <see cref="IRealtimeEntityAccess"/> authorizes it and the
/// connection joins the entity group, so changes to the record reach everyone
/// looking at it (the author's own other pages included), not just its
/// participants.</para>
/// </summary>
[Authorize]
public sealed class RealtimeHub : Hub
{
    private readonly ITenantContext _tenantContext;
    private readonly IRealtimeChannelRegistry _channels;
    private readonly IAuthorizationService _authorization;
    private readonly IEnumerable<IRealtimeEntityAccess> _entityAccess;
    private readonly IScopeRestrictedClientResolver? _scopeRestrictedClients;
    private readonly ILogger<RealtimeHub> _logger;

    public RealtimeHub(
        ITenantContext tenantContext,
        IRealtimeChannelRegistry channels,
        IAuthorizationService authorization,
        IEnumerable<IRealtimeEntityAccess> entityAccess,
        ILogger<RealtimeHub> logger,
        IScopeRestrictedClientResolver? scopeRestrictedClients = null)
    {
        _tenantContext = tenantContext;
        _channels = channels;
        _authorization = authorization;
        _entityAccess = entityAccess;
        _logger = logger;
        _scopeRestrictedClients = scopeRestrictedClients;
    }

    public const string Path = "/hubs/realtime";

    /// <summary>
    /// Method name SignalR clients use to receive every event the server
    /// pushes — the payload is a <see cref="Core.Realtime.RealtimeEvent"/>.
    /// </summary>
    public const string EventMethodName = "event";

    public override async Task OnConnectedAsync()
    {
        var tenantId = _tenantContext?.TenantId;
        var userId = Context.UserIdentifier;

        // Diagnostic dump for tracking down why tenant/user end up null in
        // OnConnectedAsync — usually means either the auth scheme didn't
        // run on the WS upgrade or the tenant middleware didn't resolve.
        // Pinpoints which side is broken without a debugger attach.
        var identity = Context.User?.Identity;
        var isAuth = identity?.IsAuthenticated ?? false;
        var authType = identity?.AuthenticationType ?? "(no-identity)";
        var nameId = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var sub = Context.User?.FindFirst("sub")?.Value;
        var name = identity?.Name;
        _logger.LogInformation(
            "Realtime hub connecting: isAuth={IsAuth} authType={AuthType} userIdentifier={UserId} nameIdentifier={NameId} sub={Sub} name={Name} tenant={TenantId} conn={ConnectionId}",
            isAuth, authType, userId, nameId, sub, name, tenantId, Context.ConnectionId);

        // Belt-and-braces: if UserIdentifier wasn't populated (no
        // NameIdentifier claim mapped) but a sub claim is present, use
        // that. SignalR's default IUserIdProvider only reads
        // ClaimTypes.NameIdentifier, so JWTs whose handler doesn't
        // remap 'sub' to it lose the user-group join.
        if (string.IsNullOrEmpty(userId))
        {
            userId = CallerUserId();
        }

        if (!string.IsNullOrEmpty(tenantId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, TenantGroup(tenantId));
        }
        if (!string.IsNullOrEmpty(userId) && !string.IsNullOrEmpty(tenantId))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(tenantId, userId));
        }

        _logger.LogInformation(
            "Realtime hub connected: tenant={TenantId} user={UserId} conn={ConnectionId}",
            tenantId, userId, Context.ConnectionId);

        await base.OnConnectedAsync();
    }

    /// <summary>
    /// Opt in to a channel. Every channel must be registered in the
    /// <see cref="IRealtimeChannelRegistry"/>; an unknown channel is rejected
    /// (fail-closed). A <see cref="RealtimeChannelScope.User"/> channel needs no
    /// group — the connection already receives its own user-targeted events — so
    /// this just acknowledges. A <see cref="RealtimeChannelScope.Group"/> channel
    /// is authorized against its required permission (if any) and only then joins
    /// the channel group the broadcaster targets. Throws <see cref="HubException"/>
    /// on an unknown channel or a permission failure so the client can surface it.
    /// </summary>
    public async Task Subscribe(string channel)
    {
        var tenantId = _tenantContext?.TenantId;
        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrWhiteSpace(channel))
            throw new HubException("A tenant context and channel are required to subscribe.");

        var descriptor = _channels.Resolve(channel);
        if (descriptor is null)
        {
            _logger.LogWarning(
                "Realtime subscribe rejected: unknown channel={Channel} tenant={TenantId} conn={ConnectionId}",
                channel, tenantId, Context.ConnectionId);
            throw new HubException($"Unknown realtime channel '{channel}'.");
        }

        // User-scoped channels deliver via the per-user group joined on connect;
        // nothing more to do than acknowledge the intent.
        if (descriptor.Scope == RealtimeChannelScope.User)
            return;

        if (descriptor.RequiredPermission is { } permission)
        {
            var policy = $"{PermissionPolicyConstants.PolicyPrefix}{permission}";
            var result = await _authorization.AuthorizeAsync(Context.User!, resource: null, policy);
            if (!result.Succeeded)
            {
                _logger.LogInformation(
                    "Realtime subscribe denied: channel={Channel} permission={Permission} tenant={TenantId} conn={ConnectionId}",
                    channel, permission, tenantId, Context.ConnectionId);
                throw new HubException($"Not authorized to subscribe to '{channel}'.");
            }
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, ChannelGroup(tenantId, channel));
        _logger.LogInformation(
            "Realtime subscribed: channel={Channel} tenant={TenantId} conn={ConnectionId}",
            channel, tenantId, Context.ConnectionId);
    }

    /// <summary>Leave a group channel. No-op for user channels / unknown channels.</summary>
    public async Task Unsubscribe(string channel)
    {
        var tenantId = _tenantContext?.TenantId;
        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrWhiteSpace(channel)) return;
        var descriptor = _channels.Resolve(channel);
        if (descriptor is not { Scope: RealtimeChannelScope.Group }) return;
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, ChannelGroup(tenantId, channel));
    }

    /// <summary>
    /// Start receiving pushes for one entity (e.g. a comment landing on the approval request this
    /// page shows). The entity type's <see cref="IRealtimeEntityAccess"/> decides, with the same rule
    /// the entity's read API uses; an entity type without one is refused (fail-closed), and a
    /// scope-restricted client also needs one of the type's restricted-client scopes, as on the
    /// user-facing APIs. Throws <see cref="HubException"/> on a refusal so the client can surface it.
    /// Group membership lasts for the connection: clients watch again after a reconnect.
    /// </summary>
    public async Task Watch(string entityType, string entityId)
    {
        var tenantId = _tenantContext?.TenantId;
        var userId = CallerUserId();
        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrEmpty(userId)
            || string.IsNullOrWhiteSpace(entityType) || string.IsNullOrWhiteSpace(entityId))
        {
            throw new HubException("A tenant context, user and entity are required to watch.");
        }

        var access = _entityAccess.FirstOrDefault(a => string.Equals(a.EntityType, entityType, StringComparison.Ordinal));
        if (access is null)
        {
            _logger.LogWarning(
                "Realtime watch rejected: unknown entityType={EntityType} tenant={TenantId} conn={ConnectionId}",
                entityType, tenantId, Context.ConnectionId);
            throw new HubException($"Unknown realtime entity type '{entityType}'.");
        }

        var cancellationToken = Context.ConnectionAborted;
        if (!await ClientMayWatchAsync(access, cancellationToken)
            || !await access.CanWatchAsync(tenantId, entityId, userId, cancellationToken))
        {
            _logger.LogInformation(
                "Realtime watch denied: entity={EntityType}/{EntityId} user={UserId} tenant={TenantId} conn={ConnectionId}",
                entityType, entityId, userId, tenantId, Context.ConnectionId);
            throw new HubException($"Not authorized to watch this {entityType}.");
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, EntityGroup(tenantId, entityType, entityId), cancellationToken);
        _logger.LogInformation(
            "Realtime watching: entity={EntityType}/{EntityId} user={UserId} tenant={TenantId} conn={ConnectionId}",
            entityType, entityId, userId, tenantId, Context.ConnectionId);
    }

    /// <summary>Stop receiving an entity's pushes. Leaving needs no check; a group never joined is a no-op.</summary>
    public async Task Unwatch(string entityType, string entityId)
    {
        var tenantId = _tenantContext?.TenantId;
        if (string.IsNullOrEmpty(tenantId) || string.IsNullOrWhiteSpace(entityType) || string.IsNullOrWhiteSpace(entityId)) return;
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, EntityGroup(tenantId, entityType, entityId));
    }

    /// <summary>
    /// The connection's user. SignalR's default <see cref="IUserIdProvider"/> only reads
    /// <see cref="ClaimTypes.NameIdentifier"/>, so a JWT whose handler doesn't remap <c>sub</c>
    /// to it would otherwise lose the user.
    /// </summary>
    private string? CallerUserId()
    {
        if (!string.IsNullOrEmpty(Context.UserIdentifier)) return Context.UserIdentifier;
        return Context.User?.FindFirst("sub")?.Value ?? Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
    }

    /// <summary>
    /// A scope-restricted client (see <see cref="ScopeRestrictedClients"/>) may watch only with one of
    /// the entity type's restricted-client scopes; every other caller passes.
    /// </summary>
    private async Task<bool> ClientMayWatchAsync(IRealtimeEntityAccess access, CancellationToken cancellationToken)
    {
        var clientId = Context.User?.FindFirst("client_id")?.Value;
        if (string.IsNullOrEmpty(clientId) || _scopeRestrictedClients is null) return true;
        if (!await _scopeRestrictedClients.IsRestrictedAsync(clientId, cancellationToken)) return true;

        var granted = Context.User!.GetGrantedScopes();
        return access.RestrictedClientScopes.Any(s => granted.Contains(s, StringComparer.Ordinal));
    }

    /// <summary>Group name shared by every connection from <paramref name="tenantId"/>.</summary>
    public static string TenantGroup(string tenantId) => $"tenant:{tenantId}";

    /// <summary>Group name for a specific user inside a tenant.</summary>
    public static string UserGroup(string tenantId, string userId) => $"tenant:{tenantId}:user:{userId}";

    /// <summary>
    /// Group name for a subscribed channel inside a tenant. Only connections the
    /// hub authorized via <see cref="Subscribe"/> are in it, so the broadcaster
    /// targeting this group delivers only to those subscribers.
    /// </summary>
    public static string ChannelGroup(string tenantId, string channel) => $"tenant:{tenantId}:channel:{channel}";

    /// <summary>
    /// Group name for one watched entity inside a tenant. Only connections the hub authorized via
    /// <see cref="Watch"/> are in it.
    /// </summary>
    public static string EntityGroup(string tenantId, string entityType, string entityId)
        => $"tenant:{tenantId}:entity:{entityType}:{entityId}";
}

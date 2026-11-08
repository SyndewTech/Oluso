using Microsoft.Extensions.Logging;
using Oluso.Core.Authorization;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Licensing;
using Oluso.Core.Notifications;
using Oluso.Core.Realtime;

namespace Oluso.Core.Services;

/// <summary>
/// Who granted a role: an admin user, or a system source (SCIM, an invitation,
/// a sign-in journey) when no user acted.
/// </summary>
public sealed record RoleGrantActor(string? UserId, string? DisplayName, string? IpAddress = null)
{
    public static RoleGrantActor System(string source) => new(null, source);

    /// <summary>Value stored as the assignment's AssignedBy and the event's AdminUserId.</summary>
    public string Identifier => UserId ?? DisplayName ?? "system";
}

/// <summary>
/// Tells the existing admins of a module, in the tenant the grant was made in,
/// that someone else has just been given that module's administration permission.
/// Called by every path that grants roles to a user; seeding and startup
/// reconciliation write assignments directly and don't notify.
/// </summary>
public interface IModuleAdminAssignmentNotifier
{
    /// <summary>
    /// For each <c>&lt;module&gt;.administration</c> permission on
    /// <paramref name="grantedRoles"/>, raises
    /// <see cref="ModuleAdminPermissionAssignedEvent"/> and notifies the module's
    /// existing admins in <paramref name="tenantId"/>. Pass only roles the user
    /// did not already hold there.
    /// </summary>
    Task NotifyAsync(
        string tenantId,
        string targetUserId,
        IEnumerable<OlusoRole> grantedRoles,
        RoleGrantActor actor,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// For each <c>&lt;module&gt;.administration</c> permission on
    /// <paramref name="removedRoles"/> that the user no longer holds in
    /// <paramref name="tenantId"/> through another role, raises
    /// <see cref="ModuleAdminPermissionRemovedEvent"/> and notifies the module's
    /// remaining admins there. Call after the assignments are removed, and pass
    /// only roles the user actually held.
    /// </summary>
    Task NotifyRemovedAsync(
        string tenantId,
        string targetUserId,
        IEnumerable<OlusoRole> removedRoles,
        RoleGrantActor actor,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// A user became, or stopped being, an Owner/Admin of an organization. That
    /// membership carries the OrgAdmin role's permissions into every tenant of
    /// the organization (see <see cref="EffectiveRoleResolver"/>), so for each
    /// administration permission the OrgAdmin role carries, the module's other
    /// admins in each of those tenants are notified. Nothing happens when the
    /// OrgAdmin role carries no administration permission. Call after the
    /// membership change, and only on an actual transition.
    /// </summary>
    Task NotifyOrganizationAdminChangedAsync(
        string organizationId,
        string userId,
        bool removed,
        RoleGrantActor actor,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Users who hold exactly <paramref name="administrationPermission"/> in
    /// <paramref name="tenantId"/>: through a role assigned in that tenant, or
    /// through the organization-admin grant when the OrgAdmin role carries it.
    /// Wildcards (<c>*</c>, <c>module.*</c>) never match an administration
    /// permission, so SuperAdmin and module-wide holders only count when they
    /// hold it explicitly.
    /// </summary>
    Task<IReadOnlyList<string>> FindModuleAdminIdsAsync(
        string tenantId, string administrationPermission, CancellationToken cancellationToken = default);
}

/// <summary>
/// Grants a role to a user in a tenant and, when that is a new assignment,
/// raises <see cref="UserRoleAssignedEvent"/> and runs
/// <see cref="IModuleAdminAssignmentNotifier"/>. The single-role grant
/// paths (module role pages, service API, SCIM, invitations, onboarding, sign-in
/// journeys) go through here.
/// </summary>
public interface IRoleGrantService
{
    /// <summary>
    /// Returns true when the assignment is new. <paramref name="tenantId"/> null
    /// means the request's tenant, or the user's home tenant at platform scope.
    /// </summary>
    Task<bool> GrantAsync(
        string roleId,
        string userId,
        string? tenantId,
        RoleGrantActor actor,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the role from the user in the tenant and, when they held it
    /// there, runs <see cref="IModuleAdminAssignmentNotifier.NotifyRemovedAsync"/>.
    /// Returns true when an assignment was removed.
    /// </summary>
    Task<bool> RevokeAsync(
        string roleId,
        string userId,
        string? tenantId,
        RoleGrantActor actor,
        CancellationToken cancellationToken = default);
}

public sealed class ModuleAdminAssignmentNotifier : IModuleAdminAssignmentNotifier
{
    private const string AdministrationSuffix = ".administration";

    private readonly IRoleStore _roleStore;
    private readonly ITenantStore _tenantStore;
    private readonly IOlusoUserService _users;
    private readonly IOlusoEventService _events;
    private readonly IOrganizationMembershipStore? _organizationMemberships;
    private readonly IRealtimeBroadcaster? _realtime;
    private readonly IPushSender? _push;
    private readonly IUserNotificationGate? _gate;
    private readonly ILogger<ModuleAdminAssignmentNotifier> _logger;

    /// <summary>Realtime channel the in-app notification is published on (the action-center toast).</summary>
    public const string ActionCenterChannel = "action-center";

    public ModuleAdminAssignmentNotifier(
        IRoleStore roleStore,
        ITenantStore tenantStore,
        IOlusoUserService users,
        IOlusoEventService events,
        ILogger<ModuleAdminAssignmentNotifier> logger,
        IOrganizationMembershipStore? organizationMemberships = null,
        IRealtimeBroadcaster? realtime = null,
        IPushSender? push = null,
        IUserNotificationGate? gate = null)
    {
        _roleStore = roleStore;
        _tenantStore = tenantStore;
        _users = users;
        _events = events;
        _logger = logger;
        _organizationMemberships = organizationMemberships;
        _realtime = realtime;
        _push = push;
        _gate = gate;
    }

    /// <summary>
    /// <c>&lt;module&gt;.administration</c> exactly: one namespace segment, then
    /// "administration". Wildcards and deeper permissions don't qualify.
    /// </summary>
    public static bool IsModuleAdministrationPermission(string permission)
    {
        if (!permission.EndsWith(AdministrationSuffix, StringComparison.OrdinalIgnoreCase)) return false;
        var prefix = permission[..^AdministrationSuffix.Length];
        return prefix.Length > 0 && !prefix.Contains('.') && !prefix.Contains('*');
    }

    public Task NotifyAsync(
        string tenantId,
        string targetUserId,
        IEnumerable<OlusoRole> grantedRoles,
        RoleGrantActor actor,
        CancellationToken cancellationToken = default)
        => NotifyChangeAsync(removed: false, tenantId, targetUserId, grantedRoles, actor, cancellationToken);

    public Task NotifyRemovedAsync(
        string tenantId,
        string targetUserId,
        IEnumerable<OlusoRole> removedRoles,
        RoleGrantActor actor,
        CancellationToken cancellationToken = default)
        => NotifyChangeAsync(removed: true, tenantId, targetUserId, removedRoles, actor, cancellationToken);

    public async Task NotifyOrganizationAdminChangedAsync(
        string organizationId,
        string userId,
        bool removed,
        RoleGrantActor actor,
        CancellationToken cancellationToken = default)
    {
        var orgAdminRole = await _roleStore.GetByNameAsync(EffectiveRoleResolver.OrgAdminRoleName, null, cancellationToken);
        if (orgAdminRole == null) return;

        foreach (var tenant in await _tenantStore.GetByOrganizationAsync(organizationId, cancellationToken))
        {
            await NotifyChangeAsync(removed, tenant.Id, userId, new[] { orgAdminRole }, actor, cancellationToken);
        }
    }

    private async Task NotifyChangeAsync(
        bool removed,
        string tenantId,
        string targetUserId,
        IEnumerable<OlusoRole> roles,
        RoleGrantActor actor,
        CancellationToken cancellationToken)
    {
        // One notification per administration permission, however many of the
        // roles carry it.
        var changes = new Dictionary<string, OlusoRole>(StringComparer.OrdinalIgnoreCase);
        foreach (var role in roles)
        {
            foreach (var claim in await _roleStore.GetRoleClaimsAsync(role.Id, cancellationToken))
            {
                if (claim.Type == OlusoRole.PermissionClaimType && IsModuleAdministrationPermission(claim.Value))
                {
                    changes.TryAdd(claim.Value.ToLowerInvariant(), role);
                }
            }
        }

        if (changes.Count == 0) return;

        foreach (var (permission, role) in changes)
        {
            // Resolved after the change, so on removal these are the remaining admins.
            var holderIds = await FindModuleAdminIdsAsync(tenantId, permission, cancellationToken);

            // Still an admin through another role: nothing was lost.
            if (removed && holderIds.Contains(targetUserId)) continue;

            var adminIds = holderIds
                .Where(id => id != targetUserId && id != actor.UserId)
                .ToList();

            var people = await _users.FindByIdsAcrossTenantsAsync(
                adminIds.Append(targetUserId).ToList(), cancellationToken);
            var recipients = adminIds
                .Where(id => people.TryGetValue(id, out var u) && u.IsActive)
                .Select(id => people[id])
                .ToList();
            people.TryGetValue(targetUserId, out var target);

            var recipientIds = recipients.Select(r => r.Id).ToList();
            var recipientEmails = recipients
                .Select(r => r.Email)
                .Where(e => !string.IsNullOrEmpty(e))
                .Select(e => e!)
                .ToList();

            ModuleAdminPermissionChangedEvent evt = removed
                ? new ModuleAdminPermissionRemovedEvent
                {
                    TenantId = tenantId,
                    AdminUserId = actor.UserId ?? "system",
                    AdminUserName = actor.DisplayName ?? actor.UserId,
                    IpAddress = actor.IpAddress,
                    ResourceId = targetUserId,
                    ResourceName = target?.Email,
                    ModuleAdminPermission = permission,
                    ModuleName = ModuleNameOf(permission),
                    TargetUserId = targetUserId,
                    TargetUserEmail = target?.Email,
                    TargetUserName = DisplayNameOf(target),
                    RoleName = role.DisplayName ?? role.Name ?? role.Id,
                    ExistingModuleAdminIds = recipientIds,
                    ExistingModuleAdminEmails = recipientEmails,
                }
                : new ModuleAdminPermissionAssignedEvent
                {
                    TenantId = tenantId,
                    AdminUserId = actor.UserId ?? "system",
                    AdminUserName = actor.DisplayName ?? actor.UserId,
                    IpAddress = actor.IpAddress,
                    ResourceId = targetUserId,
                    ResourceName = target?.Email,
                    ModuleAdminPermission = permission,
                    ModuleName = ModuleNameOf(permission),
                    TargetUserId = targetUserId,
                    TargetUserEmail = target?.Email,
                    TargetUserName = DisplayNameOf(target),
                    RoleName = role.DisplayName ?? role.Name ?? role.Id,
                    ExistingModuleAdminIds = recipientIds,
                    ExistingModuleAdminEmails = recipientEmails,
                };

            // Email goes out through the seeded workflow on this event
            // (security.module_admin_assigned / _removed); in-app is sent here.
            await _events.RaiseAsync(evt, cancellationToken);
            await NotifyInAppAsync(evt, removed, cancellationToken);

            _logger.LogInformation(
                "Module admin permission {Permission} {Change} {UserId} in tenant {TenantId} via role {Role} by {Actor}; notifying {Count} admin(s)",
                permission, removed ? "removed from" : "granted to", targetUserId, tenantId, role.Name, actor.Identifier, recipients.Count);
        }
    }

    /// <summary>
    /// The platform's in-app notification: the action-center realtime payload
    /// the toast listeners render ({title, message, type}), plus mobile push —
    /// the same delivery the workflow push step makes, behind the same user
    /// notification gate (snooze, quiet hours, muted event types). Best-effort:
    /// a delivery failure never undoes the grant.
    /// </summary>
    private async Task NotifyInAppAsync(ModuleAdminPermissionChangedEvent evt, bool removed, CancellationToken cancellationToken)
    {
        var eventType = evt.WebhookEventType!;
        var who = evt.TargetUserName ?? evt.TargetUserEmail ?? evt.TargetUserId;
        var by = evt.AdminUserName ?? evt.AdminUserId;
        var title = removed
            ? $"{evt.ModuleName} administrator removed"
            : $"New {evt.ModuleName} administrator";
        var message = removed
            ? $"{who} is no longer a {evt.ModuleName} administrator: the {evt.RoleName} role, which carried {evt.ModuleAdminPermission}, was removed by {by}."
            : $"{who} was given the {evt.RoleName} role, which carries {evt.ModuleAdminPermission}, by {by}.";

        foreach (var adminId in evt.ExistingModuleAdminIds)
        {
            try
            {
                if (_gate != null
                    && await _gate.ShouldSuppressAsync(evt.TenantId!, adminId, eventType, cancellationToken: cancellationToken))
                {
                    continue;
                }

                if (_realtime != null)
                {
                    await _realtime.BroadcastToUserAsync(
                        evt.TenantId!, adminId, ActionCenterChannel, eventType,
                        new { title, message, type = eventType, userId = evt.TargetUserId },
                        cancellationToken);
                }

                if (_push != null)
                {
                    await _push.SendAsync(
                        adminId, title, message, eventType,
                        new Dictionary<string, string> { ["type"] = eventType, ["userId"] = evt.TargetUserId },
                        cancellationToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "In-app module admin notification to {UserId} failed", adminId);
            }
        }
    }

    public async Task<IReadOnlyList<string>> FindModuleAdminIdsAsync(
        string tenantId, string administrationPermission, CancellationToken cancellationToken = default)
    {
        var ids = new HashSet<string>(StringComparer.Ordinal);

        foreach (var role in await _roleStore.GetRolesAsync(tenantId, includeSystem: true, cancellationToken))
        {
            if (!await CarriesAsync(role, administrationPermission, cancellationToken)) continue;

            if (role.TenantId == null && string.Equals(role.Name, EffectiveRoleResolver.OrgAdminRoleName, StringComparison.OrdinalIgnoreCase))
            {
                // Organization admins hold it through membership, not assignment
                // rows — the same grant EffectiveRoleResolver applies.
                foreach (var id in await OrganizationAdminIdsAsync(tenantId, cancellationToken)) ids.Add(id);
                continue;
            }

            foreach (var holder in await _roleStore.GetUsersByRoleInTenantAsync(role.Id, tenantId, cancellationToken))
            {
                ids.Add(holder.Id);
            }
        }

        return ids.ToList();
    }

    private async Task<bool> CarriesAsync(OlusoRole role, string permission, CancellationToken cancellationToken) =>
        (await _roleStore.GetRoleClaimsAsync(role.Id, cancellationToken)).Any(c =>
            c.Type == OlusoRole.PermissionClaimType
            && string.Equals(c.Value, permission, StringComparison.OrdinalIgnoreCase));

    private async Task<IEnumerable<string>> OrganizationAdminIdsAsync(string tenantId, CancellationToken cancellationToken)
    {
        if (_organizationMemberships == null) return Array.Empty<string>();

        var tenant = await _tenantStore.GetByIdAsync(tenantId, cancellationToken);
        if (string.IsNullOrEmpty(tenant?.OrganizationId)) return Array.Empty<string>();

        var memberships = await _organizationMemberships.GetByOrganizationAsync(tenant.OrganizationId, cancellationToken);
        return memberships
            .Where(m => m.Role is OrganizationRole.Owner or OrganizationRole.Admin)
            .Select(m => m.UserId);
    }

    /// <summary>
    /// The module's display name from the registered modules, by the
    /// permission's namespace; the upper-cased namespace for anything else.
    /// </summary>
    private static string ModuleNameOf(string permission)
    {
        var prefix = permission.Split('.')[0];
        var module = ModuleRegistry.Instance.GetRegisteredModules()
            .FirstOrDefault(m => string.Equals(m.FeatureKey, prefix, StringComparison.OrdinalIgnoreCase));
        return module?.DisplayName ?? prefix.ToUpperInvariant();
    }

    private static string? DisplayNameOf(OlusoUserInfo? user)
    {
        if (user == null) return null;
        if (!string.IsNullOrWhiteSpace(user.DisplayName)) return user.DisplayName;
        var full = $"{user.FirstName} {user.LastName}".Trim();
        return full.Length > 0 ? full : user.Email;
    }
}

public sealed class RoleGrantService : IRoleGrantService
{
    private readonly IRoleStore _roleStore;
    private readonly IModuleAdminAssignmentNotifier _notifier;
    private readonly IOlusoUserService _users;
    private readonly ITenantContext _tenantContext;
    private readonly IOlusoEventService _events;

    public RoleGrantService(
        IRoleStore roleStore,
        IModuleAdminAssignmentNotifier notifier,
        IOlusoUserService users,
        ITenantContext tenantContext,
        IOlusoEventService events)
    {
        _roleStore = roleStore;
        _notifier = notifier;
        _users = users;
        _tenantContext = tenantContext;
        _events = events;
    }

    public async Task<bool> GrantAsync(
        string roleId,
        string userId,
        string? tenantId,
        RoleGrantActor actor,
        CancellationToken cancellationToken = default)
    {
        var role = (await _roleStore.GetByIdsAsync(new[] { roleId }, cancellationToken)).FirstOrDefault()
            ?? throw new InvalidOperationException($"Role '{roleId}' not found");

        tenantId ??= RoleAssignmentTenant.Resolve(
            _tenantContext.TenantId,
            (await _users.FindByIdsAcrossTenantsAsync(new[] { userId }, cancellationToken))
                .GetValueOrDefault(userId)?.TenantId)
            ?? throw new InvalidOperationException(
                $"Role '{role.Name}' can't be assigned: the request has no tenant and the user has no home tenant.");

        var alreadyHeld = (await _roleStore.GetUserRolesInTenantAsync(userId, tenantId, cancellationToken))
            .Any(r => r.Id == roleId);

        await _roleStore.AssignToUserInTenantAsync(roleId, userId, tenantId, actor.Identifier, cancellationToken);

        if (alreadyHeld) return false;

        await _events.RaiseAsync(new UserRoleAssignedEvent
        {
            TenantId = tenantId,
            SubjectId = userId,
            RoleId = role.Id,
            RoleName = role.Name ?? role.Id
        }, cancellationToken);
        await _notifier.NotifyAsync(tenantId, userId, new[] { role }, actor, cancellationToken);
        return true;
    }

    public async Task<bool> RevokeAsync(
        string roleId,
        string userId,
        string? tenantId,
        RoleGrantActor actor,
        CancellationToken cancellationToken = default)
    {
        var role = (await _roleStore.GetByIdsAsync(new[] { roleId }, cancellationToken)).FirstOrDefault()
            ?? throw new InvalidOperationException($"Role '{roleId}' not found");

        tenantId ??= RoleAssignmentTenant.Resolve(
            _tenantContext.TenantId,
            (await _users.FindByIdsAcrossTenantsAsync(new[] { userId }, cancellationToken))
                .GetValueOrDefault(userId)?.TenantId);
        if (tenantId == null) return false;

        var held = (await _roleStore.GetUserRolesInTenantAsync(userId, tenantId, cancellationToken))
            .Any(r => r.Id == roleId);

        await _roleStore.RemoveFromUserInTenantAsync(roleId, userId, tenantId, cancellationToken);

        if (!held) return false;

        await _events.RaiseAsync(new UserRoleRemovedEvent
        {
            TenantId = tenantId,
            SubjectId = userId,
            RoleId = role.Id,
            RoleName = role.Name ?? role.Id
        }, cancellationToken);
        await _notifier.NotifyRemovedAsync(tenantId, userId, new[] { role }, actor, cancellationToken);
        return true;
    }
}

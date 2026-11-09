using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.AspNetCore.Identity;
using Oluso.Core.Authorization;
using Oluso.Core.Domain.Entities;

namespace Oluso.EntityFramework.Interceptors;

/// <summary>
/// Last-line-of-defence guard for reserved roles and reserved claim keys.
/// Hooks <see cref="SaveChangesInterceptor"/> on the DbContext so any
/// attempt to persist an authorization-sensitive row gets rejected at the
/// data layer regardless of which controller, module, or service issued
/// the write. Pairs with the existing controller-level checks: those
/// produce nice 400 responses on the happy path, this catches anything
/// that slipped through (forgotten guards, raw SQL via the EF context,
/// new modules whose authors didn't know about the rule).
///
/// Only writes are checked. Reads aren't gated — if a reserved row exists
/// in the database (e.g. seeded "Admin" role), it stays readable.
/// </summary>
public sealed class ReservedAuthInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        EnforcePolicy(eventData);
        return base.SavingChanges(eventData, result);
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        EnforcePolicy(eventData);
        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private static void EnforcePolicy(DbContextEventData eventData)
    {
        var context = eventData.Context;
        if (context is null) return;

        if (context is OlusoDbContext olusoDb && olusoDb.SuppressReservedGuard)
            return;

        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State != EntityState.Added && entry.State != EntityState.Modified)
                continue;

            switch (entry.Entity)
            {
                case OlusoRole role:
                    GuardRole(role);
                    break;

                case OlusoUserClaim userClaim:
                    // UserClaim has no legitimate "permission" use-case —
                    // permissions belong on RoleClaim, never directly on a
                    // user. So treat ClaimType="permission" as reserved
                    // here too (caller-context-free defense; if a module
                    // tries to grant a permission directly to a user via
                    // this entity, it fails at SaveChanges).
                    GuardClaim("user claim", userClaim.ClaimType, blockPermissionEntry: true);
                    break;

                case OlusoRoleClaim roleClaim:
                    // RoleClaim with ClaimType="permission" IS the
                    // legitimate path for assigning permissions to a role.
                    // We can't gate WHO assigns what here (no caller
                    // context — that's the controller's job), but we CAN
                    // gate the value shape: it must be a registered
                    // permission name or a registered-prefix wildcard.
                    GuardClaim("role claim", roleClaim.ClaimType, blockPermissionEntry: false);
                    if (ReservedClaimKeys.IsPermissionEntry(roleClaim.ClaimType))
                    {
                        GuardPermissionValue(roleClaim.ClaimValue);
                    }
                    break;

                case ClientClaim clientClaim:
                    // ClientClaim with ClaimType="permission" would leak
                    // into client tokens — no legitimate use-case.
                    GuardClaim("client claim", clientClaim.Type, blockPermissionEntry: true);
                    break;

                case ClientAuthorizeParameter authParam:
                    // A custom authorize-parameter mapping must never target a
                    // reserved/system claim — that would let a client mint
                    // sub/role/tenant_id/permissions/module_seats from a query
                    // string. Same surface as ClientClaim, so block "permission"
                    // (singular) here too.
                    GuardClaim("client authorize parameter", authParam.ClaimType, blockPermissionEntry: true);
                    break;
            }
        }
    }

    private static void GuardRole(OlusoRole role)
    {
        if (ReservedRoles.IsReserved(role.Name))
        {
            throw new ReservedRoleException(role.Name!);
        }
    }

    /// <summary>
    /// Validates a role-permission value against <see cref="PermissionRegistry"/>.
    /// Allowed shapes:
    /// <list type="bullet">
    ///   <item>Exact match against a registered <c>PermissionDefinition.Name</c></item>
    ///   <item><c>&lt;prefix&gt;.*</c> when at least one registered permission's name
    ///     starts with <c>&lt;prefix&gt;.</c> — admin can grant a whole namespace
    ///     wildcard but only for namespaces that actually exist.</item>
    /// </list>
    /// Rejects lone <c>*</c>, mid-string wildcards, and unknown names.
    /// Empty / null values fall through (let other validation surface them).
    /// </summary>
    private static void GuardPermissionValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return;

        // Lone "*" is the wide-open grant — never allowed via stored claim.
        if (value == "*")
        {
            throw new UnregisteredPermissionException(value);
        }

        var permissions = PermissionRegistry.Instance.GetPluginPermissions();

        // <prefix>.* — allowed when at least one registered permission's
        // name starts with "<prefix>.". Catches typos like fake.* but
        // accepts admin-friendly wildcards for real namespaces.
        if (value.EndsWith(".*", StringComparison.Ordinal))
        {
            var prefix = value[..^2];
            if (string.IsNullOrEmpty(prefix) || prefix.Contains('*'))
            {
                throw new UnregisteredPermissionException(value);
            }
            var prefixDot = prefix + ".";
            var prefixMatches = false;
            foreach (var p in permissions)
            {
                if (p.Name.StartsWith(prefixDot, StringComparison.OrdinalIgnoreCase))
                {
                    prefixMatches = true;
                    break;
                }
            }
            if (!prefixMatches)
            {
                throw new UnregisteredPermissionException(value);
            }
            return;
        }

        // Non-wildcard — must be an exact registered name.
        foreach (var p in permissions)
        {
            if (p.Name.Equals(value, StringComparison.OrdinalIgnoreCase))
                return;
        }

        // Reject mid-string wildcards (e.g. "users.*.read") and unknown names.
        throw new UnregisteredPermissionException(value);
    }

    private static void GuardClaim(string entityLabel, string? claimType, bool blockPermissionEntry)
    {
        if (string.IsNullOrEmpty(claimType)) return;

        if (ReservedClaimKeys.IsReserved(claimType))
        {
            throw new ReservedClaimKeyException(entityLabel, claimType);
        }

        // Entity-specific rule: "permission" (singular) is allowed on
        // RoleClaim (legitimate) but blocked on UserClaim / ClientClaim.
        if (blockPermissionEntry && ReservedClaimKeys.IsPermissionEntry(claimType))
        {
            throw new ReservedClaimKeyException(entityLabel, claimType);
        }
    }
}

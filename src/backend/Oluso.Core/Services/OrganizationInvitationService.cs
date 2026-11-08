using System.Text.Json;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Core.Services;

/// <summary>
/// Redeems organization invitations.
/// <para>
/// The invite link is delivered by email and opened in a fresh browser session
/// with no protocol flow in flight, so the standard Account pages (which are
/// gated behind an in-flight OAuth/OIDC cycle) can't be part of the journey.
/// The invitation token is the gate here, exactly as the reset token is for
/// password reset — possession of the mailbox is what's being proven.
/// </para>
/// </summary>
public interface IOrganizationInvitationService
{
    /// <summary>
    /// Resolves an invitation for display. Returns null when the token is
    /// unknown; otherwise the result carries why it is or isn't redeemable.
    /// Expires the record in passing if its deadline has passed.
    /// </summary>
    Task<InvitationLookup?> LookupAsync(string token, CancellationToken ct = default);

    /// <summary>
    /// Redeems the invitation: creates the account if needed, grants org
    /// membership at the invited role, grants tenant access, and marks the
    /// invitation accepted.
    /// </summary>
    Task<InvitationAcceptResult> AcceptAsync(AcceptInvitationRequest request, CancellationToken ct = default);
}

public class InvitationLookup
{
    public required OrganizationInvitation Invitation { get; init; }
    public required string OrganizationName { get; init; }

    /// <summary>Null when no local account exists for the invited email yet.</summary>
    public string? ExistingUserId { get; init; }

    /// <summary>True when the invitee must choose a password to complete sign-up.</summary>
    public bool RequiresPassword { get; init; }

    /// <summary>Null when redeemable; otherwise the reason it isn't.</summary>
    public string? BlockedReason { get; init; }

    public bool IsRedeemable => BlockedReason == null;
}

public class AcceptInvitationRequest
{
    public required string Token { get; init; }
    public string? Password { get; init; }
    public string? FirstName { get; init; }
    public string? LastName { get; init; }
}

public class InvitationAcceptResult
{
    public bool Succeeded { get; init; }
    public string? Error { get; init; }
    public string? UserId { get; init; }
    public string? OrganizationName { get; init; }

    /// <summary>Where the invitee should sign in once they're done.</summary>
    public string? TenantIdentifier { get; init; }

    public static InvitationAcceptResult Ok(string userId, string orgName, string? tenantIdentifier) =>
        new() { Succeeded = true, UserId = userId, OrganizationName = orgName, TenantIdentifier = tenantIdentifier };

    public static InvitationAcceptResult Failed(string error) =>
        new() { Succeeded = false, Error = error };
}

public class OrganizationInvitationService : IOrganizationInvitationService
{
    private readonly IOrganizationInvitationStore _invitations;
    private readonly IOrganizationMembershipStore _memberships;
    private readonly ITenantMembershipStore _tenantMemberships;
    private readonly ITenantStore _tenantStore;
    private readonly IRoleStore _roleStore;
    private readonly IOlusoUserService _userService;
    private readonly ILogger<OrganizationInvitationService> _logger;

    public OrganizationInvitationService(
        IOrganizationInvitationStore invitations,
        IOrganizationMembershipStore memberships,
        ITenantMembershipStore tenantMemberships,
        ITenantStore tenantStore,
        IRoleStore roleStore,
        IOlusoUserService userService,
        IRoleGrantService roleGrants,
        IModuleAdminAssignmentNotifier moduleAdminNotifier,
        ILogger<OrganizationInvitationService> logger)
    {
        _roleGrants = roleGrants;
        _moduleAdminNotifier = moduleAdminNotifier;
        _invitations = invitations;
        _memberships = memberships;
        _tenantMemberships = tenantMemberships;
        _tenantStore = tenantStore;
        _roleStore = roleStore;
        _userService = userService;
        _logger = logger;
    }

    private readonly IRoleGrantService _roleGrants;
    private readonly IModuleAdminAssignmentNotifier _moduleAdminNotifier;

    public async Task<InvitationLookup?> LookupAsync(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) return null;

        var invitation = await _invitations.GetByTokenAsync(token, ct);
        if (invitation == null) return null;

        var organizationName = invitation.Organization?.Name ?? "the organization";

        // Expiry is only ever evaluated on read, so record it here rather than
        // leaving a stale Pending row that looks redeemable in the admin list.
        if (invitation.Status == InvitationStatus.Pending && invitation.ExpiresAt <= DateTime.UtcNow)
        {
            invitation.Status = InvitationStatus.Expired;
            await _invitations.UpdateAsync(invitation, ct);
        }

        var blocked = invitation.Status switch
        {
            InvitationStatus.Accepted => "This invitation has already been accepted.",
            InvitationStatus.Revoked => "This invitation has been revoked.",
            InvitationStatus.Declined => "This invitation was declined.",
            InvitationStatus.Expired => "This invitation has expired. Ask an administrator to send a new one.",
            _ => null
        };

        var existingUser = await _userService.FindByEmailAsync(invitation.Email, ct);
        var hasPassword = existingUser != null && await _userService.HasPasswordAsync(existingUser.Id, ct);

        return new InvitationLookup
        {
            Invitation = invitation,
            OrganizationName = organizationName,
            ExistingUserId = existingUser?.Id,
            // A brand-new account needs one; so does a reused account that has
            // never had a local password (provisioned or SSO-only).
            RequiresPassword = !hasPassword,
            BlockedReason = blocked
        };
    }

    public async Task<InvitationAcceptResult> AcceptAsync(AcceptInvitationRequest request, CancellationToken ct = default)
    {
        var lookup = await LookupAsync(request.Token, ct);
        if (lookup == null)
            return InvitationAcceptResult.Failed("This invitation link is not valid.");

        if (!lookup.IsRedeemable)
            return InvitationAcceptResult.Failed(lookup.BlockedReason!);

        var invitation = lookup.Invitation;

        // Which tenants this invitation grants. Null AllowedTenantIdsJson means
        // every tenant the organization owns.
        var orgTenants = (await _tenantStore.GetByOrganizationAsync(invitation.OrganizationId, ct)).ToList();
        var allowedTenantIds = ParseAllowedTenantIds(invitation.AllowedTenantIdsJson);
        var grantTenants = allowedTenantIds == null
            ? orgTenants
            : orgTenants.Where(t => allowedTenantIds.Contains(t.Id)).ToList();

        if (grantTenants.Count == 0)
        {
            _logger.LogWarning(
                "Invitation {InvitationId} for organization {OrgId} grants no tenants — nothing to join",
                invitation.Id, invitation.OrganizationId);
            return InvitationAcceptResult.Failed(
                "This organization has no tenants yet, so the invitation can't be completed. Contact an administrator.");
        }

        // A user row needs a home tenant, so anchor new accounts to one of the
        // granted tenants — but not just the first. GetByOrganizationAsync orders
        // by Environment ascending and Development is 0, so "all tenants" would
        // have parked every invited user in the Development tenant. Prefer the
        // most production-like, with a stable tiebreak.
        var homeTenant = grantTenants
            .OrderByDescending(t => t.Environment)
            .ThenBy(t => t.Name, StringComparer.Ordinal)
            .First();

        var userId = lookup.ExistingUserId;
        if (userId == null)
        {
            if (string.IsNullOrEmpty(request.Password))
                return InvitationAcceptResult.Failed("Choose a password to complete your account.");

            var created = await _userService.CreateUserAsync(new CreateUserRequest
            {
                Email = invitation.Email,
                // Identity's default UserValidator rejects an empty UserName.
                Username = invitation.Email,
                Password = request.Password,
                FirstName = request.FirstName,
                LastName = request.LastName,
                TenantId = homeTenant.Id,
                RequireEmailVerification = false
            }, ct);

            if (!created.Succeeded)
            {
                _logger.LogWarning("Invitation {InvitationId}: account creation failed — {Error}",
                    invitation.Id, created.Error);
                return InvitationAcceptResult.Failed(created.MessageOr("Could not create your account."));
            }

            userId = created.UserId!;
        }
        else if (lookup.RequiresPassword)
        {
            if (string.IsNullOrEmpty(request.Password))
                return InvitationAcceptResult.Failed("Choose a password to complete your account.");

            // The account exists but has no local password (provisioned or
            // SSO-only). Set one through the normal reset path so the security
            // stamp rotates exactly as it would for a self-service reset.
            var resetToken = await _userService.GeneratePasswordResetTokenAsync(userId, ct);
            if (resetToken == null)
                return InvitationAcceptResult.Failed("Could not set a password for this account.");

            var reset = await _userService.ResetPasswordAsync(userId, request.Password, resetToken, ct);
            if (!reset.Succeeded)
            {
                _logger.LogWarning("Invitation {InvitationId}: setting password failed — {Error}",
                    invitation.Id, reset.Error);
                return InvitationAcceptResult.Failed(reset.MessageOr("Could not set a password for this account."));
            }
        }

        // Org membership at the invited role. Re-inviting someone who is already
        // a member updates their role rather than creating a duplicate row.
        var existingMembership = await _memberships.GetByUserAndOrganizationAsync(userId, invitation.OrganizationId, ct);
        var previousRole = existingMembership?.Role;
        if (existingMembership == null)
        {
            await _memberships.CreateAsync(new OrganizationMembership
            {
                OrganizationId = invitation.OrganizationId,
                UserId = userId,
                Role = invitation.Role
            }, ct);
        }
        else if (existingMembership.Role != invitation.Role)
        {
            existingMembership.Role = invitation.Role;
            await _memberships.UpdateAsync(existingMembership, ct);
        }

        foreach (var tenant in grantTenants)
        {
            var existingTenantMembership = await _tenantMemberships.GetByTenantAndUserAsync(tenant.Id, userId, ct);
            if (existingTenantMembership != null) continue;

            await _tenantMemberships.AddAsync(new TenantMembership
            {
                TenantId = tenant.Id,
                UserId = userId,
                InvitedByUserId = invitation.InvitedByUserId
            }, ct);
        }

        var actor = new RoleGrantActor(invitation.InvitedByUserId, "Organization invitation");
        await GrantRolesAsync(userId, invitation.Role, homeTenant, grantTenants, actor, ct);

        // Organization-admin rights come from the membership, so its module
        // admins are told on an actual Owner/Admin transition.
        var wasAdmin = previousRole is OrganizationRole.Owner or OrganizationRole.Admin;
        var isAdmin = invitation.Role is OrganizationRole.Owner or OrganizationRole.Admin;
        if (wasAdmin != isAdmin)
        {
            await _moduleAdminNotifier.NotifyOrganizationAdminChangedAsync(
                invitation.OrganizationId, userId, removed: !isAdmin, actor, ct);
        }

        invitation.Status = InvitationStatus.Accepted;
        invitation.AcceptedAt = DateTime.UtcNow;
        invitation.AcceptedByUserId = userId;
        await _invitations.UpdateAsync(invitation, ct);

        _logger.LogInformation(
            "Invitation {InvitationId} accepted by user {UserId} for organization {OrgId} as {Role}",
            invitation.Id, userId, invitation.OrganizationId, invitation.Role);

        return InvitationAcceptResult.Ok(userId, lookup.OrganizationName, homeTenant.Identifier);
    }

    /// <summary>
    /// Authorization keys off identity roles, not off membership rows, so an
    /// invited Owner/Admin who never receives OrgAdmin can't actually administer
    /// anything they were invited to administer.
    /// </summary>
    private async Task GrantRolesAsync(
        string userId,
        OrganizationRole role,
        Tenant homeTenant,
        List<Tenant> grantTenants,
        RoleGrantActor actor,
        CancellationToken ct)
    {
        if (role is not (OrganizationRole.Owner or OrganizationRole.Admin)) return;

        var orgAdminRole = await _roleStore.GetByNameAsync("OrgAdmin", null, ct);
        if (orgAdminRole != null)
        {
            // Kept in step with the membership but no longer an authorization
            // input, so written directly; the membership change notifies.
            await _roleStore.AssignToUserInTenantAsync(orgAdminRole.Id, userId, homeTenant.Id, actor.Identifier, ct);
        }
        else
        {
            _logger.LogWarning(
                "OrgAdmin role is missing, so user {UserId} was invited as an organization admin without it", userId);
        }

        // Tenant-scoped Admin as well, so they can administer the tenants the
        // invitation actually granted.
        foreach (var tenant in grantTenants)
        {
            var tenantAdmin = await _roleStore.GetByNameAsync("Admin", tenant.Id, ct);
            if (tenantAdmin != null)
            {
                await _roleGrants.GrantAsync(tenantAdmin.Id, userId, tenant.Id, actor, ct);
            }
        }
    }

    private static HashSet<string>? ParseAllowedTenantIds(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            var ids = JsonSerializer.Deserialize<List<string>>(json);
            return ids == null || ids.Count == 0 ? null : new HashSet<string>(ids, StringComparer.Ordinal);
        }
        catch (JsonException)
        {
            // A malformed column shouldn't widen access to every tenant.
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }
}

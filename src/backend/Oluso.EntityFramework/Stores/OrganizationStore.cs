using Microsoft.EntityFrameworkCore;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using System.Text.Json;

namespace Oluso.EntityFramework.Stores;

/// <summary>
/// Entity Framework implementation of IOrganizationStore
/// </summary>
public class OrganizationStore : IOrganizationStore
{
    private readonly OlusoDbContext _context;

    public OrganizationStore(OlusoDbContext context)
    {
        _context = context;
    }

    public async Task<Organization?> GetByIdAsync(string organizationId, CancellationToken cancellationToken = default)
    {
        return await _context.Organizations
            .Include(o => o.Tenants)
            .FirstOrDefaultAsync(o => o.Id == organizationId, cancellationToken);
    }

    public async Task<Organization?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        return await _context.Organizations
            .Include(o => o.Tenants)
            .FirstOrDefaultAsync(o => o.Slug == slug, cancellationToken);
    }

    public async Task<IEnumerable<Organization>> GetAllAsync(bool includeDisabled = false, CancellationToken cancellationToken = default)
    {
        var query = _context.Organizations.AsQueryable();

        if (!includeDisabled)
        {
            query = query.Where(o => o.Enabled);
        }

        return await query
            .OrderBy(o => o.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<Organization> CreateAsync(Organization organization, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(organization.Id))
        {
            organization.Id = Guid.NewGuid().ToString();
        }

        organization.Created = DateTime.UtcNow;

        _context.Organizations.Add(organization);
        await _context.SaveChangesAsync(cancellationToken);
        return organization;
    }

    public async Task<Organization> UpdateAsync(Organization organization, CancellationToken cancellationToken = default)
    {
        organization.Updated = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
        return organization;
    }

    public async Task DeleteAsync(string organizationId, CancellationToken cancellationToken = default)
    {
        var organization = await _context.Organizations.FindAsync(new object[] { organizationId }, cancellationToken);
        if (organization != null)
        {
            _context.Organizations.Remove(organization);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<bool> SlugExistsAsync(string slug, CancellationToken cancellationToken = default)
    {
        return await _context.Organizations
            .AnyAsync(o => o.Slug == slug, cancellationToken);
    }
}

/// <summary>
/// Entity Framework implementation of IOrganizationMembershipStore
/// </summary>
public class OrganizationMembershipStore : IOrganizationMembershipStore
{
    private readonly OlusoDbContext _context;

    public OrganizationMembershipStore(OlusoDbContext context)
    {
        _context = context;
    }

    public async Task<OrganizationMembership?> GetByIdAsync(string membershipId, CancellationToken cancellationToken = default)
    {
        return await _context.OrganizationMemberships
            .Include(m => m.Organization)
            .FirstOrDefaultAsync(m => m.Id == membershipId, cancellationToken);
    }

    public async Task<OrganizationMembership?> GetByUserAndOrganizationAsync(string userId, string organizationId, CancellationToken cancellationToken = default)
    {
        return await _context.OrganizationMemberships
            .Include(m => m.Organization)
            .FirstOrDefaultAsync(m => m.UserId == userId && m.OrganizationId == organizationId, cancellationToken);
    }

    public async Task<IEnumerable<OrganizationMembership>> GetByOrganizationAsync(string organizationId, CancellationToken cancellationToken = default)
    {
        return await _context.OrganizationMemberships
            .Where(m => m.OrganizationId == organizationId)
            .OrderBy(m => m.Role)
            .ThenBy(m => m.JoinedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<OrganizationMembership>> GetByUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await _context.OrganizationMemberships
            .Include(m => m.Organization)
            .Where(m => m.UserId == userId)
            .OrderBy(m => m.Organization.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<Organization>> GetUserOrganizationsAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await _context.OrganizationMemberships
            .Where(m => m.UserId == userId)
            .Select(m => m.Organization)
            .Where(o => o.Enabled)
            .OrderBy(o => o.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<string>> GetAllowedTenantIdsAsync(string userId, string organizationId, CancellationToken cancellationToken = default)
    {
        var membership = await _context.OrganizationMemberships
            .FirstOrDefaultAsync(m => m.UserId == userId && m.OrganizationId == organizationId, cancellationToken);

        if (membership == null)
        {
            return Enumerable.Empty<string>();
        }

        // Owner and Admin have access to all tenants
        if (membership.Role == OrganizationRole.Owner || membership.Role == OrganizationRole.Admin)
        {
            return await _context.Tenants!
                .Where(t => t.OrganizationId == organizationId)
                .Select(t => t.Id)
                .ToListAsync(cancellationToken);
        }

        // Members may have restricted access
        if (string.IsNullOrEmpty(membership.AllowedTenantIdsJson))
        {
            // No restriction = all tenants
            return await _context.Tenants!
                .Where(t => t.OrganizationId == organizationId)
                .Select(t => t.Id)
                .ToListAsync(cancellationToken);
        }

        // Parse restricted tenant IDs
        try
        {
            var allowedIds = JsonSerializer.Deserialize<List<string>>(membership.AllowedTenantIdsJson);
            return allowedIds ?? Enumerable.Empty<string>();
        }
        catch
        {
            return Enumerable.Empty<string>();
        }
    }

    public async Task<OrganizationMembership> CreateAsync(OrganizationMembership membership, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(membership.Id))
        {
            membership.Id = Guid.NewGuid().ToString();
        }

        membership.JoinedAt = DateTime.UtcNow;

        _context.OrganizationMemberships.Add(membership);
        await _context.SaveChangesAsync(cancellationToken);
        return membership;
    }

    public async Task<OrganizationMembership> UpdateAsync(OrganizationMembership membership, CancellationToken cancellationToken = default)
    {
        await _context.SaveChangesAsync(cancellationToken);
        return membership;
    }

    public async Task DeleteAsync(string membershipId, CancellationToken cancellationToken = default)
    {
        var membership = await _context.OrganizationMemberships.FindAsync(new object[] { membershipId }, cancellationToken);
        if (membership != null)
        {
            _context.OrganizationMemberships.Remove(membership);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<int> GetMemberCountAsync(string organizationId, CancellationToken cancellationToken = default)
    {
        return await _context.OrganizationMemberships
            .CountAsync(m => m.OrganizationId == organizationId, cancellationToken);
    }

    public async Task<bool> IsOwnerAsync(string userId, string organizationId, CancellationToken cancellationToken = default)
    {
        return await _context.OrganizationMemberships
            .AnyAsync(m => m.UserId == userId && m.OrganizationId == organizationId && m.Role == OrganizationRole.Owner, cancellationToken);
    }

    public async Task<bool> IsAdminAsync(string userId, string organizationId, CancellationToken cancellationToken = default)
    {
        return await _context.OrganizationMemberships
            .AnyAsync(m => m.UserId == userId && m.OrganizationId == organizationId &&
                     (m.Role == OrganizationRole.Owner || m.Role == OrganizationRole.Admin), cancellationToken);
    }

    public async Task<bool> IsMemberAsync(string userId, string organizationId, CancellationToken cancellationToken = default)
    {
        return await _context.OrganizationMemberships
            .AnyAsync(m => m.UserId == userId && m.OrganizationId == organizationId, cancellationToken);
    }
}

/// <summary>
/// Entity Framework implementation of IOrganizationInvitationStore
/// </summary>
public class OrganizationInvitationStore : IOrganizationInvitationStore
{
    private readonly OlusoDbContext _context;

    public OrganizationInvitationStore(OlusoDbContext context)
    {
        _context = context;
    }

    public async Task<OrganizationInvitation?> GetByIdAsync(string invitationId, CancellationToken cancellationToken = default)
    {
        return await _context.OrganizationInvitations
            .Include(i => i.Organization)
            .FirstOrDefaultAsync(i => i.Id == invitationId, cancellationToken);
    }

    public async Task<OrganizationInvitation?> GetByTokenAsync(string token, CancellationToken cancellationToken = default)
    {
        return await _context.OrganizationInvitations
            .Include(i => i.Organization)
            .FirstOrDefaultAsync(i => i.Token == token, cancellationToken);
    }

    public async Task<IEnumerable<OrganizationInvitation>> GetByOrganizationAsync(string organizationId, InvitationStatus? status = null, CancellationToken cancellationToken = default)
    {
        var query = _context.OrganizationInvitations
            .Where(i => i.OrganizationId == organizationId);

        if (status.HasValue)
        {
            query = query.Where(i => i.Status == status.Value);
        }

        return await query
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<OrganizationInvitation>> GetByEmailAsync(string email, InvitationStatus? status = null, CancellationToken cancellationToken = default)
    {
        var query = _context.OrganizationInvitations
            .Include(i => i.Organization)
            .Where(i => i.Email.ToLower() == email.ToLower());

        if (status.HasValue)
        {
            query = query.Where(i => i.Status == status.Value);
        }

        return await query
            .OrderByDescending(i => i.CreatedAt)
            .ToListAsync(cancellationToken);
    }

    public async Task<OrganizationInvitation?> GetPendingByEmailAndOrganizationAsync(string email, string organizationId, CancellationToken cancellationToken = default)
    {
        return await _context.OrganizationInvitations
            .Include(i => i.Organization)
            .FirstOrDefaultAsync(i =>
                i.Email.ToLower() == email.ToLower() &&
                i.OrganizationId == organizationId &&
                i.Status == InvitationStatus.Pending &&
                i.ExpiresAt > DateTime.UtcNow,
                cancellationToken);
    }

    public async Task<OrganizationInvitation> CreateAsync(OrganizationInvitation invitation, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(invitation.Id))
        {
            invitation.Id = Guid.NewGuid().ToString();
        }

        if (string.IsNullOrEmpty(invitation.Token))
        {
            invitation.Token = GenerateSecureToken();
        }

        invitation.CreatedAt = DateTime.UtcNow;

        _context.OrganizationInvitations.Add(invitation);
        await _context.SaveChangesAsync(cancellationToken);
        return invitation;
    }

    public async Task<OrganizationInvitation> UpdateAsync(OrganizationInvitation invitation, CancellationToken cancellationToken = default)
    {
        await _context.SaveChangesAsync(cancellationToken);
        return invitation;
    }

    public async Task DeleteAsync(string invitationId, CancellationToken cancellationToken = default)
    {
        var invitation = await _context.OrganizationInvitations.FindAsync(new object[] { invitationId }, cancellationToken);
        if (invitation != null)
        {
            _context.OrganizationInvitations.Remove(invitation);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<int> CleanupExpiredAsync(CancellationToken cancellationToken = default)
    {
        var expiredInvitations = await _context.OrganizationInvitations
            .Where(i => i.Status == InvitationStatus.Pending && i.ExpiresAt < DateTime.UtcNow)
            .ToListAsync(cancellationToken);

        foreach (var invitation in expiredInvitations)
        {
            invitation.Status = InvitationStatus.Expired;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return expiredInvitations.Count;
    }

    private static string GenerateSecureToken()
    {
        var bytes = new byte[32];
        using var rng = System.Security.Cryptography.RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return Convert.ToBase64String(bytes)
            .Replace("+", "-")
            .Replace("/", "_")
            .Replace("=", "");
    }
}

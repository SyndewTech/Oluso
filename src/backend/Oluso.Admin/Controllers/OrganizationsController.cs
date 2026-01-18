using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oluso.Admin.Authorization;
using Oluso.Core.Api;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using System.Text.Json;

namespace Oluso.Admin.Controllers;

/// <summary>
/// Admin API for managing Organizations
/// </summary>
[Route("api/admin/organizations")]
[Authorize(Policy = "AdminApi")]
public class OrganizationsController : AdminBaseController
{
    private readonly IOrganizationStore _organizationStore;
    private readonly IOrganizationMembershipStore _membershipStore;
    private readonly IOrganizationInvitationStore _invitationStore;
    private readonly ITenantStore _tenantStore;
    private readonly ILogger<OrganizationsController> _logger;

    public OrganizationsController(
        IOrganizationStore organizationStore,
        IOrganizationMembershipStore membershipStore,
        IOrganizationInvitationStore invitationStore,
        ITenantStore tenantStore,
        ILogger<OrganizationsController> logger,
        ITenantContext tenantContext) : base(tenantContext)
    {
        _organizationStore = organizationStore;
        _membershipStore = membershipStore;
        _invitationStore = invitationStore;
        _tenantStore = tenantStore;
        _logger = logger;
    }

    #region Organizations CRUD

    /// <summary>
    /// Get all organizations.
    /// SuperAdmins see all organizations, OrgAdmins see only their own.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<OrganizationDto>>> GetAll(
        [FromQuery] bool includeDisabled = false,
        CancellationToken cancellationToken = default)
    {
        // SuperAdmins see all organizations
        if (IsSuperAdmin)
        {
            var allOrgs = await _organizationStore.GetAllAsync(includeDisabled, cancellationToken);
            return Ok(allOrgs.Select(MapToDto));
        }

        // OrgAdmins see only organizations they're members of
        var userId = AdminUserId;
        if (string.IsNullOrEmpty(userId))
            return Ok(Enumerable.Empty<OrganizationDto>());

        var memberships = await _membershipStore.GetByUserAsync(userId, cancellationToken);
        var orgIds = memberships.Select(m => m.OrganizationId).ToHashSet();

        var organizations = await _organizationStore.GetAllAsync(includeDisabled, cancellationToken);
        var userOrgs = organizations.Where(o => orgIds.Contains(o.Id));

        return Ok(userOrgs.Select(MapToDto));
    }

    /// <summary>
    /// Get organization by ID
    /// </summary>
    [HttpGet("{organizationId}")]
    [RequirePermission(AdminPermissions.OrganizationsRead)]
    public async Task<ActionResult<OrganizationDto>> GetById(string organizationId, CancellationToken cancellationToken)
    {
        var organization = await _organizationStore.GetByIdAsync(organizationId, cancellationToken);
        if (organization == null)
            return NotFound();
        return Ok(MapToDto(organization));
    }

    /// <summary>
    /// Get organization by slug
    /// </summary>
    [HttpGet("by-slug/{slug}")]
    [RequirePermission(AdminPermissions.OrganizationsRead)]
    public async Task<ActionResult<OrganizationDto>> GetBySlug(string slug, CancellationToken cancellationToken)
    {
        var organization = await _organizationStore.GetBySlugAsync(slug, cancellationToken);
        if (organization == null)
            return NotFound();
        return Ok(MapToDto(organization));
    }

    /// <summary>
    /// Create a new organization (SuperAdmin only)
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<ActionResult<OrganizationDto>> Create(
        [FromBody] CreateOrganizationRequest request,
        CancellationToken cancellationToken)
    {
        // Check for existing slug
        if (await _organizationStore.SlugExistsAsync(request.Slug, cancellationToken))
        {
            return Conflict(new { error = $"Organization with slug '{request.Slug}' already exists" });
        }

        var organization = new Organization
        {
            Id = Guid.NewGuid().ToString(),
            Name = request.Name,
            Slug = request.Slug.ToLowerInvariant(),
            Description = request.Description,
            LogoUrl = request.LogoUrl,
            WebsiteUrl = request.WebsiteUrl,
            MaxMembers = request.MaxMembers ?? 0,
            MaxTenants = request.MaxTenants ?? 5,
            Enabled = true
        };

        var created = await _organizationStore.CreateAsync(organization, cancellationToken);

        // If a creator user ID is provided, add them as owner
        if (!string.IsNullOrEmpty(request.OwnerUserId))
        {
            var membership = new OrganizationMembership
            {
                OrganizationId = created.Id,
                UserId = request.OwnerUserId,
                Role = OrganizationRole.Owner
            };
            await _membershipStore.CreateAsync(membership, cancellationToken);
        }

        _logger.LogInformation("Created organization: {OrganizationId} ({Name}) by {AdminUserId}",
            created.Id, created.Name, AdminUserId);

        return CreatedAtAction(nameof(GetById), new { organizationId = created.Id }, MapToDto(created));
    }

    /// <summary>
    /// Update an organization
    /// </summary>
    [HttpPut("{organizationId}")]
    [RequirePermission(AdminPermissions.OrganizationsWrite)]
    public async Task<ActionResult<OrganizationDto>> Update(
        string organizationId,
        [FromBody] UpdateOrganizationRequest request,
        CancellationToken cancellationToken)
    {
        var organization = await _organizationStore.GetByIdAsync(organizationId, cancellationToken);
        if (organization == null)
            return NotFound();

        // Check slug uniqueness if changing
        if (!string.IsNullOrEmpty(request.Slug) && request.Slug != organization.Slug)
        {
            if (await _organizationStore.SlugExistsAsync(request.Slug, cancellationToken))
            {
                return Conflict(new { error = $"Organization with slug '{request.Slug}' already exists" });
            }
            organization.Slug = request.Slug.ToLowerInvariant();
        }

        if (request.Name != null) organization.Name = request.Name;
        if (request.Description != null) organization.Description = request.Description;
        if (request.LogoUrl != null) organization.LogoUrl = request.LogoUrl;
        if (request.WebsiteUrl != null) organization.WebsiteUrl = request.WebsiteUrl;
        if (request.Enabled.HasValue) organization.Enabled = request.Enabled.Value;
        if (request.MaxMembers.HasValue) organization.MaxMembers = request.MaxMembers.Value;
        if (request.MaxTenants.HasValue) organization.MaxTenants = request.MaxTenants.Value;
        if (request.Metadata != null) organization.Metadata = request.Metadata;

        var updated = await _organizationStore.UpdateAsync(organization, cancellationToken);

        _logger.LogInformation("Updated organization: {OrganizationId} by {AdminUserId}",
            organizationId, AdminUserId);

        return Ok(MapToDto(updated));
    }

    /// <summary>
    /// Delete an organization (SuperAdmin only)
    /// </summary>
    [HttpDelete("{organizationId}")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> Delete(string organizationId, CancellationToken cancellationToken)
    {
        var organization = await _organizationStore.GetByIdAsync(organizationId, cancellationToken);
        if (organization == null)
            return NotFound();

        // Check if organization has tenants
        var tenants = await _tenantStore.GetByOrganizationAsync(organizationId, cancellationToken);
        if (tenants.Any())
        {
            return BadRequest(new { error = "Cannot delete organization with existing tenants. Delete all tenants first." });
        }

        await _organizationStore.DeleteAsync(organizationId, cancellationToken);

        _logger.LogWarning("Deleted organization: {OrganizationId} ({Name}) by {AdminUserId}",
            organizationId, organization.Name, AdminUserId);

        return NoContent();
    }

    /// <summary>
    /// Enable an organization
    /// </summary>
    [HttpPost("{organizationId}/enable")]
    [RequirePermission(AdminPermissions.OrganizationsWrite)]
    public async Task<IActionResult> Enable(string organizationId, CancellationToken cancellationToken)
    {
        var organization = await _organizationStore.GetByIdAsync(organizationId, cancellationToken);
        if (organization == null)
            return NotFound();

        organization.Enabled = true;
        await _organizationStore.UpdateAsync(organization, cancellationToken);

        _logger.LogInformation("Enabled organization: {OrganizationId} by {AdminUserId}",
            organizationId, AdminUserId);

        return Ok(new { enabled = true });
    }

    /// <summary>
    /// Disable an organization
    /// </summary>
    [HttpPost("{organizationId}/disable")]
    [RequirePermission(AdminPermissions.OrganizationsWrite)]
    public async Task<IActionResult> Disable(string organizationId, CancellationToken cancellationToken)
    {
        var organization = await _organizationStore.GetByIdAsync(organizationId, cancellationToken);
        if (organization == null)
            return NotFound();

        organization.Enabled = false;
        await _organizationStore.UpdateAsync(organization, cancellationToken);

        _logger.LogWarning("Disabled organization: {OrganizationId} by {AdminUserId}",
            organizationId, AdminUserId);

        return Ok(new { enabled = false });
    }

    #endregion

    #region Organization Members

    /// <summary>
    /// Get all members of an organization
    /// </summary>
    [HttpGet("{organizationId}/members")]
    [RequirePermission(AdminPermissions.OrganizationsRead)]
    public async Task<ActionResult<IEnumerable<OrganizationMemberDto>>> GetMembers(
        string organizationId,
        CancellationToken cancellationToken)
    {
        var organization = await _organizationStore.GetByIdAsync(organizationId, cancellationToken);
        if (organization == null)
            return NotFound();

        var members = await _membershipStore.GetByOrganizationAsync(organizationId, cancellationToken);
        return Ok(members.Select(MapMemberToDto));
    }

    /// <summary>
    /// Get a specific member
    /// </summary>
    [HttpGet("{organizationId}/members/{membershipId}")]
    [RequirePermission(AdminPermissions.OrganizationsRead)]
    public async Task<ActionResult<OrganizationMemberDto>> GetMember(
        string organizationId,
        string membershipId,
        CancellationToken cancellationToken)
    {
        var membership = await _membershipStore.GetByIdAsync(membershipId, cancellationToken);
        if (membership == null || membership.OrganizationId != organizationId)
            return NotFound();

        return Ok(MapMemberToDto(membership));
    }

    /// <summary>
    /// Add a member to an organization
    /// </summary>
    [HttpPost("{organizationId}/members")]
    [RequirePermission(AdminPermissions.OrganizationsManageMembers)]
    public async Task<ActionResult<OrganizationMemberDto>> AddMember(
        string organizationId,
        [FromBody] AddOrganizationMemberRequest request,
        CancellationToken cancellationToken)
    {
        var organization = await _organizationStore.GetByIdAsync(organizationId, cancellationToken);
        if (organization == null)
            return NotFound();

        // Check if user is already a member
        var existing = await _membershipStore.GetByUserAndOrganizationAsync(
            request.UserId, organizationId, cancellationToken);
        if (existing != null)
        {
            return Conflict(new { error = "User is already a member of this organization" });
        }

        // Check member limit
        if (organization.MaxMembers > 0)
        {
            var memberCount = await _membershipStore.GetMemberCountAsync(organizationId, cancellationToken);
            if (memberCount >= organization.MaxMembers)
            {
                return BadRequest(new { error = $"Organization has reached maximum member limit of {organization.MaxMembers}" });
            }
        }

        var membership = new OrganizationMembership
        {
            OrganizationId = organizationId,
            UserId = request.UserId,
            Role = request.Role ?? OrganizationRole.Member,
            AllowedTenantIdsJson = request.AllowedTenantIds != null
                ? JsonSerializer.Serialize(request.AllowedTenantIds)
                : null,
            InvitedByUserId = AdminUserId
        };

        var created = await _membershipStore.CreateAsync(membership, cancellationToken);

        _logger.LogInformation("Added member {UserId} to organization {OrganizationId} with role {Role} by {AdminUserId}",
            request.UserId, organizationId, request.Role, AdminUserId);

        return CreatedAtAction(nameof(GetMember),
            new { organizationId, membershipId = created.Id },
            MapMemberToDto(created));
    }

    /// <summary>
    /// Update a member's role or tenant access
    /// </summary>
    [HttpPut("{organizationId}/members/{membershipId}")]
    [RequirePermission(AdminPermissions.OrganizationsManageMembers)]
    public async Task<ActionResult<OrganizationMemberDto>> UpdateMember(
        string organizationId,
        string membershipId,
        [FromBody] UpdateOrganizationMemberRequest request,
        CancellationToken cancellationToken)
    {
        var membership = await _membershipStore.GetByIdAsync(membershipId, cancellationToken);
        if (membership == null || membership.OrganizationId != organizationId)
            return NotFound();

        // Prevent removing the last owner
        if (membership.Role == OrganizationRole.Owner && request.Role.HasValue && request.Role != OrganizationRole.Owner)
        {
            var members = await _membershipStore.GetByOrganizationAsync(organizationId, cancellationToken);
            var ownerCount = members.Count(m => m.Role == OrganizationRole.Owner);
            if (ownerCount <= 1)
            {
                return BadRequest(new { error = "Cannot demote the last owner. Assign another owner first." });
            }
        }

        if (request.Role.HasValue) membership.Role = request.Role.Value;
        if (request.AllowedTenantIds != null)
        {
            membership.AllowedTenantIdsJson = request.AllowedTenantIds.Count > 0
                ? JsonSerializer.Serialize(request.AllowedTenantIds)
                : null;
        }

        var updated = await _membershipStore.UpdateAsync(membership, cancellationToken);

        _logger.LogInformation("Updated member {MembershipId} in organization {OrganizationId} by {AdminUserId}",
            membershipId, organizationId, AdminUserId);

        return Ok(MapMemberToDto(updated));
    }

    /// <summary>
    /// Remove a member from an organization
    /// </summary>
    [HttpDelete("{organizationId}/members/{membershipId}")]
    [RequirePermission(AdminPermissions.OrganizationsManageMembers)]
    public async Task<IActionResult> RemoveMember(
        string organizationId,
        string membershipId,
        CancellationToken cancellationToken)
    {
        var membership = await _membershipStore.GetByIdAsync(membershipId, cancellationToken);
        if (membership == null || membership.OrganizationId != organizationId)
            return NotFound();

        // Prevent removing the last owner
        if (membership.Role == OrganizationRole.Owner)
        {
            var members = await _membershipStore.GetByOrganizationAsync(organizationId, cancellationToken);
            var ownerCount = members.Count(m => m.Role == OrganizationRole.Owner);
            if (ownerCount <= 1)
            {
                return BadRequest(new { error = "Cannot remove the last owner. Assign another owner first." });
            }
        }

        await _membershipStore.DeleteAsync(membershipId, cancellationToken);

        _logger.LogInformation("Removed member {MembershipId} ({UserId}) from organization {OrganizationId} by {AdminUserId}",
            membershipId, membership.UserId, organizationId, AdminUserId);

        return NoContent();
    }

    #endregion

    #region Organization Invitations

    /// <summary>
    /// Get all invitations for an organization
    /// </summary>
    [HttpGet("{organizationId}/invitations")]
    [RequirePermission(AdminPermissions.OrganizationsRead)]
    public async Task<ActionResult<IEnumerable<OrganizationInvitationDto>>> GetInvitations(
        string organizationId,
        [FromQuery] InvitationStatus? status,
        CancellationToken cancellationToken)
    {
        var organization = await _organizationStore.GetByIdAsync(organizationId, cancellationToken);
        if (organization == null)
            return NotFound();

        var invitations = await _invitationStore.GetByOrganizationAsync(organizationId, status, cancellationToken);
        return Ok(invitations.Select(MapInvitationToDto));
    }

    /// <summary>
    /// Get a specific invitation
    /// </summary>
    [HttpGet("{organizationId}/invitations/{invitationId}")]
    [RequirePermission(AdminPermissions.OrganizationsRead)]
    public async Task<ActionResult<OrganizationInvitationDto>> GetInvitation(
        string organizationId,
        string invitationId,
        CancellationToken cancellationToken)
    {
        var invitation = await _invitationStore.GetByIdAsync(invitationId, cancellationToken);
        if (invitation == null || invitation.OrganizationId != organizationId)
            return NotFound();

        return Ok(MapInvitationToDto(invitation));
    }

    /// <summary>
    /// Create a new invitation
    /// </summary>
    [HttpPost("{organizationId}/invitations")]
    [RequirePermission(AdminPermissions.OrganizationsManageInvitations)]
    public async Task<ActionResult<OrganizationInvitationDto>> CreateInvitation(
        string organizationId,
        [FromBody] CreateOrganizationInvitationRequest request,
        CancellationToken cancellationToken)
    {
        var organization = await _organizationStore.GetByIdAsync(organizationId, cancellationToken);
        if (organization == null)
            return NotFound();

        // Check for existing pending invitation
        var existing = await _invitationStore.GetPendingByEmailAndOrganizationAsync(
            request.Email, organizationId, cancellationToken);
        if (existing != null)
        {
            return Conflict(new { error = "A pending invitation already exists for this email" });
        }

        // Check if user is already a member (you'd need to check user store by email)
        // For now, we'll skip this check as it requires IOlusoUserService

        var invitation = new OrganizationInvitation
        {
            OrganizationId = organizationId,
            Email = request.Email.ToLowerInvariant(),
            Role = request.Role ?? OrganizationRole.Member,
            InvitedByUserId = AdminUserId ?? "system",
            ExpiresAt = DateTime.UtcNow.AddDays(request.ExpirationDays ?? 7),
            AllowedTenantIdsJson = request.AllowedTenantIds != null
                ? JsonSerializer.Serialize(request.AllowedTenantIds)
                : null
        };

        var created = await _invitationStore.CreateAsync(invitation, cancellationToken);

        _logger.LogInformation("Created invitation for {Email} to organization {OrganizationId} by {AdminUserId}",
            request.Email, organizationId, AdminUserId);

        // TODO: Send invitation email via email service

        return CreatedAtAction(nameof(GetInvitation),
            new { organizationId, invitationId = created.Id },
            MapInvitationToDto(created));
    }

    /// <summary>
    /// Resend an invitation
    /// </summary>
    [HttpPost("{organizationId}/invitations/{invitationId}/resend")]
    [RequirePermission(AdminPermissions.OrganizationsManageInvitations)]
    public async Task<IActionResult> ResendInvitation(
        string organizationId,
        string invitationId,
        CancellationToken cancellationToken)
    {
        var invitation = await _invitationStore.GetByIdAsync(invitationId, cancellationToken);
        if (invitation == null || invitation.OrganizationId != organizationId)
            return NotFound();

        if (invitation.Status != InvitationStatus.Pending)
        {
            return BadRequest(new { error = "Can only resend pending invitations" });
        }

        // Extend expiration
        invitation.ExpiresAt = DateTime.UtcNow.AddDays(7);
        await _invitationStore.UpdateAsync(invitation, cancellationToken);

        _logger.LogInformation("Resent invitation {InvitationId} for organization {OrganizationId} by {AdminUserId}",
            invitationId, organizationId, AdminUserId);

        // TODO: Resend invitation email via email service

        return Ok(new { message = "Invitation resent", expiresAt = invitation.ExpiresAt });
    }

    /// <summary>
    /// Revoke an invitation
    /// </summary>
    [HttpPost("{organizationId}/invitations/{invitationId}/revoke")]
    [RequirePermission(AdminPermissions.OrganizationsManageInvitations)]
    public async Task<IActionResult> RevokeInvitation(
        string organizationId,
        string invitationId,
        CancellationToken cancellationToken)
    {
        var invitation = await _invitationStore.GetByIdAsync(invitationId, cancellationToken);
        if (invitation == null || invitation.OrganizationId != organizationId)
            return NotFound();

        if (invitation.Status != InvitationStatus.Pending)
        {
            return BadRequest(new { error = "Can only revoke pending invitations" });
        }

        invitation.Status = InvitationStatus.Revoked;
        await _invitationStore.UpdateAsync(invitation, cancellationToken);

        _logger.LogInformation("Revoked invitation {InvitationId} for organization {OrganizationId} by {AdminUserId}",
            invitationId, organizationId, AdminUserId);

        return Ok(new { status = "revoked" });
    }

    /// <summary>
    /// Delete an invitation
    /// </summary>
    [HttpDelete("{organizationId}/invitations/{invitationId}")]
    [RequirePermission(AdminPermissions.OrganizationsManageInvitations)]
    public async Task<IActionResult> DeleteInvitation(
        string organizationId,
        string invitationId,
        CancellationToken cancellationToken)
    {
        var invitation = await _invitationStore.GetByIdAsync(invitationId, cancellationToken);
        if (invitation == null || invitation.OrganizationId != organizationId)
            return NotFound();

        await _invitationStore.DeleteAsync(invitationId, cancellationToken);

        _logger.LogInformation("Deleted invitation {InvitationId} for organization {OrganizationId} by {AdminUserId}",
            invitationId, organizationId, AdminUserId);

        return NoContent();
    }

    #endregion

    #region Organization Tenants

    /// <summary>
    /// Get all tenants for an organization
    /// </summary>
    [HttpGet("{organizationId}/tenants")]
    [RequirePermission(AdminPermissions.OrganizationsRead)]
    public async Task<ActionResult<IEnumerable<OrganizationTenantDto>>> GetTenants(
        string organizationId,
        CancellationToken cancellationToken)
    {
        var organization = await _organizationStore.GetByIdAsync(organizationId, cancellationToken);
        if (organization == null)
            return NotFound();

        var tenants = await _tenantStore.GetByOrganizationAsync(organizationId, cancellationToken);
        return Ok(tenants.Select(MapTenantToDto));
    }

    /// <summary>
    /// Create a tenant for an organization
    /// </summary>
    [HttpPost("{organizationId}/tenants")]
    [RequirePermission(AdminPermissions.OrganizationsManageTenants)]
    public async Task<ActionResult<OrganizationTenantDto>> CreateTenant(
        string organizationId,
        [FromBody] CreateOrganizationTenantRequest request,
        CancellationToken cancellationToken)
    {
        var organization = await _organizationStore.GetByIdAsync(organizationId, cancellationToken);
        if (organization == null)
            return NotFound();

        // Check tenant limit
        if (organization.MaxTenants > 0)
        {
            var tenants = await _tenantStore.GetByOrganizationAsync(organizationId, cancellationToken);
            if (tenants.Count() >= organization.MaxTenants)
            {
                return BadRequest(new { error = $"Organization has reached maximum tenant limit of {organization.MaxTenants}" });
            }
        }

        // Check for existing identifier
        var existing = await _tenantStore.GetByIdentifierAsync(request.Identifier, cancellationToken);
        if (existing != null)
        {
            return Conflict(new { error = $"Tenant with identifier '{request.Identifier}' already exists" });
        }

        var tenant = new Tenant
        {
            Id = Guid.NewGuid().ToString(),
            Name = request.Name,
            DisplayName = request.DisplayName,
            Identifier = request.Identifier,
            Description = request.Description,
            OrganizationId = organizationId,
            Environment = request.Environment ?? TenantEnvironment.Production,
            Enabled = true
        };

        var created = await _tenantStore.CreateAsync(tenant, cancellationToken);

        _logger.LogInformation("Created tenant {TenantId} ({Name}) for organization {OrganizationId} by {AdminUserId}",
            created.Id, created.Name, organizationId, AdminUserId);

        return CreatedAtAction(nameof(GetTenants),
            new { organizationId },
            MapTenantToDto(created));
    }

    #endregion

    #region Mapping Methods

    private static OrganizationDto MapToDto(Organization org) => new()
    {
        Id = org.Id,
        Name = org.Name,
        Slug = org.Slug,
        Description = org.Description,
        Enabled = org.Enabled,
        LogoUrl = org.LogoUrl,
        WebsiteUrl = org.WebsiteUrl,
        BillingCustomerId = org.BillingCustomerId,
        PlanId = org.PlanId,
        PlanExpiresAt = org.PlanExpiresAt,
        MaxMembers = org.MaxMembers,
        MaxTenants = org.MaxTenants,
        TenantCount = org.Tenants?.Count ?? 0,
        Created = org.Created,
        Updated = org.Updated
    };

    private static OrganizationMemberDto MapMemberToDto(OrganizationMembership m) => new()
    {
        Id = m.Id,
        OrganizationId = m.OrganizationId,
        UserId = m.UserId,
        Role = m.Role,
        AllowedTenantIds = DeserializeStringList(m.AllowedTenantIdsJson),
        JoinedAt = m.JoinedAt,
        InvitedByUserId = m.InvitedByUserId,
        InvitationId = m.InvitationId
    };

    private static OrganizationInvitationDto MapInvitationToDto(OrganizationInvitation i) => new()
    {
        Id = i.Id,
        OrganizationId = i.OrganizationId,
        OrganizationName = i.Organization?.Name,
        Email = i.Email,
        Role = i.Role,
        Status = i.Status,
        AllowedTenantIds = DeserializeStringList(i.AllowedTenantIdsJson),
        InvitedByUserId = i.InvitedByUserId,
        CreatedAt = i.CreatedAt,
        ExpiresAt = i.ExpiresAt,
        AcceptedAt = i.AcceptedAt
    };

    private static OrganizationTenantDto MapTenantToDto(Tenant t) => new()
    {
        Id = t.Id,
        Name = t.Name,
        DisplayName = t.DisplayName,
        Identifier = t.Identifier,
        Description = t.Description,
        Environment = t.Environment,
        Enabled = t.Enabled,
        Created = t.Created,
        Updated = t.Updated
    };

    private static List<string>? DeserializeStringList(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonSerializer.Deserialize<List<string>>(json); }
        catch { return null; }
    }

    #endregion
}

#region DTOs

public class OrganizationDto
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string? Description { get; set; }
    public bool Enabled { get; set; }
    public string? LogoUrl { get; set; }
    public string? WebsiteUrl { get; set; }
    public string? BillingCustomerId { get; set; }
    public string? PlanId { get; set; }
    public DateTime? PlanExpiresAt { get; set; }
    public int MaxMembers { get; set; }
    public int MaxTenants { get; set; }
    public int TenantCount { get; set; }
    public DateTime Created { get; set; }
    public DateTime? Updated { get; set; }
}

public class CreateOrganizationRequest
{
    public string Name { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public string? Description { get; set; }
    public string? LogoUrl { get; set; }
    public string? WebsiteUrl { get; set; }
    public int? MaxMembers { get; set; }
    public int? MaxTenants { get; set; }
    public string? OwnerUserId { get; set; }
}

public class UpdateOrganizationRequest
{
    public string? Name { get; set; }
    public string? Slug { get; set; }
    public string? Description { get; set; }
    public string? LogoUrl { get; set; }
    public string? WebsiteUrl { get; set; }
    public bool? Enabled { get; set; }
    public int? MaxMembers { get; set; }
    public int? MaxTenants { get; set; }
    public string? Metadata { get; set; }
}

public class OrganizationMemberDto
{
    public string Id { get; set; } = null!;
    public string OrganizationId { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public OrganizationRole Role { get; set; }
    public List<string>? AllowedTenantIds { get; set; }
    public DateTime JoinedAt { get; set; }
    public string? InvitedByUserId { get; set; }
    public string? InvitationId { get; set; }
}

public class AddOrganizationMemberRequest
{
    public string UserId { get; set; } = null!;
    public OrganizationRole? Role { get; set; }
    public List<string>? AllowedTenantIds { get; set; }
}

public class UpdateOrganizationMemberRequest
{
    public OrganizationRole? Role { get; set; }
    public List<string>? AllowedTenantIds { get; set; }
}

public class OrganizationInvitationDto
{
    public string Id { get; set; } = null!;
    public string OrganizationId { get; set; } = null!;
    public string? OrganizationName { get; set; }
    public string Email { get; set; } = null!;
    public OrganizationRole Role { get; set; }
    public InvitationStatus Status { get; set; }
    public List<string>? AllowedTenantIds { get; set; }
    public string InvitedByUserId { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? AcceptedAt { get; set; }
}

public class CreateOrganizationInvitationRequest
{
    public string Email { get; set; } = null!;
    public OrganizationRole? Role { get; set; }
    public List<string>? AllowedTenantIds { get; set; }
    public int? ExpirationDays { get; set; }
}

public class OrganizationTenantDto
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string Identifier { get; set; } = null!;
    public string? Description { get; set; }
    public TenantEnvironment Environment { get; set; }
    public bool Enabled { get; set; }
    public DateTime Created { get; set; }
    public DateTime? Updated { get; set; }
}

public class CreateOrganizationTenantRequest
{
    public string Name { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string Identifier { get; set; } = null!;
    public string? Description { get; set; }
    public TenantEnvironment? Environment { get; set; }
}

#endregion

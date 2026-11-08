using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oluso.Admin.Authorization;
using Oluso.Core.Authorization;
using Oluso.Core.Api;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Licensing;
using Oluso.Core.Services;
using System.Text.Json;
using Oluso.Admin.Dto.Organizations;

namespace Oluso.Admin.Controllers;

/// <summary>
/// Admin API for managing Organizations
/// </summary>
[Route("api/admin/organizations")]
[Authorize(Policy = "AdminApi")]
[RequireOrganizationScope]
public class OrganizationsController : AdminBaseController
{
    private readonly IOrganizationStore _organizationStore;
    private readonly IOrganizationMembershipStore _membershipStore;
    private readonly IOrganizationInvitationStore _invitationStore;
    private readonly IOrganizationFeatureStore _featureStore;
    private readonly ITenantStore _tenantStore;
    private readonly ITenantMembershipStore _tenantMembershipStore;
    private readonly IRoleStore _roleStore;
    private readonly ITenantDefaultRoleSeeder _tenantDefaultRoleSeeder;
    private readonly IOlusoUserService _userService;
    private readonly IOlusoEventService _eventService;
    private readonly IIssuerResolver _issuerResolver;
    private readonly IEmailSender? _emailSender;
    private readonly IPlatformEmailTemplateService? _platformTemplates;
    private readonly ILogger<OrganizationsController> _logger;
    private readonly IRoleGrantService _roleGrants;
    private readonly IModuleAdminAssignmentNotifier _moduleAdminNotifier;
    private readonly ITenantSettingsProvider _tenantSettings;

    public OrganizationsController(
        IOrganizationStore organizationStore,
        IOrganizationMembershipStore membershipStore,
        IOrganizationInvitationStore invitationStore,
        IOrganizationFeatureStore featureStore,
        ITenantStore tenantStore,
        ITenantMembershipStore tenantMembershipStore,
        IRoleStore roleStore,
        ITenantDefaultRoleSeeder tenantDefaultRoleSeeder,
        IOlusoUserService userService,
        IOlusoEventService eventService,
        IIssuerResolver issuerResolver,
        IRoleGrantService roleGrants,
        IModuleAdminAssignmentNotifier moduleAdminNotifier,
        ILogger<OrganizationsController> logger,
        ITenantContext tenantContext,
        ITenantSettingsProvider tenantSettings,
        IEmailSender? emailSender = null,
        IPlatformEmailTemplateService? platformTemplates = null) : base(tenantContext)
    {
        _issuerResolver = issuerResolver;
        _organizationStore = organizationStore;
        _membershipStore = membershipStore;
        _invitationStore = invitationStore;
        _featureStore = featureStore;
        _tenantStore = tenantStore;
        _tenantMembershipStore = tenantMembershipStore;
        _roleStore = roleStore;
        _tenantDefaultRoleSeeder = tenantDefaultRoleSeeder;
        _userService = userService;
        _eventService = eventService;
        _emailSender = emailSender;
        _platformTemplates = platformTemplates;
        _roleGrants = roleGrants;
        _moduleAdminNotifier = moduleAdminNotifier;
        _logger = logger;
        _tenantSettings = tenantSettings;
    }

    private RoleGrantActor Actor() => new(AdminUserId, AdminUserName, ClientIp);

    #region Organizations CRUD

    /// <summary>
    /// Get all organizations with search and pagination.
    /// SuperAdmins see all organizations, OrgAdmins see only their own.
    /// </summary>
    [HttpGet]
    [RequirePermission(AdminPermissions.OrganizationsRead)]
    public async Task<ActionResult<PagedResult<OrganizationDto>>> GetAll(
        [FromQuery] bool includeDisabled = false,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        IEnumerable<Organization> organizations;

        // SuperAdmins see all organizations
        if (IsSuperAdmin)
        {
            organizations = await _organizationStore.GetAllAsync(includeDisabled, cancellationToken);
        }
        else
        {
            // OrgAdmins see only organizations they're members of
            var userId = AdminUserId;
            if (string.IsNullOrEmpty(userId))
            {
                return Ok(new PagedResult<OrganizationDto>
                {
                    Items = new List<OrganizationDto>(),
                    TotalCount = 0,
                    Page = page,
                    PageSize = pageSize,
                    TotalPages = 0
                });
            }

            var memberships = await _membershipStore.GetByUserAsync(userId, cancellationToken);
            var orgIds = memberships.Select(m => m.OrganizationId).ToHashSet();

            var allOrgs = await _organizationStore.GetAllAsync(includeDisabled, cancellationToken);
            organizations = allOrgs.Where(o => orgIds.Contains(o.Id));
        }

        // Apply search filter
        if (!string.IsNullOrWhiteSpace(search))
        {
            organizations = organizations.Where(o =>
                (o.Name != null && o.Name.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                (o.Slug != null && o.Slug.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                (o.Description != null && o.Description.Contains(search, StringComparison.OrdinalIgnoreCase)));
        }

        var orgList = organizations.ToList();
        var totalCount = orgList.Count;
        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

        var items = orgList
            .OrderByDescending(o => o.Created)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(MapToDto)
            .ToList();

        return Ok(new PagedResult<OrganizationDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = totalPages
        });
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
        // The slug route carries no {organizationId}, so the class-level scope
        // filter can't see it; apply the same rule once the organization is known.
        if (organization == null
            || !await RequireOrganizationScopeAttribute.IsAllowedAsync(HttpContext, organization.Id, isWrite: false))
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

            await SyncOrgAdminRoleAsync(created.Id, request.OwnerUserId, null, OrganizationRole.Owner, cancellationToken);
        }

        _logger.LogInformation("Created organization: {OrganizationId} ({Name}) by {AdminUserId}",
            created.Id, created.Name, AdminUserId);

        // Resolve owner email if available (used for the welcome email).
        string? ownerEmail = null;
        if (!string.IsNullOrEmpty(request.OwnerUserId))
        {
            var ownerUser = await _userService.FindByIdAsync(request.OwnerUserId, cancellationToken);
            ownerEmail = ownerUser?.Email;
        }

        await _eventService.RaiseAsync(new AdminOrganizationCreatedEvent
        {
            // Platform-scoped — caller's tenant (resolved or _platform)
            // becomes the audit/payload tenant; the dispatcher coerces to
            // _platform for routing per the event's declared scope.
            TenantId = TenantContext.TenantId,
            AdminUserId = AdminUserId ?? string.Empty,
            AdminUserName = AdminUserName,
            IpAddress = ClientIp,
            ResourceId = created.Id,
            ResourceName = created.Name,
            OrganizationSlug = created.Slug,
            OrganizationName = created.Name,
            OwnerUserId = request.OwnerUserId,
            OwnerEmail = ownerEmail
        }, cancellationToken);

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
    /// Search users across every tenant in the organization, for picking someone
    /// to add as a member.
    /// </summary>
    /// <remarks>
    /// The admin user list is scoped to the current tenant, so an org admin
    /// sitting in one tenant couldn't find a user whose home tenant is a sibling
    /// — even though they administer both. This searches the organization's
    /// tenants instead, and stays inside them: it never widens to a
    /// platform-wide user listing.
    /// </remarks>
    [HttpGet("{organizationId}/users")]
    [RequirePermission(AdminPermissions.OrganizationsManageMembers)]
    public async Task<ActionResult<PagedResult<OrganizationUserDto>>> SearchOrganizationUsers(
        string organizationId,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var organization = await _organizationStore.GetByIdAsync(organizationId, cancellationToken);
        if (organization == null)
            return NotFound();

        var tenants = (await _tenantStore.GetByOrganizationAsync(organizationId, cancellationToken)).ToList();
        var tenantIds = tenants.Select(t => t.Id).ToList();

        var result = await _userService.GetUsersAsync(new UsersQuery
        {
            Search = search,
            TenantIds = tenantIds,
            Page = page,
            PageSize = pageSize
        }, cancellationToken);

        var tenantNames = tenants.ToDictionary(t => t.Id, t => t.DisplayName ?? t.Name);

        return Ok(new PagedResult<OrganizationUserDto>
        {
            Items = result.Users.Select(u => new OrganizationUserDto
            {
                Id = u.Id,
                UserName = u.Username,
                Email = u.Email,
                DisplayName = u.DisplayName,
                TenantId = u.TenantId,
                TenantName = u.TenantId != null && tenantNames.TryGetValue(u.TenantId, out var name) ? name : null
            }).ToList(),
            TotalCount = result.TotalCount,
            Page = result.Page,
            PageSize = result.PageSize,
            TotalPages = result.TotalPages
        });
    }

    /// <summary>
    /// Get all members of an organization with search and pagination
    /// </summary>
    [HttpGet("{organizationId}/members")]
    [RequirePermission(AdminPermissions.OrganizationsRead)]
    public async Task<ActionResult<PagedResult<OrganizationMemberDto>>> GetMembers(
        string organizationId,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var organization = await _organizationStore.GetByIdAsync(organizationId, cancellationToken);
        if (organization == null)
            return NotFound();

        IEnumerable<OrganizationMembership> members = await _membershipStore.GetByOrganizationAsync(organizationId, cancellationToken);

        // Apply search filter
        if (!string.IsNullOrWhiteSpace(search))
        {
            members = members.Where(m =>
                (m.UserId != null && m.UserId.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                m.Role.ToString().Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        var memberList = members.ToList();
        var totalCount = memberList.Count;
        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

        var pagedMembers = memberList
            .OrderByDescending(m => m.JoinedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        // Members live in any tenant of the organization, so look them up across
        // tenants rather than in the tenant the admin is viewing from.
        var users = await _userService.FindByIdsAcrossTenantsAsync(
            pagedMembers.Select(m => m.UserId).ToList(), cancellationToken);
        var items = new List<OrganizationMemberDto>(pagedMembers.Count);
        foreach (var m in pagedMembers)
        {
            var dto = MapMemberToDto(m);
            if (users.TryGetValue(m.UserId, out var userInfo))
            {
                dto.UserName = userInfo.Username;
                dto.Email = userInfo.Email;
                dto.DisplayName = userInfo.DisplayName
                    ?? $"{userInfo.FirstName} {userInfo.LastName}".Trim();
            }
            items.Add(dto);
        }

        return Ok(new PagedResult<OrganizationMemberDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = totalPages
        });
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

        var requestedRole = request.Role ?? OrganizationRole.Member;
        if (!await CanGrantOrganizationRoleAsync(organizationId, requestedRole, cancellationToken))
        {
            return Forbid();
        }

        var membership = new OrganizationMembership
        {
            OrganizationId = organizationId,
            UserId = request.UserId,
            Role = requestedRole,
            AllowedTenantIdsJson = request.AllowedTenantIds != null
                ? JsonSerializer.Serialize(request.AllowedTenantIds)
                : null,
            InvitedByUserId = AdminUserId
        };

        var created = await _membershipStore.CreateAsync(membership, cancellationToken);

        await SyncOrgAdminRoleAsync(organizationId, request.UserId, null, created.Role, cancellationToken);

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

        if (request.Role.HasValue
            && !await CanGrantOrganizationRoleAsync(organizationId, request.Role.Value, cancellationToken))
        {
            return Forbid();
        }

        var previousRole = membership.Role;
        if (request.Role.HasValue) membership.Role = request.Role.Value;
        if (request.AllowedTenantIds != null)
        {
            membership.AllowedTenantIdsJson = request.AllowedTenantIds.Count > 0
                ? JsonSerializer.Serialize(request.AllowedTenantIds)
                : null;
        }

        var updated = await _membershipStore.UpdateAsync(membership, cancellationToken);

        await SyncOrgAdminRoleAsync(organizationId, updated.UserId, previousRole, updated.Role, cancellationToken);

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

        // The membership that justified OrgAdmin is gone, so re-evaluate. Without
        // this the role outlives the membership and, since it can't be assigned or
        // stripped by hand, there'd be no way left to remove it.
        await SyncOrgAdminRoleAsync(organizationId, membership.UserId, membership.Role, null, cancellationToken);

        // Leaving the organization ends access to its tenants. The home tenant is
        // where the account lives, so it stays.
        var homeTenantId = (await _userService.FindByIdsAcrossTenantsAsync(new[] { membership.UserId }, cancellationToken))
            .GetValueOrDefault(membership.UserId)?.TenantId;
        foreach (var tenant in await _tenantStore.GetByOrganizationAsync(organizationId, cancellationToken))
        {
            if (tenant.Id == homeTenantId) continue;
            await RevokeTenantAccessAsync(membership.UserId, tenant.Id, cancellationToken);
        }

        _logger.LogInformation("Removed member {MembershipId} ({UserId}) from organization {OrganizationId} by {AdminUserId}",
            membershipId, membership.UserId, organizationId, AdminUserId);

        return NoContent();
    }

    /// <summary>
    /// Removes a user's access to a tenant: every tenant role they hold there
    /// (through the grant service, so module admins are told) and the membership.
    /// Global roles and OrgAdmin are platform-scoped and owned elsewhere
    /// (<see cref="SyncOrgAdminRoleAsync"/>), so they are left alone.
    /// </summary>
    private async Task RevokeTenantAccessAsync(string userId, string tenantId, CancellationToken cancellationToken)
    {
        var roles = await _roleStore.GetUserRolesInTenantAsync(userId, tenantId, cancellationToken);
        foreach (var role in roles.Where(r => !GlobalRoles.IsGlobalName(r.NormalizedName)
                                            && !string.Equals(r.Name, EffectiveRoleResolver.OrgAdminRoleName, StringComparison.OrdinalIgnoreCase)))
            await _roleGrants.RevokeAsync(role.Id, userId, tenantId, Actor(), cancellationToken);

        await _tenantMembershipStore.RemoveAsync(tenantId, userId, cancellationToken);
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

        // An invitation is a deferred membership, so the same rule applies —
        // otherwise Owner could be handed out by inviting rather than promoting.
        var invitedRole = request.Role ?? OrganizationRole.Member;
        if (!await CanGrantOrganizationRoleAsync(organizationId, invitedRole, cancellationToken))
        {
            return Forbid();
        }

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
            ExpiresAt = DateTime.UtcNow.AddDays(request.ExpirationDays
                ?? (await _tenantSettings.GetAccountEmailSettingsAsync(cancellationToken)).InvitationLifetimeDays),
            AllowedTenantIdsJson = request.AllowedTenantIds != null
                ? JsonSerializer.Serialize(request.AllowedTenantIds)
                : null
        };

        invitation.Message = request.Message;

        var created = await _invitationStore.CreateAsync(invitation, cancellationToken);

        _logger.LogInformation("Created invitation for {Email} to organization {OrganizationId} by {AdminUserId}",
            request.Email, organizationId, AdminUserId);

        await SendInvitationEmailAsync(created, organization, cancellationToken);

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
        invitation.ExpiresAt = DateTime.UtcNow.AddDays((await _tenantSettings.GetAccountEmailSettingsAsync(cancellationToken)).InvitationLifetimeDays);
        await _invitationStore.UpdateAsync(invitation, cancellationToken);

        _logger.LogInformation("Resent invitation {InvitationId} for organization {OrganizationId} by {AdminUserId}",
            invitationId, organizationId, AdminUserId);

        var organization = invitation.Organization
            ?? await _organizationStore.GetByIdAsync(organizationId, cancellationToken);
        var emailSent = organization != null
            && await SendInvitationEmailAsync(invitation, organization, cancellationToken);

        return Ok(new { message = "Invitation resent", expiresAt = invitation.ExpiresAt, emailSent });
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
    /// Get all tenants for an organization with search and pagination
    /// </summary>
    [HttpGet("{organizationId}/tenants")]
    [RequirePermission(AdminPermissions.OrganizationsRead)]
    public async Task<ActionResult<PagedResult<OrganizationTenantDto>>> GetTenants(
        string organizationId,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var organization = await _organizationStore.GetByIdAsync(organizationId, cancellationToken);
        if (organization == null)
            return NotFound();

        IEnumerable<Tenant> tenants = await _tenantStore.GetByOrganizationAsync(organizationId, cancellationToken);

        // Apply search filter
        if (!string.IsNullOrWhiteSpace(search))
        {
            tenants = tenants.Where(t =>
                (t.Name != null && t.Name.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                (t.Identifier != null && t.Identifier.Contains(search, StringComparison.OrdinalIgnoreCase)) ||
                (t.DisplayName != null && t.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)));
        }

        var tenantList = tenants.ToList();
        var totalCount = tenantList.Count;
        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

        var items = tenantList
            .OrderByDescending(t => t.Created)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(MapTenantToDto)
            .ToList();

        return Ok(new PagedResult<OrganizationTenantDto>
        {
            Items = items,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize,
            TotalPages = totalPages
        });
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

        // Same post-create work TenantsController.Create does. Without it the
        // tenant has no Admin/User role and no module seeding, so nobody can be
        // made an admin of it afterwards — the role lookup finds nothing.
        if (!await SeedDefaultRolesAsync(created.Id, cancellationToken))
        {
            try
            {
                await _tenantStore.DeleteAsync(created.Id, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to remove tenant {TenantId} after its default roles could not be seeded", created.Id);
            }
            return StatusCode(500, new { error = "Failed to seed default roles for the new tenant" });
        }

        await _eventService.RaiseAsync(
            new AdminTenantCreatedEvent(created, AdminUserId!, User.Identity?.Name),
            cancellationToken);

        _logger.LogInformation("Created tenant {TenantId} ({Name}) for organization {OrganizationId} by {AdminUserId}",
            created.Id, created.Name, organizationId, AdminUserId);

        return CreatedAtAction(nameof(GetTenants),
            new { organizationId },
            MapTenantToDto(created));
    }

    #endregion

    #region Member Tenant Access

    /// <summary>
    /// Get a member's tenant access across this organization's tenants.
    /// Returns all org tenants with whether the member has access and their role in each.
    /// </summary>
    [HttpGet("{organizationId}/members/{userId}/tenants")]
    [RequirePermission(AdminPermissions.OrganizationsRead)]
    public async Task<ActionResult<List<MemberTenantAccessDto>>> GetMemberTenants(
        string organizationId,
        string userId,
        CancellationToken cancellationToken)
    {
        var organization = await _organizationStore.GetByIdAsync(organizationId, cancellationToken);
        if (organization == null)
            return NotFound();

        // Verify user is a member of this org
        var membership = await _membershipStore.GetByUserAndOrganizationAsync(userId, organizationId, cancellationToken);
        if (membership == null)
            return NotFound(new { error = "User is not a member of this organization" });

        // Get all tenants in this org
        var orgTenants = (await _tenantStore.GetByOrganizationAsync(organizationId, cancellationToken)).ToList();

        // Get user's tenant memberships
        var tenantMemberships = (await _tenantMembershipStore.GetByUserAsync(userId, cancellationToken)).ToList();
        var memberTenantIds = tenantMemberships.Select(tm => tm.TenantId).ToHashSet();
        var homeTenantId = (await _userService.FindByIdsAcrossTenantsAsync(new[] { userId }, cancellationToken))
            .GetValueOrDefault(userId)?.TenantId;

        var result = new List<MemberTenantAccessDto>();
        foreach (var tenant in orgTenants)
        {
            var dto = new MemberTenantAccessDto
            {
                TenantId = tenant.Id,
                TenantName = tenant.Name,
                TenantIdentifier = tenant.Identifier,
                Environment = tenant.Environment,
                HasAccess = memberTenantIds.Contains(tenant.Id) || tenant.Id == homeTenantId,
                IsHomeTenant = tenant.Id == homeTenantId,
                JoinedAt = tenantMemberships.FirstOrDefault(tm => tm.TenantId == tenant.Id)?.JoinedAt
            };

            // Roles assigned to the user in this tenant
            var heldRoles = await _roleStore.GetUserRolesInTenantAsync(userId, tenant.Id, cancellationToken);
            dto.Roles = heldRoles.Select(r => r.Name ?? r.Id).ToList();

            result.Add(dto);
        }

        return Ok(result);
    }

    /// <summary>
    /// Grants a member access to one of the organization's tenants.
    /// </summary>
    [HttpPost("{organizationId}/members/{userId}/tenants/{tenantId}")]
    [RequirePermission(AdminPermissions.OrganizationsManageMembers)]
    public async Task<IActionResult> GrantMemberTenant(
        string organizationId,
        string userId,
        string tenantId,
        CancellationToken cancellationToken)
    {
        var problem = await ValidateMemberTenantAsync(organizationId, userId, tenantId, cancellationToken);
        if (problem != null)
            return problem;

        if (await _tenantMembershipStore.GetByTenantAndUserAsync(tenantId, userId, cancellationToken) == null)
        {
            await _tenantMembershipStore.AddAsync(new TenantMembership
            {
                TenantId = tenantId,
                UserId = userId,
                InvitedByUserId = AdminUserId
            }, cancellationToken);

            _logger.LogInformation("Granted tenant {TenantId} to user {UserId} in org {OrganizationId} by {AdminUserId}",
                tenantId, userId, organizationId, AdminUserId);
        }

        return NoContent();
    }

    /// <summary>
    /// Revokes a member's access to one of the organization's tenants, with their
    /// roles there. The home tenant is where the account lives and can't be revoked.
    /// </summary>
    [HttpDelete("{organizationId}/members/{userId}/tenants/{tenantId}")]
    [RequirePermission(AdminPermissions.OrganizationsManageMembers)]
    public async Task<IActionResult> RevokeMemberTenant(
        string organizationId,
        string userId,
        string tenantId,
        CancellationToken cancellationToken)
    {
        var problem = await ValidateMemberTenantAsync(organizationId, userId, tenantId, cancellationToken);
        if (problem != null)
            return problem;

        var homeTenantId = (await _userService.FindByIdsAcrossTenantsAsync(new[] { userId }, cancellationToken))
            .GetValueOrDefault(userId)?.TenantId;
        if (tenantId == homeTenantId)
            return BadRequest(new { error = "This is the member's home tenant; access to it can't be removed." });

        await RevokeTenantAccessAsync(userId, tenantId, cancellationToken);

        _logger.LogInformation("Revoked tenant {TenantId} from user {UserId} in org {OrganizationId} by {AdminUserId}",
            tenantId, userId, organizationId, AdminUserId);

        return NoContent();
    }

    private async Task<IActionResult?> ValidateMemberTenantAsync(
        string organizationId, string userId, string tenantId, CancellationToken cancellationToken)
    {
        if (await _organizationStore.GetByIdAsync(organizationId, cancellationToken) == null)
            return NotFound();

        if (await _membershipStore.GetByUserAndOrganizationAsync(userId, organizationId, cancellationToken) == null)
            return NotFound(new { error = "User is not a member of this organization" });

        var tenant = await _tenantStore.GetByIdAsync(tenantId, cancellationToken);
        if (tenant == null || tenant.OrganizationId != organizationId)
            return BadRequest(new { error = "The tenant does not belong to this organization" });

        return null;
    }

    /// <summary>
    /// Assign or change a member's role in a specific tenant.
    /// </summary>
    [HttpPut("{organizationId}/members/{userId}/tenants/{tenantId}/role")]
    [RequirePermission(AdminPermissions.OrganizationsManageMembers)]
    public async Task<IActionResult> UpdateMemberTenantRole(
        string organizationId,
        string userId,
        string tenantId,
        [FromBody] UpdateMemberTenantRoleRequest request,
        CancellationToken cancellationToken)
    {
        var organization = await _organizationStore.GetByIdAsync(organizationId, cancellationToken);
        if (organization == null)
            return NotFound();

        // Verify tenant belongs to org
        var tenant = await _tenantStore.GetByIdAsync(tenantId, cancellationToken);
        if (tenant == null || tenant.OrganizationId != organizationId)
            return NotFound(new { error = "Tenant not found in this organization" });

        // Verify user has tenant membership
        var tenantMembership = await _tenantMembershipStore.GetByTenantAndUserAsync(tenantId, userId, cancellationToken);
        if (tenantMembership == null)
            return BadRequest(new { error = "User does not have access to this tenant. Grant tenant access first." });

        // Find the target role
        var role = await _roleStore.GetByNameAsync(request.RoleName, tenantId, cancellationToken);
        if (role == null)
            return BadRequest(new { error = $"Role '{request.RoleName}' not found in tenant" });

        // Remove the tenant's own roles held by this user in this tenant
        var heldRoles = await _roleStore.GetUserRolesInTenantAsync(userId, tenantId, cancellationToken);
        foreach (var existingRole in heldRoles.Where(r => r.TenantId == tenantId))
        {
            await _roleGrants.RevokeAsync(existingRole.Id, userId, tenantId, Actor(), cancellationToken);
        }

        // Assign new role
        await _roleGrants.GrantAsync(role.Id, userId, tenantId, Actor(), cancellationToken);

        _logger.LogInformation(
            "Assigned role '{RoleName}' to user {UserId} in tenant {TenantId} by {AdminUserId}",
            request.RoleName, userId, tenantId, AdminUserId);

        return NoContent();
    }

    #endregion

    #region Organization Features

    /// <summary>
    /// Get features for an organization, including all available feature definitions
    /// </summary>
    [HttpGet("{organizationId}/features")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<ActionResult<OrganizationFeaturesResponse>> GetFeatures(
        string organizationId,
        CancellationToken cancellationToken)
    {
        var organization = await _organizationStore.GetByIdAsync(organizationId, cancellationToken);
        if (organization == null)
            return NotFound();

        var enabledFeatures = await _featureStore.GetFeaturesAsync(organizationId, cancellationToken);

        var featureMap = enabledFeatures.ToDictionary(f => f.FeatureKey, StringComparer.OrdinalIgnoreCase);

        var features = ModuleFeatures.GetRegistered().Select(def =>
            {
                featureMap.TryGetValue(def.Key, out var existing);
                return new OrganizationFeatureDto
                {
                    Key = def.Key,
                    DisplayName = def.DisplayName,
                    Description = def.Description,
                    Enabled = existing != null && existing.Enabled && (existing.ExpiresAt == null || existing.ExpiresAt > DateTime.UtcNow),
                    GrantedAt = existing?.GrantedAt,
                    ExpiresAt = existing?.ExpiresAt,
                    MaxSeats = existing?.MaxSeats,
                    Source = existing?.Source ?? "manual",
                    SourceSubscriptionId = existing?.SourceSubscriptionId,
                };
            }).ToList();

        return Ok(new OrganizationFeaturesResponse
        {
            OrganizationId = organizationId,
            HasFeaturesConfigured = enabledFeatures.Count > 0,
            Features = features,
        });
    }

    /// <summary>
    /// Bulk set features for an organization (SuperAdmin only)
    /// </summary>
    [HttpPut("{organizationId}/features")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> SetFeatures(
        string organizationId,
        [FromBody] SetOrganizationFeaturesRequest request,
        CancellationToken cancellationToken)
    {
        var organization = await _organizationStore.GetByIdAsync(organizationId, cancellationToken);
        if (organization == null)
            return NotFound();

        var updates = request.Features.Select(f => new OrganizationFeatureUpdate(f.FeatureKey, f.Enabled, f.ExpiresAt, f.MaxSeats));
        await _featureStore.SetFeaturesAsync(organizationId, updates, AdminUserId, cancellationToken);

        _logger.LogInformation("Set features for organization {OrganizationId} by {AdminUserId}: {Features}",
            organizationId, AdminUserId,
            string.Join(", ", request.Features.Where(f => f.Enabled).Select(f => f.FeatureKey)));

        return Ok(new { message = "Features updated" });
    }

    /// <summary>
    /// Enable a single feature for an organization (SuperAdmin only)
    /// </summary>
    [HttpPost("{organizationId}/features/{featureKey}")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> EnableFeature(
        string organizationId,
        string featureKey,
        [FromBody] EnableFeatureRequest? request,
        CancellationToken cancellationToken)
    {
        var organization = await _organizationStore.GetByIdAsync(organizationId, cancellationToken);
        if (organization == null)
            return NotFound();

        await _featureStore.EnableFeatureAsync(organizationId, featureKey, AdminUserId, request?.ExpiresAt, cancellationToken);

        _logger.LogInformation("Enabled feature '{Feature}' for organization {OrganizationId} by {AdminUserId}",
            featureKey, organizationId, AdminUserId);

        await _eventService.RaiseAsync(new FeatureEnabledEvent
        {
            AdminUserId = AdminUserId!,
            AdminUserName = AdminUserName,
            IpAddress = ClientIp,
            ResourceId = organizationId,
            FeatureKey = featureKey,
            FeatureScope = "organization",
            OrganizationId = organizationId
        }, cancellationToken);

        return Ok(new { featureKey, enabled = true });
    }

    /// <summary>
    /// Disable a single feature for an organization (SuperAdmin only)
    /// </summary>
    [HttpDelete("{organizationId}/features/{featureKey}")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<IActionResult> DisableFeature(
        string organizationId,
        string featureKey,
        CancellationToken cancellationToken)
    {
        var organization = await _organizationStore.GetByIdAsync(organizationId, cancellationToken);
        if (organization == null)
            return NotFound();

        await _featureStore.DisableFeatureAsync(organizationId, featureKey, cancellationToken);

        _logger.LogInformation("Disabled feature '{Feature}' for organization {OrganizationId} by {AdminUserId}",
            featureKey, organizationId, AdminUserId);

        return Ok(new { featureKey, enabled = false });
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

    #region Provisioning

    /// <summary>
    /// Provision a new organization with a tenant and admin user in a single call.
    /// Creates the org, tenant (with default roles + module seeding), creates or reuses
    /// the admin user, assigns them as Org Owner and Tenant Admin, and sends a welcome email.
    /// </summary>
    [HttpPost("provision")]
    [Authorize(Policy = "SuperAdmin")]
    public async Task<ActionResult<ProvisionOrganizationResponse>> Provision(
        [FromBody] ProvisionOrganizationRequest request,
        [FromServices] Microsoft.Extensions.Configuration.IConfiguration configuration,
        CancellationToken cancellationToken)
    {
        // 1. Validate slug uniqueness
        if (await _organizationStore.SlugExistsAsync(request.Slug, cancellationToken))
            return Conflict(new { error = $"Organization with slug '{request.Slug}' already exists" });

        // 2. Validate tenant identifier uniqueness
        if (await _tenantStore.GetByIdentifierAsync(request.TenantIdentifier, cancellationToken) != null)
            return Conflict(new { error = $"Tenant with identifier '{request.TenantIdentifier}' already exists" });

        // 3. Create the Organization
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
        var createdOrg = await _organizationStore.CreateAsync(organization, cancellationToken);

        // 4. Create the Tenant under the organization
        var tenant = new Tenant
        {
            Id = Guid.NewGuid().ToString(),
            Name = request.TenantName ?? request.Name,
            DisplayName = request.TenantDisplayName,
            Identifier = request.TenantIdentifier,
            Description = request.TenantDescription,
            OrganizationId = createdOrg.Id,
            Environment = request.TenantEnvironment ?? TenantEnvironment.Production,
            Enabled = true
        };
        var createdTenant = await _tenantStore.CreateAsync(tenant, cancellationToken);

        // 5. Seed default roles (Admin + User) for the tenant
        if (!await SeedDefaultRolesAsync(createdTenant.Id, cancellationToken))
        {
            await RollbackProvisionAsync(createdOrg.Id, createdTenant.Id, cancellationToken);
            return StatusCode(500, new { error = "Failed to seed default roles for the new tenant" });
        }

        // 6. Fire AdminTenantCreatedEvent to trigger module seeding (email templates, etc.)
        await _eventService.RaiseAsync(
            new AdminTenantCreatedEvent(createdTenant, AdminUserId!, User.Identity?.Name),
            cancellationToken);

        // 7. Create or find the admin user
        var existingUser = await _userService.FindByEmailAsync(request.AdminEmail, cancellationToken);
        string adminUserId;
        bool isNewUser = false;

        if (existingUser != null)
        {
            adminUserId = existingUser.Id;
        }
        else
        {
            using var origin = EventOriginScope.Begin(EventOrigin.Admin(AdminUserId!, AdminUserName, ClientIp));
            var createResult = await _userService.CreateUserAsync(new Oluso.Core.Services.CreateUserRequest
            {
                Email = request.AdminEmail,
                // Identity's default UserValidator rejects a null/empty UserName,
                // so the whole provision fails without this.
                Username = request.AdminEmail,
                FirstName = request.AdminFirstName,
                LastName = request.AdminLastName,
                TenantId = createdTenant.Id,
                RequireEmailVerification = false
            }, cancellationToken);

            if (!createResult.Succeeded)
            {
                // The org and tenant are already committed at this point. Leaving
                // them behind strands the slug and identifier, so a retry with the
                // same details hits the uniqueness checks at the top and can never
                // succeed. Undo them before reporting the failure.
                await RollbackProvisionAsync(createdOrg.Id, createdTenant.Id, cancellationToken);
                return BadRequest(new { error = $"Failed to create admin user: {createResult.MessageOr("unknown error")}" });
            }

            adminUserId = createResult.UserId!;
            isNewUser = true;
        }

        // 8. Add user as Organization Owner
        var membership = new OrganizationMembership
        {
            OrganizationId = createdOrg.Id,
            UserId = adminUserId,
            Role = OrganizationRole.Owner
        };
        await _membershipStore.CreateAsync(membership, cancellationToken);

        // Owner of the organization, so they need the role that authorization
        // actually keys off — without it they can run their tenant but not the
        // organization they were just made owner of.
        await SyncOrgAdminRoleAsync(createdOrg.Id, adminUserId, null, OrganizationRole.Owner, cancellationToken);

        // 9. Create TenantMembership for admin user
        await _tenantMembershipStore.AddAsync(new TenantMembership
        {
            TenantId = createdTenant.Id,
            UserId = adminUserId,
            InvitedByUserId = AdminUserId
        }, cancellationToken);

        // 10. Add user to tenant Admin role (tenant-scoped lookup)
        var adminRole = await _roleStore.GetByNameAsync("Admin", createdTenant.Id, cancellationToken);
        if (adminRole != null)
        {
            await _roleGrants.GrantAsync(adminRole.Id, adminUserId, createdTenant.Id, Actor(), cancellationToken);
        }
        else
        {
            _logger.LogWarning("Admin role not found for tenant {TenantId} during provisioning", createdTenant.Id);
        }

        // 11. Send the welcome / set-password email.
        //
        // We send whenever the admin has no local password to sign in with — that is
        // every brand-new user, AND any *reused* account that is still password-less
        // (e.g. provisioned before, or SSO-only). An existing account that already has
        // a password doesn't need a set-password link, so we skip it there.
        //
        // Every skip below is logged. Previously the whole block was gated on isNewUser
        // with no else-branch, so a reused email produced org+tenant+user with roles but
        // *silently* no email — indistinguishable in the logs from success.
        string? resetToken = null;
        bool welcomeEmailSent = false;

        var adminHasPassword = !isNewUser && await _userService.HasPasswordAsync(adminUserId, cancellationToken);
        if (adminHasPassword)
        {
            _logger.LogInformation(
                "Provision: admin {Email} already has a password; no set-password email sent.", request.AdminEmail);
        }
        else
        {
            resetToken = await _userService.GeneratePasswordResetTokenAsync(adminUserId, cancellationToken);

            if (resetToken == null)
            {
                // GeneratePasswordResetTokenAsync refuses federated/SSO-managed accounts.
                _logger.LogWarning(
                    "Provision: could not mint a set-password token for admin {Email} (user {UserId}) — " +
                    "account is likely federated/SSO-managed; welcome email skipped.",
                    request.AdminEmail, adminUserId);
            }
            else if (_emailSender == null)
            {
                _logger.LogWarning(
                    "Provision: no IEmailSender is registered — welcome email to {Email} skipped. " +
                    "Wire AddMessaging(configuration) in startup.", request.AdminEmail);
            }
            else
            {
                // /account/reset-password is a Razor page served by the identity
                // host, not by whatever SPA called this endpoint — so the link has
                // to be built from the new tenant's own issuer (custom domain,
                // else {identifier}.{issuer-host}), never from the caller's origin.
                // BaseUrl stays honoured as an explicit operator override.
                var baseUrl = !string.IsNullOrWhiteSpace(request.BaseUrl)
                    ? request.BaseUrl.TrimEnd('/')
                    : (await _issuerResolver.GetIssuerForTenantAsync(createdTenant, cancellationToken)).TrimEnd('/');
                var resetLink = $"{baseUrl}/account/reset-password?token={Uri.EscapeDataString(resetToken)}&email={Uri.EscapeDataString(request.AdminEmail)}";
                // The link is validated on the new tenant's host, so it's that tenant's lifetime.
                var linkExpiresIn = Durations.Describe(TimeSpan.FromMinutes(
                    (await _tenantSettings.GetAccountEmailSettingsAsync(createdTenant.Id, cancellationToken)).AccountLinkLifetimeMinutes));

                // The platform welcomes the organisation, so the email names the platform, not the new tenant.
                var platformName = configuration["Oluso:Branding:CompanyName"];
                if (string.IsNullOrWhiteSpace(platformName))
                {
                    _logger.LogWarning(
                        "Provision: Oluso:Branding:CompanyName is not configured, so the welcome email to {Email} doesn't name the platform.",
                        request.AdminEmail);
                }

                string subject;
                string html;
                RenderedEmail? rendered = null;
                if (_platformTemplates != null)
                {
                    rendered = await _platformTemplates.RenderAsync(
                        PlatformTemplateNames.WelcomeInvite,
                        new Dictionary<string, object>
                        {
                            [PlatformTemplateScopeKeys.ResetLink] = resetLink,
                            ["org_name"] = createdOrg.Name,
                            [PlatformTemplateScopeKeys.TenantName] = createdTenant.Name,
                            ["tenant_identifier"] = createdTenant.Identifier,
                            [PlatformTemplateScopeKeys.ExpiresIn] = linkExpiresIn,
                            [PlatformTemplateScopeKeys.PlatformName] = platformName ?? string.Empty,
                        },
                        cancellationToken);
                }

                if (rendered != null)
                {
                    subject = rendered.Subject;
                    html = rendered.HtmlBody;
                }
                else
                {
                    var org = System.Net.WebUtility.HtmlEncode(createdOrg.Name);
                    var platform = string.IsNullOrWhiteSpace(platformName) ? null : System.Net.WebUtility.HtmlEncode(platformName);
                    var accent = BrandedEmailLayout.DefaultAccent;
                    var welcome = platform == null ? "Welcome aboard" : $"Welcome to {platform}";
                    subject = platform == null
                        ? $"Your administrator account for {createdOrg.Name} is ready"
                        : $"Welcome to {platformName}, {createdOrg.Name}";
                    html = BrandedEmailLayout.Document(
                        title: welcome,
                        preheader: "Your administrator account is ready. Set your password to get started.",
                        accent: accent,
                        brand: BrandedEmailLayout.BrandMark(new EmailBrand(platformName ?? createdOrg.Name, accent, null)),
                        eyebrow: "Welcome aboard",
                        heading: welcome,
                        rows:
                            BrandedEmailLayout.Lead((platform == null
                                    ? $"{BrandedEmailLayout.Strong(org)} is ready to go."
                                    : $"{BrandedEmailLayout.Strong(platform)} is delighted to welcome {BrandedEmailLayout.Strong(org)} on board.")
                                + " An administrator account has been created for you. Set your password to get started.") +
                            BrandedEmailLayout.Pill("Workspace", System.Net.WebUtility.HtmlEncode($"{createdTenant.Name} ({createdTenant.Identifier})"), accent) +
                            BrandedEmailLayout.Button(resetLink, "Set your password &rarr;", accent) +
                            BrandedEmailLayout.Note($"This link expires in <strong>{linkExpiresIn}</strong>.") +
                            BrandedEmailLayout.Divider() +
                            BrandedEmailLayout.LinkFallback(resetLink, accent),
                        footnote: $"You received this because an administrator account was created for this address for {org}"
                            + (platform == null ? "." : $" on {platform}."));
                }

                var emailResult = await _emailSender.SendAsync(
                    request.AdminEmail,
                    subject,
                    html,
                    cancellationToken);

                welcomeEmailSent = emailResult.Success;
                if (emailResult.Success)
                    _logger.LogInformation("Provision: welcome/set-password email sent to {Email}.", request.AdminEmail);
                else
                    _logger.LogWarning("Provision: failed to send welcome email to {Email}: {Error}", request.AdminEmail, emailResult.Error);
            }
        }

        _logger.LogInformation(
            "Provisioned organization {OrgId} ({OrgName}), tenant {TenantId} ({TenantName}), admin user {AdminUserId} ({AdminEmail}) by {ActorId}",
            createdOrg.Id, createdOrg.Name, createdTenant.Id, createdTenant.Name, adminUserId, request.AdminEmail, AdminUserId);

        return Ok(new ProvisionOrganizationResponse
        {
            OrganizationId = createdOrg.Id,
            OrganizationName = createdOrg.Name,
            TenantId = createdTenant.Id,
            TenantIdentifier = createdTenant.Identifier,
            AdminUserId = adminUserId,
            AdminEmail = request.AdminEmail,
            IsNewUser = isNewUser,
            WelcomeEmailSent = welcomeEmailSent,
        });
    }

    /// <summary>
    /// Whether the caller may hand out <paramref name="role"/> in this organization.
    /// <para>
    /// Only an Owner may create another Owner. RequireOrganizationScope admits
    /// Admins to these endpoints, so without this an Admin could set Owner on any
    /// membership — including their own — and promote themselves.
    /// </para>
    /// </summary>
    private async Task<bool> CanGrantOrganizationRoleAsync(
        string organizationId, OrganizationRole role, CancellationToken cancellationToken)
    {
        if (role != OrganizationRole.Owner) return true;
        if (IsSuperAdmin) return true;
        if (string.IsNullOrEmpty(AdminUserId)) return false;

        var isOwner = await _membershipStore.IsOwnerAsync(AdminUserId, organizationId, cancellationToken);
        if (!isOwner)
        {
            _logger.LogWarning(
                "User {AdminUserId} attempted to grant Owner in organization {OrgId} without being an owner",
                AdminUserId, organizationId);
        }

        return isOwner;
    }

    /// <summary>
    /// Follows a change to a user's membership role in an organization.
    /// <para>
    /// Organization-admin rights come from the membership itself
    /// (EffectiveRoleResolver), so the module admins affected by it are told
    /// here, on an actual Owner/Admin transition. The OrgAdmin assignment row is
    /// still kept in step, written directly: it is no longer an authorization
    /// input, so it must not notify a second time.
    /// </para>
    /// </summary>
    private async Task SyncOrgAdminRoleAsync(
        string organizationId,
        string userId,
        OrganizationRole? previousRole,
        OrganizationRole? newRole,
        CancellationToken cancellationToken)
    {
        var wasAdmin = previousRole is OrganizationRole.Owner or OrganizationRole.Admin;
        var isAdmin = newRole is OrganizationRole.Owner or OrganizationRole.Admin;
        if (wasAdmin != isAdmin)
        {
            await _moduleAdminNotifier.NotifyOrganizationAdminChangedAsync(
                organizationId, userId, removed: !isAdmin, Actor(), cancellationToken);
        }

        var orgAdminRole = await _roleStore.GetByNameAsync("OrgAdmin", null, cancellationToken);
        if (orgAdminRole == null)
        {
            _logger.LogWarning(
                "OrgAdmin role is not seeded, so user {UserId} was given org role {Role} without it", userId, newRole);
            return;
        }

        if (isAdmin)
        {
            await _roleStore.AssignToUserAsync(orgAdminRole.Id, userId, AdminUserId, cancellationToken);
            _logger.LogInformation("Granted OrgAdmin to {UserId} for org role {Role}", userId, newRole);
            return;
        }

        // Only revoke when they no longer lead ANY organization — the role is
        // platform-scoped, so a demotion in one org must not strip the rights
        // they still hold in another.
        var memberships = await _membershipStore.GetByUserAsync(userId, cancellationToken);
        if (memberships.Any(m => m.Role is OrganizationRole.Owner or OrganizationRole.Admin))
        {
            return;
        }

        await _roleStore.RemoveFromUserAsync(orgAdminRole.Id, userId, cancellationToken);
        _logger.LogInformation("Revoked OrgAdmin from {UserId} — no remaining owner/admin memberships", userId);
    }

    /// <summary>
    /// Sends the invitation email carrying the accept link. Returns whether it
    /// actually went out — invitations are useless without it, so a silent skip
    /// would leave an admin believing someone had been invited.
    /// </summary>
    private async Task<bool> SendInvitationEmailAsync(
        OrganizationInvitation invitation,
        Organization organization,
        CancellationToken cancellationToken)
    {
        if (_emailSender == null)
        {
            _logger.LogWarning(
                "No IEmailSender is registered — invitation email to {Email} skipped. " +
                "Wire AddMessaging(configuration) in startup.", invitation.Email);
            return false;
        }

        // /account/accept-invitation is served by the identity host. An org can
        // span several tenants, so anchor the link on the first tenant the
        // invitation grants; fall back to the ambient issuer if it grants none.
        var orgTenants = (await _tenantStore.GetByOrganizationAsync(organization.Id, cancellationToken)).ToList();
        var allowed = ParseAllowedTenantIds(invitation.AllowedTenantIdsJson);
        var linkTenant = orgTenants.FirstOrDefault(t => allowed == null || allowed.Contains(t.Id));

        var baseUrl = linkTenant != null
            ? (await _issuerResolver.GetIssuerForTenantAsync(linkTenant, cancellationToken)).TrimEnd('/')
            : (await _issuerResolver.GetIssuerAsync(cancellationToken)).TrimEnd('/');

        var acceptLink = $"{baseUrl}/account/accept-invitation?token={Uri.EscapeDataString(invitation.Token)}";

        string subject;
        string html;
        RenderedEmail? rendered = null;
        if (_platformTemplates != null)
        {
            rendered = await _platformTemplates.RenderAsync(
                PlatformTemplateNames.OrgInvitation,
                new Dictionary<string, object>
                {
                    ["accept_link"] = acceptLink,
                    ["org_name"] = organization.Name,
                    ["role_name"] = invitation.Role.ToString(),
                    ["inviter_name"] = User.Identity?.Name ?? string.Empty,
                    ["personal_message"] = invitation.Message ?? string.Empty,
                    ["expires_at"] = invitation.ExpiresAt.ToString("d MMMM yyyy"),
                },
                cancellationToken);
        }

        if (rendered != null)
        {
            subject = rendered.Subject;
            html = rendered.HtmlBody;
        }
        else
        {
            var org = System.Net.WebUtility.HtmlEncode(organization.Name);
            var accent = BrandedEmailLayout.DefaultAccent;
            subject = $"You've been invited to join {organization.Name}";
            html = BrandedEmailLayout.Document(
                title: $"You're invited to join {org}",
                preheader: $"You're invited to join {org} as {invitation.Role}.",
                accent: accent,
                brand: BrandedEmailLayout.Monogram(org.Length > 0 ? org[..1].ToUpperInvariant() : "", accent),
                eyebrow: "You're invited",
                heading: $"Join {org}",
                rows:
                    BrandedEmailLayout.Lead($"You've been invited to collaborate with the team at {BrandedEmailLayout.Strong(org)}.") +
                    BrandedEmailLayout.Pill("Your role", invitation.Role.ToString(), accent) +
                    BrandedEmailLayout.Button(acceptLink, "Accept invitation &rarr;", accent) +
                    BrandedEmailLayout.Note($"This invitation expires on <strong>{invitation.ExpiresAt:d MMMM yyyy}</strong>.") +
                    BrandedEmailLayout.Divider() +
                    BrandedEmailLayout.LinkFallback(acceptLink, accent),
                footnote: $"You received this because someone at {org} invited this address.");
        }

        var result = await _emailSender.SendAsync(invitation.Email, subject, html, cancellationToken);

        if (result.Success)
            _logger.LogInformation("Invitation email sent to {Email} for organization {OrgId}", invitation.Email, organization.Id);
        else
            _logger.LogWarning("Failed to send invitation email to {Email}: {Error}", invitation.Email, result.Error);

        return result.Success;
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
            return new HashSet<string>(StringComparer.Ordinal);
        }
    }

    /// <summary>
    /// Undoes a partially-completed provision. Provisioning spans several stores
    /// with no ambient transaction, so a late failure would otherwise leave an org
    /// and tenant holding the slug and identifier the operator needs to retry with.
    /// </summary>
    private async Task RollbackProvisionAsync(string organizationId, string tenantId, CancellationToken cancellationToken)
    {
        try
        {
            await _tenantStore.DeleteAsync(tenantId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to roll back tenant {TenantId} after provisioning failure", tenantId);
        }

        try
        {
            await _organizationStore.DeleteAsync(organizationId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to roll back organization {OrgId} after provisioning failure", organizationId);
        }

        _logger.LogWarning("Rolled back provisioning for organization {OrgId} / tenant {TenantId}",
            organizationId, tenantId);
    }

    private async Task<bool> SeedDefaultRolesAsync(string tenantId, CancellationToken cancellationToken)
    {
        try
        {
            await _tenantDefaultRoleSeeder.SeedAsync(tenantId, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            // A tenant with no Admin role has nobody who can sign in, so the
            // caller rolls back instead of continuing.
            _logger.LogError(ex, "Failed to seed default roles for tenant {TenantId}: {Reason}", tenantId, ex.GetBaseException().Message);
            return false;
        }
    }

    #endregion
}

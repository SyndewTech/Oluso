using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Entities;
using Oluso.Enterprise.Scim.Entities;
using Oluso.Enterprise.Scim.Models;
using Oluso.Enterprise.Scim.Services;
using Oluso.Enterprise.Scim.Stores;

namespace Oluso.Enterprise.Scim.Endpoints;

/// <summary>
/// SCIM 2.0 Groups endpoint (RFC 7644)
/// </summary>
[ApiController]
[Route("scim/v2/[controller]")]
[Produces(ScimConstants.ContentTypes.Scim, ScimConstants.ContentTypes.Json)]
public class GroupsController : ControllerBase
{
    private readonly RoleManager<OlusoRole> _roleManager;
    private readonly UserManager<OlusoUser> _userManager;
    private readonly IScimContextAccessor _scimContext;
    private readonly IScimResourceMappingStore _mappingStore;
    private readonly ILogger<GroupsController> _logger;

    public GroupsController(
        RoleManager<OlusoRole> roleManager,
        UserManager<OlusoUser> userManager,
        IScimContextAccessor scimContext,
        IScimResourceMappingStore mappingStore,
        ILogger<GroupsController> logger)
    {
        _roleManager = roleManager;
        _userManager = userManager;
        _scimContext = scimContext;
        _mappingStore = mappingStore;
        _logger = logger;
    }

    private string BaseUrl => $"{Request.Scheme}://{Request.Host}{Request.PathBase}/scim/v2";

    /// <summary>
    /// Get a group by ID (internal ID or mapped external ID)
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetGroup(string id)
    {
        var client = _scimContext.Client;
        if (client == null)
            return Unauthorized();

        var (role, mapping) = await FindGroupByIdOrExternalIdAsync(client, id);

        if (role == null || role.TenantId != client.TenantId)
        {
            return NotFound(ScimError.NotFound($"Group {id} not found"));
        }

        var scimGroup = await ToScimGroup(role, mapping?.ExternalId);
        return Ok(scimGroup);
    }

    /// <summary>
    /// Search/list groups
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> SearchGroups(
        [FromQuery] string? filter = null,
        [FromQuery] int startIndex = 1,
        [FromQuery] int count = 100)
    {
        var client = _scimContext.Client;
        if (client == null)
            return Unauthorized();

        // Check if filtering by externalId
        if (!string.IsNullOrEmpty(filter) && filter.StartsWith("externalId eq", StringComparison.OrdinalIgnoreCase))
        {
            var externalId = ExtractFilterValue(filter);
            if (!string.IsNullOrEmpty(externalId))
            {
                var mapping = await _mappingStore.GetByExternalIdAsync(client.Id, "Group", externalId);
                if (mapping != null)
                {
                    var role = await _roleManager.FindByIdAsync(mapping.InternalId);
                    if (role != null && role.TenantId == client.TenantId)
                    {
                        var scimGroup = await ToScimGroup(role, mapping.ExternalId);
                        return Ok(new ScimListResponse<ScimGroup>
                        {
                            TotalResults = 1,
                            StartIndex = 1,
                            ItemsPerPage = 1,
                            Resources = new List<ScimGroup> { scimGroup }
                        });
                    }
                }

                return Ok(new ScimListResponse<ScimGroup>
                {
                    TotalResults = 0,
                    StartIndex = startIndex,
                    ItemsPerPage = 0,
                    Resources = new List<ScimGroup>()
                });
            }
        }

        IQueryable<OlusoRole> query = _roleManager.Roles
            .Where(r => r.TenantId == client.TenantId);

        if (!string.IsNullOrEmpty(filter))
        {
            query = ApplyFilter(query, filter);
        }

        var totalResults = query.Count();

        var roles = query
            .Skip(startIndex - 1)
            .Take(Math.Min(count, 200))
            .ToList();

        var scimGroups = new List<ScimGroup>();
        foreach (var role in roles)
        {
            var mapping = await _mappingStore.GetByInternalIdAsync(client.Id, "Group", role.Id);
            scimGroups.Add(await ToScimGroup(role, mapping?.ExternalId));
        }

        var response = new ScimListResponse<ScimGroup>
        {
            TotalResults = totalResults,
            StartIndex = startIndex,
            ItemsPerPage = scimGroups.Count,
            Resources = scimGroups
        };

        return Ok(response);
    }

    /// <summary>
    /// Create a new group
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateGroup([FromBody] ScimGroup scimGroup)
    {
        var client = _scimContext.Client;
        if (client == null)
            return Unauthorized();

        if (!client.CanManageGroups)
            return StatusCode(403, ScimError.BadRequest("Client is not authorized to manage groups"));

        if (string.IsNullOrEmpty(scimGroup.DisplayName))
        {
            return BadRequest(ScimError.InvalidValue("displayName is required"));
        }

        // Check if we already have a mapping for this externalId
        if (!string.IsNullOrEmpty(scimGroup.ExternalId))
        {
            var existingMapping = await _mappingStore.GetByExternalIdAsync(client.Id, "Group", scimGroup.ExternalId);
            if (existingMapping != null)
            {
                return Conflict(ScimError.Conflict($"Group with externalId '{scimGroup.ExternalId}' already exists"));
            }
        }

        var role = new OlusoRole
        {
            Id = Guid.NewGuid().ToString(),
            Name = scimGroup.DisplayName,
            NormalizedName = scimGroup.DisplayName.ToUpperInvariant(),
            DisplayName = scimGroup.DisplayName,
            TenantId = client.TenantId,
            CreatedAt = DateTime.UtcNow
        };

        var result = await _roleManager.CreateAsync(role);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            return BadRequest(ScimError.InvalidValue(errors));
        }

        // Create the external ID mapping
        var externalId = scimGroup.ExternalId ?? role.Id;
        var resourceMapping = new ScimResourceMapping
        {
            TenantId = client.TenantId,
            ScimClientId = client.Id,
            ResourceType = "Group",
            ExternalId = externalId,
            InternalId = role.Id,
            CreatedAt = DateTime.UtcNow
        };
        await _mappingStore.CreateAsync(resourceMapping);

        // Add members if specified
        if (scimGroup.Members?.Any() == true)
        {
            foreach (var member in scimGroup.Members)
            {
                await AddMemberToRole(role, member.Value, client);
            }
        }

        _logger.LogInformation(
            "SCIM group created: {GroupId} with externalId {ExternalId} by client {ClientId}",
            role.Id, externalId, client.Id);

        var responseGroup = await ToScimGroup(role, externalId);
        return CreatedAtAction(nameof(GetGroup), new { id = role.Id }, responseGroup);
    }

    /// <summary>
    /// Replace a group (full update)
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> ReplaceGroup(string id, [FromBody] ScimGroup scimGroup)
    {
        var client = _scimContext.Client;
        if (client == null)
            return Unauthorized();

        if (!client.CanManageGroups)
            return StatusCode(403, ScimError.BadRequest("Client is not authorized to manage groups"));

        var (role, mapping) = await FindGroupByIdOrExternalIdAsync(client, id);

        if (role == null || role.TenantId != client.TenantId)
        {
            return NotFound(ScimError.NotFound($"Group {id} not found"));
        }

        role.DisplayName = scimGroup.DisplayName;
        role.UpdatedAt = DateTime.UtcNow;

        var result = await _roleManager.UpdateAsync(role);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            return BadRequest(ScimError.InvalidValue(errors));
        }

        // Update members - remove all and add specified
        var currentUsers = await _userManager.GetUsersInRoleAsync(role.Name!);
        foreach (var user in currentUsers.Where(u => u.TenantId == client.TenantId))
        {
            await _userManager.RemoveFromRoleAsync(user, role.Name!);
        }

        if (scimGroup.Members?.Any() == true)
        {
            foreach (var member in scimGroup.Members)
            {
                await AddMemberToRole(role, member.Value, client);
            }
        }

        _logger.LogInformation("SCIM group updated: {GroupId} by client {ClientId}", role.Id, client.Id);

        var responseGroup = await ToScimGroup(role, mapping?.ExternalId ?? scimGroup.ExternalId);
        return Ok(responseGroup);
    }

    /// <summary>
    /// Partial update a group (PATCH)
    /// </summary>
    [HttpPatch("{id}")]
    public async Task<IActionResult> PatchGroup(string id, [FromBody] ScimPatchRequest patchRequest)
    {
        var client = _scimContext.Client;
        if (client == null)
            return Unauthorized();

        if (!client.CanManageGroups)
            return StatusCode(403, ScimError.BadRequest("Client is not authorized to manage groups"));

        var (role, mapping) = await FindGroupByIdOrExternalIdAsync(client, id);

        if (role == null || role.TenantId != client.TenantId)
        {
            return NotFound(ScimError.NotFound($"Group {id} not found"));
        }

        foreach (var op in patchRequest.Operations)
        {
            await ApplyPatchOperation(role, op, client);
        }

        role.UpdatedAt = DateTime.UtcNow;
        await _roleManager.UpdateAsync(role);

        _logger.LogInformation("SCIM group patched: {GroupId} by client {ClientId}", role.Id, client.Id);

        var responseGroup = await ToScimGroup(role, mapping?.ExternalId);
        return Ok(responseGroup);
    }

    /// <summary>
    /// Delete a group
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteGroup(string id)
    {
        var client = _scimContext.Client;
        if (client == null)
            return Unauthorized();

        if (!client.CanManageGroups)
            return StatusCode(403, ScimError.BadRequest("Client is not authorized to manage groups"));

        var (role, mapping) = await FindGroupByIdOrExternalIdAsync(client, id);

        if (role == null || role.TenantId != client.TenantId)
        {
            return NotFound(ScimError.NotFound($"Group {id} not found"));
        }

        if (role.IsSystemRole)
        {
            return BadRequest(ScimError.Mutability("Cannot delete system role"));
        }

        await _roleManager.DeleteAsync(role);

        // Remove the mapping for this client
        if (mapping != null)
        {
            await _mappingStore.DeleteAsync(mapping.Id);
        }

        _logger.LogInformation("SCIM group deleted: {GroupId} by client {ClientId}", role.Id, client.Id);

        return NoContent();
    }

    private async Task<(OlusoRole? Role, ScimResourceMapping? Mapping)> FindGroupByIdOrExternalIdAsync(
        ScimClient client, string id)
    {
        // First try internal ID
        var role = await _roleManager.FindByIdAsync(id);
        ScimResourceMapping? mapping = null;

        if (role != null && role.TenantId == client.TenantId)
        {
            mapping = await _mappingStore.GetByInternalIdAsync(client.Id, "Group", role.Id);
            return (role, mapping);
        }

        // Try external ID
        mapping = await _mappingStore.GetByExternalIdAsync(client.Id, "Group", id);
        if (mapping != null)
        {
            role = await _roleManager.FindByIdAsync(mapping.InternalId);
            if (role != null && role.TenantId == client.TenantId)
            {
                return (role, mapping);
            }
        }

        return (null, null);
    }

    private async Task AddMemberToRole(OlusoRole role, string? memberId, ScimClient client)
    {
        if (string.IsNullOrEmpty(memberId) || role.Name == null) return;

        // Try to find user by internal ID first
        var user = await _userManager.FindByIdAsync(memberId);

        if (user == null || user.TenantId != client.TenantId)
        {
            // Try to find by external ID
            var userMapping = await _mappingStore.GetByExternalIdAsync(client.Id, "User", memberId);
            if (userMapping != null)
            {
                user = await _userManager.FindByIdAsync(userMapping.InternalId);
            }
        }

        if (user != null && user.TenantId == client.TenantId)
        {
            await _userManager.AddToRoleAsync(user, role.Name);
        }
    }

    private async Task<ScimGroup> ToScimGroup(OlusoRole role, string? externalId = null)
    {
        var group = new ScimGroup
        {
            Id = role.Id,
            ExternalId = externalId ?? role.Id,
            DisplayName = role.DisplayName ?? role.Name ?? "",
            Meta = new ScimMeta
            {
                ResourceType = "Group",
                Created = role.CreatedAt,
                LastModified = role.UpdatedAt ?? role.CreatedAt,
                Location = $"{BaseUrl}/Groups/{role.Id}",
                Version = $"W/\"{role.UpdatedAt?.Ticks ?? role.CreatedAt.Ticks}\""
            }
        };

        // Get members
        if (role.Name != null)
        {
            var users = await _userManager.GetUsersInRoleAsync(role.Name);
            group.Members = users.Select(u => new ScimMember
            {
                Value = u.Id,
                Ref = $"{BaseUrl}/Users/{u.Id}",
                Display = u.DisplayName ?? u.UserName,
                Type = "User"
            }).ToList();
        }

        return group;
    }

    private static string? ExtractFilterValue(string filter)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            filter,
            @"externalId\s+eq\s+[""']([^""']+)[""']",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return match.Success ? match.Groups[1].Value : null;
    }

    private IQueryable<OlusoRole> ApplyFilter(IQueryable<OlusoRole> query, string filter)
    {
        var parts = filter.Split(' ', 3);
        if (parts.Length < 3)
            return query;

        var attribute = parts[0].ToLowerInvariant();
        var op = parts[1].ToLowerInvariant();
        var value = parts[2].Trim('"', '\'');

        if (op != "eq")
            return query;

        return attribute switch
        {
            "displayname" => query.Where(r => r.DisplayName == value || r.Name == value),
            _ => query
        };
    }

    private async Task ApplyPatchOperation(OlusoRole role, ScimPatchOperation op, ScimClient client)
    {
        var path = op.Path?.ToLowerInvariant() ?? "";

        switch (op.Op.ToLowerInvariant())
        {
            case "replace":
                if (path == "displayname" && op.Value != null)
                {
                    role.DisplayName = op.Value.ToString();
                }
                break;

            case "add":
                if (path.StartsWith("members") && op.Value != null)
                {
                    await AddMembers(role, op.Value, client);
                }
                break;

            case "remove":
                if (path.StartsWith("members"))
                {
                    var userId = ExtractMemberIdFromPath(path);
                    if (!string.IsNullOrEmpty(userId))
                    {
                        await RemoveMemberFromRole(role, userId, client);
                    }
                }
                break;
        }
    }

    private async Task AddMembers(OlusoRole role, object value, ScimClient client)
    {
        if (role.Name == null) return;

        if (value is System.Text.Json.JsonElement jsonElement)
        {
            if (jsonElement.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var item in jsonElement.EnumerateArray())
                {
                    var memberId = item.GetProperty("value").GetString();
                    await AddMemberToRole(role, memberId, client);
                }
            }
            else if (jsonElement.ValueKind == System.Text.Json.JsonValueKind.Object)
            {
                var memberId = jsonElement.GetProperty("value").GetString();
                await AddMemberToRole(role, memberId, client);
            }
        }
    }

    private async Task RemoveMemberFromRole(OlusoRole role, string memberId, ScimClient client)
    {
        if (role.Name == null) return;

        // Try internal ID first
        var user = await _userManager.FindByIdAsync(memberId);

        if (user == null || user.TenantId != client.TenantId)
        {
            // Try external ID
            var userMapping = await _mappingStore.GetByExternalIdAsync(client.Id, "User", memberId);
            if (userMapping != null)
            {
                user = await _userManager.FindByIdAsync(userMapping.InternalId);
            }
        }

        if (user != null && user.TenantId == client.TenantId)
        {
            await _userManager.RemoveFromRoleAsync(user, role.Name);
        }
    }

    private static string? ExtractMemberIdFromPath(string path)
    {
        var match = System.Text.RegularExpressions.Regex.Match(path, @"members\[value eq ""([^""]+)""\]");
        return match.Success ? match.Groups[1].Value : null;
    }
}

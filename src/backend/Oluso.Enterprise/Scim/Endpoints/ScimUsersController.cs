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
/// SCIM 2.0 Users endpoint (RFC 7644)
/// </summary>
[ApiController]
[Route("scim/v2/[controller]")]
[Produces(ScimConstants.ContentTypes.Scim, ScimConstants.ContentTypes.Json)]
public class UsersController : ControllerBase
{
    private readonly UserManager<OlusoUser> _userManager;
    private readonly IScimUserMapper _userMapper;
    private readonly IScimContextAccessor _scimContext;
    private readonly IScimResourceMappingStore _mappingStore;
    private readonly ILogger<UsersController> _logger;

    public UsersController(
        UserManager<OlusoUser> userManager,
        IScimUserMapper userMapper,
        IScimContextAccessor scimContext,
        IScimResourceMappingStore mappingStore,
        ILogger<UsersController> logger)
    {
        _userManager = userManager;
        _userMapper = userMapper;
        _scimContext = scimContext;
        _mappingStore = mappingStore;
        _logger = logger;
    }

    private string BaseUrl => $"{Request.Scheme}://{Request.Host}{Request.PathBase}/scim/v2";

    /// <summary>
    /// Get a user by ID (internal ID or mapped external ID)
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetUser(string id)
    {
        var client = _scimContext.Client;
        if (client == null)
            return Unauthorized();

        // First try to find by internal ID
        var user = await _userManager.FindByIdAsync(id);
        string? externalId = null;

        if (user == null || user.TenantId != client.TenantId)
        {
            // Try to find by external ID mapping
            var mapping = await _mappingStore.GetByExternalIdAsync(client.Id, "User", id);
            if (mapping != null)
            {
                user = await _userManager.FindByIdAsync(mapping.InternalId);
                externalId = mapping.ExternalId;
            }
        }
        else
        {
            // Found by internal ID, get the external ID for this client
            var mapping = await _mappingStore.GetByInternalIdAsync(client.Id, "User", user.Id);
            externalId = mapping?.ExternalId;
        }

        if (user == null || user.TenantId != client.TenantId)
        {
            return NotFound(ScimError.NotFound($"User {id} not found"));
        }

        var scimUser = _userMapper.ToScimUser(user, BaseUrl, externalId);
        return Ok(scimUser);
    }

    /// <summary>
    /// Search/list users
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> SearchUsers(
        [FromQuery] string? filter = null,
        [FromQuery] int startIndex = 1,
        [FromQuery] int count = 100,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortOrder = null)
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
                var mapping = await _mappingStore.GetByExternalIdAsync(client.Id, "User", externalId);
                if (mapping != null)
                {
                    var user = await _userManager.FindByIdAsync(mapping.InternalId);
                    if (user != null && user.TenantId == client.TenantId)
                    {
                        var scimUser = _userMapper.ToScimUser(user, BaseUrl, mapping.ExternalId);
                        return Ok(new ScimListResponse<ScimUser>
                        {
                            TotalResults = 1,
                            StartIndex = 1,
                            ItemsPerPage = 1,
                            Resources = new List<ScimUser> { scimUser }
                        });
                    }
                }

                // Not found
                return Ok(new ScimListResponse<ScimUser>
                {
                    TotalResults = 0,
                    StartIndex = startIndex,
                    ItemsPerPage = 0,
                    Resources = new List<ScimUser>()
                });
            }
        }

        // Regular query
        IQueryable<OlusoUser> query = _userManager.Users
            .Where(u => u.TenantId == client.TenantId);

        if (!string.IsNullOrEmpty(filter))
        {
            query = ApplyFilter(query, filter);
        }

        var totalResults = query.Count();

        // Apply pagination
        var users = query
            .Skip(startIndex - 1)
            .Take(Math.Min(count, 200))
            .ToList();

        // Get external IDs for all users
        var scimUsers = new List<ScimUser>();
        foreach (var user in users)
        {
            var mapping = await _mappingStore.GetByInternalIdAsync(client.Id, "User", user.Id);
            scimUsers.Add(_userMapper.ToScimUser(user, BaseUrl, mapping?.ExternalId));
        }

        var response = new ScimListResponse<ScimUser>
        {
            TotalResults = totalResults,
            StartIndex = startIndex,
            ItemsPerPage = scimUsers.Count,
            Resources = scimUsers
        };

        return Ok(response);
    }

    /// <summary>
    /// Create a new user
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateUser([FromBody] ScimUser scimUser)
    {
        var client = _scimContext.Client;
        if (client == null)
            return Unauthorized();

        if (!client.CanCreateUsers)
            return StatusCode(403, ScimError.BadRequest("Client is not authorized to create users"));

        if (string.IsNullOrEmpty(scimUser.UserName))
        {
            return BadRequest(ScimError.InvalidValue("userName is required"));
        }

        // Check if we already have a mapping for this externalId
        if (!string.IsNullOrEmpty(scimUser.ExternalId))
        {
            var existingMapping = await _mappingStore.GetByExternalIdAsync(client.Id, "User", scimUser.ExternalId);
            if (existingMapping != null)
            {
                // User already exists for this client, return conflict
                return Conflict(ScimError.Conflict($"User with externalId '{scimUser.ExternalId}' already exists"));
            }
        }

        // Check for existing user by username
        var existingUser = await _userManager.FindByNameAsync(scimUser.UserName);
        if (existingUser != null && existingUser.TenantId == client.TenantId)
        {
            // User exists - if externalId provided, create mapping and return existing user
            if (!string.IsNullOrEmpty(scimUser.ExternalId))
            {
                var mapping = new ScimResourceMapping
                {
                    TenantId = client.TenantId,
                    ScimClientId = client.Id,
                    ResourceType = "User",
                    ExternalId = scimUser.ExternalId,
                    InternalId = existingUser.Id,
                    CreatedAt = DateTime.UtcNow
                };
                await _mappingStore.CreateAsync(mapping);

                _logger.LogInformation(
                    "SCIM mapped existing user {UserId} to externalId {ExternalId} for client {ClientId}",
                    existingUser.Id, scimUser.ExternalId, client.Id);

                var responseUser = _userMapper.ToScimUser(existingUser, BaseUrl, scimUser.ExternalId);
                return Ok(responseUser); // Return 200 for existing user with new mapping
            }

            return Conflict(ScimError.Conflict($"User with userName '{scimUser.UserName}' already exists"));
        }

        var user = _userMapper.CreateFromScimUser(scimUser, client.TenantId);

        // Handle password if provided
        IdentityResult result;
        if (!string.IsNullOrEmpty(scimUser.Password))
        {
            result = await _userManager.CreateAsync(user, scimUser.Password);
        }
        else
        {
            result = await _userManager.CreateAsync(user);
        }

        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            _logger.LogWarning("SCIM user creation failed: {Errors}", errors);
            return BadRequest(ScimError.InvalidValue(errors));
        }

        // Create the external ID mapping
        var externalId = scimUser.ExternalId ?? user.Id; // Use internal ID if no external ID provided
        var resourceMapping = new ScimResourceMapping
        {
            TenantId = client.TenantId,
            ScimClientId = client.Id,
            ResourceType = "User",
            ExternalId = externalId,
            InternalId = user.Id,
            CreatedAt = DateTime.UtcNow
        };
        await _mappingStore.CreateAsync(resourceMapping);

        _logger.LogInformation(
            "SCIM user created: {UserId} with externalId {ExternalId} by client {ClientId}",
            user.Id, externalId, client.Id);

        var createdUser = _userMapper.ToScimUser(user, BaseUrl, externalId);
        return CreatedAtAction(nameof(GetUser), new { id = user.Id }, createdUser);
    }

    /// <summary>
    /// Replace a user (full update)
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> ReplaceUser(string id, [FromBody] ScimUser scimUser)
    {
        var client = _scimContext.Client;
        if (client == null)
            return Unauthorized();

        if (!client.CanUpdateUsers)
            return StatusCode(403, ScimError.BadRequest("Client is not authorized to update users"));

        // Find user by ID or external ID
        var (user, mapping) = await FindUserByIdOrExternalIdAsync(client, id);

        if (user == null || user.TenantId != client.TenantId)
        {
            return NotFound(ScimError.NotFound($"User {id} not found"));
        }

        _userMapper.ApplyToUser(user, scimUser);

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            return BadRequest(ScimError.InvalidValue(errors));
        }

        // Handle password change if provided
        if (!string.IsNullOrEmpty(scimUser.Password))
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            await _userManager.ResetPasswordAsync(user, token, scimUser.Password);
        }

        // Update external ID if changed
        if (!string.IsNullOrEmpty(scimUser.ExternalId) && mapping != null && mapping.ExternalId != scimUser.ExternalId)
        {
            mapping.ExternalId = scimUser.ExternalId;
            mapping.UpdatedAt = DateTime.UtcNow;
            // Note: Would need an UpdateAsync on the store for this
        }

        _logger.LogInformation("SCIM user updated: {UserId} by client {ClientId}", user.Id, client.Id);

        var externalId = mapping?.ExternalId ?? scimUser.ExternalId;
        var responseUser = _userMapper.ToScimUser(user, BaseUrl, externalId);
        return Ok(responseUser);
    }

    /// <summary>
    /// Partial update a user (PATCH)
    /// </summary>
    [HttpPatch("{id}")]
    public async Task<IActionResult> PatchUser(string id, [FromBody] ScimPatchRequest patchRequest)
    {
        var client = _scimContext.Client;
        if (client == null)
            return Unauthorized();

        if (!client.CanUpdateUsers)
            return StatusCode(403, ScimError.BadRequest("Client is not authorized to update users"));

        var (user, mapping) = await FindUserByIdOrExternalIdAsync(client, id);

        if (user == null || user.TenantId != client.TenantId)
        {
            return NotFound(ScimError.NotFound($"User {id} not found"));
        }

        foreach (var op in patchRequest.Operations)
        {
            ApplyPatchOperation(user, op);
        }

        user.UpdatedAt = DateTime.UtcNow;

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            return BadRequest(ScimError.InvalidValue(errors));
        }

        _logger.LogInformation("SCIM user patched: {UserId} by client {ClientId}", user.Id, client.Id);

        var responseUser = _userMapper.ToScimUser(user, BaseUrl, mapping?.ExternalId);
        return Ok(responseUser);
    }

    /// <summary>
    /// Delete a user
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteUser(string id)
    {
        var client = _scimContext.Client;
        if (client == null)
            return Unauthorized();

        if (!client.CanDeleteUsers)
            return StatusCode(403, ScimError.BadRequest("Client is not authorized to delete users"));

        var (user, mapping) = await FindUserByIdOrExternalIdAsync(client, id);

        if (user == null || user.TenantId != client.TenantId)
        {
            return NotFound(ScimError.NotFound($"User {id} not found"));
        }

        // Soft delete - just deactivate
        user.IsActive = false;
        user.UpdatedAt = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);

        // Remove the mapping for this client only
        if (mapping != null)
        {
            await _mappingStore.DeleteAsync(mapping.Id);
        }

        _logger.LogInformation(
            "SCIM user deleted (deactivated): {UserId} by client {ClientId}",
            user.Id, client.Id);

        return NoContent();
    }

    private async Task<(OlusoUser? User, ScimResourceMapping? Mapping)> FindUserByIdOrExternalIdAsync(
        ScimClient client, string id)
    {
        // First try internal ID
        var user = await _userManager.FindByIdAsync(id);
        ScimResourceMapping? mapping = null;

        if (user != null && user.TenantId == client.TenantId)
        {
            // Found by internal ID, get mapping
            mapping = await _mappingStore.GetByInternalIdAsync(client.Id, "User", user.Id);
            return (user, mapping);
        }

        // Try external ID
        mapping = await _mappingStore.GetByExternalIdAsync(client.Id, "User", id);
        if (mapping != null)
        {
            user = await _userManager.FindByIdAsync(mapping.InternalId);
            if (user != null && user.TenantId == client.TenantId)
            {
                return (user, mapping);
            }
        }

        return (null, null);
    }

    private static string? ExtractFilterValue(string filter)
    {
        // Extract value from: externalId eq "value" or externalId eq 'value'
        var match = System.Text.RegularExpressions.Regex.Match(
            filter,
            @"externalId\s+eq\s+[""']([^""']+)[""']",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        return match.Success ? match.Groups[1].Value : null;
    }

    private IQueryable<OlusoUser> ApplyFilter(IQueryable<OlusoUser> query, string filter)
    {
        // Simple filter parsing - supports: attribute eq "value"
        // Example: userName eq "john@example.com"
        var parts = filter.Split(' ', 3);
        if (parts.Length < 3)
            return query;

        var attribute = parts[0].ToLowerInvariant();
        var op = parts[1].ToLowerInvariant();
        var value = parts[2].Trim('"', '\'');

        if (op != "eq")
        {
            _logger.LogWarning("Unsupported SCIM filter operator: {Op}", op);
            return query;
        }

        return attribute switch
        {
            "username" => query.Where(u => u.UserName == value || u.NormalizedUserName == value.ToUpperInvariant()),
            "emails.value" or "email" => query.Where(u => u.Email == value || u.NormalizedEmail == value.ToUpperInvariant()),
            "active" => query.Where(u => u.IsActive == bool.Parse(value)),
            "displayname" => query.Where(u => u.DisplayName == value),
            _ => query
        };
    }

    private void ApplyPatchOperation(OlusoUser user, ScimPatchOperation op)
    {
        var path = op.Path?.ToLowerInvariant() ?? "";
        var value = op.Value?.ToString();

        switch (op.Op.ToLowerInvariant())
        {
            case "replace":
            case "add":
                switch (path)
                {
                    case "active":
                        user.IsActive = bool.Parse(value ?? "true");
                        break;
                    case "username":
                        user.UserName = value;
                        user.NormalizedUserName = value?.ToUpperInvariant();
                        break;
                    case "displayname":
                        user.DisplayName = value;
                        break;
                    case "name.givenname":
                        user.FirstName = value;
                        break;
                    case "name.familyname":
                        user.LastName = value;
                        break;
                    case "emails[type eq \"work\"].value":
                    case "emails":
                        user.Email = value;
                        user.NormalizedEmail = value?.ToUpperInvariant();
                        break;
                    case "phonenumbers[type eq \"work\"].value":
                    case "phonenumbers":
                        user.PhoneNumber = value;
                        break;
                    case "locale":
                        user.Locale = value;
                        break;
                    case "timezone":
                        user.TimeZone = value;
                        break;
                }
                break;

            case "remove":
                switch (path)
                {
                    case "phonenumbers":
                        user.PhoneNumber = null;
                        break;
                    case "displayname":
                        user.DisplayName = null;
                        break;
                }
                break;
        }
    }
}

using System.Linq.Expressions;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Services;
using Oluso.Enterprise.Scim.Entities;
using Oluso.Enterprise.Scim.Filtering;
using Oluso.Enterprise.Scim.Models;
using Oluso.Enterprise.Scim.Stores;

namespace Oluso.Enterprise.Scim.Services;

/// <summary>
/// Outcome of a SCIM provisioning operation, decoupled from the HTTP layer so
/// the same logic can be reused by the REST controllers and the Bulk endpoint.
/// </summary>
public sealed record ScimOperationResult(int Status, object? Body, string? Location = null)
{
    public bool IsSuccess => Status is >= 200 and < 300;

    public static ScimOperationResult Ok(object body) => new(200, body);
    public static ScimOperationResult Created(object body, string location) => new(201, body, location);
    public static ScimOperationResult NoContent() => new(204, null);
    public static ScimOperationResult FromError(ScimError error) => new(error.Status, error);
}

/// <summary>
/// Single source of truth for SCIM User and Group provisioning. Encapsulates
/// filtering, sorting, PATCH semantics and external-id mapping so the REST
/// controllers and the Bulk endpoint behave identically.
/// </summary>
public interface IScimProvisioningService
{
    Task<ScimOperationResult> GetUserAsync(ScimClient client, string id, string baseUrl);
    Task<ScimOperationResult> SearchUsersAsync(ScimClient client, string? filter, int startIndex, int count, string? sortBy, string? sortOrder, string baseUrl);
    Task<ScimOperationResult> CreateUserAsync(ScimClient client, ScimUser scimUser, string baseUrl);
    Task<ScimOperationResult> ReplaceUserAsync(ScimClient client, string id, ScimUser scimUser, string baseUrl);
    Task<ScimOperationResult> PatchUserAsync(ScimClient client, string id, ScimPatchRequest patch, string baseUrl);
    Task<ScimOperationResult> DeleteUserAsync(ScimClient client, string id);

    Task<ScimOperationResult> GetGroupAsync(ScimClient client, string id, string baseUrl);
    Task<ScimOperationResult> SearchGroupsAsync(ScimClient client, string? filter, int startIndex, int count, string? sortBy, string? sortOrder, string baseUrl);
    Task<ScimOperationResult> CreateGroupAsync(ScimClient client, ScimGroup scimGroup, string baseUrl);
    Task<ScimOperationResult> ReplaceGroupAsync(ScimClient client, string id, ScimGroup scimGroup, string baseUrl);
    Task<ScimOperationResult> PatchGroupAsync(ScimClient client, string id, ScimPatchRequest patch, string baseUrl);
    Task<ScimOperationResult> DeleteGroupAsync(ScimClient client, string id);
}

public sealed class ScimProvisioningService : IScimProvisioningService
{
    private const int MaxPageSize = 200;

    private readonly UserManager<OlusoUser> _userManager;
    private readonly RoleManager<OlusoRole> _roleManager;
    private readonly IScimUserMapper _userMapper;
    private readonly IScimResourceMappingStore _mappingStore;
    private readonly IScimAttributeMappingStore _attributeMappingStore;
    private readonly IRoleStore _roleStore;
    private readonly IRoleGrantService _roleGrants;
    private readonly IModuleAdminAssignmentNotifier _moduleAdminNotifier;
    private readonly IOlusoEventService _events;
    private readonly ITenantEmailDomainStore _emailDomains;
    private readonly ILogger<ScimProvisioningService> _logger;

    public ScimProvisioningService(
        UserManager<OlusoUser> userManager,
        RoleManager<OlusoRole> roleManager,
        IScimUserMapper userMapper,
        IScimResourceMappingStore mappingStore,
        IScimAttributeMappingStore attributeMappingStore,
        IRoleStore roleStore,
        IRoleGrantService roleGrants,
        IModuleAdminAssignmentNotifier moduleAdminNotifier,
        IOlusoEventService events,
        ITenantEmailDomainStore emailDomains,
        ILogger<ScimProvisioningService> logger)
    {
        _emailDomains = emailDomains;
        _userManager = userManager;
        _roleManager = roleManager;
        _userMapper = userMapper;
        _mappingStore = mappingStore;
        _attributeMappingStore = attributeMappingStore;
        _roleStore = roleStore;
        _roleGrants = roleGrants;
        _moduleAdminNotifier = moduleAdminNotifier;
        _events = events;
        _logger = logger;
    }

    /// <summary>
    /// Changes an inbound SCIM client makes are its own: events raised while
    /// handling them, here and in the services called, carry it as their origin,
    /// so outbound provisioning doesn't echo them back to the same system.
    /// </summary>
    private static IDisposable BeginOrigin(ScimClient client) =>
        EventOriginScope.Begin(EventOrigin.Scim(client.Id, client.Name));

    /// <summary>
    /// A provisioned address is the tenant's word, not proof of the inbox: it counts as verified only at a domain
    /// the tenant has verified. Elsewhere it stays as it is (unverified when new) until the person proves it.
    /// </summary>
    private async Task TrustEmailAtVerifiedDomainAsync(OlusoUser user, ScimClient client)
    {
        if (!user.EmailConfirmed && !string.IsNullOrEmpty(client.TenantId)
            && await _emailDomains.IsVerifiedForTenantAsync(client.TenantId, user.Email))
        {
            user.EmailConfirmed = true;
        }
    }

    // ---------------------------------------------------------------- Users

    public async Task<ScimOperationResult> GetUserAsync(ScimClient client, string id, string baseUrl)
    {
        var (user, mapping) = await FindUserAsync(client, id);
        if (user == null)
            return ScimOperationResult.FromError(ScimError.NotFound($"User {id} not found"));

        return ScimOperationResult.Ok(_userMapper.ToScimUser(user, baseUrl, mapping?.ExternalId));
    }

    public async Task<ScimOperationResult> SearchUsersAsync(
        ScimClient client, string? filter, int startIndex, int count, string? sortBy, string? sortOrder, string baseUrl)
    {
        if (!string.IsNullOrEmpty(filter) && filter.StartsWith("externalId eq", StringComparison.OrdinalIgnoreCase))
            return await SearchUsersByExternalIdAsync(client, filter, startIndex, baseUrl);

        IQueryable<OlusoUser> query = _userManager.Users.Where(u => u.TenantId == client.TenantId);

        if (!string.IsNullOrEmpty(filter))
        {
            try
            {
                query = query.Where(BuildUserFilter(filter));
            }
            catch (ScimFilterException ex)
            {
                return ScimOperationResult.FromError(ScimError.InvalidFilter(ex.Message));
            }
        }

        var totalResults = query.Count();

        query = ApplyUserSort(query, sortBy, sortOrder);
        var (skip, take) = Paginate(startIndex, count);
        var users = query.Skip(skip).Take(take).ToList();

        var scimUsers = new List<ScimUser>();
        foreach (var user in users)
        {
            var mapping = await _mappingStore.GetByInternalIdAsync(client.Id, "User", user.Id);
            scimUsers.Add(_userMapper.ToScimUser(user, baseUrl, mapping?.ExternalId));
        }

        return ScimOperationResult.Ok(new ScimListResponse<ScimUser>
        {
            TotalResults = totalResults,
            StartIndex = startIndex < 1 ? 1 : startIndex,
            ItemsPerPage = scimUsers.Count,
            Resources = scimUsers
        });
    }

    private async Task<ScimOperationResult> SearchUsersByExternalIdAsync(ScimClient client, string filter, int startIndex, string baseUrl)
    {
        var externalId = ExtractEqValue(filter, "externalId");
        if (!string.IsNullOrEmpty(externalId))
        {
            var mapping = await _mappingStore.GetByExternalIdAsync(client.Id, "User", externalId);
            if (mapping != null)
            {
                var user = await _userManager.FindByIdAsync(mapping.InternalId);
                if (user != null && user.TenantId == client.TenantId)
                {
                    return ScimOperationResult.Ok(new ScimListResponse<ScimUser>
                    {
                        TotalResults = 1,
                        StartIndex = 1,
                        ItemsPerPage = 1,
                        Resources = new List<ScimUser> { _userMapper.ToScimUser(user, baseUrl, mapping.ExternalId) }
                    });
                }
            }
        }

        return ScimOperationResult.Ok(new ScimListResponse<ScimUser>
        {
            TotalResults = 0,
            StartIndex = startIndex < 1 ? 1 : startIndex,
            ItemsPerPage = 0,
            Resources = new List<ScimUser>()
        });
    }

    public async Task<ScimOperationResult> CreateUserAsync(ScimClient client, ScimUser scimUser, string baseUrl)
    {
        using var origin = BeginOrigin(client);

        if (!client.CanCreateUsers)
            return ScimOperationResult.FromError(ScimError.BadRequest("Client is not authorized to create users"));

        var attributeMappings = await _attributeMappingStore.GetByClientAsync(client.Id);
        OlusoUser user;
        try
        {
            user = _userMapper.CreateFromScimUser(scimUser, client.TenantId!, client, attributeMappings);
        }
        catch (ScimInboundMappingException ex)
        {
            return ScimOperationResult.FromError(ScimError.InvalidValue(ex.Message));
        }
        await TrustEmailAtVerifiedDomainAsync(user, client);

        if (string.IsNullOrEmpty(user.UserName))
            return ScimOperationResult.FromError(ScimError.InvalidValue("userName is required"));

        if (!string.IsNullOrEmpty(scimUser.ExternalId))
        {
            var existingMapping = await _mappingStore.GetByExternalIdAsync(client.Id, "User", scimUser.ExternalId);
            if (existingMapping != null)
                return ScimOperationResult.FromError(ScimError.Conflict($"User with externalId '{scimUser.ExternalId}' already exists"));
        }

        var existingUser = await _userManager.FindByNameAsync(user.UserName);
        if (existingUser != null && existingUser.TenantId == client.TenantId)
        {
            if (!string.IsNullOrEmpty(scimUser.ExternalId))
            {
                await _mappingStore.CreateAsync(new ScimResourceMapping
                {
                    TenantId = client.TenantId,
                    ScimClientId = client.Id,
                    ResourceType = "User",
                    ExternalId = scimUser.ExternalId,
                    InternalId = existingUser.Id,
                    CreatedAt = DateTime.UtcNow
                });

                _logger.LogInformation(
                    "SCIM mapped existing user {UserId} to externalId {ExternalId} for client {ClientId}",
                    existingUser.Id, scimUser.ExternalId, client.Id);

                return ScimOperationResult.Ok(_userMapper.ToScimUser(existingUser, baseUrl, scimUser.ExternalId));
            }

            return ScimOperationResult.FromError(ScimError.Conflict($"User with userName '{user.UserName}' already exists"));
        }

        var result = !string.IsNullOrEmpty(scimUser.Password)
            ? await _userManager.CreateAsync(user, scimUser.Password)
            : await _userManager.CreateAsync(user);

        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            _logger.LogWarning("SCIM user creation failed: {Errors}", errors);
            return ScimOperationResult.FromError(ScimError.InvalidValue(errors));
        }

        var externalId = scimUser.ExternalId ?? user.Id;
        await _mappingStore.CreateAsync(new ScimResourceMapping
        {
            TenantId = client.TenantId,
            ScimClientId = client.Id,
            ResourceType = "User",
            ExternalId = externalId,
            InternalId = user.Id,
            CreatedAt = DateTime.UtcNow
        });

        _logger.LogInformation(
            "SCIM user created: {UserId} with externalId {ExternalId} by client {ClientId}",
            user.Id, externalId, client.Id);

        await _events.RaiseAsync(new UserRegisteredEvent
        {
            TenantId = client.TenantId,
            SubjectId = user.Id,
            Email = user.Email ?? string.Empty,
            Username = user.UserName
        });

        var created = _userMapper.ToScimUser(user, baseUrl, externalId);
        return ScimOperationResult.Created(created, $"{baseUrl}/Users/{user.Id}");
    }

    public async Task<ScimOperationResult> ReplaceUserAsync(ScimClient client, string id, ScimUser scimUser, string baseUrl)
    {
        using var origin = BeginOrigin(client);

        if (!client.CanUpdateUsers)
            return ScimOperationResult.FromError(ScimError.BadRequest("Client is not authorized to update users"));

        var (user, mapping) = await FindUserAsync(client, id);
        if (user == null)
            return ScimOperationResult.FromError(ScimError.NotFound($"User {id} not found"));

        var wasActive = user.IsActive;
        var attributeMappings = await _attributeMappingStore.GetByClientAsync(client.Id);
        try
        {
            _userMapper.ApplyReplace(user, scimUser, client, attributeMappings);
        }
        catch (ScimInboundMappingException ex)
        {
            return ScimOperationResult.FromError(ScimError.InvalidValue(ex.Message));
        }
        await TrustEmailAtVerifiedDomainAsync(user, client);

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
            return ScimOperationResult.FromError(ScimError.InvalidValue(string.Join(", ", result.Errors.Select(e => e.Description))));

        if (!string.IsNullOrEmpty(scimUser.Password))
        {
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            await _userManager.ResetPasswordAsync(user, token, scimUser.Password);
        }

        if (!string.IsNullOrEmpty(scimUser.ExternalId) && mapping != null && mapping.ExternalId != scimUser.ExternalId)
        {
            mapping.ExternalId = scimUser.ExternalId;
            mapping.UpdatedAt = DateTime.UtcNow;
            await _mappingStore.UpdateAsync(mapping);
        }

        _logger.LogInformation("SCIM user updated: {UserId} by client {ClientId}", user.Id, client.Id);

        await RaiseUpdatedAsync(client, user);
        await RaiseActiveChangeAsync(client, user, wasActive);

        var externalId = mapping?.ExternalId ?? scimUser.ExternalId;
        return ScimOperationResult.Ok(_userMapper.ToScimUser(user, baseUrl, externalId));
    }

    public async Task<ScimOperationResult> PatchUserAsync(ScimClient client, string id, ScimPatchRequest patch, string baseUrl)
    {
        using var origin = BeginOrigin(client);

        if (!client.CanUpdateUsers)
            return ScimOperationResult.FromError(ScimError.BadRequest("Client is not authorized to update users"));

        var (user, mapping) = await FindUserAsync(client, id);
        if (user == null)
            return ScimOperationResult.FromError(ScimError.NotFound($"User {id} not found"));

        var wasActive = user.IsActive;
        var attributeMappings = await _attributeMappingStore.GetByClientAsync(client.Id);
        try
        {
            _userMapper.ApplyPatch(user, patch, client, attributeMappings);
        }
        catch (ScimInboundMappingException ex)
        {
            return ScimOperationResult.FromError(ScimError.InvalidValue(ex.Message));
        }
        await TrustEmailAtVerifiedDomainAsync(user, client);

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
            return ScimOperationResult.FromError(ScimError.InvalidValue(string.Join(", ", result.Errors.Select(e => e.Description))));

        _logger.LogInformation("SCIM user patched: {UserId} by client {ClientId}", user.Id, client.Id);

        await RaiseUpdatedAsync(client, user);
        await RaiseActiveChangeAsync(client, user, wasActive);

        return ScimOperationResult.Ok(_userMapper.ToScimUser(user, baseUrl, mapping?.ExternalId));
    }

    public async Task<ScimOperationResult> DeleteUserAsync(ScimClient client, string id)
    {
        using var origin = BeginOrigin(client);

        if (!client.CanDeleteUsers)
            return ScimOperationResult.FromError(ScimError.BadRequest("Client is not authorized to delete users"));

        var (user, mapping) = await FindUserAsync(client, id);
        if (user == null)
            return ScimOperationResult.FromError(ScimError.NotFound($"User {id} not found"));

        var wasActive = user.IsActive;
        user.IsActive = false;
        user.UpdatedAt = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);

        if (mapping != null)
            await _mappingStore.DeleteAsync(mapping.Id);

        _logger.LogInformation("SCIM user deleted (deactivated): {UserId} by client {ClientId}", user.Id, client.Id);

        // SCIM DELETE deactivates the account rather than removing it (RFC 7644
        // §3.6 leaves that to the provider): the user may hold access in other
        // tenants or be re-provisioned, and their audit trail stays attached.
        // Subscribers learn of it as a deactivation, distinct from an edit.
        await RaiseActiveChangeAsync(client, user, wasActive);

        return ScimOperationResult.NoContent();
    }

    private Task RaiseActiveChangeAsync(ScimClient client, OlusoUser user, bool wasActive)
    {
        if (user.IsActive == wasActive)
            return Task.CompletedTask;

        return _events.RaiseAsync(user.IsActive
            ? new UserReactivatedEvent { TenantId = client.TenantId, SubjectId = user.Id, Email = user.Email }
            : new UserDeactivatedEvent { TenantId = client.TenantId, SubjectId = user.Id, Email = user.Email });
    }

    private Task RaiseUpdatedAsync(ScimClient client, OlusoUser user) =>
        _events.RaiseAsync(new UserUpdatedEvent
        {
            TenantId = client.TenantId,
            SubjectId = user.Id,
            Email = user.Email
        });

    // --------------------------------------------------------------- Groups

    public async Task<ScimOperationResult> GetGroupAsync(ScimClient client, string id, string baseUrl)
    {
        var (role, mapping) = await FindGroupAsync(client, id);
        if (role == null)
            return ScimOperationResult.FromError(ScimError.NotFound($"Group {id} not found"));

        return ScimOperationResult.Ok(await ToScimGroup(client, role, baseUrl, mapping?.ExternalId));
    }

    public async Task<ScimOperationResult> SearchGroupsAsync(
        ScimClient client, string? filter, int startIndex, int count, string? sortBy, string? sortOrder, string baseUrl)
    {
        if (!string.IsNullOrEmpty(filter) && filter.StartsWith("externalId eq", StringComparison.OrdinalIgnoreCase))
        {
            var externalId = ExtractEqValue(filter, "externalId");
            if (!string.IsNullOrEmpty(externalId))
            {
                var mapping = await _mappingStore.GetByExternalIdAsync(client.Id, "Group", externalId);
                if (mapping != null)
                {
                    var role = await _roleManager.FindByIdAsync(mapping.InternalId);
                    if (role != null && role.TenantId == client.TenantId)
                    {
                        return ScimOperationResult.Ok(new ScimListResponse<ScimGroup>
                        {
                            TotalResults = 1,
                            StartIndex = 1,
                            ItemsPerPage = 1,
                            Resources = new List<ScimGroup> { await ToScimGroup(client, role, baseUrl, mapping.ExternalId) }
                        });
                    }
                }
            }

            return ScimOperationResult.Ok(new ScimListResponse<ScimGroup>
            {
                TotalResults = 0,
                StartIndex = startIndex < 1 ? 1 : startIndex,
                ItemsPerPage = 0,
                Resources = new List<ScimGroup>()
            });
        }

        IQueryable<OlusoRole> query = _roleManager.Roles.Where(r => r.TenantId == client.TenantId);

        if (!string.IsNullOrEmpty(filter))
        {
            try
            {
                query = query.Where(BuildGroupFilter(filter));
            }
            catch (ScimFilterException ex)
            {
                return ScimOperationResult.FromError(ScimError.InvalidFilter(ex.Message));
            }
        }

        var totalResults = query.Count();

        query = ApplyGroupSort(query, sortBy, sortOrder);
        var (skip, take) = Paginate(startIndex, count);
        var roles = query.Skip(skip).Take(take).ToList();

        var scimGroups = new List<ScimGroup>();
        foreach (var role in roles)
        {
            var mapping = await _mappingStore.GetByInternalIdAsync(client.Id, "Group", role.Id);
            scimGroups.Add(await ToScimGroup(client, role, baseUrl, mapping?.ExternalId));
        }

        return ScimOperationResult.Ok(new ScimListResponse<ScimGroup>
        {
            TotalResults = totalResults,
            StartIndex = startIndex < 1 ? 1 : startIndex,
            ItemsPerPage = scimGroups.Count,
            Resources = scimGroups
        });
    }

    public async Task<ScimOperationResult> CreateGroupAsync(ScimClient client, ScimGroup scimGroup, string baseUrl)
    {
        using var origin = BeginOrigin(client);

        if (!client.CanManageGroups)
            return ScimOperationResult.FromError(ScimError.BadRequest("Client is not authorized to manage groups"));

        if (string.IsNullOrEmpty(scimGroup.DisplayName))
            return ScimOperationResult.FromError(ScimError.InvalidValue("displayName is required"));

        if (!string.IsNullOrEmpty(scimGroup.ExternalId))
        {
            var existingMapping = await _mappingStore.GetByExternalIdAsync(client.Id, "Group", scimGroup.ExternalId);
            if (existingMapping != null)
                return ScimOperationResult.FromError(ScimError.Conflict($"Group with externalId '{scimGroup.ExternalId}' already exists"));
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
            return ScimOperationResult.FromError(ScimError.InvalidValue(string.Join(", ", result.Errors.Select(e => e.Description))));

        var externalId = scimGroup.ExternalId ?? role.Id;
        await _mappingStore.CreateAsync(new ScimResourceMapping
        {
            TenantId = client.TenantId,
            ScimClientId = client.Id,
            ResourceType = "Group",
            ExternalId = externalId,
            InternalId = role.Id,
            CreatedAt = DateTime.UtcNow
        });

        if (scimGroup.Members?.Any() == true)
        {
            foreach (var member in scimGroup.Members)
                await AddMemberToRole(role, member.Value, client);
        }

        _logger.LogInformation(
            "SCIM group created: {GroupId} with externalId {ExternalId} by client {ClientId}",
            role.Id, externalId, client.Id);

        var created = await ToScimGroup(client, role, baseUrl, externalId);
        return ScimOperationResult.Created(created, $"{baseUrl}/Groups/{role.Id}");
    }

    public async Task<ScimOperationResult> ReplaceGroupAsync(ScimClient client, string id, ScimGroup scimGroup, string baseUrl)
    {
        using var origin = BeginOrigin(client);

        if (!client.CanManageGroups)
            return ScimOperationResult.FromError(ScimError.BadRequest("Client is not authorized to manage groups"));

        var (role, mapping) = await FindGroupAsync(client, id);
        if (role == null)
            return ScimOperationResult.FromError(ScimError.NotFound($"Group {id} not found"));

        role.DisplayName = scimGroup.DisplayName;
        role.UpdatedAt = DateTime.UtcNow;

        var result = await _roleManager.UpdateAsync(role);
        if (!result.Succeeded)
            return ScimOperationResult.FromError(ScimError.InvalidValue(string.Join(", ", result.Errors.Select(e => e.Description))));

        var heldBefore = await RemoveAllMembers(role, client);

        var kept = new HashSet<string>(StringComparer.Ordinal);
        if (scimGroup.Members?.Any() == true)
        {
            foreach (var member in scimGroup.Members)
            {
                if (await AddMemberToRole(role, member.Value, client, heldBefore) is { } added) kept.Add(added);
            }
        }

        await NotifyRemovedAsync(role, client, heldBefore.Except(kept));

        if (!string.IsNullOrEmpty(scimGroup.ExternalId) && mapping != null && mapping.ExternalId != scimGroup.ExternalId)
        {
            mapping.ExternalId = scimGroup.ExternalId;
            mapping.UpdatedAt = DateTime.UtcNow;
            await _mappingStore.UpdateAsync(mapping);
        }

        _logger.LogInformation("SCIM group updated: {GroupId} by client {ClientId}", role.Id, client.Id);

        return ScimOperationResult.Ok(await ToScimGroup(client, role, baseUrl, mapping?.ExternalId ?? scimGroup.ExternalId));
    }

    public async Task<ScimOperationResult> PatchGroupAsync(ScimClient client, string id, ScimPatchRequest patch, string baseUrl)
    {
        using var origin = BeginOrigin(client);

        if (!client.CanManageGroups)
            return ScimOperationResult.FromError(ScimError.BadRequest("Client is not authorized to manage groups"));

        var (role, mapping) = await FindGroupAsync(client, id);
        if (role == null)
            return ScimOperationResult.FromError(ScimError.NotFound($"Group {id} not found"));

        foreach (var op in patch.Operations)
            await ApplyGroupPatch(role, op, client);

        role.UpdatedAt = DateTime.UtcNow;
        await _roleManager.UpdateAsync(role);

        _logger.LogInformation("SCIM group patched: {GroupId} by client {ClientId}", role.Id, client.Id);

        return ScimOperationResult.Ok(await ToScimGroup(client, role, baseUrl, mapping?.ExternalId));
    }

    public async Task<ScimOperationResult> DeleteGroupAsync(ScimClient client, string id)
    {
        using var origin = BeginOrigin(client);

        if (!client.CanManageGroups)
            return ScimOperationResult.FromError(ScimError.BadRequest("Client is not authorized to manage groups"));

        var (role, mapping) = await FindGroupAsync(client, id);
        if (role == null)
            return ScimOperationResult.FromError(ScimError.NotFound($"Group {id} not found"));

        if (role.IsSystemRole)
            return ScimOperationResult.FromError(ScimError.Mutability("Cannot delete system role"));

        await _roleManager.DeleteAsync(role);

        if (mapping != null)
            await _mappingStore.DeleteAsync(mapping.Id);

        _logger.LogInformation("SCIM group deleted: {GroupId} by client {ClientId}", role.Id, client.Id);

        return ScimOperationResult.NoContent();
    }

    // ------------------------------------------------------------- Lookups

    private async Task<(OlusoUser? User, ScimResourceMapping? Mapping)> FindUserAsync(ScimClient client, string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user != null && user.TenantId == client.TenantId)
        {
            var mapping = await _mappingStore.GetByInternalIdAsync(client.Id, "User", user.Id);
            return (user, mapping);
        }

        var byExternal = await _mappingStore.GetByExternalIdAsync(client.Id, "User", id);
        if (byExternal != null)
        {
            user = await _userManager.FindByIdAsync(byExternal.InternalId);
            if (user != null && user.TenantId == client.TenantId)
                return (user, byExternal);
        }

        return (null, null);
    }

    private async Task<(OlusoRole? Role, ScimResourceMapping? Mapping)> FindGroupAsync(ScimClient client, string id)
    {
        var role = await _roleManager.FindByIdAsync(id);
        if (role != null && role.TenantId == client.TenantId)
        {
            var mapping = await _mappingStore.GetByInternalIdAsync(client.Id, "Group", role.Id);
            return (role, mapping);
        }

        var byExternal = await _mappingStore.GetByExternalIdAsync(client.Id, "Group", id);
        if (byExternal != null)
        {
            role = await _roleManager.FindByIdAsync(byExternal.InternalId);
            if (role != null && role.TenantId == client.TenantId)
                return (role, byExternal);
        }

        return (null, null);
    }

    // ----------------------------------------------------- Filter / sort

    private static Expression<Func<OlusoUser, bool>> BuildUserFilter(string filter)
    {
        var node = ScimFilterParser.Parse(filter);
        var translator = new ScimFilterTranslator<OlusoUser>()
            .MapString(u => u.UserName, "username")
            .MapString(u => u.Email, "emails.value", "emails", "email")
            .MapString(_ => "work", "emails.type")
            .MapString(u => u.DisplayName, "displayname")
            .MapString(u => u.FirstName, "name.givenname", "givenname")
            .MapString(u => u.LastName, "name.familyname", "familyname")
            .MapString(u => u.PhoneNumber, "phonenumbers.value", "phonenumbers")
            .MapString(u => u.Id, "id")
            .MapBool(u => u.IsActive, "active");
        return translator.Translate(node);
    }

    private static Expression<Func<OlusoRole, bool>> BuildGroupFilter(string filter)
    {
        var node = ScimFilterParser.Parse(filter);
        var translator = new ScimFilterTranslator<OlusoRole>()
            .MapString(r => r.DisplayName ?? r.Name, "displayname")
            .MapString(r => r.Id, "id");
        return translator.Translate(node);
    }

    private static IQueryable<OlusoUser> ApplyUserSort(IQueryable<OlusoUser> query, string? sortBy, string? sortOrder)
    {
        var desc = string.Equals(sortOrder, "descending", StringComparison.OrdinalIgnoreCase);
        return (sortBy?.ToLowerInvariant()) switch
        {
            "username" => Order(query, u => u.UserName, desc),
            "displayname" => Order(query, u => u.DisplayName, desc),
            "emails.value" or "emails" or "email" => Order(query, u => u.Email, desc),
            "name.givenname" or "givenname" => Order(query, u => u.FirstName, desc),
            "name.familyname" or "familyname" => Order(query, u => u.LastName, desc),
            "active" => Order(query, u => u.IsActive, desc),
            "meta.created" or "created" => Order(query, u => u.CreatedAt, desc),
            // Stable, deterministic default so Skip/Take paging is repeatable.
            _ => query.OrderBy(u => u.Id)
        };
    }

    private static IQueryable<OlusoRole> ApplyGroupSort(IQueryable<OlusoRole> query, string? sortBy, string? sortOrder)
    {
        var desc = string.Equals(sortOrder, "descending", StringComparison.OrdinalIgnoreCase);
        return (sortBy?.ToLowerInvariant()) switch
        {
            "displayname" => Order(query, r => r.DisplayName, desc),
            "meta.created" or "created" => Order(query, r => r.CreatedAt, desc),
            _ => query.OrderBy(r => r.Id)
        };
    }

    private static IOrderedQueryable<T> Order<T, TKey>(IQueryable<T> query, Expression<Func<T, TKey>> selector, bool descending) =>
        descending ? query.OrderByDescending(selector) : query.OrderBy(selector);

    private static (int Skip, int Take) Paginate(int startIndex, int count)
    {
        if (startIndex < 1) startIndex = 1;
        var take = count < 0 ? 0 : Math.Min(count, MaxPageSize);
        return (startIndex - 1, take);
    }

    // ----------------------------------------------------- Group PATCH

    private async Task ApplyGroupPatch(OlusoRole role, ScimPatchOperation op, ScimClient client)
    {
        var opName = op.Op?.ToLowerInvariant() ?? "";
        var rawPath = op.Path ?? "";
        var path = rawPath.ToLowerInvariant();

        switch (opName)
        {
            case "replace":
                if (string.IsNullOrEmpty(path))
                {
                    if (TryGetJsonElement(op.Value, out var obj) && obj.ValueKind == JsonValueKind.Object)
                    {
                        if (obj.TryGetProperty("displayName", out var dn)) role.DisplayName = GetString(dn);
                        if (obj.TryGetProperty("members", out var members)) await ReplaceMembers(role, members, client);
                    }
                }
                else if (path == "displayname")
                {
                    if (TryGetJsonElement(op.Value, out var dn)) role.DisplayName = GetString(dn);
                }
                else if (path.StartsWith("members"))
                {
                    if (TryGetJsonElement(op.Value, out var members)) await ReplaceMembers(role, members, client);
                }
                break;

            case "add":
                if (path.StartsWith("members") || string.IsNullOrEmpty(path))
                {
                    if (TryGetJsonElement(op.Value, out var members)) await AddMembers(role, members, client);
                }
                else if (path == "displayname")
                {
                    if (TryGetJsonElement(op.Value, out var dn)) role.DisplayName = GetString(dn);
                }
                break;

            case "remove":
                if (path.StartsWith("members"))
                {
                    // Use the original-cased path: member IDs can contain uppercase.
                    var memberId = ExtractMemberIdFromPath(rawPath);
                    if (!string.IsNullOrEmpty(memberId))
                    {
                        await RemoveMemberFromRole(role, memberId, client);
                    }
                    else if (TryGetJsonElement(op.Value, out var members) && members.ValueKind != JsonValueKind.Undefined)
                    {
                        await RemoveMembers(role, members, client);
                    }
                    else
                    {
                        await NotifyRemovedAsync(role, client, await RemoveAllMembers(role, client));
                    }
                }
                break;
        }
    }

    private async Task ReplaceMembers(OlusoRole role, JsonElement value, ScimClient client)
    {
        var heldBefore = await RemoveAllMembers(role, client);
        var kept = await AddMembers(role, value, client, heldBefore);
        await NotifyRemovedAsync(role, client, heldBefore.Except(kept));
    }

    private async Task<HashSet<string>> AddMembers(OlusoRole role, JsonElement value, ScimClient client, IReadOnlySet<string>? heldBefore = null)
    {
        var added = new HashSet<string>(StringComparer.Ordinal);
        if (role.Name == null) return added;

        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                if (item.TryGetProperty("value", out var v)
                    && await AddMemberToRole(role, v.GetString(), client, heldBefore) is { } id)
                    added.Add(id);
            }
        }
        else if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("value", out var single)
            && await AddMemberToRole(role, single.GetString(), client, heldBefore) is { } singleId)
        {
            added.Add(singleId);
        }

        return added;
    }

    /// <summary>
    /// Members a replace or remove-all took the role from for good: they held
    /// it and weren't put back. Runs after the change, like any other removal.
    /// </summary>
    private async Task NotifyRemovedAsync(OlusoRole role, ScimClient client, IEnumerable<string> removedUserIds)
    {
        foreach (var userId in removedUserIds)
        {
            await _events.RaiseAsync(new UserRoleRemovedEvent
            {
                TenantId = client.TenantId,
                SubjectId = userId,
                RoleId = role.Id,
                RoleName = role.Name ?? role.Id
            });
            await _moduleAdminNotifier.NotifyRemovedAsync(client.TenantId!, userId, new[] { role }, RoleGrantActor.System($"SCIM ({client.Name})"));
        }
    }

    private async Task RemoveMembers(OlusoRole role, JsonElement value, ScimClient client)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                if (item.TryGetProperty("value", out var v) && v.GetString() is { } id)
                    await RemoveMemberFromRole(role, id, client);
            }
        }
        else if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("value", out var single) && single.GetString() is { } singleId)
        {
            await RemoveMemberFromRole(role, singleId, client);
        }
    }

    /// <summary>
    /// Removes everyone assigned the role in this tenant (the set the group
    /// lists) and returns who they were. Doesn't notify: a replace re-adds some
    /// of them, and only those not put back have lost the role.
    /// </summary>
    private async Task<IReadOnlySet<string>> RemoveAllMembers(OlusoRole role, ScimClient client)
    {
        var currentUsers = (await _roleStore.GetUsersByRoleInTenantAsync(role.Id, client.TenantId!)).ToList();
        foreach (var user in currentUsers)
            await _roleStore.RemoveFromUserInTenantAsync(role.Id, user.Id, client.TenantId!);
        return currentUsers.Select(u => u.Id).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Returns the user the role was given to, or null when the member didn't resolve.</summary>
    private async Task<string?> AddMemberToRole(OlusoRole role, string? memberId, ScimClient client, IReadOnlySet<string>? heldBefore = null)
    {
        if (string.IsNullOrEmpty(memberId)) return null;

        var user = await _userManager.FindByIdAsync(memberId);
        if (user == null || user.TenantId != client.TenantId)
        {
            var userMapping = await _mappingStore.GetByExternalIdAsync(client.Id, "User", memberId);
            if (userMapping != null)
                user = await _userManager.FindByIdAsync(userMapping.InternalId);
        }

        if (user == null || user.TenantId != client.TenantId) return null;

        if (heldBefore?.Contains(user.Id) == true)
        {
            // Re-added by a replace: not a new grant, so no notification.
            await _roleStore.AssignToUserInTenantAsync(role.Id, user.Id, client.TenantId!, "scim");
        }
        else
        {
            await _roleGrants.GrantAsync(role.Id, user.Id, client.TenantId!, RoleGrantActor.System($"SCIM ({client.Name})"));
        }

        return user.Id;
    }

    private async Task RemoveMemberFromRole(OlusoRole role, string memberId, ScimClient client)
    {
        var user = await _userManager.FindByIdAsync(memberId);
        if (user == null || user.TenantId != client.TenantId)
        {
            var userMapping = await _mappingStore.GetByExternalIdAsync(client.Id, "User", memberId);
            if (userMapping != null)
                user = await _userManager.FindByIdAsync(userMapping.InternalId);
        }

        if (user != null && user.TenantId == client.TenantId)
            await _roleGrants.RevokeAsync(role.Id, user.Id, client.TenantId!, RoleGrantActor.System($"SCIM ({client.Name})"));
    }

    private async Task<ScimGroup> ToScimGroup(ScimClient client, OlusoRole role, string baseUrl, string? externalId)
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
                Location = $"{baseUrl}/Groups/{role.Id}",
                Version = $"W/\"{role.UpdatedAt?.Ticks ?? role.CreatedAt.Ticks}\""
            }
        };

        var users = await _roleStore.GetUsersByRoleInTenantAsync(role.Id, client.TenantId!);
        group.Members = users.Select(u => new ScimMember
        {
            Value = u.Id,
            Ref = $"{baseUrl}/Users/{u.Id}",
            Display = u.DisplayName ?? u.UserName,
            Type = "User"
        }).ToList();

        return group;
    }

    // --------------------------------------------------------- Helpers

    private static string? ExtractEqValue(string filter, string attribute)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            filter,
            $@"{System.Text.RegularExpressions.Regex.Escape(attribute)}\s+eq\s+[""']([^""']+)[""']",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? ExtractMemberIdFromPath(string path)
    {
        var match = System.Text.RegularExpressions.Regex.Match(path, @"members\[value eq ""([^""]+)""\]");
        return match.Success ? match.Groups[1].Value : null;
    }

    private static bool TryGetJsonElement(object? value, out JsonElement element)
    {
        switch (value)
        {
            case null:
                element = default;
                return false;
            case JsonElement je:
                element = je;
                return true;
            default:
                element = JsonSerializer.SerializeToElement(value);
                return true;
        }
    }

    private static string? GetString(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString(),
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => null
    };

    private static bool GetBool(JsonElement value, bool fallback) => value.ValueKind switch
    {
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.String => bool.TryParse(value.GetString(), out var b) ? b : fallback,
        _ => fallback
    };

    private static string? GetPrimaryMultiValue(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                return value.GetString();
            case JsonValueKind.Object:
                return value.TryGetProperty("value", out var v) ? v.GetString() : null;
            case JsonValueKind.Array:
                JsonElement? first = null;
                foreach (var item in value.EnumerateArray())
                {
                    first ??= item;
                    if (item.TryGetProperty("primary", out var p) && p.ValueKind == JsonValueKind.True)
                        return item.TryGetProperty("value", out var pv) ? pv.GetString() : null;
                }
                return first.HasValue && first.Value.TryGetProperty("value", out var fv) ? fv.GetString() : null;
            default:
                return null;
        }
    }
}

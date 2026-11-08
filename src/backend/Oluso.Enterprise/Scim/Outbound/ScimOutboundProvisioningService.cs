using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Enterprise.Scim.Entities;
using Oluso.Enterprise.Scim.Stores;

namespace Oluso.Enterprise.Scim.Outbound;

public sealed class ScimOutboxProcessResult
{
    public bool Success { get; init; }

    /// <summary>False when retrying cannot help: bad mapping, provider rejected the payload, and so on.</summary>
    public bool Retryable { get; init; } = true;

    public int? StatusCode { get; init; }
    public string? Error { get; init; }
    public string? Note { get; init; }

    public static ScimOutboxProcessResult Ok(string? note = null, int? statusCode = null) =>
        new() { Success = true, Note = note, StatusCode = statusCode };

    public static ScimOutboxProcessResult Fail(string error, bool retryable, int? statusCode = null) =>
        new() { Success = false, Error = error, Retryable = retryable, StatusCode = statusCode };
}

/// <summary>
/// Executes one queue item against its provider: builds the payload, finds
/// or creates the remote resource, updates it, and records the remote id.
/// Runs inside a scope whose tenant context is the provider's tenant.
/// </summary>
public interface IScimOutboundProvisioningService
{
    Task<ScimOutboxProcessResult> ProcessAsync(ScimProviderOutboxItem item, ScimProvider provider, CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds the payload that would be pushed for a user, without pushing
    /// it. For checking mappings and expressions from the admin UI.
    /// </summary>
    Task<ScimOutboundPreview> PreviewUserAsync(ScimProvider provider, string userId, CancellationToken cancellationToken = default);
}

public sealed class ScimOutboundPreview
{
    public required string UserId { get; init; }
    public string? UserName { get; init; }
    public bool InScope { get; init; }
    public string? Error { get; init; }
    public JsonObject? Payload { get; init; }
}

public sealed class ScimOutboundProvisioningService : IScimOutboundProvisioningService
{
    private const string UsersCollection = "Users";
    private const string GroupsCollection = "Groups";

    private readonly IScimProviderResourceMappingStore _mappings;
    private readonly IScimProviderAttributeMappingStore _attributeMappings;
    private readonly IScimProviderHttpClient _http;
    private readonly IScimOutboundMapper _mapper;
    private readonly IScimProviderLogStore _logs;
    private readonly IScimProviderStore _providers;
    private readonly IOlusoUserService _users;
    private readonly IRoleStore _roles;
    private readonly ITeamStore _teams;
    private readonly ScimGroupSourceRegistry _groups;
    private readonly ScimProviderFlavorRegistry _flavors;
    private readonly IScimProviderEnqueuer _enqueuer;
    private readonly ScimOptions _options;
    private readonly ILogger<ScimOutboundProvisioningService> _logger;

    public ScimOutboundProvisioningService(
        IScimProviderResourceMappingStore mappings,
        IScimProviderAttributeMappingStore attributeMappings,
        IScimProviderHttpClient http,
        IScimOutboundMapper mapper,
        IScimProviderLogStore logs,
        IScimProviderStore providers,
        IOlusoUserService users,
        IRoleStore roles,
        ITeamStore teams,
        ScimGroupSourceRegistry groups,
        ScimProviderFlavorRegistry flavors,
        IScimProviderEnqueuer enqueuer,
        ScimOptions options,
        ILogger<ScimOutboundProvisioningService> logger)
    {
        _mappings = mappings;
        _attributeMappings = attributeMappings;
        _http = http;
        _mapper = mapper;
        _logs = logs;
        _providers = providers;
        _users = users;
        _roles = roles;
        _teams = teams;
        _groups = groups;
        _flavors = flavors;
        _enqueuer = enqueuer;
        _options = options;
        _logger = logger;
    }

    public async Task<ScimOutboxProcessResult> ProcessAsync(ScimProviderOutboxItem item, ScimProvider provider, CancellationToken cancellationToken = default)
    {
        ScimOutboxProcessResult result;
        try
        {
            result = (item.ResourceType, item.Operation) switch
            {
                (ScimResourceTypes.User, ScimOutboxOperations.Remove) => await RemoveUserAsync(item, provider, null, cancellationToken),
                (ScimResourceTypes.User, _) => await UpsertUserAsync(item, provider, cancellationToken),
                (ScimResourceTypes.Group, ScimOutboxOperations.Remove) => await RemoveGroupAsync(item, provider, null, cancellationToken),
                (ScimResourceTypes.Group, _) => await UpsertGroupAsync(item, provider, cancellationToken),
                _ => ScimOutboxProcessResult.Fail($"Unknown resource type '{item.ResourceType}'", retryable: false),
            };
        }
        catch (ScimOutboundMappingException ex)
        {
            result = ScimOutboxProcessResult.Fail(ex.Message, retryable: false);
        }
        catch (ScimProviderAuthenticationException ex)
        {
            result = ScimOutboxProcessResult.Fail(ex.Message, retryable: true, statusCode: 401);
        }

        await _providers.RecordOutcomeAsync(provider.Id, result.Success, result.Error, cancellationToken);
        return result;
    }

    public async Task<ScimOutboundPreview> PreviewUserAsync(ScimProvider provider, string userId, CancellationToken cancellationToken = default)
    {
        var user = await _users.GetByIdAsync(userId, cancellationToken);
        if (user == null || !string.Equals(user.TenantId, provider.TenantId, StringComparison.Ordinal))
            return new ScimOutboundPreview { UserId = userId, Error = "User not found in this tenant" };

        var inScope = await IsUserInScopeAsync(provider, user.Id, cancellationToken);
        var flavor = _flavors.Get(provider.Flavor);
        var attributeMappings = await _attributeMappings.GetByProviderAsync(provider.Id, cancellationToken);
        var context = await BuildUserContextAsync(provider, user, attributeMappings, cancellationToken);

        try
        {
            var payload = _mapper.BuildUser(provider, flavor, context, attributeMappings);
            return new ScimOutboundPreview
            {
                UserId = userId,
                UserName = payload["userName"]?.ToString(),
                InScope = inScope,
                Payload = payload,
            };
        }
        catch (ScimOutboundMappingException ex)
        {
            return new ScimOutboundPreview { UserId = userId, InScope = inScope, Error = ex.Message };
        }
    }

    #region Users

    private async Task<ScimOutboxProcessResult> UpsertUserAsync(ScimProviderOutboxItem item, ScimProvider provider, CancellationToken cancellationToken)
    {
        if (!provider.SyncUsers)
            return ScimOutboxProcessResult.Ok("User sync is disabled for this provider");

        var user = await _users.GetByIdAsync(item.InternalId, cancellationToken);
        if (user == null || !string.Equals(user.TenantId, provider.TenantId, StringComparison.Ordinal))
            return await RemoveUserAsync(item, provider, "user no longer exists in this tenant", cancellationToken);

        var mapping = await _mappings.GetByInternalIdAsync(provider.Id, ScimResourceTypes.User, user.Id, cancellationToken);

        if (!await IsUserInScopeAsync(provider, user.Id, cancellationToken))
        {
            if (mapping is { IsDeprovisioned: false })
                return await RemoveRemoteUserAsync(provider, mapping, user, "left the provisioning scope", cancellationToken);
            return ScimOutboxProcessResult.Ok("User is outside the provisioning scope");
        }

        var flavor = _flavors.Get(provider.Flavor);
        var attributeMappings = await _attributeMappings.GetByProviderAsync(provider.Id, cancellationToken);
        var context = await BuildUserContextAsync(provider, user, attributeMappings, cancellationToken);
        var payload = _mapper.BuildUser(provider, flavor, context, attributeMappings);
        var hash = _mapper.ComputeHash(payload);

        if (!IsReconcile(item) && mapping is { IsDeprovisioned: false } && mapping.PayloadHash == hash)
            return ScimOutboxProcessResult.Ok("Unchanged since the last push");

        var outcome = await PushAsync(provider, flavor.UserLookupAttributes, ScimResourceTypes.User, user.Id, payload, hash, mapping, cancellationToken);

        if (outcome.Success)
            await EnqueueGroupsForUserAsync(provider, user.Id, cancellationToken);

        return outcome;
    }

    private async Task<ScimOutboxProcessResult> RemoveUserAsync(ScimProviderOutboxItem item, ScimProvider provider, string? reason, CancellationToken cancellationToken)
    {
        var mapping = await _mappings.GetByInternalIdAsync(provider.Id, ScimResourceTypes.User, item.InternalId, cancellationToken);
        if (mapping == null)
            return ScimOutboxProcessResult.Ok(reason == null ? "Nothing to remove; user was never provisioned" : $"Nothing to remove ({reason})");

        var user = await _users.GetByIdAsync(item.InternalId, cancellationToken);
        return await RemoveRemoteUserAsync(provider, mapping, user, reason ?? "removed", cancellationToken);
    }

    private async Task<ScimOutboxProcessResult> RemoveRemoteUserAsync(ScimProvider provider, ScimProviderResourceMapping mapping, OlusoUser? user, string reason, CancellationToken cancellationToken)
    {
        var path = $"{UsersCollection}/{Uri.EscapeDataString(mapping.RemoteId)}";

        if (string.Equals(provider.DeprovisionMode, ScimProviderDeprovisionModes.Delete, StringComparison.OrdinalIgnoreCase))
        {
            var delete = await _http.SendAsync(provider, HttpMethod.Delete, path, cancellationToken: cancellationToken);
            await LogAsync(provider, "Delete", ScimResourceTypes.User, mapping.InternalId, mapping.RemoteId, delete, null, cancellationToken);

            if (!delete.IsSuccess && delete.StatusCode != 404)
                return Classify(delete);

            await _mappings.DeleteAsync(mapping.Id, cancellationToken);
            if (user != null)
                await EnqueueGroupsForUserAsync(provider, user.Id, cancellationToken);
            return ScimOutboxProcessResult.Ok($"Deleted remote user ({reason})", delete.StatusCode);
        }

        var patch = new JsonObject
        {
            ["schemas"] = new JsonArray(ScimConstants.Schemas.PatchOp),
            ["Operations"] = new JsonArray(new JsonObject
            {
                ["op"] = ScimConstants.PatchOp.Replace,
                ["path"] = "active",
                ["value"] = false,
            }),
        };

        var response = await _http.SendAsync(provider, HttpMethod.Patch, path, patch, cancellationToken: cancellationToken);
        await LogAsync(provider, "Deactivate", ScimResourceTypes.User, mapping.InternalId, mapping.RemoteId, response, patch, cancellationToken);

        if (response.StatusCode == 404)
        {
            await _mappings.DeleteAsync(mapping.Id, cancellationToken);
            return ScimOutboxProcessResult.Ok("Remote user no longer exists", 404);
        }

        if (response.StatusCode is 405 or 501)
        {
            var current = await _http.SendAsync(provider, HttpMethod.Get, path, cancellationToken: cancellationToken);
            await LogAsync(provider, "Deactivate", ScimResourceTypes.User, mapping.InternalId, mapping.RemoteId, current, null, cancellationToken);
            if (!current.IsSuccess)
                return Classify(current);

            var body = current.Body?.DeepClone() as JsonObject ?? new JsonObject();
            body.Remove("meta");
            body["active"] = false;

            response = await _http.SendAsync(provider, HttpMethod.Put, path, body, cancellationToken: cancellationToken);
            await LogAsync(provider, "Deactivate", ScimResourceTypes.User, mapping.InternalId, mapping.RemoteId, response, body, cancellationToken);
        }

        if (!response.IsSuccess)
            return Classify(response);

        mapping.IsDeprovisioned = true;
        mapping.PayloadHash = null;
        mapping.RemoteVersion = response.RemoteVersion ?? mapping.RemoteVersion;
        mapping.LastSyncedAt = DateTime.UtcNow;
        mapping.UpdatedAt = DateTime.UtcNow;
        await _mappings.UpdateAsync(mapping, cancellationToken);

        if (user != null)
            await EnqueueGroupsForUserAsync(provider, user.Id, cancellationToken);
        return ScimOutboxProcessResult.Ok($"Deactivated remote user ({reason})", response.StatusCode);
    }

    /// <summary>
    /// Role and team names are only loaded when an expression can read them.
    /// </summary>
    private async Task<ScimOutboundUserContext> BuildUserContextAsync(
        ScimProvider provider,
        OlusoUser user,
        IReadOnlyList<ScimProviderAttributeMapping> attributeMappings,
        CancellationToken cancellationToken)
    {
        if (!_mapper.UsesExpressions(provider, attributeMappings))
            return new ScimOutboundUserContext { User = user };

        var tenantId = provider.TenantId!;
        var roles = await _roles.GetUserRolesInTenantAsync(user.Id, tenantId, cancellationToken);
        var teams = await _teams.GetUserTeamsAsync(tenantId, user.Id, activeOnly: true, cancellationToken);

        return new ScimOutboundUserContext
        {
            User = user,
            RoleNames = roles.Select(r => r.Name ?? r.Id).ToList(),
            TeamNames = teams.Select(t => t.Name).ToList(),
            TeamCodes = teams.Select(t => t.Code).ToList(),
        };
    }

    /// <summary>
    /// No scope means everyone in the tenant. Otherwise the user must hold
    /// one of the scope roles or belong to one of the scope teams.
    /// </summary>
    private async Task<bool> IsUserInScopeAsync(ScimProvider provider, string userId, CancellationToken cancellationToken)
    {
        if (!provider.HasUserScope)
            return true;

        var tenantId = provider.TenantId!;
        var scopeRoles = provider.GetScopeRoleIds();
        if (scopeRoles.Count > 0)
        {
            var roleIds = await _groups.Get(ScimGroupKinds.Role).GetUserGroupIdsAsync(tenantId, userId, cancellationToken);
            if (roleIds.Any(scopeRoles.Contains))
                return true;
        }

        var scopeTeams = provider.GetScopeTeamIds();
        if (scopeTeams.Count > 0)
        {
            var teamIds = await _groups.Get(ScimGroupKinds.Team).GetUserGroupIdsAsync(tenantId, userId, cancellationToken);
            if (teamIds.Any(scopeTeams.Contains))
                return true;
        }

        return false;
    }

    /// <summary>
    /// After a user changed on the provider, every group that lists them
    /// needs its member list refreshed.
    /// </summary>
    private async Task EnqueueGroupsForUserAsync(ScimProvider provider, string userId, CancellationToken cancellationToken)
    {
        foreach (var source in _groups.EnabledFor(provider))
        {
            var ids = await source.GetUserGroupIdsAsync(provider.TenantId!, userId, cancellationToken);
            var allowed = await _groups.FilterAsync(provider, source.Kind, cancellationToken);
            foreach (var id in ids.Where(id => ScimGroupSourceRegistry.IsGroupInScope(provider, source.Kind, id) && (allowed == null || allowed.Contains(id))))
            {
                await _enqueuer.EnqueueForProviderAsync(provider, ScimResourceTypes.Group, ScimGroupKinds.Key(source.Kind, id),
                    ScimOutboxOperations.Upsert, "member-changed", cancellationToken);
            }
        }
    }

    #endregion

    #region Groups

    private async Task<ScimOutboxProcessResult> UpsertGroupAsync(ScimProviderOutboxItem item, ScimProvider provider, CancellationToken cancellationToken)
    {
        if (!ScimGroupKinds.TryParse(item.InternalId, out var kind, out var id))
            return ScimOutboxProcessResult.Fail($"Group key '{item.InternalId}' is not of the form role:<id> or team:<id>", retryable: false);

        var mapping = await _mappings.GetByInternalIdAsync(provider.Id, ScimResourceTypes.Group, item.InternalId, cancellationToken);

        if (!_groups.IsKindEnabled(provider, kind))
        {
            if (mapping != null)
                return await DeleteRemoteGroupAsync(provider, mapping, $"{kind} groups are not synced by this provider", cancellationToken);
            return ScimOutboxProcessResult.Ok($"{kind} groups are not synced by this provider");
        }

        var source = _groups.Get(kind);
        var group = await source.GetAsync(provider.TenantId!, id, cancellationToken);
        if (group == null)
            return await RemoveGroupAsync(item, provider, $"{kind} no longer exists in this tenant", cancellationToken);

        if (!await _groups.IsGroupPushedAsync(provider, kind, id, cancellationToken))
        {
            if (mapping != null)
                return await DeleteRemoteGroupAsync(provider, mapping, "outside the provisioning scope", cancellationToken);
            return ScimOutboxProcessResult.Ok("Group is outside the provisioning scope");
        }

        var members = (await source.GetMemberUserIdsAsync(provider.TenantId!, id, cancellationToken))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var memberMappings = await _mappings.GetByInternalIdsAsync(provider.Id, ScimResourceTypes.User, members, cancellationToken);
        var missing = members.Where(userId => !memberMappings.ContainsKey(userId)).ToList();
        if (provider.SyncUsers)
        {
            foreach (var userId in missing)
                await _enqueuer.EnqueueForProviderAsync(provider, ScimResourceTypes.User, userId, ScimOutboxOperations.Upsert, "group-member", cancellationToken);
        }

        var remoteMemberIds = members.Where(memberMappings.ContainsKey).Select(userId => memberMappings[userId].RemoteId).ToList();
        var flavor = _flavors.Get(provider.Flavor);
        JsonObject payload;
        if (flavor.SendsTeamExtension && group.Hierarchy is { } hierarchy)
        {
            // Leads come from the same mapping as members: one not provisioned yet is left out, and queued above.
            var remoteLeadIds = hierarchy.LeadUserIds.Where(memberMappings.ContainsKey).Select(userId => memberMappings[userId].RemoteId).ToList();
            payload = _mapper.BuildGroup(group.Key, group.DisplayName, remoteMemberIds,
                new ScimTeamGroupExtension(hierarchy.Code, hierarchy.ParentKey, remoteLeadIds, hierarchy.ManagedBy));
        }
        else
        {
            payload = _mapper.BuildGroup(group.Key, group.DisplayName, remoteMemberIds);
        }
        var hash = _mapper.ComputeHash(payload);

        if (!IsReconcile(item) && mapping != null && mapping.PayloadHash == hash)
            return ScimOutboxProcessResult.Ok("Unchanged since the last push");

        var outcome = await PushAsync(provider, flavor.GroupLookupAttributes, ScimResourceTypes.Group, group.Key, payload, hash, mapping, cancellationToken);

        if (outcome.Success && missing.Count > 0)
            return ScimOutboxProcessResult.Ok($"{outcome.Note}; {missing.Count} member(s) not yet provisioned and queued", outcome.StatusCode);

        return outcome;
    }

    private async Task<ScimOutboxProcessResult> RemoveGroupAsync(ScimProviderOutboxItem item, ScimProvider provider, string? reason, CancellationToken cancellationToken)
    {
        var mapping = await _mappings.GetByInternalIdAsync(provider.Id, ScimResourceTypes.Group, item.InternalId, cancellationToken);
        if (mapping == null)
            return ScimOutboxProcessResult.Ok(reason == null ? "Nothing to remove; group was never provisioned" : $"Nothing to remove ({reason})");

        return await DeleteRemoteGroupAsync(provider, mapping, reason ?? "removed", cancellationToken);
    }

    private async Task<ScimOutboxProcessResult> DeleteRemoteGroupAsync(ScimProvider provider, ScimProviderResourceMapping mapping, string reason, CancellationToken cancellationToken)
    {
        var path = $"{GroupsCollection}/{Uri.EscapeDataString(mapping.RemoteId)}";
        var delete = await _http.SendAsync(provider, HttpMethod.Delete, path, cancellationToken: cancellationToken);
        await LogAsync(provider, "Delete", ScimResourceTypes.Group, mapping.InternalId, mapping.RemoteId, delete, null, cancellationToken);

        if (!delete.IsSuccess && delete.StatusCode != 404)
            return Classify(delete);

        await _mappings.DeleteAsync(mapping.Id, cancellationToken);
        return ScimOutboxProcessResult.Ok($"Deleted remote group ({reason})", delete.StatusCode);
    }

    #endregion

    #region Shared push pipeline

    /// <summary>
    /// Update when we know the remote id; otherwise look the resource up by
    /// the flavor's lookup attributes and adopt it; otherwise create it. A
    /// 409 on create is treated as "exists" and resolved by lookup.
    /// </summary>
    private async Task<ScimOutboxProcessResult> PushAsync(
        ScimProvider provider,
        IReadOnlyList<string> lookupAttributes,
        string resourceType,
        string internalId,
        JsonObject payload,
        string hash,
        ScimProviderResourceMapping? mapping,
        CancellationToken cancellationToken)
    {
        var collection = resourceType == ScimResourceTypes.Group ? GroupsCollection : UsersCollection;

        if (mapping != null)
        {
            var update = await UpdateRemoteAsync(provider, collection, resourceType, internalId, mapping, payload, cancellationToken);
            if (update.StatusCode != 404)
                return await CompleteUpdateAsync(update, mapping, hash, cancellationToken);

            await _mappings.DeleteAsync(mapping.Id, cancellationToken);
            mapping = null;
        }

        var lookup = await LookupRemoteIdAsync(provider, collection, resourceType, internalId, lookupAttributes, payload, cancellationToken);
        if (lookup.Failure != null)
            return Classify(lookup.Failure);

        if (lookup.RemoteId != null)
        {
            mapping = await CreateMappingAsync(provider, resourceType, internalId, lookup.RemoteId, null, null, cancellationToken);
            var update = await UpdateRemoteAsync(provider, collection, resourceType, internalId, mapping, payload, cancellationToken);
            return await CompleteUpdateAsync(update, mapping, hash, cancellationToken);
        }

        var create = await _http.SendAsync(provider, HttpMethod.Post, collection, payload, cancellationToken: cancellationToken);
        await LogAsync(provider, "Create", resourceType, internalId, null, create, payload, cancellationToken);

        if (create.StatusCode == 409)
        {
            var retry = await LookupRemoteIdAsync(provider, collection, resourceType, internalId, lookupAttributes, payload, cancellationToken);
            if (retry.Failure != null)
                return Classify(retry.Failure);
            if (retry.RemoteId == null)
                return ScimOutboxProcessResult.Fail($"Provider reported a conflict but no matching {resourceType} was found by {string.Join("/", lookupAttributes)}: {create.Describe()}", retryable: false, 409);

            mapping = await CreateMappingAsync(provider, resourceType, internalId, retry.RemoteId, null, null, cancellationToken);
            var update = await UpdateRemoteAsync(provider, collection, resourceType, internalId, mapping, payload, cancellationToken);
            return await CompleteUpdateAsync(update, mapping, hash, cancellationToken);
        }

        if (!create.IsSuccess)
            return Classify(create);

        var remoteId = create.RemoteId;
        if (string.IsNullOrEmpty(remoteId))
            return ScimOutboxProcessResult.Fail($"Provider created the {resourceType} but returned no id", retryable: false, create.StatusCode);

        await CreateMappingAsync(provider, resourceType, internalId, remoteId, create.RemoteVersion, hash, cancellationToken);
        return ScimOutboxProcessResult.Ok($"Created remote {resourceType} {remoteId}", create.StatusCode);
    }

    private async Task<ScimRemoteResponse> UpdateRemoteAsync(
        ScimProvider provider,
        string collection,
        string resourceType,
        string internalId,
        ScimProviderResourceMapping mapping,
        JsonObject payload,
        CancellationToken cancellationToken)
    {
        var path = $"{collection}/{Uri.EscapeDataString(mapping.RemoteId)}";
        var put = await _http.SendAsync(provider, HttpMethod.Put, path, payload, cancellationToken: cancellationToken);
        await LogAsync(provider, "Update", resourceType, internalId, mapping.RemoteId, put, payload, cancellationToken);

        if (put.StatusCode is not (405 or 501))
            return put;

        var patch = BuildReplacePatch(payload);
        var response = await _http.SendAsync(provider, HttpMethod.Patch, path, patch, cancellationToken: cancellationToken);
        await LogAsync(provider, "Update", resourceType, internalId, mapping.RemoteId, response, patch, cancellationToken);
        return response;
    }

    private async Task<ScimOutboxProcessResult> CompleteUpdateAsync(ScimRemoteResponse response, ScimProviderResourceMapping mapping, string hash, CancellationToken cancellationToken)
    {
        if (!response.IsSuccess)
            return Classify(response);

        mapping.PayloadHash = hash;
        mapping.RemoteVersion = response.RemoteVersion ?? mapping.RemoteVersion;
        mapping.IsDeprovisioned = false;
        mapping.LastSyncedAt = DateTime.UtcNow;
        mapping.UpdatedAt = DateTime.UtcNow;
        await _mappings.UpdateAsync(mapping, cancellationToken);

        return ScimOutboxProcessResult.Ok($"Updated remote {mapping.ResourceType} {mapping.RemoteId}", response.StatusCode);
    }

    private Task<ScimProviderResourceMapping> CreateMappingAsync(
        ScimProvider provider,
        string resourceType,
        string internalId,
        string remoteId,
        string? remoteVersion,
        string? hash,
        CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        return _mappings.CreateAsync(new ScimProviderResourceMapping
        {
            TenantId = provider.TenantId,
            ScimProviderId = provider.Id,
            ResourceType = resourceType,
            InternalId = internalId,
            RemoteId = remoteId,
            RemoteVersion = remoteVersion,
            PayloadHash = hash,
            LastSyncedAt = hash == null ? null : now,
            CreatedAt = now,
        }, cancellationToken);
    }

    private readonly record struct LookupResult(string? RemoteId, ScimRemoteResponse? Failure);

    /// <summary>
    /// Tries each lookup attribute in turn with an <c>eq</c> filter, the one
    /// operator every provider supports. A 400 means the provider cannot
    /// filter on that attribute, so the next one is tried; any other failure
    /// aborts, because creating blind after a failed lookup risks duplicates.
    /// </summary>
    private async Task<LookupResult> LookupRemoteIdAsync(
        ScimProvider provider,
        string collection,
        string resourceType,
        string internalId,
        IReadOnlyList<string> lookupAttributes,
        JsonObject payload,
        CancellationToken cancellationToken)
    {
        foreach (var attribute in lookupAttributes)
        {
            var value = attribute.Equals("externalId", StringComparison.OrdinalIgnoreCase)
                ? internalId
                : payload[attribute]?.ToString();

            if (string.IsNullOrEmpty(value))
                continue;

            var filter = $"{attribute} eq \"{EscapeFilterValue(value)}\"";
            var path = $"{collection}?filter={Uri.EscapeDataString(filter)}&count=2";

            var response = await _http.SendAsync(provider, HttpMethod.Get, path, cancellationToken: cancellationToken);
            await LogAsync(provider, "Lookup", resourceType, internalId, null, response, null, cancellationToken);

            if (!response.IsSuccess)
            {
                if (response.StatusCode == 400)
                    continue;
                return new LookupResult(null, response);
            }

            var resources = (response.Body as JsonObject)?["Resources"] as JsonArray;
            var match = resources?.OfType<JsonObject>().FirstOrDefault();
            var id = match?["id"]?.ToString();
            if (!string.IsNullOrEmpty(id))
                return new LookupResult(id, null);
        }

        return new LookupResult(null, null);
    }

    private static string EscapeFilterValue(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"");

    /// <summary>
    /// PATCH equivalent of a full replace, for providers without PUT: one
    /// replace op per top-level attribute, extension attributes addressed
    /// individually by their qualified path.
    /// </summary>
    internal static JsonObject BuildReplacePatch(JsonObject payload)
    {
        var operations = new JsonArray();
        foreach (var (name, value) in payload)
        {
            if (value == null || name is "schemas" or "id" or "meta")
                continue;

            if (name.StartsWith("urn:", StringComparison.OrdinalIgnoreCase) && value is JsonObject extension)
            {
                foreach (var (attr, attrValue) in extension)
                {
                    if (attrValue == null)
                        continue;
                    operations.Add(new JsonObject
                    {
                        ["op"] = ScimConstants.PatchOp.Replace,
                        ["path"] = $"{name}:{attr}",
                        ["value"] = attrValue.DeepClone(),
                    });
                }

                continue;
            }

            operations.Add(new JsonObject
            {
                ["op"] = ScimConstants.PatchOp.Replace,
                ["path"] = name,
                ["value"] = value.DeepClone(),
            });
        }

        return new JsonObject
        {
            ["schemas"] = new JsonArray(ScimConstants.Schemas.PatchOp),
            ["Operations"] = operations,
        };
    }

    private static ScimOutboxProcessResult Classify(ScimRemoteResponse response)
    {
        var error = response.Describe();
        if (response.IsTransportFailure)
            return ScimOutboxProcessResult.Fail(error, retryable: true, 0);

        var retryable = response.StatusCode switch
        {
            401 or 403 => true,
            408 or 423 or 425 or 429 => true,
            >= 500 => true,
            _ => false,
        };

        return ScimOutboxProcessResult.Fail(error, retryable, response.StatusCode);
    }

    private static bool IsReconcile(ScimProviderOutboxItem item) =>
        item.Reason?.StartsWith("reconcile", StringComparison.OrdinalIgnoreCase) == true;

    private async Task LogAsync(
        ScimProvider provider,
        string operation,
        string resourceType,
        string internalId,
        string? remoteId,
        ScimRemoteResponse response,
        JsonNode? requestBody,
        CancellationToken cancellationToken)
    {
        try
        {
            await _logs.CreateAsync(new ScimProviderLog
            {
                TenantId = provider.TenantId,
                ScimProviderId = provider.Id,
                Operation = operation,
                ResourceType = resourceType,
                InternalId = internalId,
                RemoteId = remoteId ?? response.RemoteId,
                Method = response.Method,
                Path = response.Path.Length > 2000 ? response.Path[..2000] : response.Path,
                StatusCode = response.StatusCode,
                Success = response.IsSuccess,
                ErrorMessage = response.IsSuccess ? null : response.ErrorDetail,
                RequestBody = _options.LogRequestBodies ? requestBody?.ToJsonString() : null,
                ResponseBody = _options.LogRequestBodies ? response.RawBody : null,
                DurationMs = response.DurationMs,
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to write SCIM provider log for provider {ProviderId}", provider.Id);
        }
    }

    #endregion
}

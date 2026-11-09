using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Oluso.Core.Api;
using Oluso.Core.Authorization;
using Oluso.Core.Domain.Interfaces;
using Oluso.Enterprise.Scim.Entities;
using Oluso.Enterprise.Scim.Models.Admin;
using Oluso.Enterprise.Scim.Outbound;
using Oluso.Enterprise.Scim.Stores;

namespace Oluso.Enterprise.Scim.Endpoints;

/// <summary>
/// Admin API for outbound SCIM providers: downstream systems this tenant
/// pushes its users and groups to.
/// </summary>
[Route("api/admin/scim/providers")]
[RequirePermission("scim.read")]
public class ScimProvidersController : AdminBaseController
{
    private readonly IScimProviderStore _providers;
    private readonly ScimProviderFlavorRegistry _flavors;
    private readonly IScimProviderSecretStore _secrets;
    private readonly IScimProviderAttributeMappingStore _attributeMappings;
    private readonly IScimProviderResourceMappingStore _resourceMappings;
    private readonly IScimProviderOutboxStore _outbox;
    private readonly IScimProviderLogStore _logs;
    private readonly IScimProviderConnectionTester _tester;
    private readonly IScimProviderReconcileService _reconcile;
    private readonly IScimOutboundProvisioningService _provisioning;
    private readonly IRoleStore _roles;
    private readonly ITeamStore _teams;
    private readonly IScimClientStore _clients;
    private readonly ILogger<ScimProvidersController> _logger;

    public ScimProvidersController(
        IScimProviderStore providers,
        IScimProviderSecretStore secrets,
        IScimProviderAttributeMappingStore attributeMappings,
        IScimProviderResourceMappingStore resourceMappings,
        IScimProviderOutboxStore outbox,
        IScimProviderLogStore logs,
        IScimProviderConnectionTester tester,
        IScimProviderReconcileService reconcile,
        IScimOutboundProvisioningService provisioning,
        IRoleStore roles,
        ITeamStore teams,
        IScimClientStore clients,
        ScimProviderFlavorRegistry flavors,
        ILogger<ScimProvidersController> logger,
        ITenantContext tenantContext) : base(tenantContext)
    {
        _providers = providers;
        _secrets = secrets;
        _attributeMappings = attributeMappings;
        _resourceMappings = resourceMappings;
        _outbox = outbox;
        _logs = logs;
        _tester = tester;
        _reconcile = reconcile;
        _provisioning = provisioning;
        _roles = roles;
        _teams = teams;
        _clients = clients;
        _flavors = flavors;
        _logger = logger;
    }

    /// <summary>
    /// Flavors and the value lists the provider form is built from.
    /// </summary>
    [HttpGet("options")]
    public IActionResult GetOptions()
    {
        return Ok(new ScimProviderOptionsResponse
        {
            Flavors = _flavors.All.Select(ToResponse).ToList(),
            InternalProperties = ScimOutboundProperties.All.ToList(),
            Transformations = ScimOutboundTransformations.All.ToList(),
            AuthTypes = new List<string> { ScimProviderAuthTypes.Bearer, ScimProviderAuthTypes.OAuthClientCredentials },
            UserNameSources = ScimProviderUserNameSources.All.ToList(),
            DeprovisionModes = new List<string> { ScimProviderDeprovisionModes.Deactivate, ScimProviderDeprovisionModes.Delete },
            GroupSources = ScimProviderGroupSources.All.ToList(),
        });
    }

    [HttpGet]
    public async Task<IActionResult> GetProviders(CancellationToken cancellationToken)
    {
        var providers = await _providers.GetByTenantAsync(TenantId, cancellationToken);
        return Ok(providers.Select(ToResponse));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetProvider(string id, CancellationToken cancellationToken)
    {
        var provider = await FindAsync(id, cancellationToken);
        return provider == null ? NotFound() : Ok(ToResponse(provider));
    }

    [HttpPost]
    [RequirePermission("scim.write")]
    public async Task<IActionResult> CreateProvider([FromBody] CreateScimProviderRequest request, CancellationToken cancellationToken)
    {
        var provider = new ScimProvider { TenantId = TenantId };
        var error = await ApplyAsync(provider, request.Name, request.Description, request.IsEnabled, request.Flavor, request.BaseUrl,
            request.AuthType, request.OAuthTokenEndpoint, request.OAuthClientId, request.OAuthScope, request.SyncUsers, request.SyncGroups,
            request.GroupSource, request.ScopeRoleIds, request.ScopeTeamIds, request.UserNameSource, request.UserNameExpression,
            request.DeprovisionMode, request.ReconcileIntervalMinutes, request.LinkedScimClientId, cancellationToken);
        if (error != null)
            return BadRequest(error);

        if (!string.IsNullOrEmpty(request.Secret))
            provider.SecretUpdatedAt = DateTime.UtcNow;

        await _providers.CreateAsync(provider, cancellationToken);

        if (!string.IsNullOrEmpty(request.Secret))
            await _secrets.SetSecretAsync(TenantId, provider.Id, request.Secret, cancellationToken);

        _logger.LogInformation("SCIM provider created: {ProviderId} ({Flavor}) for tenant {TenantId}", provider.Id, provider.Flavor, TenantId);
        return Ok(ToResponse(provider));
    }

    [HttpPut("{id}")]
    [RequirePermission("scim.write")]
    public async Task<IActionResult> UpdateProvider(string id, [FromBody] UpdateScimProviderRequest request, CancellationToken cancellationToken)
    {
        var provider = await FindAsync(id, cancellationToken);
        if (provider == null)
            return NotFound();

        var error = await ApplyAsync(provider, request.Name, request.Description, request.IsEnabled, request.Flavor, request.BaseUrl,
            request.AuthType, request.OAuthTokenEndpoint, request.OAuthClientId, request.OAuthScope, request.SyncUsers, request.SyncGroups,
            request.GroupSource, request.ScopeRoleIds, request.ScopeTeamIds, request.UserNameSource, request.UserNameExpression,
            request.DeprovisionMode, request.ReconcileIntervalMinutes, request.LinkedScimClientId, cancellationToken);
        if (error != null)
            return BadRequest(error);

        provider.UpdatedAt = DateTime.UtcNow;
        await _providers.UpdateAsync(provider, cancellationToken);

        _logger.LogInformation("SCIM provider updated: {ProviderId}", id);
        return Ok(ToResponse(provider));
    }

    [HttpPut("{id}/secret")]
    [RequirePermission("scim.write")]
    public async Task<IActionResult> SetSecret(string id, [FromBody] SetScimProviderSecretRequest request, CancellationToken cancellationToken)
    {
        var provider = await FindAsync(id, cancellationToken);
        if (provider == null)
            return NotFound();

        if (string.IsNullOrWhiteSpace(request.Secret))
            return BadRequest("Secret is required");

        await _secrets.SetSecretAsync(TenantId, provider.Id, request.Secret.Trim(), cancellationToken);
        provider.SecretUpdatedAt = DateTime.UtcNow;
        provider.UpdatedAt = DateTime.UtcNow;
        await _providers.UpdateAsync(provider, cancellationToken);

        _logger.LogInformation("SCIM provider secret set: {ProviderId}", id);
        return Ok(ToResponse(provider));
    }

    [HttpDelete("{id}")]
    [RequirePermission("scim.write")]
    public async Task<IActionResult> DeleteProvider(string id, CancellationToken cancellationToken)
    {
        var provider = await FindAsync(id, cancellationToken);
        if (provider == null)
            return NotFound();

        await _providers.DeleteAsync(id, cancellationToken);
        await _secrets.RemoveAsync(TenantId, id, cancellationToken);

        _logger.LogInformation("SCIM provider deleted: {ProviderId}", id);
        return NoContent();
    }

    /// <summary>
    /// Probes discovery and an authenticated request. Does not change anything.
    /// </summary>
    [HttpPost("{id}/test")]
    [RequirePermission("scim.write")]
    public async Task<IActionResult> TestConnection(string id, CancellationToken cancellationToken)
    {
        var provider = await FindAsync(id, cancellationToken);
        if (provider == null)
            return NotFound();

        var result = await _tester.TestAsync(provider, cancellationToken);
        return Ok(new ScimProviderTestResponse
        {
            Success = result.Success,
            Message = result.Message,
            DiscoveryReachable = result.DiscoveryReachable,
            DiscoveryStatusCode = result.DiscoveryStatusCode,
            AuthenticationOk = result.AuthenticationOk,
            AuthenticationStatusCode = result.AuthenticationStatusCode,
            AuthenticationError = result.AuthenticationError,
            PatchSupported = result.PatchSupported,
            BulkSupported = result.BulkSupported,
            FilterSupported = result.FilterSupported,
            FilterMaxResults = result.FilterMaxResults,
            EtagSupported = result.EtagSupported,
            AuthenticationSchemes = result.AuthenticationSchemes.ToList(),
        });
    }

    /// <summary>
    /// Shows the payload the current mappings produce for one user, without
    /// sending it. Expression and condition errors come back as the error field.
    /// </summary>
    [HttpPost("{id}/preview")]
    public async Task<IActionResult> Preview(string id, [FromBody] ScimProviderPreviewRequest request, CancellationToken cancellationToken)
    {
        var provider = await FindAsync(id, cancellationToken);
        if (provider == null)
            return NotFound();

        if (string.IsNullOrWhiteSpace(request.UserId))
            return BadRequest("userId is required");

        var preview = await _provisioning.PreviewUserAsync(provider, request.UserId.Trim(), cancellationToken);
        return Ok(new ScimProviderPreviewResponse
        {
            UserId = preview.UserId,
            UserName = preview.UserName,
            InScope = preview.InScope,
            Error = preview.Error,
            Payload = preview.Payload,
        });
    }

    /// <summary>
    /// Queues a full pass over every in-scope user and group. The worker
    /// pushes them in the background.
    /// </summary>
    [HttpPost("{id}/reconcile")]
    [RequirePermission("scim.write")]
    public async Task<IActionResult> Reconcile(string id, CancellationToken cancellationToken)
    {
        var provider = await FindAsync(id, cancellationToken);
        if (provider == null)
            return NotFound();

        if (!provider.IsEnabled)
            return BadRequest("Enable the provider before running a reconcile");
        if (provider.SecretUpdatedAt == null)
            return BadRequest("Set the provider secret before running a reconcile");

        var summary = await _reconcile.ReconcileAsync(provider, cancellationToken);
        return Ok(new ScimProviderReconcileResponse
        {
            UsersQueued = summary.UsersQueued,
            UserRemovalsQueued = summary.UserRemovalsQueued,
            GroupsQueued = summary.GroupsQueued,
            GroupRemovalsQueued = summary.GroupRemovalsQueued,
            Summary = summary.ToString(),
        });
    }

    #region Queue

    [HttpGet("{id}/queue")]
    public async Task<IActionResult> GetQueue(string id, [FromQuery] string? status, [FromQuery] int skip = 0, [FromQuery] int take = 50, CancellationToken cancellationToken = default)
    {
        var provider = await FindAsync(id, cancellationToken);
        if (provider == null)
            return NotFound();

        take = Math.Clamp(take, 1, 200);
        var statuses = string.IsNullOrWhiteSpace(status)
            ? null
            : status.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var summary = await _outbox.GetSummaryAsync(id, cancellationToken);
        var items = await _outbox.GetByProviderAsync(id, statuses, skip, take, cancellationToken);
        var total = await _outbox.GetCountByProviderAsync(id, statuses, cancellationToken);

        return Ok(new ScimProviderQueueResponse
        {
            Summary = new ScimProviderQueueSummaryResponse
            {
                Pending = summary.Pending,
                Processing = summary.Processing,
                Failed = summary.Failed,
                Dead = summary.Dead,
                SucceededLast24h = summary.SucceededLast24h,
                OldestPendingAt = summary.OldestPendingAt,
            },
            Total = total,
            Skip = skip,
            Take = take,
            Items = items.Select(i => new ScimProviderQueueItemResponse
            {
                Id = i.Id,
                ResourceType = i.ResourceType,
                InternalId = i.InternalId,
                Operation = i.Operation,
                Status = i.Status,
                Attempts = i.Attempts,
                NextAttemptAt = i.NextAttemptAt,
                LastStatusCode = i.LastStatusCode,
                LastError = i.LastError,
                Reason = i.Reason,
                CreatedAt = i.CreatedAt,
                UpdatedAt = i.UpdatedAt,
                CompletedAt = i.CompletedAt,
            }).ToList(),
        });
    }

    /// <summary>
    /// Puts failed and abandoned items back in the queue, due now.
    /// </summary>
    [HttpPost("{id}/queue/retry")]
    [RequirePermission("scim.write")]
    public async Task<IActionResult> RetryFailed(string id, CancellationToken cancellationToken)
    {
        var provider = await FindAsync(id, cancellationToken);
        if (provider == null)
            return NotFound();

        var count = await _outbox.RetryFailedAsync(id, cancellationToken);
        return Ok(new { Requeued = count });
    }

    #endregion

    #region Logs and resources

    [HttpGet("{id}/logs")]
    public async Task<ActionResult<PagedResult<ScimProviderLogResponse>>> GetLogs(string id, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, [FromQuery] bool? success = null, CancellationToken cancellationToken = default)
    {
        var provider = await FindAsync(id, cancellationToken);
        if (provider == null)
            return NotFound();

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var logs = await _logs.GetByProviderAsync(id, success, (page - 1) * pageSize, pageSize, cancellationToken);
        var total = await _logs.GetCountByProviderAsync(id, success, cancellationToken);

        return Ok(PagedResult<ScimProviderLogResponse>.Create(
            logs.Select(l => new ScimProviderLogResponse
            {
                Id = l.Id,
                Operation = l.Operation,
                ResourceType = l.ResourceType,
                InternalId = l.InternalId,
                RemoteId = l.RemoteId,
                Method = l.Method,
                Path = l.Path,
                StatusCode = l.StatusCode,
                Success = l.Success,
                ErrorMessage = l.ErrorMessage,
                DurationMs = l.DurationMs,
                Timestamp = l.Timestamp,
            }).ToList(),
            total, page, pageSize));
    }

    /// <summary>
    /// What the provider currently holds for this tenant, by our id and its id.
    /// </summary>
    [HttpGet("{id}/resources")]
    public async Task<ActionResult<PagedResult<ScimProviderResourceResponse>>> GetResources(string id, [FromQuery] string? type, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken cancellationToken = default)
    {
        var provider = await FindAsync(id, cancellationToken);
        if (provider == null)
            return NotFound();

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 200);
        var items = await _resourceMappings.GetByProviderAsync(id, type, (page - 1) * pageSize, pageSize, cancellationToken);
        var total = await _resourceMappings.GetCountByProviderAsync(id, type, cancellationToken);

        return Ok(PagedResult<ScimProviderResourceResponse>.Create(
            items.Select(m => new ScimProviderResourceResponse
            {
                Id = m.Id,
                ResourceType = m.ResourceType,
                InternalId = m.InternalId,
                RemoteId = m.RemoteId,
                RemoteVersion = m.RemoteVersion,
                IsDeprovisioned = m.IsDeprovisioned,
                CreatedAt = m.CreatedAt,
                LastSyncedAt = m.LastSyncedAt,
            }).ToList(),
            total, page, pageSize));
    }

    #endregion

    #region Attribute mappings

    [HttpGet("{id}/mappings")]
    public async Task<IActionResult> GetMappings(string id, CancellationToken cancellationToken)
    {
        var provider = await FindAsync(id, cancellationToken);
        if (provider == null)
            return NotFound();

        var mappings = await _attributeMappings.GetByProviderAsync(id, cancellationToken);
        return Ok(mappings.Select(ToResponse));
    }

    [HttpPost("{id}/mappings")]
    [RequirePermission("scim.write")]
    public async Task<IActionResult> CreateMapping(string id, [FromBody] SaveScimProviderAttributeMappingRequest request, CancellationToken cancellationToken)
    {
        var provider = await FindAsync(id, cancellationToken);
        if (provider == null)
            return NotFound();

        var error = ValidateMapping(request);
        if (error != null)
            return BadRequest(error);

        var existing = await _attributeMappings.GetByProviderAsync(id, cancellationToken);
        if (existing.Any(m => string.Equals(m.ScimAttribute, request.ScimAttribute.Trim(), StringComparison.OrdinalIgnoreCase)))
            return Conflict($"A mapping for '{request.ScimAttribute}' already exists");

        var mapping = new ScimProviderAttributeMapping
        {
            TenantId = TenantId,
            ScimProviderId = id,
            ScimAttribute = request.ScimAttribute.Trim(),
            InternalProperty = request.InternalProperty?.Trim() ?? string.Empty,
            Expression = Blank(request.Expression),
            Condition = Blank(request.Condition),
            Transformation = NormalizeTransformation(request.Transformation),
            DefaultValue = string.IsNullOrWhiteSpace(request.DefaultValue) ? null : request.DefaultValue,
            IsEnabled = request.IsEnabled,
        };

        await _attributeMappings.CreateAsync(mapping, cancellationToken);
        return Ok(ToResponse(mapping));
    }

    [HttpPut("{id}/mappings/{mappingId}")]
    [RequirePermission("scim.write")]
    public async Task<IActionResult> UpdateMapping(string id, string mappingId, [FromBody] SaveScimProviderAttributeMappingRequest request, CancellationToken cancellationToken)
    {
        var provider = await FindAsync(id, cancellationToken);
        if (provider == null)
            return NotFound();

        var mapping = await _attributeMappings.GetByIdAsync(mappingId, cancellationToken);
        if (mapping == null || mapping.ScimProviderId != id)
            return NotFound();

        var error = ValidateMapping(request);
        if (error != null)
            return BadRequest(error);

        var existing = await _attributeMappings.GetByProviderAsync(id, cancellationToken);
        if (existing.Any(m => m.Id != mappingId && string.Equals(m.ScimAttribute, request.ScimAttribute.Trim(), StringComparison.OrdinalIgnoreCase)))
            return Conflict($"A mapping for '{request.ScimAttribute}' already exists");

        mapping.ScimAttribute = request.ScimAttribute.Trim();
        mapping.InternalProperty = request.InternalProperty?.Trim() ?? string.Empty;
        mapping.Expression = Blank(request.Expression);
        mapping.Condition = Blank(request.Condition);
        mapping.Transformation = NormalizeTransformation(request.Transformation);
        mapping.DefaultValue = string.IsNullOrWhiteSpace(request.DefaultValue) ? null : request.DefaultValue;
        mapping.IsEnabled = request.IsEnabled;
        mapping.UpdatedAt = DateTime.UtcNow;

        await _attributeMappings.UpdateAsync(mapping, cancellationToken);
        return Ok(ToResponse(mapping));
    }

    [HttpDelete("{id}/mappings/{mappingId}")]
    [RequirePermission("scim.write")]
    public async Task<IActionResult> DeleteMapping(string id, string mappingId, CancellationToken cancellationToken)
    {
        var provider = await FindAsync(id, cancellationToken);
        if (provider == null)
            return NotFound();

        var mapping = await _attributeMappings.GetByIdAsync(mappingId, cancellationToken);
        if (mapping == null || mapping.ScimProviderId != id)
            return NotFound();

        await _attributeMappings.DeleteAsync(mappingId, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Materialises the flavor's default mappings as editable rows, skipping
    /// attributes that already have one.
    /// </summary>
    [HttpPost("{id}/mappings/defaults")]
    [RequirePermission("scim.write")]
    public async Task<IActionResult> ApplyDefaultMappings(string id, CancellationToken cancellationToken)
    {
        var provider = await FindAsync(id, cancellationToken);
        if (provider == null)
            return NotFound();

        var flavor = _flavors.Get(provider.Flavor);
        var existing = await _attributeMappings.GetByProviderAsync(id, cancellationToken);
        var taken = existing.Select(m => m.ScimAttribute).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var created = new List<ScimProviderAttributeMapping>();
        foreach (var def in flavor.DefaultUserMappings)
        {
            if (taken.Contains(def.ScimAttribute))
                continue;

            var mapping = new ScimProviderAttributeMapping
            {
                TenantId = TenantId,
                ScimProviderId = id,
                ScimAttribute = def.ScimAttribute,
                InternalProperty = def.InternalProperty,
                IsEnabled = true,
            };
            await _attributeMappings.CreateAsync(mapping, cancellationToken);
            created.Add(mapping);
        }

        return Ok(created.Select(ToResponse));
    }

    #endregion

    private async Task<ScimProvider?> FindAsync(string id, CancellationToken cancellationToken)
    {
        var provider = await _providers.GetByIdAsync(id, cancellationToken);
        return provider == null || provider.TenantId != TenantId ? null : provider;
    }

    /// <summary>
    /// Validates and copies form fields onto the entity. Returns an error
    /// message, or null when the entity was updated.
    /// </summary>
    private async Task<string?> ApplyAsync(
        ScimProvider provider,
        string name,
        string? description,
        bool isEnabled,
        string flavorKey,
        string baseUrl,
        string authType,
        string? oauthTokenEndpoint,
        string? oauthClientId,
        string? oauthScope,
        bool syncUsers,
        bool syncGroups,
        string? groupSource,
        List<string>? scopeRoleIds,
        List<string>? scopeTeamIds,
        string? userNameSource,
        string? userNameExpression,
        string deprovisionMode,
        int reconcileIntervalMinutes,
        string? linkedScimClientId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Name is required";

        var siblings = await _providers.GetByTenantAsync(TenantId, cancellationToken);
        if (siblings.Any(p => p.Id != provider.Id && string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)))
            return $"A provider named '{name.Trim()}' already exists";

        if (!_flavors.IsKnown(flavorKey))
            return $"Unknown flavor '{flavorKey}'";
        var flavor = _flavors.Get(flavorKey);

        if (!Uri.TryCreate(baseUrl?.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            return "Base URL must be an absolute http(s) URL, e.g. https://mail.example.org/scim/v2";

        var normalizedAuth = authType?.Trim().ToLowerInvariant();
        if (normalizedAuth != ScimProviderAuthTypes.Bearer && normalizedAuth != ScimProviderAuthTypes.OAuthClientCredentials)
            return $"Unknown auth type '{authType}'";

        if (normalizedAuth == ScimProviderAuthTypes.OAuthClientCredentials)
        {
            if (!Uri.TryCreate(oauthTokenEndpoint?.Trim(), UriKind.Absolute, out _))
                return "OAuth token endpoint is required for client credentials";
            if (string.IsNullOrWhiteSpace(oauthClientId))
                return "OAuth client id is required for client credentials";
        }

        var normalizedSource = string.IsNullOrWhiteSpace(userNameSource) ? flavor.DefaultUserNameSource : userNameSource.Trim();
        normalizedSource = ScimProviderUserNameSources.All.FirstOrDefault(s => string.Equals(s, normalizedSource, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException();
        var expression = Blank(userNameExpression);
        if (normalizedSource == ScimProviderUserNameSources.Expression)
        {
            if (expression == null)
                return "A userName expression is required when the userName source is 'expression'";
            var parseError = ScimExpressionEngine.ValidateTemplate(expression);
            if (parseError != null)
                return $"userName expression does not parse: {parseError}";
        }
        else if (flavor.RequiresEmailUserName)
        {
            normalizedSource = ScimProviderUserNameSources.Email;
        }

        var normalizedDeprovision = deprovisionMode?.Trim().ToLowerInvariant();
        if (normalizedDeprovision != ScimProviderDeprovisionModes.Deactivate && normalizedDeprovision != ScimProviderDeprovisionModes.Delete)
            return $"Unknown deprovision mode '{deprovisionMode}'";

        if (reconcileIntervalMinutes < 0)
            return "Reconcile interval cannot be negative";

        if (syncGroups && !flavor.SupportsGroups)
            return $"{flavor.DisplayName} does not support groups";

        var normalizedGroupSource = string.IsNullOrWhiteSpace(groupSource) ? ScimProviderGroupSources.Roles : groupSource.Trim().ToLowerInvariant();
        if (!ScimProviderGroupSources.All.Contains(normalizedGroupSource))
            return $"Unknown group source '{groupSource}'. Use roles, teams or both";

        var roleScope = NormalizeIds(scopeRoleIds);
        if (roleScope.Count > 0)
        {
            var roles = await _roles.GetByIdsAsync(roleScope, cancellationToken);
            var missing = roleScope.Where(id => roles.All(r => r.Id != id || !string.Equals(r.TenantId, TenantId, StringComparison.Ordinal))).ToList();
            if (missing.Count > 0)
                return $"Scope role(s) not found in this tenant: {string.Join(", ", missing)}";
        }

        var teamScope = NormalizeIds(scopeTeamIds);
        foreach (var teamId in teamScope)
        {
            var team = await _teams.GetByIdAsync(TenantId, teamId, cancellationToken);
            if (team == null)
                return $"Scope team not found in this tenant: {teamId}";
        }

        var linkedClientId = Blank(linkedScimClientId);
        if (linkedClientId != null)
        {
            var client = await _clients.GetByIdAsync(linkedClientId, cancellationToken);
            if (client == null || !string.Equals(client.TenantId, TenantId, StringComparison.Ordinal))
                return $"Inbound SCIM client not found in this tenant: {linkedClientId}";
        }

        provider.Name = name.Trim();
        provider.Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        provider.IsEnabled = isEnabled;
        provider.Flavor = flavor.Key;
        provider.BaseUrl = uri.ToString().TrimEnd('/');
        provider.AuthType = normalizedAuth;
        provider.OAuthTokenEndpoint = normalizedAuth == ScimProviderAuthTypes.OAuthClientCredentials ? oauthTokenEndpoint!.Trim() : null;
        provider.OAuthClientId = normalizedAuth == ScimProviderAuthTypes.OAuthClientCredentials ? oauthClientId!.Trim() : null;
        provider.OAuthScope = normalizedAuth == ScimProviderAuthTypes.OAuthClientCredentials && !string.IsNullOrWhiteSpace(oauthScope) ? oauthScope.Trim() : null;
        provider.SyncUsers = syncUsers;
        provider.SyncGroups = syncGroups;
        provider.GroupSource = normalizedGroupSource;
        provider.ScopeRoleIds = roleScope.Count == 0 ? null : string.Join(",", roleScope);
        provider.ScopeTeamIds = teamScope.Count == 0 ? null : string.Join(",", teamScope);
        provider.UserNameSource = normalizedSource;
        provider.UserNameExpression = normalizedSource == ScimProviderUserNameSources.Expression ? expression : null;
        provider.DeprovisionMode = normalizedDeprovision;
        provider.ReconcileIntervalMinutes = reconcileIntervalMinutes;
        provider.LinkedScimClientId = linkedClientId;
        return null;
    }

    private static List<string> NormalizeIds(List<string>? ids) =>
        (ids ?? new List<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static string? ValidateMapping(SaveScimProviderAttributeMappingRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ScimAttribute))
            return "SCIM attribute is required";

        var expression = Blank(request.Expression);
        var property = request.InternalProperty?.Trim();
        if (expression == null && string.IsNullOrEmpty(property))
            return "Either an internal property or an expression is required";

        if (!string.IsNullOrEmpty(property))
        {
            var known = ScimOutboundProperties.All.Any(p => string.Equals(p, property, StringComparison.OrdinalIgnoreCase))
                        || property.StartsWith(ScimOutboundProperties.CustomPrefix, StringComparison.OrdinalIgnoreCase);
            if (!known)
                return $"Unknown internal property '{property}'. Use one of {string.Join(", ", ScimOutboundProperties.All)} or custom:<key>";
        }

        if (expression != null)
        {
            var parseError = ScimExpressionEngine.ValidateTemplate(expression);
            if (parseError != null)
                return $"Expression does not parse: {parseError}";
        }

        var condition = Blank(request.Condition);
        if (condition != null)
        {
            var parseError = ScimExpressionEngine.ValidateExpression(condition);
            if (parseError != null)
                return $"Condition does not parse: {parseError}";
        }

        var transformation = NormalizeTransformation(request.Transformation);
        if (transformation != null && !ScimOutboundTransformations.All.Contains(transformation))
            return $"Unknown transformation '{request.Transformation}'";

        return null;
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? NormalizeTransformation(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return string.IsNullOrEmpty(normalized) || normalized == ScimOutboundTransformations.None ? null : normalized;
    }

    private static ScimProviderResponse ToResponse(ScimProvider p) => new()
    {
        Id = p.Id,
        Name = p.Name,
        Description = p.Description,
        IsEnabled = p.IsEnabled,
        Flavor = p.Flavor,
        BaseUrl = p.BaseUrl,
        AuthType = p.AuthType,
        OAuthTokenEndpoint = p.OAuthTokenEndpoint,
        OAuthClientId = p.OAuthClientId,
        OAuthScope = p.OAuthScope,
        HasSecret = p.SecretUpdatedAt != null,
        SecretUpdatedAt = p.SecretUpdatedAt,
        SyncUsers = p.SyncUsers,
        SyncGroups = p.SyncGroups,
        GroupSource = p.GroupSource,
        ScopeRoleIds = p.GetScopeRoleIds().ToList(),
        ScopeTeamIds = p.GetScopeTeamIds().ToList(),
        UserNameSource = p.UserNameSource,
        UserNameExpression = p.UserNameExpression,
        DeprovisionMode = p.DeprovisionMode,
        ReconcileIntervalMinutes = p.ReconcileIntervalMinutes,
        LinkedScimClientId = p.LinkedScimClientId,
        CreatedAt = p.CreatedAt,
        UpdatedAt = p.UpdatedAt,
        LastReconcileAt = p.LastReconcileAt,
        LastReconcileSummary = p.LastReconcileSummary,
        LastSuccessAt = p.LastSuccessAt,
        LastErrorAt = p.LastErrorAt,
        LastError = p.LastError,
        SuccessCount = p.SuccessCount,
        ErrorCount = p.ErrorCount,
    };

    private static ScimProviderAttributeMappingResponse ToResponse(ScimProviderAttributeMapping m) => new()
    {
        Id = m.Id,
        ScimProviderId = m.ScimProviderId,
        ScimAttribute = m.ScimAttribute,
        InternalProperty = m.InternalProperty,
        Expression = m.Expression,
        Condition = m.Condition,
        Transformation = m.Transformation,
        DefaultValue = m.DefaultValue,
        IsEnabled = m.IsEnabled,
        CreatedAt = m.CreatedAt,
        UpdatedAt = m.UpdatedAt,
    };

    private static ScimProviderFlavorResponse ToResponse(ScimProviderFlavorProfile f) => new()
    {
        Key = f.Key,
        DisplayName = f.DisplayName,
        Description = f.Description,
        Notes = f.Notes,
        DefaultUserNameSource = f.DefaultUserNameSource,
        RequiresEmailUserName = f.RequiresEmailUserName,
        SupportsPhoneNumbers = f.SupportsPhoneNumbers,
        SupportsEnterpriseExtension = f.SupportsEnterpriseExtension,
        SupportsGroups = f.SupportsGroups,
        DefaultUserMappings = f.DefaultUserMappings
            .Select(m => new ScimProviderDefaultMappingResponse { ScimAttribute = m.ScimAttribute, InternalProperty = m.InternalProperty })
            .ToList(),
    };
}

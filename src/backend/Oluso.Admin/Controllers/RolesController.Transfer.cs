using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oluso.Admin.Authorization;
using Oluso.Admin.Dto.Roles;
using Oluso.Core.Authorization;
using Oluso.Core.Domain.Entities;

namespace Oluso.Admin.Controllers;

/// <summary>
/// Moving a tenant's own roles between environments: export writes their permissions, entitlements and other claims
/// (and the entitlements' definitions) to a file, and import previews then applies that file in the current tenant.
/// Applying goes through <see cref="CreateRole"/> and <see cref="UpdateRole"/>, so an imported role passes the same
/// checks and raises the same events as one saved by hand. Holders are never part of the file.
/// </summary>
public partial class RolesController
{
    private static readonly JsonSerializerOptions TransferJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    [HttpPost("export")]
    [RequirePermission(AdminPermissions.RolesRead)]
    public async Task<IActionResult> ExportRoles([FromBody] ExportRolesRequest request, CancellationToken cancellationToken)
    {
        var ids = request.RoleIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.Ordinal).ToList();
        if (ids.Count == 0) return BadRequest(new { error = "Choose at least one role to export." });

        var roles = await _roleStore.GetByIdsAsync(ids, cancellationToken);
        var exportable = roles.Where(IsTransferable).ToList();
        var notExportable = ids.Except(exportable.Select(r => r.Id), StringComparer.Ordinal).ToList();
        if (notExportable.Count > 0)
        {
            return BadRequest(new
            {
                error = "Only this organization's own roles can be exported. Platform, system and module roles are set up by each environment."
            });
        }

        var document = new RoleExportDocument { ExportedAt = DateTime.UtcNow };
        var entitlementNames = new HashSet<string>(StringComparer.Ordinal);

        foreach (var role in exportable.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase))
        {
            var claims = (await _roleStore.GetRoleClaimsAsync(role.Id, cancellationToken)).ToList();
            var item = new RoleExportItem
            {
                Name = role.Name!,
                DisplayName = role.DisplayName,
                Description = role.Description,
                Permissions = claims.Where(c => c.Type == OlusoRole.PermissionClaimType).Select(c => c.Value).Order(StringComparer.Ordinal).ToList(),
                Entitlements = claims.Where(c => c.Type == OlusoRole.EntitlementClaimType).Select(c => c.Value).Order(StringComparer.Ordinal).ToList(),
                Claims = claims
                    .Where(c => c.Type != OlusoRole.PermissionClaimType && c.Type != OlusoRole.EntitlementClaimType)
                    .Select(c => new RoleClaimDto { Type = c.Type, Value = c.Value })
                    .ToList(),
            };
            entitlementNames.UnionWith(item.Entitlements);
            document.Roles.Add(item);
        }

        var definitions = await _entitlementStore.FindByNamesAsync(TenantId, entitlementNames.ToList(), cancellationToken);
        document.Entitlements = definitions
            .OrderBy(e => e.Name, StringComparer.Ordinal)
            .Select(e => new EntitlementExportItem { Name = e.Name, DisplayName = e.DisplayName, Description = e.Description, Group = e.Group })
            .ToList();

        _logger.LogInformation("Exported {Count} roles from tenant {TenantId}", document.Roles.Count, TenantId);

        var fileName = exportable.Count == 1
            ? $"role-{FileSafe(exportable[0].Name!)}.json"
            : $"roles-{DateTime.UtcNow:yyyy-MM-dd}.json";
        return File(JsonSerializer.SerializeToUtf8Bytes(document, TransferJson), "application/json", fileName);
    }

    /// <summary>What importing the file would do in this tenant. Writes nothing.</summary>
    [HttpPost("import/preview")]
    [RequirePermission(AdminPermissions.RolesWrite)]
    public async Task<ActionResult<RoleImportResultDto>> PreviewImport(
        [FromBody] ImportRolesRequest request, [FromServices] IAuthorizationService authorization, CancellationToken cancellationToken)
    {
        var (result, _) = await PlanImportAsync(request, authorization, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Creates the missing entitlements, then creates or updates each role the preview marks create or update.
    /// </summary>
    [HttpPost("import")]
    [RequirePermission(AdminPermissions.RolesWrite)]
    public async Task<ActionResult<RoleImportResultDto>> ImportRoles(
        [FromBody] ImportRolesRequest request, [FromServices] IAuthorizationService authorization, CancellationToken cancellationToken)
    {
        var (result, plan) = await PlanImportAsync(request, authorization, cancellationToken);
        if (!result.CanImport) return BadRequest(result);

        foreach (var name in result.EntitlementsToCreate)
        {
            var definition = plan!.Entitlements.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.Ordinal));
            await _entitlementStore.CreateAsync(new Entitlement
            {
                Name = name,
                DisplayName = string.IsNullOrWhiteSpace(definition?.DisplayName) ? name : definition.DisplayName.Trim(),
                Description = definition?.Description?.Trim(),
                Group = string.IsNullOrWhiteSpace(definition?.Group) ? null : definition.Group.Trim(),
                TenantId = TenantId,
                CreatedBy = AdminUserId,
            }, cancellationToken);
        }

        if (result.EntitlementsToCreate.Count > 0)
        {
            await _entitlementScopeProvisioner.EnsureAsync(cancellationToken);
        }

        foreach (var item in result.Roles.Where(r => r.Action is "create" or "update"))
        {
            var source = plan!.Roles[item.Name];
            var outcome = item.Action == "create"
                ? (await CreateRole(new CreateRoleRequest
                {
                    Name = source.Name,
                    DisplayName = source.DisplayName,
                    Description = source.Description,
                    Permissions = source.Permissions,
                    Entitlements = source.Entitlements,
                    Claims = source.Claims,
                }, cancellationToken)).Result
                : (await UpdateRole(source.ExistingId!, new RoleDetailDto
                {
                    Name = source.Name,
                    DisplayName = source.DisplayName ?? source.Name,
                    Description = source.Description,
                    Permissions = source.Permissions,
                    Entitlements = source.Entitlements,
                    Claims = source.Claims,
                }, cancellationToken)).Result;

            if (outcome is ObjectResult { StatusCode: >= 400 } failed)
            {
                item.Error = ErrorText(failed.Value);
            }
            else if (outcome is StatusCodeResult { StatusCode: >= 400 } status)
            {
                item.Error = $"Refused ({status.StatusCode}).";
            }
        }

        result.Applied = true;
        _logger.LogInformation(
            "Imported roles into tenant {TenantId}: {Created} created, {Updated} updated, {Entitlements} entitlements created",
            TenantId, result.Roles.Count(r => r.Action == "create" && r.Error == null),
            result.Roles.Count(r => r.Action == "update" && r.Error == null), result.EntitlementsToCreate.Count);
        return Ok(result);
    }

    private sealed record PlannedRole(
        string Name, string? DisplayName, string? Description, string? ExistingId,
        List<string> Permissions, List<string> Entitlements, List<RoleClaimDto> Claims);

    private sealed record ImportPlan(Dictionary<string, PlannedRole> Roles, List<EntitlementExportItem> Entitlements);

    private async Task<(RoleImportResultDto Result, ImportPlan? Plan)> PlanImportAsync(
        ImportRolesRequest request, IAuthorizationService authorization, CancellationToken cancellationToken)
    {
        var result = new RoleImportResultDto();

        if (!TryReadDocument(request.Content, out var document, out var readError))
        {
            result.Error = readError;
            return (result, null);
        }

        var planned = new Dictionary<string, PlannedRole>(StringComparer.OrdinalIgnoreCase);
        var neededEntitlements = new HashSet<string>(StringComparer.Ordinal);

        foreach (var role in document!.Roles)
        {
            var name = role.Name?.Trim() ?? string.Empty;
            var item = new RoleImportItemDto { Name = name };
            result.Roles.Add(item);

            var invalid = await InvalidReasonAsync(name, planned, cancellationToken);
            var existing = invalid == null ? await _roleStore.GetByNameAsync(name, TenantId, cancellationToken) : null;
            if (invalid == null && existing != null && (existing.IsSystemRole || IsManagedByModule(existing)))
            {
                invalid = "A system or module role here has this name.";
            }

            if (invalid != null)
            {
                item.Action = "invalid";
                item.Reason = invalid;
                continue;
            }

            if (existing != null && !request.ReplaceExisting)
            {
                item.Action = "skip";
                item.Reason = "A role with this name already exists.";
                planned[name] = new PlannedRole(name, null, null, existing.Id, new(), new(), new());
                continue;
            }

            item.Action = existing == null ? "create" : "update";

            var permissions = new List<string>();
            foreach (var permission in role.Permissions.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()).Distinct(StringComparer.Ordinal))
            {
                if (ValidatePermissions(new List<string> { permission }) == null) permissions.Add(permission);
                else item.DroppedPermissions.Add(permission);
            }

            var claims = new List<RoleClaimDto>();
            foreach (var claim in role.Claims.Where(c => !string.IsNullOrWhiteSpace(c.Type)))
            {
                var isProtected = ReservedClaimTypes.GetProtectedClaimTypes(new[] { (claim.Type, claim.Value) }).Any()
                    || ValidateNoRawEntitlementClaims(new[] { claim }) != null;
                if (isProtected)
                {
                    if (!item.DroppedClaims.Contains(claim.Type)) item.DroppedClaims.Add(claim.Type);
                }
                else
                {
                    claims.Add(new RoleClaimDto { Type = claim.Type, Value = claim.Value ?? string.Empty });
                }
            }

            var entitlements = new List<string>();
            foreach (var entitlement in role.Entitlements.Select(e => e?.Trim() ?? string.Empty).Distinct(StringComparer.Ordinal))
            {
                if (EntitlementsController.ValidateName(entitlement) == null) entitlements.Add(entitlement);
                else if (entitlement.Length > 0) item.DroppedEntitlements.Add(entitlement);
            }
            neededEntitlements.UnionWith(entitlements);

            planned[name] = new PlannedRole(
                name, role.DisplayName?.Trim(), role.Description?.Trim(), existing?.Id, permissions, entitlements, claims);
        }

        if (neededEntitlements.Count > 0)
        {
            var known = (await _entitlementStore.FindByNamesAsync(TenantId, neededEntitlements.ToList(), cancellationToken))
                .Select(e => e.Name)
                .ToHashSet(StringComparer.Ordinal);
            result.EntitlementsToCreate = neededEntitlements.Where(n => !known.Contains(n)).Order(StringComparer.Ordinal).ToList();
        }

        if (result.EntitlementsToCreate.Count > 0
            && !(await authorization.AuthorizeAsync(User, PermissionPolicyConstants.PolicyPrefix + AdminPermissions.EntitlementsWrite)).Succeeded)
        {
            result.Error = $"The file uses entitlements this organization doesn't have ({string.Join(", ", result.EntitlementsToCreate)}), " +
                           "and creating them needs permission to manage entitlements.";
            return (result, null);
        }

        if (!result.Roles.Any(r => r.Action is "create" or "update"))
        {
            result.Error = result.Roles.Count == 0
                ? "The file has no roles."
                : "Nothing to import: every role already exists or can't be imported. Turn on replacing existing roles to update them.";
            return (result, null);
        }

        result.CanImport = true;
        return (result, new ImportPlan(planned, document.Entitlements));
    }

    private async Task<string?> InvalidReasonAsync(string name, IReadOnlyDictionary<string, PlannedRole> planned, CancellationToken cancellationToken)
    {
        if (name.Length == 0) return "The role has no name.";
        if (planned.ContainsKey(name)) return "The file has this role twice.";
        if (ReservedClaimTypes.IsReservedRoleName(name)) return "This name is reserved for system use.";
        if (await _roleStore.GetByNameAsync(name, null, cancellationToken) != null) return "A platform role has this name.";
        return null;
    }

    /// <summary>A tenant's own role that an administrator manages on the Roles page.</summary>
    private bool IsTransferable(OlusoRole role) =>
        role.TenantId == TenantId && !role.IsSystemRole && !IsManagedByModule(role);

    private static bool TryReadDocument(string content, out RoleExportDocument? document, out string? error)
    {
        document = null;
        error = null;

        if (string.IsNullOrWhiteSpace(content))
        {
            error = "The file is empty.";
            return false;
        }

        try
        {
            document = JsonSerializer.Deserialize<RoleExportDocument>(content, TransferJson);
        }
        catch (JsonException)
        {
            error = "This is not a roles export file.";
            return false;
        }

        if (document == null || document.Format != RoleExportDocument.CurrentFormat)
        {
            error = "This is not a roles export file.";
            document = null;
            return false;
        }

        if (document.Version > RoleExportDocument.CurrentVersion)
        {
            error = $"These roles were exported by a newer version (format {document.Version}). Upgrade this environment before importing.";
            document = null;
            return false;
        }

        document.Roles ??= new();
        document.Entitlements ??= new();
        foreach (var role in document.Roles)
        {
            role.Permissions ??= new();
            role.Entitlements ??= new();
            role.Claims ??= new();
        }
        return true;
    }

    private static string ErrorText(object? value)
    {
        if (value == null) return "Refused.";
        var json = JsonSerializer.SerializeToElement(value);
        return json.ValueKind == JsonValueKind.Object && json.TryGetProperty("error", out var error)
            ? error.GetString() ?? "Refused."
            : value.ToString() ?? "Refused.";
    }

    private static string FileSafe(string name)
    {
        var builder = new StringBuilder();
        foreach (var c in name.ToLowerInvariant())
        {
            builder.Append(char.IsLetterOrDigit(c) ? c : '-');
        }
        return builder.ToString().Trim('-');
    }
}

using Microsoft.AspNetCore.Mvc;
using Oluso.Core.Api;
using Oluso.Core.Blueprints;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Licensing;
using Oluso.Admin.Authorization;

namespace Oluso.Admin.Controllers;

/// <summary>
/// The Blueprints (starter-pack) backend — one central, module-agnostic place
/// for a tenant administrator to browse and apply the starter packs their
/// licensed modules ship (note templates, order sets, automation workflows,
/// …) instead of hand-assembling that configuration. Depends only on the Core
/// <see cref="IBlueprintContributor"/> contract, so it has no knowledge of any
/// specific module — the same pattern as <see cref="TerminologyController"/>.
///
/// <para>Application receipts (which pack was applied, when, by whom) are
/// recorded per tenant in the <see cref="BlueprintConstants.ReceiptsSection"/>
/// configuration section via <see cref="ITenantConfigurationService"/>.</para>
/// </summary>
[Route("api/admin/blueprints")]
public class BlueprintsController : AdminBaseController
{
    private readonly IEnumerable<IBlueprintContributor> _contributors;
    private readonly IFeatureGate _featureGate;
    private readonly ITenantConfigurationService _tenantConfiguration;

    public BlueprintsController(
        ITenantContext tenantContext,
        IEnumerable<IBlueprintContributor> contributors,
        IFeatureGate featureGate,
        ITenantConfigurationService tenantConfiguration)
        : base(tenantContext)
    {
        _contributors = contributors;
        _featureGate = featureGate;
        _tenantConfiguration = tenantConfiguration;
    }

    /// <summary>
    /// The blueprints available to this tenant — filtered to licensed modules
    /// (defence in depth, mirroring the Terminology Center's authoring grid) —
    /// with the application receipt for any pack applied before.
    /// </summary>
    [HttpGet]
    [RequirePermission(AdminPermissions.SettingsRead)]
    public async Task<ActionResult<IReadOnlyList<BlueprintListEntry>>> List(
        CancellationToken cancellationToken = default)
    {
        var receipts = await GetReceiptsAsync(cancellationToken);
        var result = new List<BlueprintListEntry>();

        foreach (var contributor in _contributors)
        {
            // Defence in depth: only surface packs for modules the tenant is
            // licensed for and the caller has a seat for.
            var gate = await _featureGate.CheckFeatureAsync(
                contributor.ModuleKey, AdminUserId, cancellationToken);
            if (!gate.IsAllowed)
                continue;

            foreach (var descriptor in contributor.GetBlueprints())
            {
                receipts.Applied.TryGetValue(descriptor.Key, out var receipt);
                result.Add(new BlueprintListEntry
                {
                    Key = descriptor.Key,
                    ModuleKey = descriptor.ModuleKey,
                    Title = descriptor.Title,
                    Description = descriptor.Description,
                    ItemCounts = descriptor.ItemCounts,
                    AppliedAt = receipt?.AppliedAtUtc,
                    AppliedBy = receipt?.AppliedBy,
                });
            }
        }

        return Ok(result);
    }

    /// <summary>
    /// Apply one blueprint to this tenant. Idempotent — existing items
    /// (matched by name) are skipped unless <c>overwrite</c> is set. Seeded
    /// automation workflows land disabled (draft) for admin review. Records an
    /// application receipt and returns the created/skipped item lists.
    /// </summary>
    [HttpPost("{key}/apply")]
    [RequirePermission(AdminPermissions.SettingsWrite)]
    public async Task<ActionResult<BlueprintApplyResult>> Apply(
        string key,
        [FromBody] ApplyBlueprintRequest? request,
        CancellationToken cancellationToken = default)
    {
        // The write is gated the same way the list is: the key must belong to
        // a registered contributor AND its owning module must pass the
        // caller's feature/seat gate (fail closed — mirror the terminology
        // SetOverrideAsync gating). Unknown and unlicensed keys are
        // indistinguishable to the caller.
        var owner = _contributors.FirstOrDefault(
            c => c.GetBlueprints().Any(b => b.Key == key));
        if (owner is null)
            return NotFound(new { Message = $"Unknown blueprint '{key}'." });

        var gate = await _featureGate.CheckFeatureAsync(
            owner.ModuleKey, AdminUserId, cancellationToken);
        if (!gate.IsAllowed)
            return NotFound(new { Message = $"Unknown blueprint '{key}'." });

        var options = new BlueprintApplyOptions { Overwrite = request?.Overwrite ?? false };
        var result = await owner.ApplyAsync(
            key, TenantId, options, HttpContext.RequestServices, cancellationToken);

        // Record the application receipt.
        var receipts = await GetReceiptsAsync(cancellationToken);
        receipts.Applied[key] = new BlueprintApplicationReceipt
        {
            Key = key,
            AppliedAtUtc = DateTime.UtcNow,
            AppliedBy = RequireAdminUserId,
            Overwrite = options.Overwrite,
            CreatedCount = result.Created.Count,
            SkippedCount = result.Skipped.Count,
            CreatedByKind = result.Created
                .GroupBy(i => i.Kind)
                .ToDictionary(g => g.Key, g => g.Count()),
        };
        await _tenantConfiguration.SetSectionAsync(
            TenantId, BlueprintConstants.ReceiptsSection, receipts, cancellationToken);

        return Ok(result);
    }

    private async Task<BlueprintReceiptsSection> GetReceiptsAsync(CancellationToken cancellationToken) =>
        await _tenantConfiguration.GetSectionAsync<BlueprintReceiptsSection>(
            TenantId, BlueprintConstants.ReceiptsSection, cancellationToken)
        ?? new BlueprintReceiptsSection();
}

/// <summary>Request body for applying a blueprint.</summary>
public sealed class ApplyBlueprintRequest
{
    /// <summary>Replace existing same-named items instead of skipping them.</summary>
    public bool Overwrite { get; set; }
}

/// <summary>One row of the tenant's blueprint list.</summary>
public sealed class BlueprintListEntry
{
    public required string Key { get; init; }
    public required string ModuleKey { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public IReadOnlyDictionary<string, int> ItemCounts { get; init; } = new Dictionary<string, int>();

    /// <summary>When this blueprint was last applied for the tenant (null = never).</summary>
    public DateTime? AppliedAt { get; init; }

    /// <summary>Admin user id that last applied it.</summary>
    public string? AppliedBy { get; init; }
}

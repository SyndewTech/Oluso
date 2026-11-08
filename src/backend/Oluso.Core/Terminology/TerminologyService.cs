using Oluso.Core.Events;
using Oluso.Core.Licensing;

namespace Oluso.Core.Terminology;

/// <summary>
/// Default <see cref="ITerminologyService"/> implementation — the platform's
/// single terminology engine. Owns the override storage and resolves the
/// cascade (generic default ◁ industry pack ◁ tenant override) across every
/// registered <see cref="ITerminologyContributor"/>. The tenant's
/// industry/type is read from the central <see cref="ITenantProfileService"/>
/// only — there is no per-module fallback, so nothing leaks from a specific
/// module into the resolved vocabulary.
///
/// <para>Lives in the platform (not inside any vertical): the engine, the
/// override table, the update event and the realtime channel all work even in
/// a deployment with no Facilities module. Modules contribute keys and
/// bundles; the platform merges, persists overrides, and serves.</para>
/// </summary>
public sealed class TerminologyService : ITerminologyService
{
    private readonly IEnumerable<ITerminologyContributor> _contributors;
    private readonly ITerminologyOverrideStore _overrideStore;
    private readonly ITenantProfileService _tenantProfile;
    private readonly IFeatureGate _featureGate;
    private readonly IOlusoEventService _eventService;

    public TerminologyService(
        IEnumerable<ITerminologyContributor> contributors,
        ITerminologyOverrideStore overrideStore,
        ITenantProfileService tenantProfile,
        IFeatureGate featureGate,
        IOlusoEventService eventService)
    {
        _contributors = contributors;
        _overrideStore = overrideStore;
        _tenantProfile = tenantProfile;
        _featureGate = featureGate;
        _eventService = eventService;
    }

    public async Task<IReadOnlyDictionary<string, string>> ResolveAsync(
        string tenantId, string locale, CancellationToken cancellationToken = default)
    {
        var industry = await _tenantProfile.GetIndustryAsync(tenantId, cancellationToken);

        var map = new Dictionary<string, string>();
        foreach (var contributor in _contributors)
        {
            foreach (var (key, value) in contributor.GetBundle(industry, locale))
                map[key] = value;
        }

        var overrides = await _overrideStore.GetForTenantAsync(tenantId, locale, cancellationToken);
        foreach (var o in overrides)
            map[o.Key] = o.Value;

        return map;
    }

    public async Task<IReadOnlyList<ResolvedTerminologyKey>> GetAuthoringKeysAsync(
        string tenantId, string locale, string? userId, CancellationToken cancellationToken = default)
    {
        var industry = await _tenantProfile.GetIndustryAsync(tenantId, cancellationToken);
        var overrides = (await _overrideStore.GetForTenantAsync(tenantId, locale, cancellationToken))
            .ToDictionary(o => o.Key, o => o.Value);

        var result = new List<ResolvedTerminologyKey>();
        foreach (var contributor in _contributors)
        {
            // Defence in depth: only surface keys for modules the tenant is
            // licensed for and the caller has a seat for.
            var gate = await _featureGate.CheckFeatureAsync(contributor.ModuleKey, userId, cancellationToken);
            if (!gate.IsAllowed)
                continue;

            var inherited = contributor.GetBundle(industry, locale);
            foreach (var descriptor in contributor.GetKeys())
            {
                inherited.TryGetValue(descriptor.Key, out var inheritedValue);
                overrides.TryGetValue(descriptor.Key, out var overrideValue);

                result.Add(new ResolvedTerminologyKey(
                    contributor.ModuleKey,
                    descriptor.Key,
                    descriptor.Group,
                    descriptor.Description,
                    descriptor.IsOverridable,
                    descriptor.SupportsPlural,
                    inheritedValue ?? string.Empty,
                    overrideValue));
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<TerminologyOverrideEntry>> GetOverridesAsync(
        string tenantId, string locale, CancellationToken cancellationToken = default)
    {
        var overrides = await _overrideStore.GetForTenantAsync(tenantId, locale, cancellationToken);
        return overrides.Select(o => new TerminologyOverrideEntry(o.Key, o.Locale, o.Value)).ToList();
    }

    public async Task<TerminologyOverrideEntry?> SetOverrideAsync(
        string tenantId, string locale, string key, string value,
        TerminologyActor actor, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            return null;

        // The write is gated the same way the authoring grid is: the key must
        // be a registered overridable key AND its owning module must pass the
        // caller's feature/seat gate. Without the second check a tenant admin
        // could persist overrides for modules the tenant isn't licensed for.
        var owner = _contributors.FirstOrDefault(
            c => c.GetKeys().Any(d => d.Key == key && d.IsOverridable));
        if (owner is null)
            return null;

        var gate = await _featureGate.CheckFeatureAsync(owner.ModuleKey, actor.UserId, cancellationToken);
        if (!gate.IsAllowed)
            return null;

        var entity = new TerminologyOverride
        {
            TenantId = tenantId,
            Locale = locale,
            Key = key.Trim(),
            Value = value.Trim(),
            CreatedBy = actor.UserName,
            UpdatedBy = actor.UserName
        };

        var saved = await _overrideStore.UpsertAsync(entity, cancellationToken);
        await RaiseUpdatedAsync(tenantId, saved.Key, saved.Locale, actor, cancellationToken);
        return new TerminologyOverrideEntry(saved.Key, saved.Locale, saved.Value);
    }

    public async Task<bool> ResetOverrideAsync(
        string tenantId, string locale, string key,
        TerminologyActor actor, CancellationToken cancellationToken = default)
    {
        var removed = await _overrideStore.DeleteAsync(tenantId, locale, key, cancellationToken);
        if (removed)
            await RaiseUpdatedAsync(tenantId, key, locale, actor, cancellationToken);
        return removed;
    }

    private Task RaiseUpdatedAsync(
        string tenantId, string key, string locale, TerminologyActor actor, CancellationToken cancellationToken) =>
        _eventService.RaiseAsync(new TerminologyUpdatedEvent
        {
            TenantId = tenantId,
            AdminUserId = actor.UserId,
            AdminUserName = actor.UserName,
            Key = key,
            Locale = locale
        }, cancellationToken);
}

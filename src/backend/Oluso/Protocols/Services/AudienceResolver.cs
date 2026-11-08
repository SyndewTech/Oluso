using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;

namespace Oluso.Protocols.Services;

/// <summary>
/// The <c>aud</c> of an access token. One rule for every grant: the resources asked for
/// (RFC 8707), else the registered resources that own a requested scope (RFC 9068 §3), plus the client itself when a
/// requested API scope belongs to no resource (a stand-alone scope is Oluṣọ's own API's, which accepts the client as
/// audience), else the tenant's default resource, else the client itself. A token whose scopes span several APIs lists each.
/// </summary>
public interface IAudienceResolver
{
    /// <param name="resources">Resource indicators the token is for (already validated).</param>
    /// <param name="scopes">The token's scopes.</param>
    /// <param name="clientAllowedResources">Resources the client may use; empty means any.</param>
    Task<ICollection<string>> ResolveAsync(
        IEnumerable<string>? resources,
        IEnumerable<string> scopes,
        string clientId,
        IEnumerable<string> clientAllowedResources,
        CancellationToken cancellationToken = default);
}

public sealed class AudienceResolver : IAudienceResolver
{
    private readonly IResourceStore _resources;
    private readonly ITenantSettingsProvider _tenantSettings;

    public AudienceResolver(IResourceStore resources, ITenantSettingsProvider tenantSettings)
    {
        _resources = resources;
        _tenantSettings = tenantSettings;
    }

    public async Task<ICollection<string>> ResolveAsync(
        IEnumerable<string>? resources,
        IEnumerable<string> scopes,
        string clientId,
        IEnumerable<string> clientAllowedResources,
        CancellationToken cancellationToken = default)
    {
        var requested = resources?.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.Ordinal).ToList();
        if (requested is { Count: > 0 }) return requested;

        // RFC 9068 §3: infer the resource from the requested scopes. Only resources that own a
        // scope count, so OpenID scopes (openid, profile, ...) never add an audience.
        var allowed = new HashSet<string>(clientAllowedResources, StringComparer.OrdinalIgnoreCase);
        var scopeNames = await WithPrefixScopeNamesAsync(scopes.ToList(), cancellationToken);
        var owning = (await _resources.FindResourcesByScopeNamesAsync(scopeNames, cancellationToken))
            .Where(r => r.Enabled && (allowed.Count == 0 || allowed.Contains(r.Uri)))
            .ToList();
        var inferred = owning
            .Select(r => r.Uri)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(u => u, StringComparer.Ordinal)
            .ToList();
        if (inferred.Count > 0)
        {
            // Scopes spanning several APIs (e.g. a homeserver's and Oluṣọ's own, stand-alone ones): the token is for each.
            if (await HasStandaloneApiScopeAsync(scopeNames, owning, cancellationToken) && !inferred.Contains(clientId, StringComparer.Ordinal))
                inferred.Add(clientId);
            return inferred;
        }

        var settings = await _tenantSettings.GetProtocolSettingsAsync(cancellationToken);
        if (!string.IsNullOrEmpty(settings.DefaultResourceUri)) return new List<string> { settings.DefaultResourceUri };

        // Nothing names an API: the token is for the client itself.
        return new List<string> { clientId };
    }

    /// <summary>Whether a requested scope is an API scope that none of <paramref name="owning"/> owns (Oluṣọ's own APIs').</summary>
    private async Task<bool> HasStandaloneApiScopeAsync(IReadOnlyCollection<string> scopeNames, IReadOnlyCollection<Oluso.Core.Domain.Entities.Resource> owning,
        CancellationToken cancellationToken)
    {
        var owned = new HashSet<string>(owning.SelectMany(r => r.AllowedScopes.Select(s => s.Scope)), StringComparer.Ordinal);
        var candidates = scopeNames.Where(s => !owned.Contains(s)).ToList();
        if (candidates.Count == 0)
            return false;
        var apiScopes = await _resources.FindApiScopesByNameAsync(candidates, cancellationToken);
        return apiScopes?.Any(s => s.Enabled && !ApiScopePrefixes.IsPrefix(s)) == true;
    }

    /// <summary>
    /// The scopes plus, for each instance of a prefix scope (e.g. <c>device:abc</c> of <c>device:</c>), the prefix
    /// scope's name: a resource lists the prefix scope, not each client-chosen value.
    /// </summary>
    private async Task<List<string>> WithPrefixScopeNamesAsync(List<string> scopes, CancellationToken cancellationToken)
    {
        if (scopes.Count == 0)
            return scopes;
        var prefixScopes = (await _resources.FindPrefixApiScopesAsync(cancellationToken) ?? Enumerable.Empty<Oluso.Core.Domain.Entities.ApiScope>()).ToList();
        if (prefixScopes.Count == 0)
            return scopes;

        var defaultSuffixMaxLength = await ApiScopePrefixes.GetDefaultSuffixMaxLengthAsync(_tenantSettings, cancellationToken);
        var names = new List<string>(scopes);
        foreach (var scope in scopes)
        {
            if (ApiScopePrefixes.Match(prefixScopes, scope, defaultSuffixMaxLength) is { } prefix && !names.Contains(prefix.Name, StringComparer.Ordinal))
                names.Add(prefix.Name);
        }
        return names;
    }
}

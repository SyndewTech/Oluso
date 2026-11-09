using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Protocols.Validation;
using Oluso.Core.Services;

namespace Oluso.Protocols.Validation;

/// <summary>
/// Default implementation of scope validator.
/// </summary>
/// <remarks>
/// Requested scopes the server won't issue are left out of the grant rather than failing the request
/// (RFC 6749 §3.3: the server "MAY fully or partially ignore the scope requested by the client" and must report
/// the issued scope when it differs; OIDC Core §5.4: scope values that aren't understood SHOULD be ignored). A
/// scope is left out when the client isn't allowed it, when no identity resource or API scope has the name, or
/// when the routed tenant doesn't have the feature it needs. Each one is logged and audited
/// (<see cref="ScopesDroppedEvent"/>).
///
/// A prefix scope (<see cref="ApiScopePrefixes"/>) stands for every value made of its name and a valid suffix, such
/// as <c>device:&lt;id&gt;</c> for a prefix scope <c>device:</c>: the client must be allowed the prefix scope's name, the full
/// value is granted, and a value whose suffix the scope doesn't accept is left out (<see cref="DroppedScope.InvalidValue"/>).
///
/// The request still fails with <c>invalid_scope</c> when:
/// <list type="bullet">
///   <item>a scope token is malformed (RFC 6749 §3.3 scope-token syntax);</item>
///   <item>scopes were requested and none is left;</item>
///   <item><c>openid</c> was requested and left out: an OpenID Connect request can't be answered as plain OAuth;</item>
///   <item>the client is strict (client property <c>strict_scopes=true</c>) and any scope would be left out.</item>
/// </list>
/// Narrowing against an earlier grant (refresh, token exchange) is checked by the grant handlers: asking for more
/// than was granted stays <c>invalid_scope</c> (RFC 6749 §6).
/// </remarks>
public class ScopeValidator : IScopeValidator
{
    private readonly IResourceStore _resourceStore;
    private readonly IScopeFeatureGate? _featureGate;
    private readonly IOlusoEventService? _events;
    private readonly ITenantContext? _tenant;
    private readonly ITenantSettingsProvider? _tenantSettings;
    private readonly ILogger<ScopeValidator> _logger;

    private static readonly HashSet<string> StandardIdentityScopes = new(
        OidcConstants.Scopes.StandardIdentity.Append(OidcConstants.Scopes.OfflineAccess).Append(OidcConstants.Scopes.Permissions),
        StringComparer.Ordinal);

    public ScopeValidator(
        IResourceStore resourceStore,
        IScopeFeatureGate? featureGate = null,
        IOlusoEventService? events = null,
        ITenantContext? tenant = null,
        ILogger<ScopeValidator>? logger = null,
        ITenantSettingsProvider? tenantSettings = null)
    {
        _resourceStore = resourceStore;
        _featureGate = featureGate;
        _events = events;
        _tenant = tenant;
        _tenantSettings = tenantSettings;
        _logger = logger ?? NullLogger<ScopeValidator>.Instance;
    }

    public Task<ScopeValidationResult> ValidateAsync(
        IEnumerable<string> requestedScopes,
        IEnumerable<string> clientAllowedScopes,
        CancellationToken cancellationToken = default)
        => ValidateAsync(new ScopeValidationContext
        {
            RequestedScopes = requestedScopes,
            ClientAllowedScopes = clientAllowedScopes
        }, cancellationToken);

    public async Task<ScopeValidationResult> ValidateAsync(ScopeValidationContext context, CancellationToken cancellationToken = default)
    {
        var requested = context.RequestedScopes.Distinct(StringComparer.Ordinal).ToList();
        var allowed = context.ClientAllowedScopes.ToHashSet(StringComparer.Ordinal);

        var malformed = requested.Where(scope => !IsScopeToken(scope)).ToList();
        if (malformed.Count > 0)
        {
            return Failure($"Malformed scope value(s): {string.Join(", ", malformed)}");
        }

        var result = new ScopeValidationResult();
        if (requested.Count == 0)
        {
            return result;
        }

        var identityResources = await _resourceStore.GetAllIdentityResourcesAsync(cancellationToken);
        var identityResourceNames = identityResources.Select(r => r.Name).ToHashSet(StringComparer.Ordinal);

        var apiScopes = (await _resourceStore.GetAllApiScopesAsync(cancellationToken)).ToList();
        // A prefix scope's own name isn't a scope value: only its instances (name + suffix) are.
        var apiScopeNames = apiScopes.Where(s => !ApiScopePrefixes.IsPrefix(s)).Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        var prefixScopes = apiScopes.Where(ApiScopePrefixes.IsPrefix).ToList();
        var defaultSuffixMaxLength = prefixScopes.Count > 0
            ? await ApiScopePrefixes.GetDefaultSuffixMaxLengthAsync(_tenantSettings, cancellationToken)
            : ApiScopePrefixes.DefaultSuffixMaxLength;

        var dropped = new List<DroppedScope>();
        foreach (var scope in requested)
        {
            var isIdentity = StandardIdentityScopes.Contains(scope) || identityResourceNames.Contains(scope);
            ApiScope? prefixScope = null;
            var suffixValid = false;
            if (!isIdentity && !apiScopeNames.Contains(scope) && prefixScopes.Count > 0)
            {
                prefixScope = ApiScopePrefixes.FindPrefixScope(prefixScopes, scope, out suffixValid, defaultSuffixMaxLength);
            }

            if (!isIdentity && !apiScopeNames.Contains(scope) && prefixScope == null)
            {
                // Reported as unknown even when the client isn't allowed it either: the more useful reason.
                dropped.Add(new DroppedScope(scope, DroppedScope.Unknown));
            }
            else if (prefixScope != null && !suffixValid)
            {
                dropped.Add(new DroppedScope(scope, DroppedScope.InvalidValue));
            }
            else if (!allowed.Contains(scope) && (prefixScope == null || !allowed.Contains(prefixScope.Name)))
            {
                // A client allowed a prefix scope may ask for any valid instance of it.
                dropped.Add(new DroppedScope(scope, DroppedScope.NotAllowed));
            }
            else if (isIdentity)
            {
                result.IdentityScopes.Add(scope);
            }
            else
            {
                // With RFC 8707, resources are identified by the 'resource' parameter, not derived from scopes;
                // result.ApiResources is not populated here.
                result.ApiScopes.Add(scope);
            }
        }

        // A scope that requires a tenant feature (ApiScopeFeatures) is issued only while the routed tenant has the
        // feature on.
        if (_featureGate != null && result.ApiScopes.Count > 0)
        {
            var unavailable = await _featureGate.FindUnavailableAsync(result.ApiScopes, cancellationToken);
            foreach (var scope in unavailable)
            {
                result.ApiScopes.Remove(scope);
                dropped.Add(new DroppedScope(scope, DroppedScope.FeatureUnavailable));
            }
        }

        if (dropped.Count > 0 && context.Strict)
        {
            return Failure(Describe(dropped));
        }

        if (dropped.Any(d => d.Scope == OidcConstants.Scopes.OpenId))
        {
            return Failure($"Client is not allowed to request scope '{OidcConstants.Scopes.OpenId}'");
        }

        // Keep the request's order.
        var granted = result.IdentityScopes.Concat(result.ApiScopes).ToHashSet(StringComparer.Ordinal);
        result.ValidScopes = requested.Where(granted.Contains).ToList();
        result.DroppedScopes = dropped;

        if (result.ValidScopes.Count == 0)
        {
            return Failure(Describe(dropped));
        }

        if (dropped.Count > 0)
        {
            await ReportDroppedAsync(context, requested, result, cancellationToken);
        }

        return result;
    }

    public IEnumerable<string> ParseScopes(string? scopeString)
    {
        if (string.IsNullOrWhiteSpace(scopeString))
        {
            return Enumerable.Empty<string>();
        }

        return scopeString
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal);
    }

    /// <summary>RFC 6749 §3.3: scope-token = 1*( %x21 / %x23-5B / %x5D-7E ).</summary>
    private static bool IsScopeToken(string scope) =>
        scope.Length > 0 && scope.All(ApiScopePrefixes.IsScopeTokenChar);

    private static ScopeValidationResult Failure(string description) => new()
    {
        Error = OidcConstants.Errors.InvalidScope,
        ErrorDescription = description
    };

    private static string Describe(IEnumerable<DroppedScope> dropped)
    {
        var parts = dropped.GroupBy(d => d.Reason).Select(g => g.Key switch
        {
            DroppedScope.NotAllowed => $"Client is not allowed to request scope(s): {string.Join(", ", g.Select(d => d.Scope))}",
            DroppedScope.Unknown => $"Unknown scope(s): {string.Join(", ", g.Select(d => d.Scope))}",
            DroppedScope.FeatureUnavailable => $"Scope(s) not available for this tenant: {string.Join(", ", g.Select(d => d.Scope))}",
            DroppedScope.InvalidValue => $"Invalid scope value(s): {string.Join(", ", g.Select(d => d.Scope))}",
            _ => string.Join(", ", g.Select(d => d.Scope))
        });
        var description = string.Join("; ", parts);
        return string.IsNullOrEmpty(description) ? "No valid scope requested" : description;
    }

    private async Task ReportDroppedAsync(
        ScopeValidationContext context, IReadOnlyList<string> requested, ScopeValidationResult result, CancellationToken cancellationToken)
    {
        _logger.LogDebug(
            "Leaving scope(s) out of the grant for client {ClientId} at {Endpoint}: {Dropped}; granting {Granted}",
            context.ClientId ?? "(unknown)", context.Endpoint ?? "(unknown)",
            string.Join(", ", result.DroppedScopes.Select(d => $"{d.Scope} ({d.Reason})")),
            string.Join(" ", result.ValidScopes));

        if (_events == null)
        {
            return;
        }

        try
        {
            await _events.RaiseAsync(new ScopesDroppedEvent
            {
                TenantId = _tenant?.TenantId,
                ClientId = context.ClientId,
                Endpoint = context.Endpoint,
                RequestedScopes = requested.ToList(),
                GrantedScopes = result.ValidScopes.ToList(),
                DroppedScopes = result.DroppedScopes.ToDictionary(d => d.Scope, d => d.Reason, StringComparer.Ordinal)
            }, cancellationToken);
        }
        catch (Exception ex)
        {
            // Auditing must not fail the request.
            _logger.LogWarning(ex, "Could not raise the dropped-scopes event for client {ClientId}", context.ClientId);
        }
    }
}

using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.UserJourneys;

namespace Oluso.Core.Services;

/// <summary>
/// Default implementation of IClaimsProviderRegistry.
/// Collects claims from two sources:
/// 1. WASM plugins via IManagedPluginRegistry
/// 2. DI-registered IClaimsProvider instances (module claims providers)
/// </summary>
public class ClaimsProviderRegistry : IClaimsProviderRegistry
{
    private readonly IManagedPluginRegistry _pluginRegistry;
    private readonly IEnumerable<IClaimsProvider> _claimsProviders;
    private readonly ILogger<ClaimsProviderRegistry> _logger;

    public ClaimsProviderRegistry(
        IManagedPluginRegistry pluginRegistry,
        IEnumerable<IClaimsProvider> claimsProviders,
        ILogger<ClaimsProviderRegistry> logger)
    {
        _pluginRegistry = pluginRegistry;
        _claimsProviders = claimsProviders;
        _logger = logger;
    }

    public async Task<IDictionary<string, object>> GetAllClaimsAsync(
        ClaimsProviderContext context,
        CancellationToken cancellationToken = default)
    {
        var allClaims = new Dictionary<string, object>();

        // 1. Get claims from WASM plugins
        await CollectPluginClaimsAsync(context, allClaims, cancellationToken);

        // 2. Get claims from DI-registered module providers
        await CollectModuleClaimsAsync(context, allClaims, cancellationToken);

        return allClaims;
    }

    private async Task CollectPluginClaimsAsync(
        ClaimsProviderContext context,
        Dictionary<string, object> allClaims,
        CancellationToken cancellationToken)
    {
        var providers = GetEnabledProviders()
            .OrderByDescending(p => p.Provider.Priority)
            .ToList();

        _logger.LogDebug(
            "Found {ProviderCount} enabled plugin claims providers for user {SubjectId}",
            providers.Count, context.SubjectId);

        var pluginContext = context.ToPluginContext();

        foreach (var (pluginName, provider) in providers)
        {
            if (!ShouldProvideForProtocol(provider, context.Protocol))
            {
                _logger.LogDebug(
                    "Skipping claims provider from plugin {PluginName} - protocol {Protocol} doesn't match triggers",
                    pluginName, context.Protocol);
                continue;
            }

            if (!ShouldProvideForScopes(provider, context.Scopes))
            {
                _logger.LogDebug(
                    "Skipping claims provider from plugin {PluginName} - scopes don't match triggers",
                    pluginName);
                continue;
            }

            try
            {
                _logger.LogDebug(
                    "Invoking claims provider from plugin {PluginName} for user {SubjectId}",
                    pluginName, context.SubjectId);

                var result = await provider.GetClaimsAsync(pluginContext, cancellationToken);

                if (!result.Success)
                {
                    _logger.LogWarning(
                        "Claims provider from plugin {PluginName} failed: {Error}",
                        pluginName, result.Error);
                    continue;
                }

                MergeClaims(allClaims, result.Claims);

                _logger.LogDebug(
                    "Claims provider from plugin {PluginName} contributed {ClaimCount} claims",
                    pluginName, result.Claims.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error invoking claims provider from plugin {PluginName} for user {SubjectId}",
                    pluginName, context.SubjectId);
            }
        }
    }

    private async Task CollectModuleClaimsAsync(
        ClaimsProviderContext context,
        Dictionary<string, object> allClaims,
        CancellationToken cancellationToken)
    {
        var providers = _claimsProviders
            .OrderByDescending(p => p.Priority)
            .ToList();

        if (providers.Count == 0)
            return;

        _logger.LogDebug(
            "Found {ProviderCount} DI-registered claims providers for user {SubjectId}",
            providers.Count, context.SubjectId);

        foreach (var provider in providers)
        {
            // Check scope triggers
            if (provider.TriggerScopes != null && provider.TriggerScopes.Any())
            {
                if (!context.Scopes.Any(s => provider.TriggerScopes.Contains(s, StringComparer.OrdinalIgnoreCase)))
                {
                    _logger.LogDebug(
                        "Skipping claims provider {ProviderId} - scopes don't match triggers",
                        provider.ProviderId);
                    continue;
                }
            }

            if (!provider.ShouldProvide(context))
            {
                _logger.LogDebug(
                    "Skipping claims provider {ProviderId} - ShouldProvide returned false",
                    provider.ProviderId);
                continue;
            }

            try
            {
                _logger.LogDebug(
                    "Invoking claims provider {ProviderId} for user {SubjectId}",
                    provider.ProviderId, context.SubjectId);

                var result = await provider.GetClaimsAsync(context, cancellationToken);

                if (!result.Success)
                {
                    _logger.LogWarning(
                        "Claims provider {ProviderId} failed: {Error}",
                        provider.ProviderId, result.Error);
                    continue;
                }

                MergeClaims(allClaims, result.Claims);

                _logger.LogDebug(
                    "Claims provider {ProviderId} contributed {ClaimCount} claims for user {SubjectId}",
                    provider.ProviderId, result.Claims.Count, context.SubjectId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error invoking claims provider {ProviderId} for user {SubjectId}",
                    provider.ProviderId, context.SubjectId);
            }
        }
    }

    private static void MergeClaims(Dictionary<string, object> target, IDictionary<string, object> source)
    {
        foreach (var claim in source)
        {
            if (target.TryGetValue(claim.Key, out var existingValue))
            {
                target[claim.Key] = MergeClaimValues(existingValue, claim.Value);
            }
            else
            {
                target[claim.Key] = claim.Value;
            }
        }
    }

    private IEnumerable<(string PluginName, IPluginClaimsProvider Provider)> GetEnabledProviders()
    {
        foreach (var (name, plugin) in _pluginRegistry.GetAll())
        {
            if (!plugin.IsEnabled)
            {
                continue;
            }

            var provider = plugin.GetClaimsProvider();
            if (provider != null)
            {
                yield return (name, provider);
            }
        }
    }

    private static bool ShouldProvideForScopes(IPluginClaimsProvider provider, IEnumerable<string> requestedScopes)
    {
        var triggers = provider.TriggerScopes?.ToList();
        if (triggers == null || triggers.Count == 0)
        {
            return true;
        }

        return requestedScopes.Any(s => triggers.Contains(s, StringComparer.OrdinalIgnoreCase));
    }

    private static bool ShouldProvideForProtocol(IPluginClaimsProvider provider, string? protocol)
    {
        var triggers = provider.TriggerProtocols?.ToList();
        if (triggers == null || triggers.Count == 0)
        {
            return true;
        }

        if (string.IsNullOrEmpty(protocol))
        {
            return true;
        }

        return triggers.Contains(protocol, StringComparer.OrdinalIgnoreCase);
    }

    private static object MergeClaimValues(object existing, object newValue)
    {
        if (existing is IList<object> existingList && newValue is IList<object> newList)
        {
            var merged = new List<object>(existingList);
            foreach (var item in newList)
            {
                if (!merged.Contains(item))
                {
                    merged.Add(item);
                }
            }
            return merged;
        }

        if (existing is IList<object> list && newValue is not IList<object>)
        {
            if (!list.Contains(newValue))
            {
                var merged = new List<object>(list) { newValue };
                return merged;
            }
            return list;
        }

        if (newValue is IList<object> newValueList && existing is not IList<object>)
        {
            var merged = new List<object> { existing };
            foreach (var item in newValueList)
            {
                if (!merged.Contains(item))
                {
                    merged.Add(item);
                }
            }
            return merged;
        }

        if (existing is IEnumerable<string> existingStrings && newValue is IEnumerable<string> newStrings)
        {
            var merged = existingStrings.ToList();
            foreach (var item in newStrings)
            {
                if (!merged.Contains(item))
                {
                    merged.Add(item);
                }
            }
            return merged;
        }

        return newValue;
    }
}

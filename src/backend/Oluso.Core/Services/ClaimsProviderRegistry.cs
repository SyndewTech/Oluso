using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.UserJourneys;

namespace Oluso.Core.Services;

/// <summary>
/// Default implementation of IClaimsProviderRegistry.
/// Automatically discovers and invokes claims providers from enabled plugins.
/// </summary>
public class ClaimsProviderRegistry : IClaimsProviderRegistry
{
    private readonly IManagedPluginRegistry _pluginRegistry;
    private readonly ILogger<ClaimsProviderRegistry> _logger;

    public ClaimsProviderRegistry(
        IManagedPluginRegistry pluginRegistry,
        ILogger<ClaimsProviderRegistry> logger)
    {
        _pluginRegistry = pluginRegistry;
        _logger = logger;
    }

    public async Task<IDictionary<string, object>> GetAllClaimsAsync(
        ClaimsProviderContext context,
        CancellationToken cancellationToken = default)
    {
        var allClaims = new Dictionary<string, object>();

        // Get all claims providers from enabled plugins, ordered by priority
        var providers = GetEnabledProviders()
            .OrderByDescending(p => p.Provider.Priority)
            .ToList();

        _logger.LogDebug(
            "Found {ProviderCount} enabled claims providers for user {SubjectId}",
            providers.Count, context.SubjectId);

        var pluginContext = context.ToPluginContext();

        foreach (var (pluginName, provider) in providers)
        {
            // Check if this provider should run based on protocol
            if (!ShouldProvideForProtocol(provider, context.Protocol))
            {
                _logger.LogDebug(
                    "Skipping claims provider from plugin {PluginName} - protocol {Protocol} doesn't match triggers",
                    pluginName, context.Protocol);
                continue;
            }

            // Check if this provider should run based on scopes
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

                // Merge claims - later providers can override earlier ones
                foreach (var claim in result.Claims)
                {
                    if (allClaims.TryGetValue(claim.Key, out var existingValue))
                    {
                        allClaims[claim.Key] = MergeClaimValues(existingValue, claim.Value);
                    }
                    else
                    {
                        allClaims[claim.Key] = claim.Value;
                    }
                }

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

        return allClaims;
    }

    private IEnumerable<(string PluginName, IPluginClaimsProvider Provider)> GetEnabledProviders()
    {
        foreach (var (name, plugin) in _pluginRegistry.GetAll())
        {
            // Only get providers from enabled plugins
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
            return true; // Always provide if no triggers specified
        }

        return requestedScopes.Any(s => triggers.Contains(s, StringComparer.OrdinalIgnoreCase));
    }

    private static bool ShouldProvideForProtocol(IPluginClaimsProvider provider, string? protocol)
    {
        var triggers = provider.TriggerProtocols?.ToList();
        if (triggers == null || triggers.Count == 0)
        {
            return true; // Always provide if no protocol triggers specified
        }

        if (string.IsNullOrEmpty(protocol))
        {
            return true; // If no protocol specified in context, allow all
        }

        return triggers.Contains(protocol, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Merge claim values intelligently - arrays are concatenated, scalars are replaced
    /// </summary>
    private static object MergeClaimValues(object existing, object newValue)
    {
        // If both are lists, concatenate
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

        // If existing is a list and new is scalar, add to list
        if (existing is IList<object> list && newValue is not IList<object>)
        {
            if (!list.Contains(newValue))
            {
                var merged = new List<object>(list) { newValue };
                return merged;
            }
            return list;
        }

        // If new is a list and existing is scalar, create new list with both
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

        // Handle string lists
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

        // Default: new value wins
        return newValue;
    }
}

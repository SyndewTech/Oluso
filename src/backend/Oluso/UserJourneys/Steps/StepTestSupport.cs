using Microsoft.Extensions.DependencyInjection;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;

namespace Oluso.UserJourneys.Steps;

/// <summary>Shared checks for the builder's step "Test".</summary>
internal static class StepTestSupport
{
    public static bool HasPlaceholder(string? value) => value?.Contains('{') == true;

    /// <summary>
    /// Sends <paramref name="request"/> (a HEAD, so nothing is created) in place of the step's real
    /// <paramref name="method"/> and reports whether the endpoint answered. Any answer counts except
    /// missing, rejected or failing. <c>Checked</c> is false when the endpoint refused the HEAD itself
    /// (405), so only its reachability is known, not whether it accepts the call or its credentials.
    /// </summary>
    public static async Task<(bool Ok, bool Checked, string Message)> ProbeAsync(
        HttpClient client, HttpRequestMessage request, string method, CancellationToken cancellationToken)
    {
        var target = $"HEAD {request.RequestUri}";
        try
        {
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            var status = $"{(int)response.StatusCode} {response.StatusCode}";
            return (int)response.StatusCode switch
            {
                401 or 403 => (false, true, $"{target} → {status}: the endpoint rejected the credentials"),
                404 => (false, true, $"{target} → {status}: the endpoint doesn't exist"),
                405 => (true, false,
                    $"{target} → {status}: the endpoint is reachable but doesn't answer HEAD, so the {method} and its credentials couldn't be checked. The test never sends the {method}, so nothing is created"),
                >= 500 => (false, true, $"{target} → {status}: the endpoint returned a server error"),
                _ => (true, true, $"{target} → {status}. The test sends HEAD instead of the {method}, so nothing is created")
            };
        }
        catch (HttpRequestException ex)
        {
            return (false, true, $"{target}: couldn't reach the endpoint: {ex.Message}");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return (false, true, $"{target}: no answer within {client.Timeout.TotalSeconds:0} seconds");
        }
    }

    /// <summary>The identity providers a login step offers: the ones it names, or every enabled one.</summary>
    public static async Task<StepConfigurationTestResult> TestExternalProvidersAsync(
        StepExecutionContext context, string setting, CancellationToken cancellationToken)
    {
        var store = context.ServiceProvider.GetService<IExternalProviderConfigStore>();
        if (store == null)
        {
            return StepConfigurationTestResult.Failed("Identity providers aren't available in this deployment");
        }

        var enabled = await store.GetEnabledProvidersAsync(cancellationToken: cancellationToken);
        var named = context.GetConfig<List<string>>(setting, null)?.Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        if (named is not { Count: > 0 })
        {
            return enabled.Count == 0
                ? StepConfigurationTestResult.Failed("No identity providers are enabled, so the step offers none")
                : StepConfigurationTestResult.Passed($"All {enabled.Count} enabled identity providers will be offered",
                    enabled.Select(p => $"{p.DisplayName ?? p.Scheme} ({p.Scheme})").ToArray());
        }

        var details = new List<string>();
        var ok = true;
        foreach (var scheme in named)
        {
            if (enabled.FirstOrDefault(p => p.Scheme.Equals(scheme, StringComparison.OrdinalIgnoreCase)) is { } provider)
            {
                details.Add($"{provider.DisplayName ?? scheme} ({scheme}) is enabled");
                continue;
            }

            ok = false;
            details.Add(await store.GetBySchemeAsync(scheme, cancellationToken: cancellationToken) == null
                ? $"{scheme} doesn't exist"
                : $"{scheme} is disabled");
        }

        return StepConfigurationTestResult.Of(ok, ok ? "All selected identity providers are enabled" : "Some selected identity providers can't be used", details);
    }
}

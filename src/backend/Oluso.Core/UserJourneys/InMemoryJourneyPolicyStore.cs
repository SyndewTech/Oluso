using System.Collections.Concurrent;

namespace Oluso.Core.UserJourneys;

/// <summary>
/// In-memory implementation of journey policy store (for development/testing)
/// </summary>
public class InMemoryJourneyPolicyStore : IJourneyPolicyStore
{
    private readonly ConcurrentDictionary<string, JourneyPolicy> _policies = new();

    public InMemoryJourneyPolicyStore()
    {
        // Single source of truth for the canonical platform defaults lives in
        // DefaultJourneyPolicies — the EF-backed seeder uses the same list so
        // database deployments get the same baseline as in-memory ones.
        foreach (var policy in DefaultJourneyPolicies.Build())
        {
            _policies[policy.Id] = policy;
        }
    }

    public Task<JourneyPolicy?> GetAsync(string policyId, CancellationToken cancellationToken = default)
    {
        return GetByIdAsync(policyId, cancellationToken);
    }

    public Task<JourneyPolicy?> GetByIdAsync(string policyId, CancellationToken cancellationToken = default)
    {
        _policies.TryGetValue(policyId, out var policy);
        return Task.FromResult(policy);
    }

    public Task<JourneyPolicy?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default) =>
        Task.FromResult(_policies.Values.FirstOrDefault(p => p.Slug == slug));

    public Task<bool> IsSlugTakenAsync(string slug, string? exceptPolicyId = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(_policies.Values.Any(p => p.Slug == slug && p.Id != exceptPolicyId));

    public Task<JourneyPolicy?> GetByTypeAsync(JourneyType type, CancellationToken cancellationToken = default)
    {
        var policy = _policies.Values
            .Where(p => p.Enabled && p.Type == type)
            .OrderByDescending(p => p.Priority)
            .FirstOrDefault();

        return Task.FromResult(policy);
    }

    public Task<IEnumerable<JourneyPolicy>> GetByTenantAsync(string? tenantId, CancellationToken cancellationToken = default)
    {
        var policies = _policies.Values
            .Where(p => p.TenantId == tenantId || p.TenantId == null)
            .OrderByDescending(p => p.Priority);

        return Task.FromResult<IEnumerable<JourneyPolicy>>(policies);
    }

    public Task<JourneyPolicy?> FindMatchingAsync(JourneyPolicyMatchContext context, CancellationToken cancellationToken = default)
    {
        var policies = _policies.Values
            .Where(p => p.Enabled)
            .Where(p => p.TenantId == context.TenantId || p.TenantId == null)
            .Where(p => p.Type == context.Type || p.Type == JourneyType.Custom)
            .OrderByDescending(p => p.TenantId != null) // Prefer tenant-specific
            .ThenByDescending(p => p.Priority)
            .ToList();

        foreach (var policy in policies)
        {
            if (MatchesConditions(policy, context))
            {
                return Task.FromResult<JourneyPolicy?>(policy);
            }
        }

        // Return first matching type as fallback
        return Task.FromResult(policies.FirstOrDefault());
    }

    private static bool MatchesConditions(JourneyPolicy policy, JourneyPolicyMatchContext context)
    {
        if (policy.Conditions == null || policy.Conditions.Count == 0)
        {
            return true;
        }

        foreach (var condition in policy.Conditions)
        {
            string? value = condition.Type switch
            {
                "ClientId" => context.ClientId,
                "Scope" => context.Scopes != null ? string.Join(" ", context.Scopes) : null,
                "AcrValue" => context.AcrValues,
                _ => context.AdditionalParameters?.TryGetValue(condition.Type, out var v) == true ? v : null
            };

            var matches = condition.Operator switch
            {
                "equals" => value == condition.Value,
                "contains" => value?.Contains(condition.Value) ?? false,
                "startsWith" => value?.StartsWith(condition.Value) ?? false,
                "endsWith" => value?.EndsWith(condition.Value) ?? false,
                "matches" => !string.IsNullOrEmpty(value) &&
                    System.Text.RegularExpressions.Regex.IsMatch(value, condition.Value),
                _ => value == condition.Value
            };

            if (!matches) return false;
        }

        return true;
    }

    public Task SaveAsync(JourneyPolicy policy, CancellationToken cancellationToken = default)
    {
        policy.UpdatedAt = DateTime.UtcNow;
        _policies[policy.Id] = policy;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string policyId, CancellationToken cancellationToken = default)
    {
        _policies.TryRemove(policyId, out _);
        return Task.CompletedTask;
    }
}

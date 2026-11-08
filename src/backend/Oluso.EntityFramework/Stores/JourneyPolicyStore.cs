using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.UserJourneys;

namespace Oluso.EntityFramework.Stores;

/// <summary>
/// Entity Framework implementation of IJourneyPolicyStore
/// </summary>
public class JourneyPolicyStore : IJourneyPolicyStore
{
    private readonly IOlusoDbContext _context;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly ITenantContext _tenantContext;
    private readonly IJourneyStepSecretProtector? _secretProtector;

    public JourneyPolicyStore(
        IOlusoDbContext context,
        ITenantContext tenantContext,
        IJourneyStepSecretProtector? secretProtector = null)
    {
        _context = context;
        _tenantContext = tenantContext;
        _secretProtector = secretProtector;
    }

    /// <summary>
    /// The policies the current tenant names by slug: its own, then the global ones.
    /// Slugs are unique per tenant, so another tenant's policy with the same slug is
    /// never in reach — even where the tenant query filter is off (no current tenant).
    /// </summary>
    private IQueryable<JourneyPolicyEntity> WithSlug(string slug)
    {
        var tenantId = _tenantContext.TenantId;
        return _context.JourneyPolicies
            .Where(p => p.Slug == slug && (p.TenantId == tenantId || p.TenantId == null))
            .OrderBy(p => p.TenantId == null ? 1 : 0);
    }

    public async Task<JourneyPolicy?> GetAsync(string policyId, CancellationToken cancellationToken = default)
    {
        return await GetByIdAsync(policyId, cancellationToken);
    }

    public async Task<JourneyPolicy?> GetByIdAsync(string policyId, CancellationToken cancellationToken = default)
    {
        var entity = await _context.JourneyPolicies
            .FirstOrDefaultAsync(p => p.Id == policyId, cancellationToken);

        return entity != null ? MapToPolicy(entity) : null;
    }

    public async Task<JourneyPolicy?> GetBySlugAsync(string slug, CancellationToken cancellationToken = default)
    {
        var entity = await WithSlug(slug).FirstOrDefaultAsync(cancellationToken);

        return entity != null ? MapToPolicy(entity) : null;
    }

    public Task<bool> IsSlugTakenAsync(string slug, string? exceptPolicyId = null, CancellationToken cancellationToken = default)
    {
        // A global policy is seen by every tenant, so its slug must be free in all of them.
        var holders = _tenantContext.TenantId == null
            ? _context.JourneyPolicies.IgnoreQueryFilters().Where(p => p.Slug == slug)
            : WithSlug(slug);
        return holders.AnyAsync(p => p.Id != exceptPolicyId, cancellationToken);
    }

    public async Task<JourneyPolicy?> GetByTypeAsync(JourneyType type, CancellationToken cancellationToken = default)
    {
        var typeString = type.ToString();
        var entity = await _context.JourneyPolicies
            .Where(p => p.Type == typeString && p.Enabled)
            .OrderByDescending(p => p.Priority)
            .FirstOrDefaultAsync(cancellationToken);

        return entity != null ? MapToPolicy(entity) : null;
    }

    public async Task<IEnumerable<JourneyPolicy>> GetByTenantAsync(string? tenantId, CancellationToken cancellationToken = default)
    {
        // Return policies for the tenant AND global policies (TenantId == null)
        var entities = await _context.JourneyPolicies
            .Where(p => p.TenantId == tenantId || p.TenantId == null)
            .OrderBy(p => p.Name)
            .ToListAsync(cancellationToken);

        return entities.Select(MapToPolicy);
    }

    public async Task<JourneyPolicy?> FindMatchingAsync(JourneyPolicyMatchContext context, CancellationToken cancellationToken = default)
    {
        var typeString = context.Type.ToString();

        var candidates = await _context.JourneyPolicies
            .Where(p => p.Enabled && (p.TenantId == context.TenantId || p.TenantId == null))
            .OrderByDescending(p => p.Priority)
            .ThenBy(p => p.TenantId == null ? 1 : 0) // Prefer tenant-specific over global
            .ToListAsync(cancellationToken);

        foreach (var entity in candidates)
        {
            var policy = MapToPolicy(entity);

            // Check if type matches
            if (policy.Type.ToString() != typeString)
                continue;

            // Check conditions if any
            if (policy.Conditions != null && policy.Conditions.Count > 0)
            {
                var conditionsMet = EvaluatePolicyConditions(policy.Conditions, context);
                if (!conditionsMet)
                    continue;
            }

            return policy;
        }

        return null;
    }

    public async Task SaveAsync(JourneyPolicy policy, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(policy.Slug))
        {
            throw new ArgumentException($"Journey policy {policy.Id} has no slug.", nameof(policy));
        }

        var entity = await _context.JourneyPolicies
            .FirstOrDefaultAsync(p => p.Id == policy.Id, cancellationToken);

        if (entity == null)
        {
            entity = new JourneyPolicyEntity { Id = policy.Id };
            _context.JourneyPolicies.Add(entity);
        }

        MapToEntity(policy, entity);
        entity.UpdatedAt = DateTime.UtcNow;
        entity.Version++;

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(string policyId, CancellationToken cancellationToken = default)
    {
        var entity = await _context.JourneyPolicies
            .FirstOrDefaultAsync(p => p.Id == policyId, cancellationToken);

        if (entity != null)
        {
            _context.JourneyPolicies.Remove(entity);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Decrypts step secrets so every caller — journey execution and the admin
    /// API alike — works with plaintext. Redaction for API responses happens at
    /// the controller, not here.
    /// </summary>
    private IList<JourneyPolicyStep> UnprotectSteps(IList<JourneyPolicyStep> steps)
    {
        _secretProtector?.Unprotect(steps);
        return steps;
    }

    /// <summary>
    /// Encrypts step secrets on a copy. The caller's policy object stays
    /// plaintext, so a save doesn't leave ciphertext in an object the request
    /// goes on to serialise back to the client.
    /// </summary>
    private List<JourneyPolicyStep> ProtectedCopy(IEnumerable<JourneyPolicyStep>? steps)
    {
        var list = steps?.ToList() ?? new List<JourneyPolicyStep>();
        if (_secretProtector == null || list.Count == 0)
        {
            return list;
        }

        var copy = JsonSerializer.Deserialize<List<JourneyPolicyStep>>(
            JsonSerializer.Serialize(list, JsonOptions), JsonOptions) ?? list;

        _secretProtector.Protect(copy);
        return copy;
    }

    private JourneyPolicy MapToPolicy(JourneyPolicyEntity entity)
    {
        return new JourneyPolicy
        {
            Id = entity.Id,
            Slug = entity.Slug!,
            Name = entity.Name,
            TenantId = entity.TenantId,
            Type = Enum.TryParse<JourneyType>(entity.Type, out var type) ? type : JourneyType.SignIn,
            Enabled = entity.Enabled,
            Priority = entity.Priority,
            Description = entity.Description,
            Version = entity.Version,
            Steps = UnprotectSteps(DeserializeList<JourneyPolicyStep>(entity.Steps) ?? new List<JourneyPolicyStep>()),
            Conditions = DeserializeList<JourneyPolicyCondition>(entity.Conditions),
            OutputClaims = DeserializeList<ClaimMapping>(entity.OutputClaims),
            Session = Deserialize<SessionConfiguration>(entity.SessionConfig),
            Ui = Deserialize<JourneyUiConfiguration>(entity.UiConfig),
            DefaultStepTimeoutSeconds = entity.DefaultStepTimeoutSeconds,
            MaxJourneyDurationMinutes = entity.MaxJourneyDurationMinutes,
            Tags = entity.Tags?.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
            OwnerModule = entity.OwnerModule,
            Sharing = Enum.IsDefined(typeof(JourneySharing), entity.Sharing) ? (JourneySharing)entity.Sharing : JourneySharing.Private,
            // Data collection journey properties
            RequiresAuthentication = entity.RequiresAuthentication,
            PersistSubmissions = entity.PersistSubmissions,
            SubmissionCollection = entity.SubmissionCollection,
            MaxSubmissions = entity.MaxSubmissions,
            AllowDuplicates = entity.AllowDuplicates,
            DuplicateCheckFields = entity.DuplicateCheckFields?.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
            SuccessRedirectUrl = entity.SuccessRedirectUrl,
            SuccessMessage = entity.SuccessMessage,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt
        };
    }

    private void MapToEntity(JourneyPolicy policy, JourneyPolicyEntity entity)
    {
        entity.Name = policy.Name;
        entity.TenantId = policy.TenantId;
        entity.Slug = policy.Slug;
        entity.Type = policy.Type.ToString();
        entity.Enabled = policy.Enabled;
        entity.Priority = policy.Priority;
        entity.Description = policy.Description;
        entity.Version = policy.Version;
        entity.Steps = Serialize(ProtectedCopy(policy.Steps));
        entity.Conditions = Serialize(policy.Conditions);
        entity.OutputClaims = Serialize(policy.OutputClaims);
        entity.SessionConfig = Serialize(policy.Session);
        entity.UiConfig = Serialize(policy.Ui);
        entity.DefaultStepTimeoutSeconds = policy.DefaultStepTimeoutSeconds;
        entity.MaxJourneyDurationMinutes = policy.MaxJourneyDurationMinutes;
        entity.Tags = policy.Tags != null ? string.Join(',', policy.Tags) : null;
        entity.OwnerModule = string.IsNullOrWhiteSpace(policy.OwnerModule) ? null : policy.OwnerModule.Trim();
        entity.Sharing = (int)policy.Sharing;
        // Data collection journey properties
        entity.RequiresAuthentication = policy.RequiresAuthentication;
        entity.PersistSubmissions = policy.PersistSubmissions;
        entity.SubmissionCollection = policy.SubmissionCollection;
        entity.MaxSubmissions = policy.MaxSubmissions;
        entity.AllowDuplicates = policy.AllowDuplicates;
        entity.DuplicateCheckFields = policy.DuplicateCheckFields != null ? string.Join(',', policy.DuplicateCheckFields) : null;
        entity.SuccessRedirectUrl = policy.SuccessRedirectUrl;
        entity.SuccessMessage = policy.SuccessMessage;
        entity.CreatedAt = policy.CreatedAt;
    }

    private static T? Deserialize<T>(string? json) where T : class
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static IList<T>? DeserializeList<T>(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<List<T>>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static string Serialize<T>(T? obj)
    {
        if (obj == null) return "null";
        return JsonSerializer.Serialize(obj, JsonOptions);
    }

    private static bool EvaluatePolicyConditions(IList<JourneyPolicyCondition> conditions, JourneyPolicyMatchContext context)
    {
        foreach (var condition in conditions)
        {
            var value = condition.Type.ToLowerInvariant() switch
            {
                "clientid" or "client_id" => context.ClientId,
                "tenantid" or "tenant_id" => context.TenantId,
                "acrvalues" or "acr_values" => context.AcrValues,
                "scope" when context.Scopes != null => string.Join(" ", context.Scopes),
                _ => null
            };

            var matches = condition.Operator.ToLowerInvariant() switch
            {
                "eq" or "equals" => string.Equals(value, condition.Value, StringComparison.OrdinalIgnoreCase),
                "ne" or "not_equals" => !string.Equals(value, condition.Value, StringComparison.OrdinalIgnoreCase),
                "contains" => value?.Contains(condition.Value ?? "", StringComparison.OrdinalIgnoreCase) ?? false,
                "starts_with" => value?.StartsWith(condition.Value ?? "", StringComparison.OrdinalIgnoreCase) ?? false,
                "ends_with" => value?.EndsWith(condition.Value ?? "", StringComparison.OrdinalIgnoreCase) ?? false,
                "exists" => !string.IsNullOrEmpty(value),
                "not_exists" => string.IsNullOrEmpty(value),
                _ => false
            };

            if (!matches) return false;
        }

        return true;
    }
}

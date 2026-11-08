using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Admin.Dto.Search;
using Oluso.Core.Authorization;
using Oluso.Core.Licensing;

namespace Oluso.Admin.Search;

/// <summary>
/// Fans a query out to every provider the caller is allowed to search. Gates
/// run first, in the request scope: the tenant must have the provider's
/// feature and the caller must satisfy the provider's permission policy. The
/// providers that pass then run in parallel, each in its own DI scope so they
/// never share a DbContext, each under its own timeout so one slow module
/// cannot hold up the rest.
/// </summary>
public sealed class AdminSearchService : IAdminSearchService
{
    private readonly IEnumerable<IAdminSearchProvider> _providers;
    private readonly IFeatureGate _featureGate;
    private readonly IAuthorizationService _authorization;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<AdminSearchService> _logger;

    public AdminSearchService(
        IEnumerable<IAdminSearchProvider> providers,
        IFeatureGate featureGate,
        IAuthorizationService authorization,
        IServiceScopeFactory scopeFactory,
        ILogger<AdminSearchService> logger)
    {
        _providers = providers;
        _featureGate = featureGate;
        _authorization = authorization;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task<IReadOnlyList<AdminSearchProviderDto>> GetAvailableProvidersAsync(
        ClaimsPrincipal user, string userId, string? tenantId, CancellationToken cancellationToken = default)
    {
        var allowed = await AllowedProvidersAsync(user, userId, null, null, cancellationToken);
        return allowed
            .Select(p => new AdminSearchProviderDto
            {
                ProviderId = p.ProviderId,
                Module = p.Module,
                Category = p.Category,
                DisplayName = p.DisplayName,
                Icon = p.Icon,
                Filters = p.Filters.ToList()
            })
            .ToList();
    }

    public async Task<AdminSearchResponse> SearchAsync(
        AdminSearchRequest request, ClaimsPrincipal user, string userId, string? tenantId, CancellationToken cancellationToken = default)
    {
        var query = request.Query.Trim();
        var response = new AdminSearchResponse { Query = query };
        if (query.Length < AdminSearchDefaults.MinQueryLength)
            return response;

        var allowed = await AllowedProvidersAsync(user, userId, request.Module, request.ProviderId, cancellationToken);
        if (allowed.Count == 0)
            return response;

        var context = new AdminSearchContext
        {
            TenantId = tenantId,
            UserId = userId,
            User = user,
            Query = query,
            Filters = request.Filters,
            Limit = Math.Clamp(request.Limit, 1, AdminSearchDefaults.MaxLimit)
        };

        var groups = await Task.WhenAll(allowed.Select(p => RunProviderAsync(p.ProviderId, context, cancellationToken)));

        var ordered = groups.Where(g => g != null).Cast<AdminSearchGroupDto>()
            .OrderByDescending(g => g.Items.Count == 0 ? 0 : g.Items.Max(i => i.Score))
            .ThenBy(g => g.DisplayName, StringComparer.OrdinalIgnoreCase);
        foreach (var group in ordered)
        {
            response.Groups.Add(group);
        }
        return new AdminSearchResponse
        {
            Query = query,
            Groups = response.Groups,
            TotalCount = response.Groups.Sum(g => g.TotalCount)
        };
    }

    private async Task<List<IAdminSearchProvider>> AllowedProvidersAsync(
        ClaimsPrincipal user, string userId, string? module, string? providerId, CancellationToken cancellationToken)
    {
        var allowed = new List<IAdminSearchProvider>();
        var featureResults = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var permissionResults = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

        foreach (var provider in _providers)
        {
            if (module != null && !string.Equals(provider.Module, module, StringComparison.OrdinalIgnoreCase)) continue;
            if (providerId != null && !string.Equals(provider.ProviderId, providerId, StringComparison.OrdinalIgnoreCase)) continue;

            if (provider.FeatureKey is { } featureKey)
            {
                if (!featureResults.TryGetValue(featureKey, out var hasFeature))
                {
                    var gate = await _featureGate.CheckFeatureAsync(featureKey, userId, cancellationToken);
                    hasFeature = gate.IsAllowed;
                    featureResults[featureKey] = hasFeature;
                }
                if (!hasFeature) continue;
            }

            var permitted = true;
            foreach (var permission in provider.RequiredPermissions)
            {
                if (!permissionResults.TryGetValue(permission, out var granted))
                {
                    var authorized = await _authorization.AuthorizeAsync(user, null, PermissionPolicyConstants.PolicyPrefix + permission);
                    granted = authorized.Succeeded;
                    permissionResults[permission] = granted;
                }
                if (granted) continue;
                permitted = false;
                break;
            }
            if (!permitted) continue;

            allowed.Add(provider);
        }
        return allowed;
    }

    private async Task<AdminSearchGroupDto?> RunProviderAsync(string providerId, AdminSearchContext context, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var provider = scope.ServiceProvider.GetServices<IAdminSearchProvider>().FirstOrDefault(p => p.ProviderId == providerId);
        if (provider == null) return null;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(AdminSearchDefaults.ProviderTimeout);
        try
        {
            var result = await provider.SearchAsync(context, timeout.Token);
            if (result.Items.Count == 0) return null;

            return new AdminSearchGroupDto
            {
                ProviderId = provider.ProviderId,
                Module = provider.Module,
                Category = provider.Category,
                DisplayName = provider.DisplayName,
                Icon = provider.Icon,
                TotalCount = result.TotalCount ?? result.Items.Count,
                SeeAllRoute = result.SeeAllRoute,
                Items = result.Items.OrderByDescending(i => i.Score).Take(context.Limit).ToList()
            };
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Admin search provider {ProviderId} timed out after {Timeout}", providerId, AdminSearchDefaults.ProviderTimeout);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Admin search provider {ProviderId} failed", providerId);
            return null;
        }
    }
}

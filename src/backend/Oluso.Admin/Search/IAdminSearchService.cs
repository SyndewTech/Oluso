using System.Security.Claims;
using Oluso.Admin.Dto.Search;

namespace Oluso.Admin.Search;

public sealed class AdminSearchRequest
{
    public required string Query { get; init; }

    /// <summary>Restrict to one module's providers.</summary>
    public string? Module { get; init; }

    /// <summary>Restrict to one provider by <see cref="IAdminSearchProvider.ProviderId"/>.</summary>
    public string? ProviderId { get; init; }

    public IReadOnlyDictionary<string, string> Filters { get; init; } = new Dictionary<string, string>();
    public int Limit { get; init; } = AdminSearchDefaults.Limit;
}

public interface IAdminSearchService
{
    /// <summary>Providers the caller may search right now, after feature and permission gates.</summary>
    Task<IReadOnlyList<AdminSearchProviderDto>> GetAvailableProvidersAsync(
        ClaimsPrincipal user, string userId, string? tenantId, CancellationToken cancellationToken = default);

    Task<AdminSearchResponse> SearchAsync(
        AdminSearchRequest request, ClaimsPrincipal user, string userId, string? tenantId, CancellationToken cancellationToken = default);
}

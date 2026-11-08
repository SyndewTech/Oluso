using Oluso.Admin.Controllers.Service;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Admin.Services;

/// <summary>
/// Creates the <see cref="ApiScope"/> rows behind the Service API on demand.
///
/// Scope names alone don't grant anything: ScopeValidator rejects a requested
/// scope that has no ApiScope row, so a client with "oluso:service" in its
/// allowed scopes would still fail token issuance with invalid_scope. Rather
/// than making every admin hand-create seven scopes with exactly-right names,
/// enabling Service API access on a client provisions them in the current
/// tenant.
/// </summary>
public interface IServiceApiScopeProvisioner
{
    /// <summary>
    /// Ensures an ApiScope row exists in the current tenant for every managed
    /// service scope in <paramref name="requestedScopes"/>. Unmanaged names are
    /// ignored — this never creates scopes it doesn't own.
    /// </summary>
    /// <returns>The names actually created, for logging.</returns>
    Task<IReadOnlyList<string>> EnsureAsync(
        IEnumerable<string> requestedScopes,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public class ServiceApiScopeProvisioner : IServiceApiScopeProvisioner
{
    private readonly IResourceStore _resourceStore;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<ServiceApiScopeProvisioner> _logger;

    public ServiceApiScopeProvisioner(
        IResourceStore resourceStore,
        ITenantContext tenantContext,
        ILogger<ServiceApiScopeProvisioner> logger)
    {
        _resourceStore = resourceStore;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task<IReadOnlyList<string>> EnsureAsync(
        IEnumerable<string> requestedScopes,
        CancellationToken cancellationToken = default)
    {
        var wanted = requestedScopes
            .Where(ServiceScopes.IsManaged)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (wanted.Count == 0)
        {
            return Array.Empty<string>();
        }

        var existing = (await _resourceStore.FindApiScopesByNameAsync(wanted, cancellationToken))
            .Select(s => s.Name)
            .ToHashSet(StringComparer.Ordinal);

        var created = new List<string>();

        foreach (var name in wanted.Where(n => !existing.Contains(n)))
        {
            var definition = ServiceScopes.Find(name);

            await _resourceStore.AddApiScopeAsync(new ApiScope
            {
                Name = name,
                DisplayName = definition?.DisplayName ?? "Service API access",
                Description = definition?.Description
                    ?? "Grants access to the machine-to-machine Service API.",
                Enabled = true,
                // Kept out of the discovery document: advertising the admin
                // surface to every OIDC client that reads /.well-known adds no
                // value and tells anonymous callers what to aim at.
                ShowInDiscoveryDocument = false,
                // Platform-managed — the admin API refuses to edit or delete
                // these, so a rename can't silently break every service client.
                NonEditable = true
            }, cancellationToken);

            created.Add(name);
        }

        if (created.Count > 0)
        {
            _logger.LogInformation(
                "Provisioned service API scopes for tenant {TenantId}: {Scopes}",
                _tenantContext.TenantId ?? "(none)", string.Join(", ", created));
        }

        return created;
    }
}

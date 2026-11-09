using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Admin.Services;

/// <summary>
/// Ensures the tenant has the identity resource that releases the
/// <c>entitlements</c> claim.
///
/// Role claims are scope-gated: DefaultProfileService only emits a claim type
/// that some granted scope's UserClaims lists. Without this row, entitlements
/// assigned to a role would be stored, valid, and permanently invisible — the
/// exact dead end the catalog exists to remove. Provisioned on first use rather
/// than seeded, so tenants that never define an entitlement don't get the scope.
/// </summary>
public interface IEntitlementScopeProvisioner
{
    /// <summary>
    /// Creates the <c>entitlements</c> identity resource for the current tenant
    /// if it isn't there. Idempotent.
    /// </summary>
    /// <returns>True when it was created by this call.</returns>
    Task<bool> EnsureAsync(CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public class EntitlementScopeProvisioner : IEntitlementScopeProvisioner
{
    /// <summary>
    /// Scope name a client requests, and the claim type it releases. Same string
    /// deliberately — it mirrors how <c>permissions</c> works, so the thing you
    /// grant and the thing you receive have the same name.
    /// </summary>
    public const string ScopeName = OlusoRole.EntitlementClaimType;

    private readonly IResourceStore _resourceStore;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<EntitlementScopeProvisioner> _logger;

    public EntitlementScopeProvisioner(
        IResourceStore resourceStore,
        ITenantContext tenantContext,
        ILogger<EntitlementScopeProvisioner> logger)
    {
        _resourceStore = resourceStore;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    public async Task<bool> EnsureAsync(CancellationToken cancellationToken = default)
    {
        var existing = await _resourceStore.FindApiScopesByNameAsync(
            new[] { ScopeName }, cancellationToken);

        if (existing.Any())
        {
            return false;
        }

        // An ApiScope, not an IdentityResource: entitlements are authorization
        // attributes for the tenant's API to enforce, so they belong in the
        // access token (RFC 9068 §2.2.3.1). An identity resource would declare
        // them as claims about who the user is, which they aren't.
        await _resourceStore.AddApiScopeAsync(new ApiScope
        {
            Name = ScopeName,
            DisplayName = "Application entitlements",
            Description = "Permissions this tenant's applications enforce for the signed-in user.",
            Enabled = true,
            ShowInDiscoveryDocument = true,
            UserClaims = new List<ApiScopeClaim>
            {
                new() { Type = OlusoRole.EntitlementClaimType }
            }
        }, cancellationToken);

        _logger.LogInformation(
            "Provisioned '{Scope}' identity resource for tenant {TenantId}",
            ScopeName, _tenantContext.TenantId ?? "(none)");

        return true;
    }
}

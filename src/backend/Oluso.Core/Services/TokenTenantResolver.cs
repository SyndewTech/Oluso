using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Core.Services;

/// <summary>
/// Which tenant a user's token is for, and whether the tenant a request is routed to may issue them one.
/// </summary>
public interface ITokenTenantResolver
{
    /// <summary>
    /// The tenant a token for <paramref name="user"/> is for: the tenant the caller names, else the routed one, when
    /// the user may act there (home tenant, member, owner or admin of its organization, SuperAdmin); otherwise the
    /// user's home tenant.
    /// </summary>
    Task<string?> ResolveAsync(OlusoUserInfo user, string? requestedTenantId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the routed tenant may issue tokens to <paramref name="subjectId"/>. A tenant's host signs only its own
    /// tokens (and the platform's), so a user who has no access to the routed tenant (their token would be for
    /// another tenant) is refused there. True on the platform host and for an unknown subject (other checks decide).
    /// </summary>
    Task<bool> CanIssueOnRoutedTenantAsync(string subjectId, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class TokenTenantResolver : ITokenTenantResolver
{
    private readonly IOlusoUserService _users;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantStore _tenants;
    private readonly ITenantMembershipStore? _tenantMemberships;
    private readonly IOrganizationMembershipStore? _organizationMemberships;
    private readonly ILogger _logger;

    public TokenTenantResolver(
        IOlusoUserService users,
        ITenantContext tenantContext,
        ITenantStore tenants,
        ILogger<TokenTenantResolver> logger,
        ITenantMembershipStore? tenantMemberships = null,
        IOrganizationMembershipStore? organizationMemberships = null)
        : this(users, tenantContext, tenants, (ILogger)logger, tenantMemberships, organizationMemberships)
    {
    }

    internal TokenTenantResolver(
        IOlusoUserService users,
        ITenantContext tenantContext,
        ITenantStore tenants,
        ILogger logger,
        ITenantMembershipStore? tenantMemberships,
        IOrganizationMembershipStore? organizationMemberships)
    {
        _users = users;
        _tenantContext = tenantContext;
        _tenants = tenants;
        _logger = logger;
        _tenantMemberships = tenantMemberships;
        _organizationMemberships = organizationMemberships;
    }

    /// <remarks>
    /// The entitlement check is the point. Ambient context is set from the request, and not every mint path is behind
    /// the allowed_tenants gate — notably the token endpoint, which isn't an /api/ path and so resolves X-Tenant-Id
    /// without validating it. Roles and seats are self-limiting (a tenant you don't belong to yields none), but
    /// tenant_id is an assertion, so it must not be taken on the request's word alone.
    /// </remarks>
    public async Task<string?> ResolveAsync(OlusoUserInfo user, string? requestedTenantId = null, CancellationToken cancellationToken = default)
    {
        var ambient = requestedTenantId ?? (_tenantContext.HasTenant ? _tenantContext.TenantId : null);

        if (string.IsNullOrEmpty(ambient)
            || ambient == PlatformConstants.TenantId
            || ambient == user.TenantId)
        {
            return user.TenantId;
        }

        if (await MayActInAsync(user, ambient, cancellationToken))
        {
            return ambient;
        }

        _logger.LogWarning(
            "Token requested for tenant {AmbientTenant} but user {UserId} has no access there — " +
            "issuing for their home tenant {HomeTenant} instead",
            ambient, user.Id, user.TenantId ?? "(none)");

        return user.TenantId;
    }

    public async Task<bool> CanIssueOnRoutedTenantAsync(string subjectId, CancellationToken cancellationToken = default)
    {
        var routed = _tenantContext.HasTenant ? _tenantContext.TenantId : null;
        if (string.IsNullOrEmpty(routed) || routed == PlatformConstants.TenantId || string.IsNullOrEmpty(subjectId))
            return true;

        var user = (await _users.FindByIdsAcrossTenantsAsync(new[] { subjectId }, cancellationToken)).GetValueOrDefault(subjectId);
        if (user == null)
            return true;

        // The same outcome the token service signs by: a token for the routed tenant, for no tenant, or for the
        // platform can be issued here; one for another tenant cannot.
        var tokenTenant = await ResolveAsync(user, null, cancellationToken);
        return string.IsNullOrEmpty(tokenTenant) || tokenTenant == routed || tokenTenant == PlatformConstants.TenantId;
    }

    private async Task<bool> MayActInAsync(OlusoUserInfo user, string tenantId, CancellationToken cancellationToken)
    {
        // SuperAdmins operate across tenants without membership rows — the same exemption
        // TenantResolutionMiddleware makes.
        var isSuperAdmin = user.Roles?.Any(r =>
            r.Equals("SuperAdmin", StringComparison.OrdinalIgnoreCase)
            || r.Equals("SystemAdmin", StringComparison.OrdinalIgnoreCase)) == true;
        if (isSuperAdmin)
        {
            return true;
        }

        if (_tenantMemberships != null
            && await _tenantMemberships.GetByTenantAndUserAsync(tenantId, user.Id) != null)
        {
            return true;
        }

        // Organization owners and admins reach every tenant of their organization without a membership row in each.
        return _organizationMemberships != null
            && (await _tenants.GetByIdAsync(tenantId, cancellationToken))?.OrganizationId is { Length: > 0 } organizationId
            && await _organizationMemberships.IsAdminAsync(user.Id, organizationId);
    }
}

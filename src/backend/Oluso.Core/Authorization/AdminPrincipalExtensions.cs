using System.Security.Claims;
using System.Text.Json;

namespace Oluso.Core.Authorization;

/// <summary>
/// Reads the admin scope of a caller from claims: platform-wide SuperAdmin,
/// and the organisations the caller belongs to. Shared by the admin base
/// controller and by services that must scope results the same way.
/// </summary>
public static class AdminPrincipalExtensions
{
    private static readonly string[] SuperAdminRoles = { "SuperAdmin", "SystemAdmin", "super_admin", "platform_admin" };

    public static bool IsSuperAdmin(this ClaimsPrincipal user)
    {
        var superAdminClaim = user.FindFirst("super_admin")?.Value;
        if (string.Equals(superAdminClaim, "true", StringComparison.OrdinalIgnoreCase) || superAdminClaim == "1")
            return true;

        // Only a caller with no tenant scope can be a SuperAdmin.
        var tenantId = user.FindFirst("tenant_id")?.Value ?? user.FindFirst("tid")?.Value;
        if (!string.IsNullOrEmpty(tenantId))
            return false;

        return SuperAdminRoles.Any(user.HasAdminRole);
    }

    public static bool HasAdminRole(this ClaimsPrincipal user, string role)
        => user.GetAdminRoles().Contains(role);

    /// <summary>
    /// Roles from every claim type tokens use (<c>role</c>, <c>roles</c>,
    /// <see cref="ClaimTypes.Role"/>), including roles emitted as a JSON array.
    /// </summary>
    public static IReadOnlySet<string> GetAdminRoles(this ClaimsPrincipal user)
    {
        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var claims = user.FindAll("role").Concat(user.FindAll("roles")).Concat(user.FindAll(ClaimTypes.Role));
        foreach (var claim in claims)
        {
            if (string.IsNullOrWhiteSpace(claim.Value)) continue;

            if (claim.Value.StartsWith('['))
            {
                try
                {
                    foreach (var role in JsonSerializer.Deserialize<string[]>(claim.Value) ?? Array.Empty<string>())
                    {
                        if (!string.IsNullOrWhiteSpace(role)) roles.Add(role);
                    }
                }
                catch (JsonException)
                {
                    roles.Add(claim.Value);
                }
                continue;
            }

            roles.Add(claim.Value);
        }
        return roles;
    }

    /// <summary>The caller's role in one organisation, from the <c>org_role</c> claims (<c>orgId:role</c>).</summary>
    public static Domain.Entities.OrganizationRole? GetOrganizationRole(this ClaimsPrincipal user, string organizationId)
    {
        var value = user.FindAll("org_role")
            .Select(c => c.Value)
            .FirstOrDefault(v => v.StartsWith($"{organizationId}:", StringComparison.OrdinalIgnoreCase));
        if (string.IsNullOrEmpty(value)) return null;
        return Enum.TryParse<Domain.Entities.OrganizationRole>(value.Substring(organizationId.Length + 1), ignoreCase: true, out var role) ? role : null;
    }

    /// <summary>Owner or admin of the organisation named by the <c>current_org_id</c> claim.</summary>
    public static bool IsCurrentOrgAdmin(this ClaimsPrincipal user)
    {
        var organizationId = user.FindFirst("current_org_id")?.Value;
        if (string.IsNullOrEmpty(organizationId)) return false;
        var role = user.GetOrganizationRole(organizationId);
        return role is Domain.Entities.OrganizationRole.Owner or Domain.Entities.OrganizationRole.Admin;
    }

    /// <summary>Organisations the caller is a member of, from the <c>org_ids</c> claim.</summary>
    public static IReadOnlyList<string> GetOrganizationIds(this ClaimsPrincipal user)
    {
        var claim = user.FindFirst("org_ids")?.Value;
        if (string.IsNullOrEmpty(claim))
            return Array.Empty<string>();

        if (claim.StartsWith('['))
        {
            try
            {
                return JsonSerializer.Deserialize<string[]>(claim) ?? Array.Empty<string>();
            }
            catch (JsonException)
            {
                return Array.Empty<string>();
            }
        }

        return new[] { claim };
    }
}

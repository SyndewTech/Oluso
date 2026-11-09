using Oluso.Core.Protocols.Models;

namespace Oluso.Core.Authorization;

/// <summary>
/// Claim keys that the platform considers system-managed. No user / role /
/// client claim row may persist with one of these as its claim type.
///
/// SECURITY MODEL:
/// - Standard OIDC/JWT registered claims (sub, iss, aud, exp, …) are set
///   by the token service. A stored claim with one of these keys would
///   silently override the token-service value — privilege escalation
///   surface.
/// - System claims (tenant_id, role, permissions [plural], org_ids, etc.)
///   carry authorization meaning. The token service computes them from
///   the database; nothing should write them as a stored claim.
/// - <c>permission</c> (singular) is NOT reserved — that's the legitimate
///   <c>OlusoRoleClaim.ClaimType</c> for storing individual permissions
///   per role. Only the aggregated JWT key <c>permissions</c> (plural) is
///   blocked.
///
/// Wherever an existing OIDC constant defines the literal string (e.g.
/// <see cref="OidcConstants.StandardClaims.Subject"/>) we reference it
/// rather than redeclaring — keeps the auth vocabulary single-sourced.
/// </summary>
public static class ReservedClaimKeys
{
    // ── Names that originate from OidcConstants are re-exported here so
    //    callers don't have to know which assembly the literal lives in.

    public const string Subject       = OidcConstants.StandardClaims.Subject;        // "sub"
    public const string Issuer        = OidcConstants.StandardClaims.Issuer;         // "iss"
    public const string Audience      = OidcConstants.StandardClaims.Audience;       // "aud"
    public const string Expiration    = OidcConstants.StandardClaims.Expiration;     // "exp"
    public const string NotBefore     = OidcConstants.StandardClaims.NotBefore;      // "nbf"
    public const string IssuedAt      = OidcConstants.StandardClaims.IssuedAt;       // "iat"
    public const string JwtId         = OidcConstants.StandardClaims.JwtId;          // "jti"

    public const string AuthTime      = OidcConstants.StandardClaims.AuthTime;       // "auth_time"
    public const string Amr           = OidcConstants.StandardClaims.Amr;            // "amr"
    public const string Acr           = OidcConstants.StandardClaims.Acr;            // "acr"
    public const string Azp           = OidcConstants.StandardClaims.Azp;            // "azp"
    public const string Nonce         = OidcConstants.StandardClaims.Nonce;          // "nonce"
    public const string AtHash        = OidcConstants.StandardClaims.AtHash;         // "at_hash"
    public const string CHash         = OidcConstants.StandardClaims.CHash;          // "c_hash"
    public const string SessionId     = OidcConstants.StandardClaims.SessionId;      // "sid"
    /// <summary>"true" on tokens issued from a remembered (persistent) sign-in, so clients may keep them across browser restarts.</summary>
    public const string RememberMe    = "remember_me";
    public const string SHash         = "s_hash";

    public const string TenantId      = OidcConstants.StandardClaims.TenantId;       // "tenant_id"
    public const string Tid           = "tid";
    public const string Role          = OidcConstants.StandardClaims.Role;           // "role"

    /// <summary>
    /// Aggregated JWT permissions array claim. Computed by the token
    /// service from <see cref="PermissionEntry"/> values. Plural.
    /// </summary>
    public const string PermissionsAggregate = OidcConstants.Scopes.Permissions;     // "permissions"

    /// <summary>
    /// Per-role permission entry. Stored as <c>OlusoRoleClaim.ClaimType ==
    /// "permission"</c> with the permission string as ClaimValue (e.g.
    /// <c>users.read</c>, <c>&lt;module&gt;.users.write</c>).
    ///
    /// SECURITY NOTE — this is the legitimate path for assigning permissions
    /// to a role. The interceptor allows it on RoleClaim but BLOCKS it on
    /// UserClaim and ClientClaim (those have no legitimate need to carry a
    /// "permission" key — UserClaim would bypass the role layer; ClientClaim
    /// would leak into client tokens). Controllers / services must validate
    /// the ClaimValue against a permission namespace they own (see
    /// <c>HRPermissions.IsHRPermission</c> for the pattern). The interceptor
    /// cannot enforce that because it has no caller context.
    /// </summary>
    public const string PermissionEntry = "permission";

    public const string OrgIds        = "org_ids";
    public const string OrgRole       = "org_role";
    public const string OrgRoles      = "org_roles";
    public const string IsOrgAdmin    = "is_org_admin";
    public const string AllowedTenants = "allowed_tenants";

    /// <summary>
    /// Platform-wide privilege flag, computed at issuance from the user's
    /// system roles (DefaultProfileService, AuthController). TenantResolution
    /// Middleware grants access to <em>every</em> tenant on this claim alone,
    /// so a stored role/user/client claim carrying it would be a complete
    /// cross-tenant escalation.
    /// </summary>
    public const string SuperAdmin    = "super_admin";

    /// <summary>
    /// Per-user module seats — JSON array (or repeated multi-value)
    /// emitted by the token service from <c>UserModuleAccess</c>. Drives
    /// <c>RequireModuleSeat</c> and the workspace app switcher / nav
    /// visibility without any per-request DB call. A stored user/role/
    /// client claim with this key would let an attacker phantom-grant
    /// themselves access to a module — blocked here.
    /// </summary>
    public const string ModuleSeats   = "module_seats";

    /// <summary>
    /// Standard OIDC / JWT registered claim keys. Set by the token service;
    /// must never be persisted as a stored claim.
    /// </summary>
    public static readonly HashSet<string> Standard = new(StringComparer.OrdinalIgnoreCase)
    {
        Subject, Issuer, Audience, Expiration, NotBefore, IssuedAt, JwtId,
        AuthTime, Amr, Acr, Azp, Nonce, AtHash, CHash, SHash, SessionId,
    };

    /// <summary>
    /// System-level claim keys carrying authorization meaning. Computed
    /// by the token service from the database; storing them as user/role/
    /// client claims would override the computed value. <c>permission</c>
    /// (singular) is intentionally NOT here — that's the canonical key
    /// for storing role-permission rows.
    /// </summary>
    public static readonly HashSet<string> System = new(StringComparer.OrdinalIgnoreCase)
    {
        TenantId, Tid,
        Role,
        PermissionsAggregate,
        OrgIds, OrgRole, OrgRoles, IsOrgAdmin,
        AllowedTenants,
        SuperAdmin,
        ModuleSeats,
    };

    /// <summary>
    /// Combined view: every key blocked by <see cref="IsReserved"/>.
    /// Useful for UI hint pages or tests asserting completeness.
    /// </summary>
    public static readonly HashSet<string> All =
        new(Standard.Concat(System), StringComparer.OrdinalIgnoreCase);

    private static readonly HashSet<string> _canonical =
        All.Select(ReservedRoles.Canonicalize).ToHashSet();

    /// <summary>
    /// True when <paramref name="claimType"/> matches a reserved key
    /// under canonical comparison: lowercase + non-alphanumerics stripped.
    /// Catches <c>tenant_id</c>, <c>tenantId</c>, <c>Tenant-Id</c>,
    /// <c>TENANT_ID</c> alike. Empty / null returns false.
    /// </summary>
    public static bool IsReserved(string? claimType)
    {
        if (string.IsNullOrWhiteSpace(claimType)) return false;
        return _canonical.Contains(ReservedRoles.Canonicalize(claimType));
    }

    private static readonly string _canonicalPermissionEntry =
        ReservedRoles.Canonicalize(PermissionEntry);

    /// <summary>
    /// True when <paramref name="claimType"/> is the permission-entry key
    /// (allowed on <c>OlusoRoleClaim</c>, blocked on <c>OlusoUserClaim</c>
    /// and <c>ClientClaim</c>). Same canonical comparison as
    /// <see cref="IsReserved"/>.
    /// </summary>
    public static bool IsPermissionEntry(string? claimType)
    {
        if (string.IsNullOrWhiteSpace(claimType)) return false;
        return ReservedRoles.Canonicalize(claimType) == _canonicalPermissionEntry;
    }
}

/// <summary>
/// Thrown by the platform's SaveChanges interceptor when a user / role /
/// client claim row would persist with a reserved claim key.
/// </summary>
public class ReservedClaimKeyException : InvalidOperationException
{
    public string ClaimType { get; }
    public string EntityName { get; }

    public ReservedClaimKeyException(string entityName, string claimType)
        : base($"Cannot persist {entityName} with reserved claim type '{claimType}': this key is system-managed and would override values computed by the token service.")
    {
        EntityName = entityName;
        ClaimType = claimType;
    }
}

/// <summary>
/// Thrown by the platform's SaveChanges interceptor when a role-claim row
/// is being persisted with <c>ClaimType == "permission"</c> but
/// <c>ClaimValue</c> is not a known permission. Catches typos and
/// fabricated permission strings that would otherwise grant phantom
/// authorization (or silently no-op) at runtime.
/// </summary>
public class UnregisteredPermissionException : InvalidOperationException
{
    public string Value { get; }

    public UnregisteredPermissionException(string value)
        : base($"Cannot persist role permission '{value}': not a registered permission. Wildcards are allowed only as '<prefix>.*' where the prefix matches an existing permission namespace; lone '*' is never allowed.")
    {
        Value = value;
    }
}

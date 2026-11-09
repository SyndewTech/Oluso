using Oluso.Core.Domain.Entities;

namespace Oluso.Enterprise.Scim.Entities;

/// <summary>
/// A downstream SCIM 2.0 service provider that this tenant pushes its users
/// (and optionally groups) to. This is the outbound counterpart of
/// <see cref="ScimClient"/>: a <c>ScimClient</c> is an identity provider that
/// calls us, a <c>ScimProvider</c> is a system we call.
/// </summary>
public class ScimProvider : TenantEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Display name for the target (e.g., "Stalwart Mail", "Wiki")
    /// </summary>
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>
    /// Disabled providers accept no new queue items and their pending items
    /// are left untouched until re-enabled.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Target-specific behaviour profile. See <see cref="ScimProviderFlavors"/>.
    /// </summary>
    public string Flavor { get; set; } = ScimProviderFlavors.Generic;

    /// <summary>
    /// SCIM 2.0 base URL of the target, e.g. <c>https://mail.example.org/scim/v2</c>.
    /// </summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// How we authenticate to the target. See <see cref="ScimProviderAuthTypes"/>.
    /// The secret itself (bearer token or OAuth client secret) is stored
    /// encrypted in tenant configuration, never on this row.
    /// </summary>
    public string AuthType { get; set; } = ScimProviderAuthTypes.Bearer;

    /// <summary>
    /// OAuth 2.0 token endpoint, when <see cref="AuthType"/> is client credentials.
    /// </summary>
    public string? OAuthTokenEndpoint { get; set; }

    public string? OAuthClientId { get; set; }

    public string? OAuthScope { get; set; }

    /// <summary>
    /// When the stored secret was last set. Null means no secret has been
    /// configured yet and pushes will fail authentication.
    /// </summary>
    public DateTime? SecretUpdatedAt { get; set; }

    /// <summary>
    /// The inbound <see cref="ScimClient"/> that is this same system, when it also
    /// provisions into the tenant. Changes that client makes are not pushed back
    /// to this provider, which already has them.
    /// </summary>
    public string? LinkedScimClientId { get; set; }

    public bool SyncUsers { get; set; } = true;

    /// <summary>
    /// Push groups with their members. Off by default: mail-oriented targets
    /// turn every group into a distribution address.
    /// </summary>
    public bool SyncGroups { get; set; }

    /// <summary>
    /// What a SCIM Group is built from: roles, teams, or both. See
    /// <see cref="Outbound.ScimProviderGroupSources"/>.
    /// </summary>
    public string GroupSource { get; set; } = Outbound.ScimProviderGroupSources.Roles;

    /// <summary>
    /// Comma-separated role ids. Together with <see cref="ScopeTeamIds"/> this
    /// limits provisioning: a user is provisioned when they hold one of these
    /// roles or belong to one of those teams, and only these roles are pushed
    /// as groups. Both empty means every user in the tenant.
    /// </summary>
    public string? ScopeRoleIds { get; set; }

    /// <summary>
    /// Comma-separated team ids; membership includes sub-teams. See <see cref="ScopeRoleIds"/>.
    /// </summary>
    public string? ScopeTeamIds { get; set; }

    /// <summary>
    /// Which internal field becomes the SCIM <c>userName</c>. See
    /// <see cref="ScimProviderUserNameSources"/>.
    /// </summary>
    public string UserNameSource { get; set; } = ScimProviderUserNameSources.UserName;

    /// <summary>
    /// Scriban template for the <c>userName</c> when <see cref="UserNameSource"/>
    /// is <c>expression</c>, e.g. <c>{{ user.first_name | string.downcase }}.{{ user.last_name | string.downcase }}@mail.example.org</c>.
    /// </summary>
    public string? UserNameExpression { get; set; }

    /// <summary>
    /// What happens on the target when a user is deleted here or leaves the
    /// provisioning scope. See <see cref="ScimProviderDeprovisionModes"/>.
    /// </summary>
    public string DeprovisionMode { get; set; } = ScimProviderDeprovisionModes.Deactivate;

    /// <summary>
    /// Full reconcile cadence in minutes. 0 disables scheduled reconciliation
    /// (manual runs from the admin API still work).
    /// </summary>
    public int ReconcileIntervalMinutes { get; set; } = 360;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? UpdatedAt { get; set; }

    public DateTime? LastReconcileAt { get; set; }

    public string? LastReconcileSummary { get; set; }

    public DateTime? LastSuccessAt { get; set; }

    public DateTime? LastErrorAt { get; set; }

    public string? LastError { get; set; }

    public long SuccessCount { get; set; }

    public long ErrorCount { get; set; }

    public IReadOnlyList<string> GetScopeRoleIds() => SplitIds(ScopeRoleIds);

    public IReadOnlyList<string> GetScopeTeamIds() => SplitIds(ScopeTeamIds);

    /// <summary>True when any role or team scope is configured.</summary>
    public bool HasUserScope => !string.IsNullOrWhiteSpace(ScopeRoleIds) || !string.IsNullOrWhiteSpace(ScopeTeamIds);

    private static IReadOnlyList<string> SplitIds(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? Array.Empty<string>()
            : value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

public static class ScimProviderFlavors
{
    public const string Generic = "generic";
    public const string Stalwart = "stalwart";
}

public static class ScimProviderAuthTypes
{
    public const string Bearer = "bearer";
    public const string OAuthClientCredentials = "oauth_client_credentials";
}

public static class ScimProviderUserNameSources
{
    public const string UserName = "userName";
    public const string Email = "email";
    public const string Expression = "expression";

    public static readonly string[] All = { UserName, Email, Expression };
}

public static class ScimProviderDeprovisionModes
{
    public const string Deactivate = "deactivate";
    public const string Delete = "delete";
}

public static class ScimResourceTypes
{
    public const string User = "User";
    public const string Group = "Group";
}

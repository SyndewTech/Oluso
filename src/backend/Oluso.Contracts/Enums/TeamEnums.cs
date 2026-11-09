using System.Text.Json.Serialization;

namespace Oluso.Core.Domain.Entities;

/// <summary>
/// Coarse classification of a team's purpose. Drives default UI filters,
/// dashboard widgets, and (eventually) feature gating — e.g., the billing
/// dashboard surfaces only <see cref="TeamType.Billing"/> teams.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TeamType
{
    /// <summary>An ongoing functional group (Finance, Audit, GM Office).</summary>
    Functional = 0,

    /// <summary>A distribution list / notification target.</summary>
    Distribution = 1,

    /// <summary>A short-lived project team.</summary>
    Project = 2,

    /// <summary>A team that purchases / inherits a billing subscription.</summary>
    Billing = 3,

    /// <summary>None of the above; tenant-defined.</summary>
    Custom = 99,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TeamMemberRole
{
    /// <summary>Regular member — included in approvals and entitlement, can't edit team.</summary>
    Member = 0,

    /// <summary>
    /// Team lead — same scope as Member for approvals / entitlement, plus the
    /// ability to add / remove members and edit team metadata. Multiple leads
    /// allowed.
    /// </summary>
    Lead = 1,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TeamVisibility
{
    /// <summary>Listed for everyone in the tenant. Default.</summary>
    TenantWide = 0,

    /// <summary>Visible only to members + tenant admins. Useful for sensitive
    /// teams (e.g., a confidential review committee).</summary>
    Private = 1,
}

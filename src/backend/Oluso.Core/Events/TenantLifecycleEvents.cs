using System.Diagnostics.CodeAnalysis;
using Oluso.Core.Domain.Entities;

namespace Oluso.Core.Events;

/// <summary>
/// Base of the platform-scoped tenant lifecycle events. Each carries a snapshot of the
/// tenant as it is after the change, so a subscriber (a downstream product that mirrors
/// tenants) can apply it as a whole without calling back.
/// </summary>
public abstract class AdminTenantLifecycleEvent : AdminActionEvent
{
    public override string ResourceType => "Tenant";

    /// <summary>The tenant identifier (slug used in URLs and subdomains).</summary>
    public string TenantIdentifier { get; init; } = string.Empty;

    public string? TenantName { get; init; }

    public string? TenantDisplayName { get; init; }

    /// <summary>Whether the tenant is enabled after the change.</summary>
    public bool TenantEnabled { get; init; } = true;

    public string? TenantCustomDomain { get; init; }

    /// <summary>Comma-separated allowed email domains (null = any).</summary>
    public string? TenantAllowedEmailDomains { get; init; }

    protected AdminTenantLifecycleEvent()
    {
    }

    /// <summary>Fills the snapshot (and the admin fields) from <paramref name="tenant"/>.</summary>
    [SetsRequiredMembers]
    protected AdminTenantLifecycleEvent(Tenant tenant, string adminUserId, string? adminUserName)
    {
        TenantId = tenant.Id;
        AdminUserId = adminUserId;
        AdminUserName = adminUserName;
        ResourceId = tenant.Id;
        ResourceName = tenant.Name;
        TenantIdentifier = tenant.Identifier;
        TenantName = tenant.Name;
        TenantDisplayName = tenant.DisplayName;
        TenantEnabled = tenant.Enabled;
        TenantCustomDomain = tenant.CustomDomain;
        TenantAllowedEmailDomains = tenant.AllowedEmailDomains;
    }
}

/// <summary>Raised when a tenant's settings (name, display name, domains, …) are changed.</summary>
[WebhookEvent("Admin Tenant Updated", Description = "Triggered when a tenant's settings are changed")]
public class AdminTenantUpdatedEvent : AdminTenantLifecycleEvent
{
    public AdminTenantUpdatedEvent() { }

    [SetsRequiredMembers]
    public AdminTenantUpdatedEvent(Tenant tenant, string adminUserId, string? adminUserName = null)
        : base(tenant, adminUserId, adminUserName) { }

    public override string? WebhookEventType => "admin.tenant_updated";
}

/// <summary>Raised when a tenant is disabled (its users can no longer sign in).</summary>
[WebhookEvent("Admin Tenant Suspended", Description = "Triggered when a tenant is disabled")]
public class AdminTenantSuspendedEvent : AdminTenantLifecycleEvent
{
    public AdminTenantSuspendedEvent() { }

    [SetsRequiredMembers]
    public AdminTenantSuspendedEvent(Tenant tenant, string adminUserId, string? adminUserName = null)
        : base(tenant, adminUserId, adminUserName) { }

    public override string? WebhookEventType => "admin.tenant_suspended";
}

/// <summary>Raised when a disabled tenant is enabled again.</summary>
[WebhookEvent("Admin Tenant Reactivated", Description = "Triggered when a disabled tenant is enabled again")]
public class AdminTenantReactivatedEvent : AdminTenantLifecycleEvent
{
    public AdminTenantReactivatedEvent() { }

    [SetsRequiredMembers]
    public AdminTenantReactivatedEvent(Tenant tenant, string adminUserId, string? adminUserName = null)
        : base(tenant, adminUserId, adminUserName) { }

    public override string? WebhookEventType => "admin.tenant_reactivated";
}

/// <summary>Raised after a tenant is deleted. The snapshot is the tenant as it was.</summary>
[WebhookEvent("Admin Tenant Deleted", Description = "Triggered when a tenant is deleted")]
public class AdminTenantDeletedEvent : AdminTenantLifecycleEvent
{
    public AdminTenantDeletedEvent() { }

    [SetsRequiredMembers]
    public AdminTenantDeletedEvent(Tenant tenant, string adminUserId, string? adminUserName = null)
        : base(tenant, adminUserId, adminUserName) { }

    public override string? WebhookEventType => "admin.tenant_deleted";
}

using System.Security.Claims;
using Oluso.Core.Domain.Entities;

[assembly: Oluso.Core.Events.WebhookProvider("core", "Oluso Core")]

namespace Oluso.Core.Events;

/// <summary>
/// Base class for all Oluso events.
/// Enterprise packages can derive their own events from this class.
/// </summary>
public abstract class OlusoEvent
{
    /// <summary>
    /// Unique ID for this event instance
    /// </summary>
    [TemplateIgnore]
    public string Id { get; } = Guid.NewGuid().ToString();

    /// <summary>
    /// When the event occurred
    /// </summary>
    [TemplateIgnore]
    public DateTime Timestamp { get; } = DateTime.UtcNow;

    /// <summary>
    /// Tenant context for multi-tenant scenarios
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// Activity/correlation ID for distributed tracing
    /// </summary>
    [TemplateIgnore]
    public string? ActivityId { get; set; }

    /// <summary>
    /// What caused the event. Raisers that know set it; otherwise
    /// <see cref="IOlusoEventService.RaiseAsync"/> fills it from the ambient
    /// <see cref="EventOriginScope"/>, the request, <see cref="ImpliedOrigin"/>,
    /// or the system, in that order.
    /// </summary>
    [TemplateIgnore]
    public EventOrigin? Origin { get; set; }

    /// <summary>
    /// The origin the event's own fields describe, used when neither the raiser,
    /// an origin scope nor an authenticated request supplies one.
    /// </summary>
    protected internal virtual EventOrigin? ImpliedOrigin() => null;

    /// <summary>
    /// Event category for filtering and routing
    /// </summary>
    public abstract string Category { get; }

    /// <summary>
    /// Subscription scope. Defaults to Tenant — override to Platform for
    /// admin actions, lifecycle events, and anything tenant subscribers
    /// should never receive. Override to Both for events that fire from
    /// either context (login, logout, token issuance).
    /// </summary>
    public virtual EventScope Scope => EventScope.Tenant;

    /// <summary>
    /// Event type identifier (e.g., "UserSignedIn", "PasskeyRegistered")
    /// </summary>
    public virtual string EventType => GetType().Name;

    /// <summary>
    /// Whether this event represents a failure
    /// </summary>
    public virtual bool IsFailure => false;

    /// <summary>
    /// The corresponding webhook event type, if any (e.g., "user.signed_in")
    /// Returns null if this event should not trigger webhooks.
    /// </summary>
    public virtual string? WebhookEventType => null;

    /// <summary>
    /// Whether the audit log should record this event. False for events that are
    /// mechanisms rather than facts: cancellable pre-hooks that precede the real
    /// event, and scheduler ticks. Webhooks and other sinks still receive them.
    /// </summary>
    public virtual bool IsAuditable => true;
}

/// <summary>
/// Standard event categories
/// </summary>
public static class EventCategories
{
    public const string Authentication = "Authentication";
    public const string User = "User";
    public const string Token = "Token";
    public const string Client = "Client";
    public const string Security = "Security";
    public const string Role = "Role";
    public const string Resource = "Resource";
    public const string Settings = "Settings";
    public const string Admin = "Admin";
    public const string System = "System";
    public const string Team = "Team";
    public const string Conversation = "Conversation";
}

/// <summary>
/// Subscription scope of an event — controls which webhook endpoints are
/// allowed to subscribe and where the dispatcher will route the payload.
/// </summary>
public enum EventScope
{
    /// <summary>
    /// Tenant-owned events. Routed to the tenant's webhook endpoints.
    /// Tenant admins can subscribe in their own tenant.
    /// </summary>
    Tenant = 0,

    /// <summary>
    /// Platform-owned events (admin actions, org/tenant lifecycle, system).
    /// Routed to platform-tenant endpoints only. Tenant admins cannot
    /// subscribe — visible only to SuperAdmin platform endpoints.
    /// </summary>
    Platform = 1,

    /// <summary>
    /// Events that fire from both contexts (login, logout, token issuance).
    /// Routed to whichever tenant the payload carries; both platform and
    /// tenant admins can subscribe.
    /// </summary>
    Both = 2,
}

/// <summary>
/// Raised before a user signs in. Can be cancelled to prevent sign-in.
/// </summary>
public class UserSigningInEvent : OlusoEvent
{
    public override EventScope Scope => EventScope.Both;
    public override string Category => EventCategories.Authentication;
    // Pre-hook: subscribers may veto; the audit row is the event that follows.
    public override bool IsAuditable => false;

    public required string Username { get; init; }
    public required string ClientId { get; init; }
    public string? IpAddress { get; init; }
    public string? UserAgent { get; init; }

    /// <summary>
    /// Set to true to prevent sign-in
    /// </summary>
    public bool Cancel { get; set; }

    /// <summary>
    /// Reason for cancellation (shown to user)
    /// </summary>
    public string? CancelReason { get; set; }
}

/// <summary>
/// Raised after a successful sign-in
/// </summary>
[WebhookEvent("Login Success", Description = "Triggered when a user successfully logs in")]
public class UserSignedInEvent : OlusoEvent
{
    public override EventScope Scope => EventScope.Both;
    public override string Category => EventCategories.Authentication;
    public override string? WebhookEventType => "auth.login_success";

    public required string SubjectId { get; init; }
    public required string Username { get; init; }
    public required string ClientId { get; init; }
    public required string AuthenticationMethod { get; init; }
    public string? IpAddress { get; init; }
    public string? SessionId { get; init; }
}

/// <summary>
/// Raised when sign-in fails
/// </summary>
[WebhookEvent("Login Failed", Description = "Triggered when a login attempt fails")]
public class UserSignInFailedEvent : OlusoEvent
{
    public override EventScope Scope => EventScope.Both;
    public override string Category => EventCategories.Authentication;
    public override bool IsFailure => true;
    public override string? WebhookEventType => "auth.login_failed";

    public required string Username { get; init; }
    public required string ClientId { get; init; }
    public required string FailureReason { get; init; }
    public string? IpAddress { get; init; }
}

/// <summary>
/// Raised at each hop of an organisation sign-in that starts on the platform
/// login page: <c>start</c> (platform sends the browser to the tenant, and the
/// tenant accepts the request), <c>complete</c> (tenant authenticated the user
/// and minted a hand-off code) and <c>redeem</c> (platform exchanged the code and
/// signed the user in). <see cref="OlusoEvent.TenantId"/> is the target tenant.
/// </summary>
public class OrganizationSignInEvent : OlusoEvent
{
    public const string StageBegin = "begin";
    public const string StageStart = "start";
    public const string StageComplete = "complete";
    public const string StageRedeem = "redeem";

    public override EventScope Scope => EventScope.Both;
    public override string Category => EventCategories.Authentication;
    public override bool IsFailure => !Succeeded;

    public required string Stage { get; init; }
    public required bool Succeeded { get; init; }
    public string? Error { get; init; }
    public string? SubjectId { get; init; }
    public string? ClientId { get; init; }
    public string? IdentityProvider { get; init; }
    public string? IpAddress { get; init; }
}

/// <summary>
/// Raised when a user signs out
/// </summary>
[WebhookEvent("User Logout", Description = "Triggered when a user logs out")]
public class UserSignedOutEvent : OlusoEvent
{
    public override EventScope Scope => EventScope.Both;
    public override string Category => EventCategories.Authentication;
    public override string? WebhookEventType => "auth.logout";

    public required string SubjectId { get; init; }
    public string? SessionId { get; init; }
}

/// <summary>
/// Raised before a token is issued. Can be denied to prevent token issuance.
/// </summary>
public class TokenIssuingEvent : OlusoEvent
{
    public override EventScope Scope => EventScope.Both;
    public override string Category => EventCategories.Token;
    // Pre-hook: subscribers may veto; the audit row is the event that follows.
    public override bool IsAuditable => false;

    public required string ClientId { get; init; }
    public string? SubjectId { get; init; }
    public required IEnumerable<string> Scopes { get; init; }
    public required string GrantType { get; init; }

    /// <summary>
    /// Additional claims to add to the token
    /// </summary>
    public List<Claim> AdditionalClaims { get; } = new();

    /// <summary>
    /// Set to true to deny token issuance
    /// </summary>
    public bool Deny { get; set; }
    public string? DenyReason { get; set; }
}

/// <summary>
/// Raised after a token is issued
/// </summary>
[WebhookEvent("Token Issued", Description = "Triggered when a token is issued")]
public class TokenIssuedEvent : OlusoEvent
{
    public override EventScope Scope => EventScope.Both;
    public override string Category => EventCategories.Token;
    public override string? WebhookEventType => "auth.token_issued";

    public required string ClientId { get; init; }
    public string? SubjectId { get; init; }
    public required IEnumerable<string> Scopes { get; init; }
    public required string TokenType { get; init; }
}

/// <summary>
/// Raised when a token is revoked
/// </summary>
[WebhookEvent("Token Revoked", Description = "Triggered when a token is revoked")]
public class TokenRevokedEvent : OlusoEvent
{
    public override EventScope Scope => EventScope.Both;
    public override string Category => EventCategories.Token;
    public override string? WebhookEventType => "auth.token_revoked";

    public required string ClientId { get; init; }
    public string? SubjectId { get; init; }
    public string? TokenType { get; init; }
}

/// <summary>
/// Raised when user consent is granted
/// </summary>
[WebhookEvent("Consent Granted", Description = "Triggered when user consent is granted")]
public class ConsentGrantedEvent : OlusoEvent
{
    public override EventScope Scope => EventScope.Both;
    public override string Category => EventCategories.Security;
    public override string? WebhookEventType => "security.consent_granted";

    public required string SubjectId { get; init; }
    public required string ClientId { get; init; }
    public required IEnumerable<string> Scopes { get; init; }
    public bool RememberConsent { get; init; }
}

/// <summary>
/// Raised when user consent is denied
/// </summary>
[WebhookEvent("Consent Revoked", Description = "Triggered when user consent is denied")]
public class ConsentDeniedEvent : OlusoEvent
{
    public override EventScope Scope => EventScope.Both;
    public override string Category => EventCategories.Security;
    public override string? WebhookEventType => "security.consent_revoked";

    public required string SubjectId { get; init; }
    public required string ClientId { get; init; }
}

/// <summary>
/// Raised when a new user registers
/// </summary>
[WebhookEvent("User Created", Description = "Triggered when a new user is registered or created", EnabledByDefault = true)]
public class UserRegisteredEvent : OlusoEvent
{
    public override string Category => EventCategories.User;
    public override string? WebhookEventType => "user.created";

    public required string SubjectId { get; init; }
    public required string Email { get; init; }
    public string? Username { get; init; }
    public string? ClientId { get; init; }

    /// <summary>The user journey policy the user signed up through, when they signed up through one.</summary>
    public string? PolicyId { get; init; }
}

/// <summary>
/// Raised when MFA is completed
/// </summary>
public class MfaCompletedEvent : OlusoEvent
{
    public override EventScope Scope => EventScope.Both;
    public override string Category => EventCategories.Authentication;

    public required string SubjectId { get; init; }
    public required string MfaMethod { get; init; }
    public bool Success { get; init; }
    public override bool IsFailure => !Success;
}

#region User Profile Events

/// <summary>
/// Raised when a user profile is updated
/// </summary>
[WebhookEvent("User Updated", Description = "Triggered when a user's profile is updated", EnabledByDefault = true)]
public class UserUpdatedEvent : OlusoEvent
{
    public override string Category => EventCategories.User;
    public override string? WebhookEventType => "user.updated";

    public required string SubjectId { get; init; }
    public string? Email { get; init; }
    public Dictionary<string, string>? ChangedFields { get; init; }
}

/// <summary>
/// Raised when a user is deleted
/// </summary>
[WebhookEvent("User Deleted", Description = "Triggered when a user account is deleted", EnabledByDefault = true)]
public class UserDeletedEvent : OlusoEvent
{
    public override string Category => EventCategories.User;
    public override string? WebhookEventType => "user.deleted";

    public required string SubjectId { get; init; }
    public string? Email { get; init; }
    public bool DeletedByAdmin { get; init; }
}

/// <summary>
/// Raised when a user is given a role in a tenant, whoever gave it: the
/// <see cref="OlusoEvent.Origin"/> says whether that was an admin, a service
/// client, SCIM, a journey or an invitation.
/// </summary>
[WebhookEvent("User Role Assigned", Description = "Triggered when a user is given a role in a tenant")]
public class UserRoleAssignedEvent : OlusoEvent
{
    public override string Category => EventCategories.Role;
    public override string? WebhookEventType => "user.role_assigned";

    public required string SubjectId { get; init; }
    public required string RoleId { get; init; }
    public required string RoleName { get; init; }
}

/// <summary>
/// Raised when a role is taken from a user in a tenant, whoever took it.
/// </summary>
[WebhookEvent("User Role Removed", Description = "Triggered when a role is removed from a user in a tenant")]
public class UserRoleRemovedEvent : OlusoEvent
{
    public override string Category => EventCategories.Role;
    public override string? WebhookEventType => "user.role_removed";

    public required string SubjectId { get; init; }
    public required string RoleId { get; init; }
    public required string RoleName { get; init; }
}

/// <summary>
/// Raised when a user's email is verified
/// </summary>
[WebhookEvent("Email Verified", Description = "Triggered when a user verifies their email address")]
public class UserEmailVerifiedEvent : OlusoEvent
{
    public override string Category => EventCategories.User;
    public override string? WebhookEventType => "user.email_verified";

    public required string SubjectId { get; init; }
    public required string Email { get; init; }
}

/// <summary>
/// Raised when a user's password is changed
/// </summary>
[WebhookEvent("Password Changed", Description = "Triggered when a user's password is changed, by the user or by an admin")]
public class UserPasswordChangedEvent : OlusoEvent
{
    public override string Category => EventCategories.User;
    public override string? WebhookEventType => "user.password_changed";

    public required string SubjectId { get; init; }
    public bool ChangedByAdmin { get; init; }
}

/// <summary>
/// Raised when a user account is locked
/// </summary>
[WebhookEvent("User Locked Out", Description = "Triggered when a user account is locked due to failed login attempts", EnabledByDefault = true)]
public class UserLockedOutEvent : OlusoEvent
{
    public override EventScope Scope => EventScope.Both;
    public override string Category => EventCategories.Security;
    public override string? WebhookEventType => "user.locked_out";
    public override bool IsFailure => true;

    public required string SubjectId { get; init; }
    public string? Reason { get; init; }
    public DateTime? LockoutEnd { get; init; }
}

#endregion

#region Admin Events

/// <summary>
/// Base class for admin actions performed via the Admin API.
/// Category is derived from ResourceType so each subclass automatically
/// reports a meaningful category (Role, Client, Resource, Settings, …)
/// without hand-written overrides — the ResourceType is already required
/// to be declared by every concrete subclass, so it's the natural source
/// of truth and keeps the Activity Logs filter dropdown actionable.
/// </summary>
public abstract class AdminActionEvent : OlusoEvent
{
    public override EventScope Scope => EventScope.Platform;

    public override string Category => ResourceType switch
    {
        "User" or "UserRole" => EventCategories.User,
        "Role" or "ModuleAdmin" => EventCategories.Role,
        "Client" => EventCategories.Client,
        "Resource" or "ApiResource" or "ApiScope" => EventCategories.Resource,
        "SigningKey" or "AuditLog" => EventCategories.Security,
        "Tenant" or "TenantSettings" or "Organization" or "IdentityProvider"
            or "Webhook" or "JourneyPolicy" or "Feature" => EventCategories.Settings,
        _ => EventCategories.Admin
    };

    /// <summary>
    /// Admin user who performed the action
    /// </summary>
    public required string AdminUserId { get; init; }

    /// <summary>
    /// Admin user's display name/email
    /// </summary>
    public string? AdminUserName { get; init; }

    /// <summary>
    /// IP address of the admin making the request
    /// </summary>
    public string? IpAddress { get; init; }

    /// <summary>
    /// Resource type being modified (User, Client, Role, etc.)
    /// </summary>
    public abstract string ResourceType { get; }

    /// <summary>
    /// ID of the resource being modified
    /// </summary>
    public string? ResourceId { get; init; }

    /// <summary>
    /// Name of the resource for display purposes
    /// </summary>
    public string? ResourceName { get; init; }

    /// <summary>
    /// The acting admin. Raisers without a user pass a system label as
    /// <see cref="AdminUserId"/> ("system", or a source such as an invitation),
    /// which is a system origin.
    /// </summary>
    protected internal override EventOrigin? ImpliedOrigin() =>
        string.IsNullOrEmpty(AdminUserId) || string.Equals(AdminUserId, "system", StringComparison.OrdinalIgnoreCase)
            ? EventOrigin.System(AdminUserName)
            : EventOrigin.Admin(AdminUserId, AdminUserName, IpAddress);
}

/// <summary>
/// Raised when an admin creates a client
/// </summary>
[WebhookEvent("Admin Client Created", Description = "Triggered when an admin creates a client")]
public class AdminClientCreatedEvent : AdminActionEvent
{
    public override string ResourceType => "Client";
    public override string? WebhookEventType => "admin.client_created";

    public required string ClientId { get; init; }
}

/// <summary>
/// Raised when an admin updates a client
/// </summary>
[WebhookEvent("Admin Client Updated", Description = "Triggered when an admin updates a client")]
public class AdminClientUpdatedEvent : AdminActionEvent
{
    public override string ResourceType => "Client";
    public override string? WebhookEventType => "admin.client_updated";

    public required string ClientId { get; init; }
    public List<string>? ChangedFields { get; init; }
}

/// <summary>
/// Raised when an admin deletes a client
/// </summary>
[WebhookEvent("Admin Client Deleted", Description = "Triggered when an admin deletes a client")]
public class AdminClientDeletedEvent : AdminActionEvent
{
    public override string ResourceType => "Client";
    public override string? WebhookEventType => "admin.client_deleted";

    public required string ClientId { get; init; }
}

/// <summary>
/// Raised when an admin creates a role
/// </summary>
[WebhookEvent("Admin Role Created", Description = "Triggered when an admin creates a role")]
public class AdminRoleCreatedEvent : AdminActionEvent
{
    public override string ResourceType => "Role";
    public override string? WebhookEventType => "admin.role_created";

    public required string RoleName { get; init; }
}

/// <summary>
/// Raised when an admin updates a role
/// </summary>
[WebhookEvent("Admin Role Updated", Description = "Triggered when an admin updates a role")]
public class AdminRoleUpdatedEvent : AdminActionEvent
{
    public override string ResourceType => "Role";
    public override string? WebhookEventType => "admin.role_updated";

    public required string RoleName { get; init; }
}

/// <summary>
/// Raised when an admin deletes a role
/// </summary>
[WebhookEvent("Admin Role Deleted", Description = "Triggered when an admin deletes a role")]
public class AdminRoleDeletedEvent : AdminActionEvent
{
    public override string ResourceType => "Role";
    public override string? WebhookEventType => "admin.role_deleted";

    public required string RoleName { get; init; }
}

/// <summary>
/// A user gained or lost a module's administration permission in a tenant.
/// Carries the module's other administrators in that tenant, who are notified.
/// </summary>
public abstract class ModuleAdminPermissionChangedEvent : AdminActionEvent
{
    public override string ResourceType => "ModuleAdmin";

    /// <summary>
    /// The module administration permission (e.g., "<module>.administration")
    /// </summary>
    public required string ModuleAdminPermission { get; init; }

    /// <summary>
    /// The module display name
    /// </summary>
    public required string ModuleName { get; init; }

    /// <summary>
    /// The user who gained or lost the module admin permission
    /// </summary>
    public required string TargetUserId { get; init; }

    /// <summary>
    /// Email of that user
    /// </summary>
    public string? TargetUserEmail { get; init; }

    /// <summary>
    /// Display name of that user
    /// </summary>
    public string? TargetUserName { get; init; }

    /// <summary>
    /// The role that carries the module admin permission
    /// </summary>
    public required string RoleName { get; init; }

    /// <summary>
    /// The module's other administrators in the tenant, who are notified
    /// </summary>
    public IReadOnlyList<string> ExistingModuleAdminIds { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Emails of those administrators, for the notification email step
    /// </summary>
    public IReadOnlyList<string> ExistingModuleAdminEmails { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Raised when a module administration permission is assigned to a user.
/// This is a security-sensitive event that should notify existing module admins.
/// </summary>
[WebhookEvent("Module Admin Permission Assigned", Description = "Triggered when a module administration permission is assigned to a user")]
public class ModuleAdminPermissionAssignedEvent : ModuleAdminPermissionChangedEvent
{
    public override string? WebhookEventType => "security.module_admin_assigned";
}

/// <summary>
/// Raised when a user loses a module administration permission in a tenant —
/// the last role carrying it there was removed. Notifies the remaining admins.
/// </summary>
[WebhookEvent("Module Admin Permission Removed", Description = "Triggered when a user loses a module administration permission")]
public class ModuleAdminPermissionRemovedEvent : ModuleAdminPermissionChangedEvent
{
    public override string? WebhookEventType => "security.module_admin_removed";
}

/// <summary>
/// Raised when a new tenant is created via the Admin API.
/// Triggers module seeding (email templates, default data) via TenantSeedEventSink.
/// </summary>
[WebhookEvent("Admin Tenant Created", Description = "Triggered when a new tenant is created via the Admin API")]
public class AdminTenantCreatedEvent : AdminTenantLifecycleEvent
{
    public AdminTenantCreatedEvent() { }

    [System.Diagnostics.CodeAnalysis.SetsRequiredMembers]
    public AdminTenantCreatedEvent(Tenant tenant, string adminUserId, string? adminUserName = null)
        : base(tenant, adminUserId, adminUserName) { }

    public override string? WebhookEventType => "admin.tenant_created";
}

/// <summary>
/// Raised when a new organization is created (provisioned) via the Admin API.
/// Platform-scoped: only platform endpoints can subscribe — used to drive
/// the org welcome email and any platform-level provisioning workflows.
/// </summary>
[WebhookEvent("Admin Organization Created", Description = "Triggered when an admin provisions a new organization")]
public class AdminOrganizationCreatedEvent : AdminActionEvent
{
    public override string ResourceType => "Organization";
    public override string? WebhookEventType => "admin.organization_created";

    /// <summary>The slug used in URLs / subdomains.</summary>
    public required string OrganizationSlug { get; init; }

    /// <summary>Display name of the organization.</summary>
    public required string OrganizationName { get; init; }

    /// <summary>Owner user ID, if assigned at creation.</summary>
    public string? OwnerUserId { get; init; }

    /// <summary>Owner email — used as the welcome-email recipient.</summary>
    public string? OwnerEmail { get; init; }
}

/// <summary>
/// Raised when an admin creates an identity provider
/// </summary>
[WebhookEvent("Admin IdP Created", Description = "Triggered when an admin creates an identity provider")]
public class AdminIdpCreatedEvent : AdminActionEvent
{
    public override string ResourceType => "IdentityProvider";
    public override string? WebhookEventType => "admin.idp_created";

    public required string Scheme { get; init; }
    public string? ProviderType { get; init; }
}

/// <summary>
/// Raised when an admin updates an identity provider
/// </summary>
[WebhookEvent("Admin IdP Updated", Description = "Triggered when an admin updates an identity provider")]
public class AdminIdpUpdatedEvent : AdminActionEvent
{
    public override string ResourceType => "IdentityProvider";
    public override string? WebhookEventType => "admin.idp_updated";

    public required string Scheme { get; init; }
}

/// <summary>
/// Raised when an admin deletes an identity provider
/// </summary>
[WebhookEvent("Admin IdP Deleted", Description = "Triggered when an admin deletes an identity provider")]
public class AdminIdpDeletedEvent : AdminActionEvent
{
    public override string ResourceType => "IdentityProvider";
    public override string? WebhookEventType => "admin.idp_deleted";

    public required string Scheme { get; init; }
}

/// <summary>
/// Raised when an admin creates a webhook subscription
/// </summary>
[WebhookEvent("Admin Webhook Created", Description = "Triggered when an admin creates a webhook subscription")]
public class AdminWebhookCreatedEvent : AdminActionEvent
{
    public override string ResourceType => "Webhook";
    public override string? WebhookEventType => "admin.webhook_created";

    public required string EndpointUrl { get; init; }
}

/// <summary>
/// Raised when an admin updates a webhook subscription
/// </summary>
[WebhookEvent("Admin Webhook Updated", Description = "Triggered when an admin updates a webhook subscription")]
public class AdminWebhookUpdatedEvent : AdminActionEvent
{
    public override string ResourceType => "Webhook";
    public override string? WebhookEventType => "admin.webhook_updated";
}

/// <summary>
/// Raised when an admin deletes a webhook subscription
/// </summary>
[WebhookEvent("Admin Webhook Deleted", Description = "Triggered when an admin deletes a webhook subscription")]
public class AdminWebhookDeletedEvent : AdminActionEvent
{
    public override string ResourceType => "Webhook";
    public override string? WebhookEventType => "admin.webhook_deleted";
}

/// <summary>
/// Raised when an admin authorises a webhook endpoint to receive the sensitive properties of
/// restricted events, or revokes that authorisation.
/// </summary>
[WebhookEvent("Admin Webhook Sensitive Data Changed", Description = "Triggered when an admin authorises or revokes a webhook endpoint's access to sensitive event data")]
public class AdminWebhookSensitiveDataChangedEvent : AdminActionEvent
{
    public override string ResourceType => "Webhook";
    public override string? WebhookEventType => "admin.webhook_sensitive_data_changed";

    public required string EndpointUrl { get; init; }

    /// <summary>True when the endpoint was authorised, false when authorisation was revoked.</summary>
    public bool Authorized { get; init; }

    /// <summary>The restricted event types the endpoint was subscribed to at the time.</summary>
    public IReadOnlyList<string> RestrictedEventTypes { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Raised when an admin updates tenant settings
/// </summary>
[WebhookEvent("Admin Tenant Settings Updated", Description = "Triggered when an admin updates tenant settings")]
public class AdminTenantSettingsUpdatedEvent : AdminActionEvent
{
    public override string ResourceType => "TenantSettings";
    public override string? WebhookEventType => "admin.tenant_settings_updated";

    public List<string>? ChangedSettings { get; init; }
}

/// <summary>
/// Raised when an admin creates or updates a signing key
/// </summary>
[WebhookEvent("Admin Signing Key Changed", Description = "Triggered when an admin creates or updates a signing key")]
public class AdminSigningKeyChangedEvent : AdminActionEvent
{
    public override string ResourceType => "SigningKey";
    public override string? WebhookEventType => "admin.signing_key_changed";

    public required string KeyId { get; init; }
    public required string Action { get; init; } // Created, Rotated, Revoked, Deleted
}

/// <summary>
/// Raised when an admin updates a journey policy
/// </summary>
[WebhookEvent("Admin Journey Policy Updated", Description = "Triggered when an admin updates a journey policy")]
public class AdminJourneyPolicyUpdatedEvent : AdminActionEvent
{
    public override string ResourceType => "JourneyPolicy";
    public override string? WebhookEventType => "admin.journey_policy_updated";

    public required string PolicyType { get; init; }
}

/// <summary>
/// Raised when admin purges audit logs
/// </summary>
public class AdminAuditLogsPurgedEvent : AdminActionEvent
{
    public override string ResourceType => "AuditLog";

    public required DateTime CutoffDate { get; init; }
    public required int DeletedCount { get; init; }
}

/// <summary>
/// Raised when an admin creates an API resource (legacy Duende-style)
/// </summary>
[WebhookEvent("Admin API Resource Created", Description = "Triggered when an admin creates an API resource")]
public class AdminApiResourceCreatedEvent : AdminActionEvent
{
    public override string ResourceType => "ApiResource";
    public override string? WebhookEventType => "admin.api_resource_created";

    public required string ApiResourceName { get; init; }
}

/// <summary>
/// Raised when an admin updates an API resource (legacy Duende-style)
/// </summary>
[WebhookEvent("Admin API Resource Updated", Description = "Triggered when an admin updates an API resource")]
public class AdminApiResourceUpdatedEvent : AdminActionEvent
{
    public override string ResourceType => "ApiResource";
    public override string? WebhookEventType => "admin.api_resource_updated";

    public required string ApiResourceName { get; init; }
}

/// <summary>
/// Raised when an admin deletes an API resource (legacy Duende-style)
/// </summary>
[WebhookEvent("Admin API Resource Deleted", Description = "Triggered when an admin deletes an API resource")]
public class AdminApiResourceDeletedEvent : AdminActionEvent
{
    public override string ResourceType => "ApiResource";
    public override string? WebhookEventType => "admin.api_resource_deleted";

    public required string ApiResourceName { get; init; }
}

/// <summary>
/// Raised when an admin creates a resource (RFC 8707)
/// </summary>
[WebhookEvent("Admin Resource Created", Description = "Triggered when an admin creates a resource")]
public class AdminResourceCreatedEvent : AdminActionEvent
{
    public override string ResourceType => "Resource";
    public override string? WebhookEventType => "admin.resource_created";

    public required string ResourceUri { get; init; }
}

/// <summary>
/// Raised when an admin updates a resource (RFC 8707)
/// </summary>
[WebhookEvent("Admin Resource Updated", Description = "Triggered when an admin updates a resource")]
public class AdminResourceUpdatedEvent : AdminActionEvent
{
    public override string ResourceType => "Resource";
    public override string? WebhookEventType => "admin.resource_updated";

    public required string ResourceUri { get; init; }
}

/// <summary>
/// Raised when an admin deletes a resource (RFC 8707)
/// </summary>
[WebhookEvent("Admin Resource Deleted", Description = "Triggered when an admin deletes a resource")]
public class AdminResourceDeletedEvent : AdminActionEvent
{
    public override string ResourceType => "Resource";
    public override string? WebhookEventType => "admin.resource_deleted";

    public required string ResourceUri { get; init; }
}

/// <summary>
/// Raised when an admin creates an API scope
/// </summary>
[WebhookEvent("Admin API Scope Created", Description = "Triggered when an admin creates an API scope")]
public class AdminApiScopeCreatedEvent : AdminActionEvent
{
    public override string ResourceType => "ApiScope";
    public override string? WebhookEventType => "admin.api_scope_created";

    public required string ApiScopeName { get; init; }
}

/// <summary>
/// Raised when an admin updates an API scope
/// </summary>
[WebhookEvent("Admin API Scope Updated", Description = "Triggered when an admin updates an API scope")]
public class AdminApiScopeUpdatedEvent : AdminActionEvent
{
    public override string ResourceType => "ApiScope";
    public override string? WebhookEventType => "admin.api_scope_updated";

    public required string ApiScopeName { get; init; }
}

/// <summary>
/// Raised when an admin deletes an API scope
/// </summary>
[WebhookEvent("Admin API Scope Deleted", Description = "Triggered when an admin deletes an API scope")]
public class AdminApiScopeDeletedEvent : AdminActionEvent
{
    public override string ResourceType => "ApiScope";
    public override string? WebhookEventType => "admin.api_scope_deleted";

    public required string ApiScopeName { get; init; }
}

#endregion

#region Journey/Submission Events

/// <summary>
/// Raised when a data collection journey submission is created
/// </summary>
[WebhookEvent("Journey Submission Created", Description = "Triggered when a data collection journey submission is created")]
public class JourneySubmissionCreatedEvent : OlusoEvent
{
    public override string Category => EventCategories.User;
    public override string? WebhookEventType => "journey.submission_created";

    /// <summary>
    /// The journey policy ID that generated this submission
    /// </summary>
    public required string PolicyId { get; init; }

    /// <summary>
    /// Display name of the policy
    /// </summary>
    public string? PolicyName { get; init; }

    /// <summary>
    /// The submission ID
    /// </summary>
    public required string SubmissionId { get; init; }

    /// <summary>
    /// Journey ID that created the submission (if applicable)
    /// </summary>
    public string? JourneyId { get; init; }

    /// <summary>
    /// The collected field names (not values, for privacy)
    /// </summary>
    public IReadOnlyList<string>? FieldNames { get; init; }

    /// <summary>
    /// The collected data fields. Never set for a module's journey, whose answers go only to that module.
    /// </summary>
    public IDictionary<string, object>? Fields { get; init; }

    /// <summary>The module the run belongs to, for a module's journey; null for a platform journey.</summary>
    public string? Module { get; init; }

    /// <summary>
    /// Number of fields collected
    /// </summary>
    public int FieldCount { get; init; }

    /// <summary>
    /// The signed-in user who completed the journey, when there was one
    /// </summary>
    public string? SubjectUserId { get; init; }

    /// <summary>
    /// Context the calling application attached to the journey's signed launch link
    /// </summary>
    public IReadOnlyDictionary<string, string>? LaunchContext { get; init; }
}

#endregion

#region OIDC Protocol Events

/// <summary>
/// Raised when an authorization request is received
/// </summary>
[WebhookEvent("Authorization Request Received", Description = "Triggered when an authorization request is received")]
public class AuthorizationRequestReceivedEvent : OlusoEvent
{
    public override EventScope Scope => EventScope.Both;
    public override string Category => EventCategories.Authentication;
    public override string? WebhookEventType => "oidc.authorization_request";

    public required string ClientId { get; init; }
    public required string ResponseType { get; init; }
    public IEnumerable<string> Scopes { get; init; } = Array.Empty<string>();
    public string? RedirectUri { get; init; }
    public string? State { get; init; }
    public bool UsePkce { get; init; }
}

/// <summary>
/// Raised when an authorization code is issued
/// </summary>
[WebhookEvent("Authorization Code Issued", Description = "Triggered when an authorization code is issued")]
public class AuthorizationCodeIssuedEvent : OlusoEvent
{
    public override EventScope Scope => EventScope.Both;
    public override string Category => EventCategories.Token;
    public override string? WebhookEventType => "oidc.authorization_code_issued";

    public required string ClientId { get; init; }
    public required string SubjectId { get; init; }
    public IEnumerable<string> Scopes { get; init; } = Array.Empty<string>();
    public string? RedirectUri { get; init; }
}

/// <summary>
/// Raised when an authorization request fails
/// </summary>
[WebhookEvent("Authorization Request Failed", Description = "Triggered when an authorization request fails")]
public class AuthorizationRequestFailedEvent : OlusoEvent
{
    public override EventScope Scope => EventScope.Both;
    public override string Category => EventCategories.Authentication;
    public override bool IsFailure => true;
    public override string? WebhookEventType => "oidc.authorization_failed";

    public string? ClientId { get; init; }
    public required string Error { get; init; }
    public string? ErrorDescription { get; init; }
}

/// <summary>
/// Raised when a token request is received
/// </summary>
public class TokenRequestReceivedEvent : OlusoEvent
{
    public override EventScope Scope => EventScope.Both;
    public override string Category => EventCategories.Token;

    public required string ClientId { get; init; }
    public required string GrantType { get; init; }
    public IEnumerable<string> Scopes { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Raised when the server leaves requested scopes out of a grant (RFC 6749 §3.3): the client isn't allowed them,
/// they don't exist, or the tenant doesn't offer them. Audited so admins can see why an app got less than it asked.
/// </summary>
public class ScopesDroppedEvent : OlusoEvent
{
    public override EventScope Scope => EventScope.Both;
    public override string Category => EventCategories.Token;

    public string? ClientId { get; init; }

    /// <summary>Where the scopes were requested (authorize, par, device_authorization, token, direct).</summary>
    public string? Endpoint { get; init; }

    public IEnumerable<string> RequestedScopes { get; init; } = Array.Empty<string>();
    public IEnumerable<string> GrantedScopes { get; init; } = Array.Empty<string>();

    /// <summary>Each scope left out, with the reason (not_allowed, unknown, feature_unavailable).</summary>
    public IReadOnlyDictionary<string, string> DroppedScopes { get; init; } = new Dictionary<string, string>();
}

/// <summary>
/// Raised when a token request fails
/// </summary>
[WebhookEvent("Token Request Failed", Description = "Triggered when a token request fails")]
public class TokenRequestFailedEvent : OlusoEvent
{
    public override EventScope Scope => EventScope.Both;
    public override string Category => EventCategories.Token;
    public override bool IsFailure => true;
    public override string? WebhookEventType => "oidc.token_failed";

    public required string ClientId { get; init; }
    public required string GrantType { get; init; }
    public required string Error { get; init; }
    public string? ErrorDescription { get; init; }
}

/// <summary>
/// Raised when a refresh token is used
/// </summary>
[WebhookEvent("Refresh Token Used", Description = "Triggered when a refresh token is used")]
public class RefreshTokenUsedEvent : OlusoEvent
{
    public override EventScope Scope => EventScope.Both;
    public override string Category => EventCategories.Token;
    public override string? WebhookEventType => "oidc.refresh_token_used";

    public required string ClientId { get; init; }
    public required string SubjectId { get; init; }
}

/// <summary>
/// Raised when token introspection is performed
/// </summary>
public class TokenIntrospectedEvent : OlusoEvent
{
    public override EventScope Scope => EventScope.Both;
    public override string Category => EventCategories.Token;

    public required string ClientId { get; init; }
    public required string TokenType { get; init; }
    public bool IsActive { get; init; }
    public string? SubjectId { get; init; }
}

#endregion

#region Feedback Events

/// <summary>
/// Raised when a user submits feedback via the beta feedback widget
/// </summary>
[WebhookEvent("Feedback Submitted", Description = "Triggered when a user submits feedback via the beta feedback widget")]
public class FeedbackSubmittedEvent : OlusoEvent
{
    public override string Category => EventCategories.System;
    public override string? WebhookEventType => FeedbackWebhookEvents.FeedbackSubmitted;

    public required string FeedbackId { get; init; }
    public required string Title { get; init; }
    public required string FeedbackCategory { get; init; }
    public required string Portal { get; init; }
    public required string SubmittedByUserId { get; init; }
    public string? SubmittedByEmail { get; init; }
    public string? Url { get; init; }
}

/// <summary>
/// Webhook event type constants for feedback
/// </summary>
public static class FeedbackWebhookEvents
{
    public const string FeedbackSubmitted = "feedback.submitted";
}

#endregion

#region Feature Events

/// <summary>
/// Raised when a feature/module is enabled for an organization or tenant.
/// Triggers deferred seeding for tenants that were created before the feature was enabled.
/// </summary>
[WebhookEvent("Feature Enabled", Description = "Triggered when a module feature is enabled for an organization or tenant")]
public class FeatureEnabledEvent : AdminActionEvent
{
    public override string EventType => "Security";
    public override string ResourceType => "Feature";
    public override string? WebhookEventType => "security.feature_enabled";

    /// <summary>
    /// The feature key that was enabled.
    /// </summary>
    public required string FeatureKey { get; init; }

    /// <summary>
    /// "organization" or "tenant" — the level at which the feature was enabled.
    /// </summary>
    public required string FeatureScope { get; init; }

    /// <summary>
    /// The organization ID (always present).
    /// </summary>
    public string? OrganizationId { get; init; }
}

/// <summary>
/// Raised when a feature/module is disabled for a tenant. Modules that keep state in an external
/// service use it to suspend that state; module data in Oluṣọ is kept.
/// </summary>
[WebhookEvent("Feature Disabled", Description = "Triggered when a module feature is disabled for a tenant")]
public class FeatureDisabledEvent : AdminActionEvent
{
    public override string EventType => "Security";
    public override string ResourceType => "Feature";
    public override string? WebhookEventType => "security.feature_disabled";

    /// <summary>
    /// The feature key that was disabled.
    /// </summary>
    public required string FeatureKey { get; init; }

    /// <summary>
    /// "tenant" (organization-level disabling raises no event).
    /// </summary>
    public required string FeatureScope { get; init; }

    /// <summary>
    /// The tenant's organization ID, when it has one.
    /// </summary>
    public string? OrganizationId { get; init; }
}


/// <summary>
/// Raised when an admin creates a journey policy.
/// Journey steps can carry outbound credentials, so these changes are auditable
/// like any other credential-bearing configuration.
/// </summary>
public class AdminJourneyPolicyCreatedEvent : AdminActionEvent
{
    public override string ResourceType => "JourneyPolicy";
    public override string? WebhookEventType => "admin.journey_policy_created";

    public required string PolicyId { get; init; }
    public string? PolicyType { get; init; }
}

/// <summary>
/// Raised when an admin deletes a journey policy
/// </summary>
public class AdminJourneyPolicyDeletedEvent : AdminActionEvent
{
    public override string ResourceType => "JourneyPolicy";
    public override string? WebhookEventType => "admin.journey_policy_deleted";

    public required string PolicyId { get; init; }
}

#endregion

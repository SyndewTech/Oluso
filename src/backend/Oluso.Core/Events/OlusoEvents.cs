using System.Security.Claims;
using Oluso.Core.Domain.Entities;

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
    public string Id { get; } = Guid.NewGuid().ToString();

    /// <summary>
    /// When the event occurred
    /// </summary>
    public DateTime Timestamp { get; } = DateTime.UtcNow;

    /// <summary>
    /// Tenant context for multi-tenant scenarios
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// Activity/correlation ID for distributed tracing
    /// </summary>
    public string? ActivityId { get; set; }

    /// <summary>
    /// Event category for filtering and routing
    /// </summary>
    public abstract string Category { get; }

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
    public const string Admin = "Admin";
    public const string System = "System";
}

/// <summary>
/// Raised before a user signs in. Can be cancelled to prevent sign-in.
/// </summary>
public class UserSigningInEvent : OlusoEvent
{
    public override string Category => EventCategories.Authentication;

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
public class UserSignedInEvent : OlusoEvent
{
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
public class UserSignInFailedEvent : OlusoEvent
{
    public override string Category => EventCategories.Authentication;
    public override bool IsFailure => true;
    public override string? WebhookEventType => "auth.login_failed";

    public required string Username { get; init; }
    public required string ClientId { get; init; }
    public required string FailureReason { get; init; }
    public string? IpAddress { get; init; }
}

/// <summary>
/// Raised when a user signs out
/// </summary>
public class UserSignedOutEvent : OlusoEvent
{
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
    public override string Category => EventCategories.Token;

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
public class TokenIssuedEvent : OlusoEvent
{
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
public class TokenRevokedEvent : OlusoEvent
{
    public override string Category => EventCategories.Token;
    public override string? WebhookEventType => "auth.token_revoked";

    public required string ClientId { get; init; }
    public string? SubjectId { get; init; }
    public string? TokenType { get; init; }
}

/// <summary>
/// Raised when user consent is granted
/// </summary>
public class ConsentGrantedEvent : OlusoEvent
{
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
public class ConsentDeniedEvent : OlusoEvent
{
    public override string Category => EventCategories.Security;
    public override string? WebhookEventType => "security.consent_revoked";

    public required string SubjectId { get; init; }
    public required string ClientId { get; init; }
}

/// <summary>
/// Raised when a new user registers
/// </summary>
public class UserRegisteredEvent : OlusoEvent
{
    public override string Category => EventCategories.User;
    public override string? WebhookEventType => "user.created";

    public required string SubjectId { get; init; }
    public required string Email { get; init; }
    public string? Username { get; init; }
    public string? ClientId { get; init; }
}

/// <summary>
/// Raised when MFA is completed
/// </summary>
public class MfaCompletedEvent : OlusoEvent
{
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
public class UserDeletedEvent : OlusoEvent
{
    public override string Category => EventCategories.User;
    public override string? WebhookEventType => "user.deleted";

    public required string SubjectId { get; init; }
    public string? Email { get; init; }
    public bool DeletedByAdmin { get; init; }
}

/// <summary>
/// Raised when a user's email is verified
/// </summary>
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
public class UserLockedOutEvent : OlusoEvent
{
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
/// Base class for admin actions performed via the Admin API
/// </summary>
public abstract class AdminActionEvent : OlusoEvent
{
    public override string Category => EventCategories.Admin;

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
}

/// <summary>
/// Raised when an admin creates a user
/// </summary>
public class AdminUserCreatedEvent : AdminActionEvent
{
    public override string ResourceType => "User";
    public override string? WebhookEventType => "admin.user_created";

    public required string Email { get; init; }
    public string? Username { get; init; }
}

/// <summary>
/// Raised when an admin updates a user
/// </summary>
public class AdminUserUpdatedEvent : AdminActionEvent
{
    public override string ResourceType => "User";
    public override string? WebhookEventType => "admin.user_updated";

    public Dictionary<string, string>? ChangedFields { get; init; }
}

/// <summary>
/// Raised when an admin deletes a user
/// </summary>
public class AdminUserDeletedEvent : AdminActionEvent
{
    public override string ResourceType => "User";
    public override string? WebhookEventType => "admin.user_deleted";
}

/// <summary>
/// Raised when an admin resets a user's password
/// </summary>
public class AdminPasswordResetEvent : AdminActionEvent
{
    public override string ResourceType => "User";
    public override string? WebhookEventType => "admin.password_reset";
}

/// <summary>
/// Raised when an admin creates a client
/// </summary>
public class AdminClientCreatedEvent : AdminActionEvent
{
    public override string ResourceType => "Client";
    public override string? WebhookEventType => "admin.client_created";

    public required string ClientId { get; init; }
}

/// <summary>
/// Raised when an admin updates a client
/// </summary>
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
public class AdminClientDeletedEvent : AdminActionEvent
{
    public override string ResourceType => "Client";
    public override string? WebhookEventType => "admin.client_deleted";

    public required string ClientId { get; init; }
}

/// <summary>
/// Raised when an admin creates a role
/// </summary>
public class AdminRoleCreatedEvent : AdminActionEvent
{
    public override string ResourceType => "Role";
    public override string? WebhookEventType => "admin.role_created";

    public required string RoleName { get; init; }
}

/// <summary>
/// Raised when an admin updates a role
/// </summary>
public class AdminRoleUpdatedEvent : AdminActionEvent
{
    public override string ResourceType => "Role";
    public override string? WebhookEventType => "admin.role_updated";

    public required string RoleName { get; init; }
}

/// <summary>
/// Raised when an admin deletes a role
/// </summary>
public class AdminRoleDeletedEvent : AdminActionEvent
{
    public override string ResourceType => "Role";
    public override string? WebhookEventType => "admin.role_deleted";

    public required string RoleName { get; init; }
}

/// <summary>
/// Raised when an admin assigns a role to a user
/// </summary>
public class AdminUserRoleAssignedEvent : AdminActionEvent
{
    public override string ResourceType => "UserRole";
    public override string? WebhookEventType => "admin.user_role_assigned";

    public required string RoleName { get; init; }
}

/// <summary>
/// Raised when an admin removes a role from a user
/// </summary>
public class AdminUserRoleRemovedEvent : AdminActionEvent
{
    public override string ResourceType => "UserRole";
    public override string? WebhookEventType => "admin.user_role_removed";

    public required string RoleName { get; init; }
}

/// <summary>
/// Raised when an admin creates an identity provider
/// </summary>
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
public class AdminIdpUpdatedEvent : AdminActionEvent
{
    public override string ResourceType => "IdentityProvider";
    public override string? WebhookEventType => "admin.idp_updated";

    public required string Scheme { get; init; }
}

/// <summary>
/// Raised when an admin deletes an identity provider
/// </summary>
public class AdminIdpDeletedEvent : AdminActionEvent
{
    public override string ResourceType => "IdentityProvider";
    public override string? WebhookEventType => "admin.idp_deleted";

    public required string Scheme { get; init; }
}

/// <summary>
/// Raised when an admin creates a webhook subscription
/// </summary>
public class AdminWebhookCreatedEvent : AdminActionEvent
{
    public override string ResourceType => "Webhook";
    public override string? WebhookEventType => "admin.webhook_created";

    public required string EndpointUrl { get; init; }
}

/// <summary>
/// Raised when an admin updates a webhook subscription
/// </summary>
public class AdminWebhookUpdatedEvent : AdminActionEvent
{
    public override string ResourceType => "Webhook";
    public override string? WebhookEventType => "admin.webhook_updated";
}

/// <summary>
/// Raised when an admin deletes a webhook subscription
/// </summary>
public class AdminWebhookDeletedEvent : AdminActionEvent
{
    public override string ResourceType => "Webhook";
    public override string? WebhookEventType => "admin.webhook_deleted";
}

/// <summary>
/// Raised when an admin updates tenant settings
/// </summary>
public class AdminTenantSettingsUpdatedEvent : AdminActionEvent
{
    public override string ResourceType => "TenantSettings";
    public override string? WebhookEventType => "admin.tenant_settings_updated";

    public List<string>? ChangedSettings { get; init; }
}

/// <summary>
/// Raised when an admin creates or updates a signing key
/// </summary>
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
/// Raised when an admin creates an API resource
/// </summary>
public class AdminApiResourceCreatedEvent : AdminActionEvent
{
    public override string ResourceType => "ApiResource";
    public override string? WebhookEventType => "admin.api_resource_created";

    public required string ApiResourceName { get; init; }
}

/// <summary>
/// Raised when an admin updates an API resource
/// </summary>
public class AdminApiResourceUpdatedEvent : AdminActionEvent
{
    public override string ResourceType => "ApiResource";
    public override string? WebhookEventType => "admin.api_resource_updated";

    public required string ApiResourceName { get; init; }
}

/// <summary>
/// Raised when an admin deletes an API resource
/// </summary>
public class AdminApiResourceDeletedEvent : AdminActionEvent
{
    public override string ResourceType => "ApiResource";
    public override string? WebhookEventType => "admin.api_resource_deleted";

    public required string ApiResourceName { get; init; }
}

/// <summary>
/// Raised when an admin creates an API scope
/// </summary>
public class AdminApiScopeCreatedEvent : AdminActionEvent
{
    public override string ResourceType => "ApiScope";
    public override string? WebhookEventType => "admin.api_scope_created";

    public required string ApiScopeName { get; init; }
}

/// <summary>
/// Raised when an admin updates an API scope
/// </summary>
public class AdminApiScopeUpdatedEvent : AdminActionEvent
{
    public override string ResourceType => "ApiScope";
    public override string? WebhookEventType => "admin.api_scope_updated";

    public required string ApiScopeName { get; init; }
}

/// <summary>
/// Raised when an admin deletes an API scope
/// </summary>
public class AdminApiScopeDeletedEvent : AdminActionEvent
{
    public override string ResourceType => "ApiScope";
    public override string? WebhookEventType => "admin.api_scope_deleted";

    public required string ApiScopeName { get; init; }
}

/// <summary>
/// Raised when an admin creates a SAML Service Provider
/// </summary>
public class AdminSamlSpCreatedEvent : AdminActionEvent
{
    public override string ResourceType => "SamlServiceProvider";
    public override string? WebhookEventType => "admin.saml_sp_created";

    public required string EntityId { get; init; }
}

/// <summary>
/// Raised when an admin updates a SAML Service Provider
/// </summary>
public class AdminSamlSpUpdatedEvent : AdminActionEvent
{
    public override string ResourceType => "SamlServiceProvider";
    public override string? WebhookEventType => "admin.saml_sp_updated";

    public required string EntityId { get; init; }
}

/// <summary>
/// Raised when an admin deletes a SAML Service Provider
/// </summary>
public class AdminSamlSpDeletedEvent : AdminActionEvent
{
    public override string ResourceType => "SamlServiceProvider";
    public override string? WebhookEventType => "admin.saml_sp_deleted";

    public required string EntityId { get; init; }
}

#endregion

#region SAML Events

/// <summary>
/// Raised when a SAML SSO request is received
/// </summary>
public class SamlSsoRequestReceivedEvent : OlusoEvent
{
    public override string Category => EventCategories.Authentication;
    public override string? WebhookEventType => "saml.sso_request_received";

    public required string SpEntityId { get; init; }
    public string? RequestId { get; init; }
    public string? AcsUrl { get; init; }
    public bool ForceAuthn { get; init; }
}

/// <summary>
/// Raised when a SAML assertion is issued
/// </summary>
public class SamlAssertionIssuedEvent : OlusoEvent
{
    public override string Category => EventCategories.Authentication;
    public override string? WebhookEventType => "saml.assertion_issued";

    public required string SubjectId { get; init; }
    public required string SpEntityId { get; init; }
    public string? NameId { get; init; }
    public string? SessionIndex { get; init; }
}

/// <summary>
/// Raised when SAML SSO fails
/// </summary>
public class SamlSsoFailedEvent : OlusoEvent
{
    public override string Category => EventCategories.Authentication;
    public override bool IsFailure => true;
    public override string? WebhookEventType => "saml.sso_failed";

    public string? SpEntityId { get; init; }
    public required string Error { get; init; }
    public string? ErrorDescription { get; init; }
}

/// <summary>
/// Raised when a SAML logout request is received
/// </summary>
public class SamlLogoutRequestReceivedEvent : OlusoEvent
{
    public override string Category => EventCategories.Authentication;
    public override string? WebhookEventType => "saml.logout_request_received";

    public required string SpEntityId { get; init; }
    public string? NameId { get; init; }
    public string? SessionIndex { get; init; }
}

/// <summary>
/// Raised when SAML logout is completed
/// </summary>
public class SamlLogoutCompletedEvent : OlusoEvent
{
    public override string Category => EventCategories.Authentication;
    public override string? WebhookEventType => "saml.logout_completed";

    public required string SpEntityId { get; init; }
    public string? SubjectId { get; init; }
    public bool Success { get; init; }
}

/// <summary>
/// Raised when a user authenticates via SAML SP (external IdP)
/// </summary>
public class SamlSpLoginEvent : OlusoEvent
{
    public override string Category => EventCategories.Authentication;
    public override string? WebhookEventType => "saml.sp_login";

    public required string SubjectId { get; init; }
    public required string IdpEntityId { get; init; }
    public string? NameId { get; init; }
    public string? SessionIndex { get; init; }
}

#endregion

#region LDAP Events

/// <summary>
/// Raised when LDAP authentication is attempted
/// </summary>
public class LdapAuthenticationAttemptEvent : OlusoEvent
{
    public override string Category => EventCategories.Authentication;

    public required string Username { get; init; }
    public string? IpAddress { get; init; }
}

/// <summary>
/// Raised when LDAP authentication succeeds
/// </summary>
public class LdapAuthenticationSuccessEvent : OlusoEvent
{
    public override string Category => EventCategories.Authentication;
    public override string? WebhookEventType => "ldap.login_success";

    public required string SubjectId { get; init; }
    public required string Username { get; init; }
    public string? DistinguishedName { get; init; }
    public IReadOnlyList<string>? Groups { get; init; }
    public IReadOnlyList<string>? MappedRoles { get; init; }
    public string? IpAddress { get; init; }
}

/// <summary>
/// Raised when LDAP authentication fails
/// </summary>
public class LdapAuthenticationFailedEvent : OlusoEvent
{
    public override string Category => EventCategories.Authentication;
    public override bool IsFailure => true;
    public override string? WebhookEventType => "ldap.login_failed";

    public required string Username { get; init; }
    public required string FailureReason { get; init; }
    public string? IpAddress { get; init; }
}

/// <summary>
/// Raised when a new user is provisioned from LDAP
/// </summary>
public class LdapUserProvisionedEvent : OlusoEvent
{
    public override string Category => EventCategories.User;
    public override string? WebhookEventType => "ldap.user_provisioned";

    public required string SubjectId { get; init; }
    public required string Username { get; init; }
    public string? Email { get; init; }
    public string? DistinguishedName { get; init; }
}

#endregion

#region OIDC Protocol Events

/// <summary>
/// Raised when an authorization request is received
/// </summary>
public class AuthorizationRequestReceivedEvent : OlusoEvent
{
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
public class AuthorizationCodeIssuedEvent : OlusoEvent
{
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
public class AuthorizationRequestFailedEvent : OlusoEvent
{
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
    public override string Category => EventCategories.Token;

    public required string ClientId { get; init; }
    public required string GrantType { get; init; }
    public IEnumerable<string> Scopes { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Raised when a token request fails
/// </summary>
public class TokenRequestFailedEvent : OlusoEvent
{
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
public class RefreshTokenUsedEvent : OlusoEvent
{
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
    public override string Category => EventCategories.Token;

    public required string ClientId { get; init; }
    public required string TokenType { get; init; }
    public bool IsActive { get; init; }
    public string? SubjectId { get; init; }
}

#endregion

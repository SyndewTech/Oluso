using System.Text.Json;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;

namespace Oluso.EntityFramework.Events;

/// <summary>
/// Event sink that persists events to the audit log database.
/// Maps OlusoEvents to AuditLog entries.
/// </summary>
public class AuditEventSink : IOlusoEventSink
{
    private readonly IAuditLogStore _store;
    private readonly ILogger<AuditEventSink> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public string Name => "Audit";

    public AuditEventSink(IAuditLogStore store, ILogger<AuditEventSink> logger)
    {
        _store = store;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public async Task HandleAsync(OlusoEvent evt, CancellationToken cancellationToken = default)
    {
        try
        {
            var auditLog = MapToAuditLog(evt);
            await _store.WriteAsync(auditLog, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write audit log for event {EventType}", evt.EventType);
            // Don't rethrow - audit failures shouldn't break the event flow
        }
    }

    private AuditLog MapToAuditLog(OlusoEvent evt)
    {
        var auditLog = new AuditLog
        {
            Timestamp = evt.Timestamp,
            EventType = evt.EventType,
            Category = evt.Category,
            Success = !evt.IsFailure,
            ActivityId = evt.ActivityId
        };

        // Map event-specific properties
        switch (evt)
        {
            case UserSignedInEvent e:
                auditLog.Action = "Login";
                auditLog.SubjectId = e.SubjectId;
                auditLog.SubjectName = e.Username;
                auditLog.ClientId = e.ClientId;
                auditLog.IpAddress = e.IpAddress;
                auditLog.Details = JsonSerializer.Serialize(new
                {
                    e.AuthenticationMethod,
                    e.SessionId
                }, _jsonOptions);
                break;

            case UserSignInFailedEvent e:
                auditLog.Action = "LoginFailed";
                auditLog.SubjectName = e.Username;
                auditLog.ClientId = e.ClientId;
                auditLog.IpAddress = e.IpAddress;
                auditLog.ErrorMessage = e.FailureReason;
                break;

            case UserSignedOutEvent e:
                auditLog.Action = "Logout";
                auditLog.SubjectId = e.SubjectId;
                auditLog.Details = e.SessionId;
                break;

            case UserRegisteredEvent e:
                auditLog.Action = "Register";
                auditLog.SubjectId = e.SubjectId;
                auditLog.SubjectEmail = e.Email;
                auditLog.SubjectName = e.Username;
                auditLog.ClientId = e.ClientId;
                break;

            case UserUpdatedEvent e:
                auditLog.Action = "UpdateProfile";
                auditLog.SubjectId = e.SubjectId;
                auditLog.SubjectEmail = e.Email;
                auditLog.ResourceType = "User";
                auditLog.ResourceId = e.SubjectId;
                auditLog.Details = e.ChangedFields != null
                    ? JsonSerializer.Serialize(e.ChangedFields.Keys, _jsonOptions)
                    : null;
                break;

            case UserDeletedEvent e:
                auditLog.Action = "DeleteUser";
                auditLog.SubjectId = e.SubjectId;
                auditLog.SubjectEmail = e.Email;
                auditLog.ResourceType = "User";
                auditLog.ResourceId = e.SubjectId;
                auditLog.Reason = e.DeletedByAdmin ? "Admin" : "Self";
                break;

            case UserEmailVerifiedEvent e:
                auditLog.Action = "VerifyEmail";
                auditLog.SubjectId = e.SubjectId;
                auditLog.SubjectEmail = e.Email;
                break;

            case UserPasswordChangedEvent e:
                auditLog.Action = "ChangePassword";
                auditLog.SubjectId = e.SubjectId;
                auditLog.Reason = e.ChangedByAdmin ? "Admin" : "Self";
                break;

            case UserLockedOutEvent e:
                auditLog.Action = "Lockout";
                auditLog.SubjectId = e.SubjectId;
                auditLog.Reason = e.Reason;
                auditLog.Details = e.LockoutEnd?.ToString("O");
                break;

            case TokenIssuedEvent e:
                auditLog.Action = "TokenIssued";
                auditLog.SubjectId = e.SubjectId;
                auditLog.ClientId = e.ClientId;
                auditLog.Details = JsonSerializer.Serialize(new
                {
                    e.TokenType,
                    Scopes = e.Scopes.ToList()
                }, _jsonOptions);
                break;

            case ConsentGrantedEvent e:
                auditLog.Action = "ConsentGranted";
                auditLog.SubjectId = e.SubjectId;
                auditLog.ClientId = e.ClientId;
                auditLog.ResourceType = "Client";
                auditLog.ResourceId = e.ClientId;
                auditLog.Details = JsonSerializer.Serialize(new
                {
                    Scopes = e.Scopes.ToList(),
                    e.RememberConsent
                }, _jsonOptions);
                break;

            case ConsentDeniedEvent e:
                auditLog.Action = "ConsentDenied";
                auditLog.SubjectId = e.SubjectId;
                auditLog.ClientId = e.ClientId;
                auditLog.ResourceType = "Client";
                auditLog.ResourceId = e.ClientId;
                break;

            case MfaCompletedEvent e:
                auditLog.Action = e.Success ? "MfaSuccess" : "MfaFailed";
                auditLog.SubjectId = e.SubjectId;
                auditLog.Details = e.MfaMethod;
                break;

            // Admin action events
            case AdminActionEvent adminEvent:
                auditLog.Action = GetAdminAction(adminEvent);
                auditLog.SubjectId = adminEvent.AdminUserId;
                auditLog.SubjectName = adminEvent.AdminUserName;
                auditLog.IpAddress = adminEvent.IpAddress;
                auditLog.ResourceType = adminEvent.ResourceType;
                auditLog.ResourceId = adminEvent.ResourceId;
                auditLog.ResourceName = adminEvent.ResourceName;
                auditLog.Details = GetAdminEventDetails(adminEvent);
                break;

            // SAML events
            case SamlSsoRequestReceivedEvent e:
                auditLog.Action = "SamlSsoRequest";
                auditLog.ResourceType = "ServiceProvider";
                auditLog.ResourceId = e.SpEntityId;
                auditLog.Details = JsonSerializer.Serialize(new
                {
                    e.RequestId,
                    e.AcsUrl,
                    e.ForceAuthn
                }, _jsonOptions);
                break;

            case SamlAssertionIssuedEvent e:
                auditLog.Action = "SamlAssertionIssued";
                auditLog.SubjectId = e.SubjectId;
                auditLog.ResourceType = "ServiceProvider";
                auditLog.ResourceId = e.SpEntityId;
                auditLog.Details = JsonSerializer.Serialize(new
                {
                    e.NameId,
                    e.SessionIndex
                }, _jsonOptions);
                break;

            case SamlSsoFailedEvent e:
                auditLog.Action = "SamlSsoFailed";
                auditLog.ResourceType = "ServiceProvider";
                auditLog.ResourceId = e.SpEntityId;
                auditLog.ErrorMessage = e.Error;
                auditLog.Details = e.ErrorDescription;
                break;

            case SamlLogoutRequestReceivedEvent e:
                auditLog.Action = "SamlLogoutRequest";
                auditLog.ResourceType = "ServiceProvider";
                auditLog.ResourceId = e.SpEntityId;
                auditLog.Details = JsonSerializer.Serialize(new
                {
                    e.NameId,
                    e.SessionIndex
                }, _jsonOptions);
                break;

            case SamlLogoutCompletedEvent e:
                auditLog.Action = "SamlLogoutCompleted";
                auditLog.SubjectId = e.SubjectId;
                auditLog.ResourceType = "ServiceProvider";
                auditLog.ResourceId = e.SpEntityId;
                auditLog.Details = e.Success ? "Success" : "Failed";
                break;

            case SamlSpLoginEvent e:
                auditLog.Action = "SamlSpLogin";
                auditLog.SubjectId = e.SubjectId;
                auditLog.ResourceType = "IdentityProvider";
                auditLog.ResourceId = e.IdpEntityId;
                auditLog.Details = JsonSerializer.Serialize(new
                {
                    e.NameId,
                    e.SessionIndex
                }, _jsonOptions);
                break;

            // LDAP events
            case LdapAuthenticationSuccessEvent e:
                auditLog.Action = "LdapLogin";
                auditLog.SubjectId = e.SubjectId;
                auditLog.SubjectName = e.Username;
                auditLog.IpAddress = e.IpAddress;
                auditLog.Details = JsonSerializer.Serialize(new
                {
                    e.DistinguishedName,
                    Groups = e.Groups?.ToList(),
                    Roles = e.MappedRoles?.ToList()
                }, _jsonOptions);
                break;

            case LdapAuthenticationFailedEvent e:
                auditLog.Action = "LdapLoginFailed";
                auditLog.SubjectName = e.Username;
                auditLog.IpAddress = e.IpAddress;
                auditLog.ErrorMessage = e.FailureReason;
                break;

            case LdapUserProvisionedEvent e:
                auditLog.Action = "LdapUserProvisioned";
                auditLog.SubjectId = e.SubjectId;
                auditLog.SubjectName = e.Username;
                auditLog.SubjectEmail = e.Email;
                auditLog.Details = e.DistinguishedName;
                break;

            // OIDC protocol events
            case AuthorizationRequestReceivedEvent e:
                auditLog.Action = "AuthorizationRequest";
                auditLog.ClientId = e.ClientId;
                auditLog.Details = JsonSerializer.Serialize(new
                {
                    e.ResponseType,
                    Scopes = e.Scopes.ToList(),
                    e.RedirectUri,
                    e.UsePkce
                }, _jsonOptions);
                break;

            case AuthorizationCodeIssuedEvent e:
                auditLog.Action = "AuthorizationCodeIssued";
                auditLog.SubjectId = e.SubjectId;
                auditLog.ClientId = e.ClientId;
                auditLog.Details = JsonSerializer.Serialize(new
                {
                    Scopes = e.Scopes.ToList(),
                    e.RedirectUri
                }, _jsonOptions);
                break;

            case AuthorizationRequestFailedEvent e:
                auditLog.Action = "AuthorizationFailed";
                auditLog.ClientId = e.ClientId;
                auditLog.ErrorMessage = e.Error;
                auditLog.Details = e.ErrorDescription;
                break;

            case TokenRequestReceivedEvent e:
                auditLog.Action = "TokenRequest";
                auditLog.ClientId = e.ClientId;
                auditLog.Details = JsonSerializer.Serialize(new
                {
                    e.GrantType,
                    Scopes = e.Scopes.ToList()
                }, _jsonOptions);
                break;

            case TokenRequestFailedEvent e:
                auditLog.Action = "TokenRequestFailed";
                auditLog.ClientId = e.ClientId;
                auditLog.ErrorMessage = e.Error;
                auditLog.Details = JsonSerializer.Serialize(new
                {
                    e.GrantType,
                    e.ErrorDescription
                }, _jsonOptions);
                break;

            case RefreshTokenUsedEvent e:
                auditLog.Action = "RefreshTokenUsed";
                auditLog.SubjectId = e.SubjectId;
                auditLog.ClientId = e.ClientId;
                break;

            case TokenIntrospectedEvent e:
                auditLog.Action = "TokenIntrospected";
                auditLog.SubjectId = e.SubjectId;
                auditLog.ClientId = e.ClientId;
                auditLog.Details = JsonSerializer.Serialize(new
                {
                    e.TokenType,
                    e.IsActive
                }, _jsonOptions);
                break;

            default:
                // Generic mapping for unknown events
                auditLog.Action = evt.EventType;
                auditLog.Details = JsonSerializer.Serialize(evt, evt.GetType(), _jsonOptions);
                break;
        }

        return auditLog;
    }

    private static string GetAdminAction(AdminActionEvent evt)
    {
        return evt switch
        {
            AdminUserCreatedEvent => "Admin.CreateUser",
            AdminUserUpdatedEvent => "Admin.UpdateUser",
            AdminUserDeletedEvent => "Admin.DeleteUser",
            AdminPasswordResetEvent => "Admin.ResetPassword",
            AdminClientCreatedEvent => "Admin.CreateClient",
            AdminClientUpdatedEvent => "Admin.UpdateClient",
            AdminClientDeletedEvent => "Admin.DeleteClient",
            AdminRoleCreatedEvent => "Admin.CreateRole",
            AdminRoleUpdatedEvent => "Admin.UpdateRole",
            AdminRoleDeletedEvent => "Admin.DeleteRole",
            AdminUserRoleAssignedEvent => "Admin.AssignRole",
            AdminUserRoleRemovedEvent => "Admin.RemoveRole",
            AdminIdpCreatedEvent => "Admin.CreateIdp",
            AdminIdpUpdatedEvent => "Admin.UpdateIdp",
            AdminIdpDeletedEvent => "Admin.DeleteIdp",
            AdminWebhookCreatedEvent => "Admin.CreateWebhook",
            AdminWebhookUpdatedEvent => "Admin.UpdateWebhook",
            AdminWebhookDeletedEvent => "Admin.DeleteWebhook",
            AdminTenantSettingsUpdatedEvent => "Admin.UpdateTenantSettings",
            AdminSigningKeyChangedEvent e => $"Admin.SigningKey.{e.Action}",
            AdminJourneyPolicyUpdatedEvent => "Admin.UpdateJourneyPolicy",
            AdminAuditLogsPurgedEvent => "Admin.PurgeAuditLogs",
            AdminApiResourceCreatedEvent => "Admin.CreateApiResource",
            AdminApiResourceUpdatedEvent => "Admin.UpdateApiResource",
            AdminApiResourceDeletedEvent => "Admin.DeleteApiResource",
            AdminApiScopeCreatedEvent => "Admin.CreateApiScope",
            AdminApiScopeUpdatedEvent => "Admin.UpdateApiScope",
            AdminApiScopeDeletedEvent => "Admin.DeleteApiScope",
            AdminSamlSpCreatedEvent => "Admin.CreateSamlSp",
            AdminSamlSpUpdatedEvent => "Admin.UpdateSamlSp",
            AdminSamlSpDeletedEvent => "Admin.DeleteSamlSp",
            _ => $"Admin.{evt.EventType}"
        };
    }

    private string? GetAdminEventDetails(AdminActionEvent evt)
    {
        return evt switch
        {
            AdminUserCreatedEvent e => JsonSerializer.Serialize(new { e.Email, e.Username }, _jsonOptions),
            AdminUserUpdatedEvent e when e.ChangedFields != null => JsonSerializer.Serialize(new { ChangedFields = e.ChangedFields.Keys }, _jsonOptions),
            AdminClientCreatedEvent e => JsonSerializer.Serialize(new { e.ClientId }, _jsonOptions),
            AdminClientUpdatedEvent e => JsonSerializer.Serialize(new { e.ClientId, e.ChangedFields }, _jsonOptions),
            AdminClientDeletedEvent e => JsonSerializer.Serialize(new { e.ClientId }, _jsonOptions),
            AdminRoleCreatedEvent e => JsonSerializer.Serialize(new { e.RoleName }, _jsonOptions),
            AdminRoleUpdatedEvent e => JsonSerializer.Serialize(new { e.RoleName }, _jsonOptions),
            AdminRoleDeletedEvent e => JsonSerializer.Serialize(new { e.RoleName }, _jsonOptions),
            AdminUserRoleAssignedEvent e => JsonSerializer.Serialize(new { UserId = e.ResourceId, e.RoleName }, _jsonOptions),
            AdminUserRoleRemovedEvent e => JsonSerializer.Serialize(new { UserId = e.ResourceId, e.RoleName }, _jsonOptions),
            AdminIdpCreatedEvent e => JsonSerializer.Serialize(new { e.Scheme, e.ProviderType }, _jsonOptions),
            AdminIdpUpdatedEvent e => JsonSerializer.Serialize(new { e.Scheme }, _jsonOptions),
            AdminIdpDeletedEvent e => JsonSerializer.Serialize(new { e.Scheme }, _jsonOptions),
            AdminWebhookCreatedEvent e => JsonSerializer.Serialize(new { e.EndpointUrl }, _jsonOptions),
            AdminTenantSettingsUpdatedEvent e => JsonSerializer.Serialize(new { e.ChangedSettings }, _jsonOptions),
            AdminSigningKeyChangedEvent e => JsonSerializer.Serialize(new { e.KeyId, e.Action }, _jsonOptions),
            AdminJourneyPolicyUpdatedEvent e => JsonSerializer.Serialize(new { e.PolicyType }, _jsonOptions),
            AdminAuditLogsPurgedEvent e => JsonSerializer.Serialize(new { e.CutoffDate, e.DeletedCount }, _jsonOptions),
            AdminApiResourceCreatedEvent e => JsonSerializer.Serialize(new { e.ApiResourceName }, _jsonOptions),
            AdminApiResourceUpdatedEvent e => JsonSerializer.Serialize(new { e.ApiResourceName }, _jsonOptions),
            AdminApiResourceDeletedEvent e => JsonSerializer.Serialize(new { e.ApiResourceName }, _jsonOptions),
            AdminApiScopeCreatedEvent e => JsonSerializer.Serialize(new { e.ApiScopeName }, _jsonOptions),
            AdminApiScopeUpdatedEvent e => JsonSerializer.Serialize(new { e.ApiScopeName }, _jsonOptions),
            AdminApiScopeDeletedEvent e => JsonSerializer.Serialize(new { e.ApiScopeName }, _jsonOptions),
            AdminSamlSpCreatedEvent e => JsonSerializer.Serialize(new { e.EntityId }, _jsonOptions),
            AdminSamlSpUpdatedEvent e => JsonSerializer.Serialize(new { e.EntityId }, _jsonOptions),
            AdminSamlSpDeletedEvent e => JsonSerializer.Serialize(new { e.EntityId }, _jsonOptions),
            _ => null
        };
    }
}

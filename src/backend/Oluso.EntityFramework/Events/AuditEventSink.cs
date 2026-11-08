using System.Text.Json;
using Microsoft.Extensions.Logging;
using Oluso.Core;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Services;

namespace Oluso.EntityFramework.Events;

/// <summary>
/// Event sink that persists events to the audit log database.
/// Maps OlusoEvents to AuditLog entries.
/// </summary>
public class AuditEventSink : IOlusoEventSink
{
    private readonly IAuditLogStore _store;
    private readonly IOlusoUserService? _userService;
    private readonly ILogger<AuditEventSink> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    public string Name => "Audit";

    public AuditEventSink(
        IAuditLogStore store,
        ILogger<AuditEventSink> logger,
        IOlusoUserService? userService = null)
    {
        _store = store;
        _userService = userService;
        _logger = logger;
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
    }

    public async Task HandleAsync(OlusoEvent evt, CancellationToken cancellationToken = default)
    {
        if (!evt.IsAuditable)
        {
            return;
        }

        try
        {
            var auditLog = MapToAuditLog(evt);
            auditLog.IpAddress ??= evt.Origin?.IpAddress;

            // OIDC protocol endpoints (token, end-session, etc.) run on the
            // platform IdP host so the raise site's _tenantContext.TenantId
            // is "_platform" — which would tag the audit row with _platform
            // and make it invisible to the user's tenant Activity Logs page.
            // Resolve the user's home tenant via SubjectId in those cases so
            // the row attributes correctly to the tenant that owns the user.
            if (NeedsTenantResolution(auditLog) && _userService != null && !string.IsNullOrEmpty(auditLog.SubjectId))
            {
                var user = await _userService.GetByIdAsync(auditLog.SubjectId, cancellationToken);
                if (user != null && !string.IsNullOrEmpty(user.TenantId))
                {
                    auditLog.TenantId = user.TenantId;
                }
            }

            await _store.WriteAsync(auditLog, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write audit log for event {EventType}", evt.EventType);
            // Don't rethrow - audit failures shouldn't break the event flow
        }
    }

    /// <summary>
    /// Whether the user the event is about caused it themselves: a self-service
    /// sign-up or profile edit, as opposed to an admin, a service client or SCIM
    /// acting on their account.
    /// </summary>
    private static bool ActsOnOwnAccount(OlusoEvent evt) =>
        evt.Origin?.Kind is null or EventOriginKinds.User or EventOriginKinds.Anonymous or EventOriginKinds.Journey;

    private static bool NeedsTenantResolution(AuditLog log)
    {
        return string.IsNullOrEmpty(log.TenantId)
            || string.Equals(log.TenantId, PlatformConstants.TenantId, StringComparison.Ordinal);
    }

    private AuditLog MapToAuditLog(OlusoEvent evt)
    {
        var auditLog = new AuditLog
        {
            // Tag with the event's tenant so cross-tenant flows (e.g. login
            // via the platform URL while signing into tenant-X) attribute
            // the row to the user's tenant rather than landing as _platform
            // and being invisible to that tenant's Activity Logs view.
            TenantId = evt.TenantId,
            Timestamp = evt.Timestamp,
            EventType = evt.EventType,
            Category = evt.Category,
            Success = !evt.IsFailure,
            ActivityId = evt.ActivityId,
            OriginKind = evt.Origin?.Kind,
            OriginId = Truncate(evt.Origin?.Id, 200),
            OriginName = Truncate(evt.Origin?.Name, 200)
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
                auditLog.Action = ActsOnOwnAccount(e) ? "Register" : "CreateUser";
                auditLog.SubjectId = e.SubjectId;
                auditLog.SubjectEmail = e.Email;
                auditLog.SubjectName = e.Username;
                auditLog.ClientId = e.ClientId;
                auditLog.ResourceType = "User";
                auditLog.ResourceId = e.SubjectId;
                break;

            case UserUpdatedEvent e:
                auditLog.Action = ActsOnOwnAccount(e) ? "UpdateProfile" : "UpdateUser";
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

            case UserDeactivatedEvent e:
                auditLog.Action = "DeactivateUser";
                auditLog.SubjectId = e.SubjectId;
                auditLog.SubjectEmail = e.Email;
                auditLog.ResourceType = "User";
                auditLog.ResourceId = e.SubjectId;
                break;

            case UserReactivatedEvent e:
                auditLog.Action = "ReactivateUser";
                auditLog.SubjectId = e.SubjectId;
                auditLog.SubjectEmail = e.Email;
                auditLog.ResourceType = "User";
                auditLog.ResourceId = e.SubjectId;
                break;

            case UserEmailVerifiedEvent e:
                auditLog.Action = "VerifyEmail";
                auditLog.SubjectId = e.SubjectId;
                auditLog.SubjectEmail = e.Email;
                break;

            case UserPasswordChangedEvent e:
                auditLog.Action = e.ChangedByAdmin ? "ResetPassword" : "ChangePassword";
                auditLog.SubjectId = e.SubjectId;
                auditLog.ResourceType = "User";
                auditLog.ResourceId = e.SubjectId;
                auditLog.Reason = e.ChangedByAdmin ? "Admin" : "Self";
                break;

            case UserRoleAssignedEvent e:
                auditLog.Action = "AssignRole";
                auditLog.SubjectId = e.SubjectId;
                auditLog.ResourceType = "UserRole";
                auditLog.ResourceId = e.RoleId;
                auditLog.ResourceName = e.RoleName;
                break;

            case UserRoleRemovedEvent e:
                auditLog.Action = "RemoveRole";
                auditLog.SubjectId = e.SubjectId;
                auditLog.ResourceType = "UserRole";
                auditLog.ResourceId = e.RoleId;
                auditLog.ResourceName = e.RoleName;
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

            case ScopesDroppedEvent e:
                auditLog.Action = "ScopesDropped";
                auditLog.ClientId = e.ClientId;
                auditLog.Details = JsonSerializer.Serialize(new
                {
                    e.Endpoint,
                    RequestedScopes = e.RequestedScopes.ToList(),
                    GrantedScopes = e.GrantedScopes.ToList(),
                    e.DroppedScopes
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

            // The answers stay with the submission: the audit trail records that one arrived, never what it said.
            case JourneySubmissionCreatedEvent e:
                auditLog.Action = e.WebhookEventType!;
                auditLog.ResourceType = "JourneySubmission";
                auditLog.ResourceId = e.SubmissionId;
                auditLog.ResourceName = e.PolicyName;
                auditLog.Details = JsonSerializer.Serialize(new
                {
                    e.PolicyId,
                    e.JourneyId,
                    e.FieldNames,
                    e.FieldCount
                }, _jsonOptions);
                break;

            default:
                MapGeneric(evt, auditLog);
                break;
        }

        return auditLog;
    }

    // Base-class and hook plumbing that says nothing about what happened.
    private static readonly HashSet<string> NonDetailProperties = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(OlusoEvent.Id), nameof(OlusoEvent.Timestamp), nameof(OlusoEvent.TenantId), nameof(OlusoEvent.ActivityId),
        nameof(OlusoEvent.Category), nameof(OlusoEvent.Scope), nameof(OlusoEvent.EventType), nameof(OlusoEvent.IsFailure),
        nameof(OlusoEvent.WebhookEventType), nameof(OlusoEvent.IsAuditable), nameof(OlusoEvent.Origin), "Cancel", "CancelReason",
        // lifted into columns below
        "SubjectId", "UserId", "SubjectName", "Username", "UserName", "ClientId", "IpAddress", "UserAgent",
        "ResourceType", "ResourceId", "ResourceName", "ErrorMessage", "Error"
    };

    /// <summary>
    /// Events without a dedicated mapping: the webhook name is the action when
    /// there is one (readable and stable), well-known properties become the
    /// user/client/resource/IP columns, and only the event's own remaining
    /// properties go into Details.
    /// </summary>
    private void MapGeneric(OlusoEvent evt, AuditLog auditLog)
    {
        auditLog.Action = string.IsNullOrEmpty(evt.WebhookEventType) ? evt.EventType : evt.WebhookEventType;

        var props = evt.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Where(p => p.CanRead && p.GetIndexParameters().Length == 0)
            .ToList();
        string? Text(params string[] names)
        {
            foreach (var name in names)
            {
                var prop = props.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
                var value = prop?.GetValue(evt);
                if (value is string str && !string.IsNullOrWhiteSpace(str)) return str;
            }
            return null;
        }

        auditLog.SubjectId = Text("SubjectId", "UserId");
        auditLog.SubjectName = Text("SubjectName", "Username", "UserName");
        auditLog.ClientId = Text("ClientId");
        auditLog.IpAddress = Text("IpAddress");
        auditLog.UserAgent = Text("UserAgent");
        auditLog.ResourceType = Text("ResourceType");
        auditLog.ResourceId = Text("ResourceId");
        auditLog.ResourceName = Text("ResourceName");
        auditLog.ErrorMessage = Text("ErrorMessage", "Error");

        var details = new Dictionary<string, object?>();
        foreach (var prop in props.Where(p => !NonDetailProperties.Contains(p.Name)))
        {
            var value = prop.GetValue(evt);
            if (value == null) continue;
            if (value is string str && str.Length == 0) continue;
            details[JsonNamingPolicy.CamelCase.ConvertName(prop.Name)] = value;
        }
        auditLog.Details = details.Count == 0 ? null : JsonSerializer.Serialize(details, _jsonOptions);
    }

    private static string? Truncate(string? value, int max) =>
        value == null || value.Length <= max ? value : value[..max];

    private static string GetAdminAction(AdminActionEvent evt)
    {
        return evt switch
        {
            AdminClientCreatedEvent => "Admin.CreateClient",
            AdminClientUpdatedEvent => "Admin.UpdateClient",
            AdminClientDeletedEvent => "Admin.DeleteClient",
            AdminRoleCreatedEvent => "Admin.CreateRole",
            AdminRoleUpdatedEvent => "Admin.UpdateRole",
            AdminRoleDeletedEvent => "Admin.DeleteRole",
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
            AdminResourceCreatedEvent => "Admin.CreateResource",
            AdminResourceUpdatedEvent => "Admin.UpdateResource",
            AdminResourceDeletedEvent => "Admin.DeleteResource",
            AdminApiScopeCreatedEvent => "Admin.CreateApiScope",
            AdminApiScopeUpdatedEvent => "Admin.UpdateApiScope",
            AdminApiScopeDeletedEvent => "Admin.DeleteApiScope",
            _ => $"Admin.{evt.ResourceType}.{evt.WebhookEventType ?? evt.EventType}"
        };
    }

    private string? GetAdminEventDetails(AdminActionEvent evt)
    {
        return evt switch
        {
            AdminClientCreatedEvent e => JsonSerializer.Serialize(new { e.ClientId }, _jsonOptions),
            AdminClientUpdatedEvent e => JsonSerializer.Serialize(new { e.ClientId, e.ChangedFields }, _jsonOptions),
            AdminClientDeletedEvent e => JsonSerializer.Serialize(new { e.ClientId }, _jsonOptions),
            AdminRoleCreatedEvent e => JsonSerializer.Serialize(new { e.RoleName }, _jsonOptions),
            AdminRoleUpdatedEvent e => JsonSerializer.Serialize(new { e.RoleName }, _jsonOptions),
            AdminRoleDeletedEvent e => JsonSerializer.Serialize(new { e.RoleName }, _jsonOptions),
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
            AdminResourceCreatedEvent e => JsonSerializer.Serialize(new { e.ResourceUri }, _jsonOptions),
            AdminResourceUpdatedEvent e => JsonSerializer.Serialize(new { e.ResourceUri }, _jsonOptions),
            AdminResourceDeletedEvent e => JsonSerializer.Serialize(new { e.ResourceUri }, _jsonOptions),
            AdminApiScopeCreatedEvent e => JsonSerializer.Serialize(new { e.ApiScopeName }, _jsonOptions),
            AdminApiScopeUpdatedEvent e => JsonSerializer.Serialize(new { e.ApiScopeName }, _jsonOptions),
            AdminApiScopeDeletedEvent e => JsonSerializer.Serialize(new { e.ApiScopeName }, _jsonOptions),
            _ => JsonSerializer.Serialize(evt, evt.GetType(), _jsonOptions)
        };
    }
}

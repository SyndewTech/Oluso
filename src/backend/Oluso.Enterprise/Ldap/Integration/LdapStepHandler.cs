using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Oluso.Enterprise.Ldap.Authentication;
using Oluso.Enterprise.Ldap.GroupMapping;

namespace Oluso.Enterprise.Ldap.Integration;

/// <summary>
/// Journey step handler for LDAP authentication.
/// Uses IOlusoUserService for user management abstraction.
/// </summary>
public class LdapStepHandler : IStepHandler
{
    public string StepType => "ldap";

    public async Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default)
    {
        var authenticator = context.ServiceProvider.GetRequiredService<ILdapAuthenticator>();
        var groupMapper = context.ServiceProvider.GetRequiredService<ILdapGroupMapper>();
        var userService = context.ServiceProvider.GetRequiredService<IOlusoUserService>();
        var tenantContext = context.ServiceProvider.GetRequiredService<ITenantContext>();
        var eventService = context.ServiceProvider.GetRequiredService<IOlusoEventService>();
        var httpContextAccessor = context.ServiceProvider.GetService<IHttpContextAccessor>();
        var logger = context.ServiceProvider.GetRequiredService<ILogger<LdapStepHandler>>();

        var ipAddress = httpContextAccessor?.HttpContext?.Connection.RemoteIpAddress?.ToString();

        // Check if credentials were submitted
        var username = context.GetInput("username");
        var password = context.GetInput("password");

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            // Show login form
            return StepHandlerResult.ShowUi("Journey/_LdapLogin", new LdapLoginViewModel
            {
                TenantName = tenantContext.Tenant?.Name,
                ProviderDisplayName = context.GetConfig("displayName", "Corporate Directory")
            });
        }

        // Authenticate against LDAP
        var ldapResult = await authenticator.AuthenticateAsync(username, password, cancellationToken);

        if (!ldapResult.Success || ldapResult.User == null)
        {
            logger.LogWarning("LDAP authentication failed for {Username}: {Error}", username, ldapResult.ErrorMessage);

            // Raise authentication failed event
            await eventService.RaiseAsync(new LdapAuthenticationFailedEvent
            {
                TenantId = tenantContext.TenantId,
                Username = username,
                FailureReason = ldapResult.ErrorMessage ?? "Invalid credentials",
                IpAddress = ipAddress
            }, cancellationToken);

            return StepHandlerResult.ShowUi("Journey/_LdapLogin", new LdapLoginViewModel
            {
                TenantName = tenantContext.Tenant?.Name,
                ProviderDisplayName = context.GetConfig("displayName", "Corporate Directory"),
                ErrorMessage = "Invalid username or password",
                Username = username
            });
        }

        // Get LDAP groups and map to roles
        var groups = await groupMapper.GetUserGroupsAsync(ldapResult.User.DistinguishedName, cancellationToken);
        var roles = groupMapper.MapGroupsToRoles(groups);

        // Find or create user via user service
        var user = await FindOrProvisionUserAsync(
            userService,
            tenantContext,
            eventService,
            ldapResult.User,
            roles,
            context.GetConfig("autoProvision", true),
            logger,
            cancellationToken);

        if (user == null)
        {
            return StepHandlerResult.Failure("user_not_found", "No account found. Please contact your administrator.");
        }

        if (!user.IsActive)
        {
            return StepHandlerResult.Failure("user_deactivated", "Your account has been deactivated");
        }

        // Update journey context with authenticated user
        context.UserId = user.Id;
        context.SetData("authenticated_at", DateTime.UtcNow);
        context.SetData("auth_method", "ldap");
        context.SetData("amr", new[] { "pwd", "ldap" });

        // Record login
        await userService.RecordLoginAsync(user.Id, cancellationToken);

        logger.LogInformation("User {UserId} authenticated via LDAP", user.Id);

        // Raise authentication success event
        await eventService.RaiseAsync(new LdapAuthenticationSuccessEvent
        {
            TenantId = tenantContext.TenantId,
            SubjectId = user.Id,
            Username = ldapResult.User.Username,
            DistinguishedName = ldapResult.User.DistinguishedName,
            Groups = groups.ToList(),
            MappedRoles = roles.ToList(),
            IpAddress = ipAddress
        }, cancellationToken);

        // Return success with claims
        var outputData = new Dictionary<string, object>
        {
            ["sub"] = user.Id,
            ["name"] = user.DisplayName ?? user.Username,
            ["email"] = user.Email ?? "",
            ["email_verified"] = user.EmailVerified,
            ["idp"] = "ldap"
        };

        // Add roles
        if (roles.Count > 0)
        {
            outputData["roles"] = roles;
        }

        return StepHandlerResult.Success(outputData);
    }

    private async Task<OlusoUserInfo?> FindOrProvisionUserAsync(
        IOlusoUserService userService,
        ITenantContext tenantContext,
        IOlusoEventService eventService,
        LdapUser ldapUser,
        IReadOnlyList<string> roles,
        bool autoProvision,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // Try to find user by username first
        var user = await userService.FindByUsernameAsync(ldapUser.Username, cancellationToken);

        if (user == null && !string.IsNullOrEmpty(ldapUser.Email))
        {
            // Try to find by email
            user = await userService.FindByEmailAsync(ldapUser.Email, cancellationToken);
        }

        if (user == null && autoProvision)
        {
            // Create new user via user service
            var createResult = await userService.CreateUserAsync(new CreateUserRequest
            {
                Username = ldapUser.Username,
                Email = ldapUser.Email ?? $"{ldapUser.Username}@ldap.local",
                Password = Guid.NewGuid().ToString(), // Random password - user authenticates via LDAP
                FirstName = ldapUser.FirstName,
                LastName = ldapUser.LastName,
                TenantId = tenantContext.TenantId,
                RequireEmailVerification = false // LDAP users don't need email verification
            }, cancellationToken);

            if (!createResult.Succeeded)
            {
                logger.LogError("Failed to create user from LDAP: {Error}", createResult.Error);
                return null;
            }

            logger.LogInformation("Created new user {UserId} from LDAP with DN {Dn}",
                createResult.UserId, ldapUser.DistinguishedName);

            // Raise user provisioned event
            await eventService.RaiseAsync(new LdapUserProvisionedEvent
            {
                TenantId = tenantContext.TenantId,
                SubjectId = createResult.UserId!,
                Username = ldapUser.Username,
                Email = ldapUser.Email,
                DistinguishedName = ldapUser.DistinguishedName
            }, cancellationToken);

            user = createResult.User;
        }

        return user;
    }

    public Task<StepConfigurationValidationResult> ValidateConfigurationAsync(IDictionary<string, object>? configuration)
    {
        // LDAP configuration is validated at service registration
        return Task.FromResult(StepConfigurationValidationResult.Valid());
    }
}

public class LdapLoginViewModel
{
    public string? TenantName { get; set; }
    public string ProviderDisplayName { get; set; } = "LDAP";
    public string? Username { get; set; }
    public string? ErrorMessage { get; set; }
    public bool ShowRememberMe { get; set; } = true;
}

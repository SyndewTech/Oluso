using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Oluso.Enterprise.Ldap.Authentication;
using Oluso.Enterprise.Ldap.Configuration;
using Oluso.Enterprise.Ldap.Events;
using Oluso.Enterprise.Ldap.GroupMapping;

namespace Oluso.Enterprise.Ldap.Integration;

/// <summary>
/// Journey step handler for LDAP authentication.
/// Uses IOlusoUserService for user management abstraction.
/// </summary>
public class LdapStepHandler : IPrimaryAuthenticationStep, IStepConfigurationTester
{
    /// <summary>Connects to the step's directory with its service account and reads the base DN.</summary>
    public async Task<StepConfigurationTestResult> TestConfigurationAsync(StepExecutionContext context, CancellationToken cancellationToken = default)
    {
        var directory = await ResolveDirectoryAsync(context, cancellationToken);
        if (directory.Error != null)
        {
            return StepConfigurationTestResult.Failed(directory.Error);
        }

        var result = await context.ServiceProvider.GetRequiredService<ILdapAuthenticator>()
            .TestConnectionAsync(directory.Settings, cancellationToken);
        var details = new List<string> { directory.Settings == null ? "Directory: the LDAP module's" : $"Provider: {directory.DisplayName ?? directory.Idp}" };
        details.AddRange(result.Details);
        return StepConfigurationTestResult.Of(result.Success, result.Message, details);
    }

    public string StepType => "ldap";

    /// <summary>Step settings that override the chosen provider's directory settings.</summary>
    internal static readonly string[] OverrideSettings = { "server", "baseDn", "userSearchFilter", "bindDn", "useSsl" };

    public async Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default)
    {
        var authenticator = context.ServiceProvider.GetRequiredService<ILdapAuthenticator>();
        var userService = context.ServiceProvider.GetRequiredService<IOlusoUserService>();
        var tenantContext = context.ServiceProvider.GetRequiredService<ITenantContext>();
        var eventService = context.ServiceProvider.GetRequiredService<IOlusoEventService>();
        var httpContextAccessor = context.ServiceProvider.GetService<IHttpContextAccessor>();
        var logger = context.ServiceProvider.GetRequiredService<ILogger<LdapStepHandler>>();

        var ipAddress = httpContextAccessor?.HttpContext?.Connection.RemoteIpAddress?.ToString();

        var directory = await ResolveDirectoryAsync(context, cancellationToken);
        if (directory.Error != null)
        {
            logger.LogError("LDAP step {StepId}: {Error}", context.StepId, directory.Error);
            return StepHandlerResult.Failure("ldap_provider_unavailable", "Directory sign-in isn't available. Please contact your administrator.");
        }

        var allowPasswordChange = context.GetConfig("allowPasswordChange", false);
        LdapLoginViewModel Form(string? error = null, string? username = null, bool changePassword = false, string? info = null) => new()
        {
            TenantName = tenantContext.Tenant?.Name,
            ProviderDisplayName = context.GetLocalizedConfig("displayName") ?? directory.DisplayName ?? "Corporate Directory",
            ErrorMessage = error,
            InfoMessage = info,
            Username = username,
            AllowPasswordChange = allowPasswordChange,
            ChangePasswordMode = allowPasswordChange && changePassword
        };

        var action = context.GetInput("action");
        var username = context.GetInput("username");
        var password = context.GetInput("password");

        if (allowPasswordChange && action == "show_change_password")
        {
            return StepHandlerResult.ShowUi("Journey/_LdapLogin", Form(username: username, changePassword: true));
        }

        if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        {
            return StepHandlerResult.ShowUi("Journey/_LdapLogin", Form(username: username, changePassword: action == "change_password"));
        }

        if (allowPasswordChange && action == "change_password")
        {
            var newPassword = context.GetInput("newPassword");
            if (string.IsNullOrEmpty(newPassword) || newPassword != context.GetInput("confirmPassword"))
            {
                return StepHandlerResult.ShowUi("Journey/_LdapLogin", Form("The new passwords don't match", username, changePassword: true));
            }

            var change = await authenticator.ChangePasswordAsync(username, password, newPassword, directory.Settings, cancellationToken);
            if (!change.Success)
            {
                logger.LogWarning("LDAP password change failed for {Username}: {Status}", username, change.Status);
                return StepHandlerResult.ShowUi("Journey/_LdapLogin", Form(change.ErrorMessage, username, changePassword: true));
            }

            await eventService.RaiseAsync(new LdapPasswordChangedEvent
            {
                TenantId = tenantContext.TenantId,
                Username = username,
                IpAddress = ipAddress
            }, cancellationToken);

            // Signs in with the new password.
            password = newPassword;
        }

        var ldapResult = directory.Settings == null
            ? await authenticator.AuthenticateAsync(username, password, cancellationToken)
            : await authenticator.AuthenticateAsync(username, password, directory.Settings, cancellationToken);

        if (!ldapResult.Success || ldapResult.User == null)
        {
            logger.LogWarning("LDAP authentication failed for {Username}: {Error}", username, ldapResult.ErrorMessage);

            await eventService.RaiseAsync(new LdapAuthenticationFailedEvent
            {
                TenantId = tenantContext.TenantId,
                Username = username,
                FailureReason = ldapResult.ErrorMessage ?? "Invalid credentials",
                IpAddress = ipAddress
            }, cancellationToken);

            return StepHandlerResult.ShowUi("Journey/_LdapLogin", Form("Invalid username or password", username));
        }

        IReadOnlyList<string> groups = Array.Empty<string>();
        IReadOnlyList<string> roles = Array.Empty<string>();
        if (context.GetConfig("syncGroups", true))
        {
            if (directory.Settings == null)
            {
                var groupMapper = context.ServiceProvider.GetRequiredService<ILdapGroupMapper>();
                groups = await groupMapper.GetUserGroupsAsync(ldapResult.User.DistinguishedName, cancellationToken);
                roles = groupMapper.MapGroupsToRoles(groups);
            }
            else
            {
                // The provider's own memberOf groups; roles when the provider maps groups to roles.
                groups = ldapResult.User.Groups;
                roles = ldapResult.Claims.Where(c => c.Type == ClaimTypes.Role).Select(c => c.Value).Distinct().ToList();
            }
        }

        var autoProvision = context.GetConfig("autoProvision", true) && (directory.Settings?.AutoProvisionUsers ?? true);
        var user = await FindOrProvisionUserAsync(
            userService,
            tenantContext,
            context.ServiceProvider.GetRequiredService<ITenantEmailDomainStore>(),
            eventService,
            ldapResult.User,
            roles,
            autoProvision,
            context.ClientId,
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

        context.UserId = user.Id;
        context.SetData("authenticated_at", DateTime.UtcNow);
        context.SetData("auth_method", "ldap");
        context.SetData("amr", new[] { "pwd", "ldap" });

        await userService.RecordLoginAsync(user.Id, cancellationToken);

        logger.LogInformation("User {UserId} authenticated via LDAP ({Idp})", user.Id, directory.Idp);

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

        var outputData = new Dictionary<string, object>
        {
            ["sub"] = user.Id,
            ["name"] = user.DisplayName ?? user.Username,
            ["email"] = user.Email ?? "",
            ["email_verified"] = user.EmailVerified,
            ["idp"] = directory.Idp
        };

        if (groups.Count > 0)
        {
            outputData["groups"] = groups.ToList();
        }

        if (roles.Count > 0)
        {
            outputData["roles"] = roles;
        }

        return StepHandlerResult.Success(outputData);
    }

    internal sealed record Directory(LdapProviderSettings? Settings, string Idp, string? DisplayName, string? Error);

    /// <summary>
    /// The chosen LDAP identity provider with this step's overrides applied, or the LDAP
    /// module's directory when the step names no provider.
    /// </summary>
    internal static async Task<Directory> ResolveDirectoryAsync(StepExecutionContext context, CancellationToken cancellationToken)
    {
        var scheme = context.GetConfig<string>("ldapProvider");
        if (string.IsNullOrWhiteSpace(scheme))
        {
            return new Directory(null, "ldap", null, null);
        }

        var store = context.ServiceProvider.GetService<IExternalProviderConfigStore>();
        var provider = store == null ? null : await store.GetBySchemeAsync(scheme, cancellationToken: cancellationToken);
        if (provider == null || !provider.ProviderType.Equals("ldap", StringComparison.OrdinalIgnoreCase))
        {
            return new Directory(null, scheme, null, $"LDAP identity provider '{scheme}' was not found");
        }
        if (!provider.Enabled)
        {
            return new Directory(null, scheme, null, $"LDAP identity provider '{scheme}' is disabled");
        }

        var settings = LdapProviderSettings.FromProviderDefinition(provider);
        if (context.GetConfig<string>("server") is { Length: > 0 } server) settings.Server = server;
        if (context.GetConfig<string>("baseDn") is { Length: > 0 } baseDn) settings.BaseDn = baseDn;
        if (context.GetConfig<string>("userSearchFilter") is { Length: > 0 } filter) settings.UserSearchFilter = filter;
        if (context.GetConfig<string>("bindDn") is { Length: > 0 } bindDn) settings.BindDn = bindDn;
        if (context.GetConfig<bool?>("useSsl", null) is { } useSsl) settings.UseSsl = useSsl;

        return settings.IsValid(out var invalid)
            ? new Directory(settings, provider.Scheme, provider.DisplayName, null)
            : new Directory(null, provider.Scheme, provider.DisplayName, $"LDAP identity provider '{scheme}': {invalid}");
    }

    private async Task<OlusoUserInfo?> FindOrProvisionUserAsync(
        IOlusoUserService userService,
        ITenantContext tenantContext,
        ITenantEmailDomainStore emailDomains,
        IOlusoEventService eventService,
        LdapUser ldapUser,
        IReadOnlyList<string> roles,
        bool autoProvision,
        string? clientId,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        // Try to find user by username first
        var user = await userService.FindByUsernameAsync(ldapUser.Username, cancellationToken);

        // The directory's mail attribute names an account only at a domain the tenant has verified: anywhere else
        // it is just text the directory holds, and would sign the person in as whoever has that address.
        var emailAtVerifiedDomain = !string.IsNullOrEmpty(ldapUser.Email) && !string.IsNullOrEmpty(tenantContext.TenantId)
            && await emailDomains.IsVerifiedForTenantAsync(tenantContext.TenantId, ldapUser.Email, cancellationToken);

        if (user == null && !string.IsNullOrEmpty(ldapUser.Email))
        {
            var owner = await userService.FindByEmailAsync(ldapUser.Email, cancellationToken);
            if (owner != null && !emailAtVerifiedDomain)
            {
                logger.LogWarning(
                    "LDAP user {Username} has the email of account {UserId}, at a domain tenant {TenantId} hasn't verified; not signing in as it",
                    ldapUser.Username, owner.Id, tenantContext.TenantId);
                return null;
            }
            user = owner;
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
                // Verified only at a domain the tenant has verified (below).
                ClientId = clientId
            }, cancellationToken);

            if (!createResult.Succeeded)
            {
                logger.LogError("Failed to create user from LDAP: {Error}", createResult.Error);
                return null;
            }

            if (emailAtVerifiedDomain)
            {
                await userService.SetEmailVerifiedAsync(createResult.UserId!, true, cancellationToken);
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

            user = await userService.FindByIdAsync(createResult.UserId!, cancellationToken);
        }

        return user;
    }

    public Task<StepConfigurationValidationResult> ValidateConfigurationAsync(IDictionary<string, object>? configuration)
    {
        static bool IsSet(IDictionary<string, object>? config, string key) =>
            config != null && config.TryGetValue(key, out var value) && value != null && !string.IsNullOrWhiteSpace(value.ToString());

        // Overrides adjust a chosen provider; the module's directory is configured on its own.
        var stray = OverrideSettings.Where(k => IsSet(configuration, k)).ToList();
        return Task.FromResult(stray.Count > 0 && !IsSet(configuration, "ldapProvider")
            ? StepConfigurationValidationResult.Invalid($"{string.Join(", ", stray)} only apply when an LDAP provider is selected")
            : StepConfigurationValidationResult.Valid());
    }
}

public class LdapLoginViewModel
{
    public string? TenantName { get; set; }
    public string ProviderDisplayName { get; set; } = "LDAP";
    public string? Username { get; set; }
    public string? ErrorMessage { get; set; }
    public string? InfoMessage { get; set; }
    public bool ShowRememberMe { get; set; } = true;
    public bool AllowPasswordChange { get; set; }
    public bool ChangePasswordMode { get; set; }
}

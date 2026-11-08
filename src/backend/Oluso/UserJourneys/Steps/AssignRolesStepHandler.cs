using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;

namespace Oluso.UserJourneys.Steps;

/// <summary>
/// Grants roles to the journey's user: the account an earlier step created or signed in.
/// Placed on a branch, it grants only when that branch is taken (e.g. owner rights after a
/// business was registered). Roles are role ids of this organisation or platform-wide ones;
/// granting a role the user already holds changes nothing.
///
/// Configuration:
/// - roles: role ids to grant (required)
/// </summary>
public class AssignRolesStepHandler : IStepHandler, IStepConfigurationTester
{
    public const string RolesSetting = "roles";

    public string StepType => "assign_roles";

    public Task<StepConfigurationValidationResult> ValidateConfigurationAsync(IDictionary<string, object>? configuration)
    {
        var roles = configuration?.FirstOrDefault(kv => string.Equals(kv.Key, RolesSetting, StringComparison.OrdinalIgnoreCase)).Value;
        return Task.FromResult(StepConfigurationHelpers.IsNonEmptyList(roles)
            ? StepConfigurationValidationResult.Valid()
            : StepConfigurationValidationResult.Invalid("Choose at least one role to assign"));
    }

    public async Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default)
    {
        var logger = context.ServiceProvider.GetRequiredService<ILogger<AssignRolesStepHandler>>();
        var tenantContext = context.ServiceProvider.GetRequiredService<ITenantContext>();

        if (string.IsNullOrEmpty(context.UserId))
        {
            logger.LogError("Assign roles step {StepId} ran with no user; place it after the step that creates or signs in the account", context.StepId);
            return StepHandlerResult.Fail("no_user", "There's no account to give access to yet.");
        }

        var roleIds = RoleIds(context);
        if (roleIds.Count == 0)
        {
            logger.LogError("Assign roles step {StepId} has no roles configured", context.StepId);
            return StepHandlerResult.Fail("assign_roles_not_configured", "Access couldn't be set up for this account.");
        }

        var roles = await GrantableRolesAsync(context, roleIds, tenantContext.TenantId, cancellationToken);
        var missing = roleIds.Where(id => !roles.ContainsKey(id)).ToList();
        if (missing.Count > 0)
        {
            logger.LogError("Assign roles step {StepId}: roles {RoleIds} aren't roles of tenant {TenantId}",
                context.StepId, string.Join(", ", missing), tenantContext.TenantId);
            return StepHandlerResult.Fail("assign_roles_unknown_role", "Access couldn't be set up for this account.");
        }

        var grants = context.ServiceProvider.GetRequiredService<IRoleGrantService>();
        foreach (var roleId in roleIds)
        {
            var added = await grants.GrantAsync(roleId, context.UserId, null, RoleGrantActor.System("Journey"), cancellationToken);
            logger.LogInformation("Journey step {StepId} {Outcome} role {Role} ({RoleId}) for user {UserId}",
                context.StepId, added ? "granted" : "kept", roles[roleId], roleId, context.UserId);
        }

        return StepHandlerResult.Success(new Dictionary<string, object> { ["assigned_roles"] = roleIds });
    }

    public async Task<StepConfigurationTestResult> TestConfigurationAsync(StepExecutionContext context, CancellationToken cancellationToken = default)
    {
        var roleIds = RoleIds(context);
        if (roleIds.Count == 0)
        {
            return StepConfigurationTestResult.Failed("No roles are chosen");
        }

        var tenantId = context.ServiceProvider.GetRequiredService<ITenantContext>().TenantId;
        var roles = await GrantableRolesAsync(context, roleIds, tenantId, cancellationToken);
        var details = roleIds
            .Select(id => roles.TryGetValue(id, out var name) ? $"{name} will be assigned" : $"{id} isn't a role of this organisation")
            .ToArray();
        var ok = roles.Count == roleIds.Count;
        return StepConfigurationTestResult.Of(ok, ok ? "All chosen roles can be assigned" : "Some chosen roles can't be assigned", details);
    }

    private static List<string> RoleIds(StepExecutionContext context) =>
        (context.GetConfig<List<string>>(RolesSetting, null) ?? new List<string>())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct()
            .ToList();

    /// <summary>The chosen roles this organisation can grant (its own or platform-wide), by id, with their names.</summary>
    private static async Task<Dictionary<string, string>> GrantableRolesAsync(
        StepExecutionContext context, IReadOnlyCollection<string> roleIds, string? tenantId, CancellationToken cancellationToken)
    {
        var roleStore = context.ServiceProvider.GetRequiredService<IRoleStore>();
        return (await roleStore.GetByIdsAsync(roleIds, cancellationToken))
            .Where(r => r.TenantId == null || r.TenantId == tenantId)
            .ToDictionary(r => r.Id, r => r.DisplayName ?? r.Name ?? r.Id);
    }
}

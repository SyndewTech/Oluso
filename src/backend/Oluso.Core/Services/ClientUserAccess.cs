using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Protocols.Models;

namespace Oluso.Core.Services;

/// <summary>
/// What a client asks of the users it receives tokens or assertions for.
/// </summary>
/// <param name="AllowedUsers">Subject ids; empty means any user.</param>
/// <param name="AllowedRoles">Role names, any one of which admits the user; empty means any role.</param>
/// <param name="RequiredPermission">A permission the user must hold in the token's tenant; null for none.</param>
/// <param name="RequireVerifiedEmail">Only users whose email address is verified.</param>
public sealed record ClientUserRestrictions(
    IReadOnlyCollection<string> AllowedUsers,
    IReadOnlyCollection<string> AllowedRoles,
    string? RequiredPermission,
    bool RequireVerifiedEmail)
{
    public static ClientUserRestrictions From(Client client) => new(
        client.AllowedUsers.Select(u => u.SubjectId).ToList(),
        client.AllowedRoles.Select(r => r.Role).ToList(),
        client.RequiredPermission,
        client.RequireVerifiedEmail);

    public static ClientUserRestrictions From(ValidatedClient client) => new(
        client.AllowedUsers.ToList(),
        client.AllowedRoles.ToList(),
        client.RequiredPermission,
        client.RequireVerifiedEmail);

    public bool IsEmpty =>
        AllowedUsers.Count == 0 && AllowedRoles.Count == 0
        && string.IsNullOrEmpty(RequiredPermission) && !RequireVerifiedEmail;
}

/// <summary>
/// Whether a user may receive a token or assertion for a client. Every place that issues one for a user asks this,
/// so a restriction set on the client holds whichever grant, response type or protocol the user arrives by.
/// </summary>
public interface IClientUserAccess
{
    /// <summary>
    /// Null when <paramref name="subjectId"/> may have a token for the client; otherwise why not, worded for the user.
    /// Allowed users and allowed roles together say who is assigned to the client: listed by name or holding one of
    /// the roles. The required permission and a verified email are further conditions, each of which must hold.
    /// </summary>
    /// <param name="tenantId">The tenant the token is for; the required permission is looked up there.</param>
    Task<string?> FindDenialAsync(
        string clientId,
        ClientUserRestrictions restrictions,
        string subjectId,
        string? tenantId,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class ClientUserAccess : IClientUserAccess
{
    public const string NotAllowedDescription = "User is not authorized to access this application";
    public const string MissingRoleDescription = "User does not have the required role to access this application";
    public const string MissingPermissionDescription = "User does not have the required permission to access this application";
    public const string UnverifiedEmailDescription = "Verify your email address to access this application";

    private readonly IProfileService _profiles;
    private readonly IOlusoUserService _users;
    private readonly ILogger<ClientUserAccess> _logger;

    public ClientUserAccess(IProfileService profiles, IOlusoUserService users, ILogger<ClientUserAccess> logger)
    {
        _profiles = profiles;
        _users = users;
        _logger = logger;
    }

    public async Task<string?> FindDenialAsync(
        string clientId,
        ClientUserRestrictions restrictions,
        string subjectId,
        string? tenantId,
        CancellationToken cancellationToken = default)
    {
        if (restrictions.IsEmpty)
        {
            return null;
        }

        var restrictsUsers = restrictions.AllowedUsers.Count > 0;
        var restrictsRoles = restrictions.AllowedRoles.Count > 0;
        if (restrictsUsers || restrictsRoles)
        {
            var listed = restrictsUsers && restrictions.AllowedUsers.Contains(subjectId, StringComparer.Ordinal);
            if (!listed)
            {
                var roles = restrictsRoles
                    ? await _profiles.GetUserRolesAsync(subjectId, cancellationToken)
                    : Array.Empty<string>();
                if (!restrictions.AllowedRoles.Any(r => roles.Contains(r, StringComparer.OrdinalIgnoreCase)))
                {
                    _logger.LogWarning(
                        "User {SubjectId} is not assigned to client {ClientId}: not an allowed user and none of the roles [{AllowedRoles}] (has [{UserRoles}])",
                        subjectId, clientId, string.Join(", ", restrictions.AllowedRoles), string.Join(", ", roles));
                    return restrictsRoles && !restrictsUsers ? MissingRoleDescription : NotAllowedDescription;
                }
            }
        }

        // Exact match only: no wildcards, no partial matches.
        if (!string.IsNullOrEmpty(restrictions.RequiredPermission))
        {
            var permissions = await _profiles.GetUserPermissionsAsync(subjectId, tenantId, cancellationToken);
            if (!permissions.Any(p => string.Equals(p, restrictions.RequiredPermission, StringComparison.OrdinalIgnoreCase)))
            {
                _logger.LogWarning(
                    "User {SubjectId} does not have required permission '{Permission}' for client {ClientId} in tenant {TenantId}",
                    subjectId, restrictions.RequiredPermission, clientId, tenantId ?? "(none)");
                return MissingPermissionDescription;
            }
        }

        if (restrictions.RequireVerifiedEmail)
        {
            var user = (await _users.FindByIdsAcrossTenantsAsync(new[] { subjectId }, cancellationToken)).GetValueOrDefault(subjectId);
            if (user == null || string.IsNullOrEmpty(user.Email) || !user.EmailVerified)
            {
                _logger.LogWarning("User {SubjectId} has no verified email; client {ClientId} requires one", subjectId, clientId);
                return UnverifiedEmailDescription;
            }
        }

        return null;
    }
}

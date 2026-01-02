using System.Security.Claims;
using Oluso.Core.Domain.Entities;

namespace Oluso.Core.Services;

/// <summary>
/// Context for profile data requests
/// </summary>
public class ProfileDataRequestContext
{
    /// <summary>
    /// The subject (user) identifier
    /// </summary>
    public required string SubjectId { get; init; }

    /// <summary>
    /// The client requesting the data
    /// </summary>
    public required Client Client { get; init; }

    /// <summary>
    /// The scopes requested
    /// </summary>
    public required IEnumerable<string> RequestedScopes { get; init; }

    /// <summary>
    /// Claim types specifically requested
    /// </summary>
    public IEnumerable<string>? RequestedClaimTypes { get; init; }

    /// <summary>
    /// The caller (e.g., "UserInfoEndpoint", "TokenEndpoint")
    /// </summary>
    public string? Caller { get; init; }

    /// <summary>
    /// The protocol being used (e.g., "oidc", "saml", "wsfed").
    /// Claims providers can use this to only respond to specific protocols.
    /// </summary>
    public string? Protocol { get; init; }

    /// <summary>
    /// The issued claims - add claims here
    /// </summary>
    public List<Claim> IssuedClaims { get; } = new();
}

/// <summary>
/// Context for checking if a user is active
/// </summary>
public class IsActiveContext
{
    /// <summary>
    /// The subject (user) identifier
    /// </summary>
    public required string SubjectId { get; init; }

    /// <summary>
    /// The client making the request
    /// </summary>
    public Client? Client { get; init; }

    /// <summary>
    /// Set to false if the user should be considered inactive
    /// </summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// Service for retrieving user profile data for tokens.
/// Implement this to customize how claims are populated.
/// </summary>
/// <example>
/// <code>
/// public class CustomProfileService : IProfileService
/// {
///     private readonly IUserRepository _users;
///
///     public async Task GetProfileDataAsync(ProfileDataRequestContext context)
///     {
///         var user = await _users.GetByIdAsync(context.SubjectId);
///
///         context.IssuedClaims.Add(new Claim("department", user.Department));
///         context.IssuedClaims.Add(new Claim("employee_id", user.EmployeeId));
///     }
///
///     public Task IsActiveAsync(IsActiveContext context)
///     {
///         // Check if user account is still active
///         context.IsActive = true;
///         return Task.CompletedTask;
///     }
/// }
/// </code>
/// </example>
public interface IProfileService
{
    /// <summary>
    /// Gets profile data for the user
    /// </summary>
    Task GetProfileDataAsync(ProfileDataRequestContext context);

    /// <summary>
    /// Checks if the user is active
    /// </summary>
    Task IsActiveAsync(IsActiveContext context);
}

/// <summary>
/// Extended profile service interface that supports role retrieval.
/// Implement this interface to enable AllowedRoles client restrictions.
/// </summary>
public interface IExtendedProfileService : IProfileService
{
    /// <summary>
    /// Gets the roles assigned to a user
    /// </summary>
    Task<ICollection<string>> GetUserRolesAsync(string subjectId, CancellationToken cancellationToken = default);
}

/// <summary>
/// Extension methods for IProfileService
/// </summary>
public static class ProfileServiceExtensions
{
    /// <summary>
    /// Checks if a user is active (convenience method)
    /// </summary>
    public static async Task<bool> IsActiveAsync(this IProfileService profileService, string subjectId, CancellationToken cancellationToken = default)
    {
        var context = new IsActiveContext { SubjectId = subjectId };
        await profileService.IsActiveAsync(context);
        return context.IsActive;
    }

    /// <summary>
    /// Gets the roles assigned to a user. If the profile service implements IExtendedProfileService,
    /// it will use that. Otherwise, it tries to get roles from the profile claims.
    /// </summary>
    public static async Task<ICollection<string>> GetUserRolesAsync(
        this IProfileService profileService,
        string subjectId,
        CancellationToken cancellationToken = default)
    {
        // If the profile service implements IExtendedProfileService, use that
        if (profileService is IExtendedProfileService extendedService)
        {
            return await extendedService.GetUserRolesAsync(subjectId, cancellationToken);
        }

        // Fallback: try to get roles from profile claims
        var claims = await profileService.GetProfileClaimsAsync(subjectId, new[] { "roles" }, cancellationToken);
        if (claims.TryGetValue("role", out var roleValue))
        {
            if (roleValue is IEnumerable<string> roles)
                return roles.ToList();
            if (roleValue is string roleString)
                return new[] { roleString };
        }

        return Array.Empty<string>();
    }

    /// <summary>
    /// Gets profile claims for a user (convenience method for UserInfo endpoint)
    /// </summary>
    public static async Task<IDictionary<string, object>> GetProfileClaimsAsync(
        this IProfileService profileService,
        string subjectId,
        IEnumerable<string> scopes,
        CancellationToken cancellationToken = default)
    {
        // Create a minimal client for the context
        var context = new ProfileDataRequestContext
        {
            SubjectId = subjectId,
            Client = new Client { ClientId = "internal" },
            RequestedScopes = scopes,
            Caller = "UserInfoEndpoint"
        };

        await profileService.GetProfileDataAsync(context);

        return context.IssuedClaims.ToDictionary(
            c => c.Type,
            c => (object)c.Value);
    }

    /// <summary>
    /// Gets profile claims for a user with protocol context
    /// </summary>
    public static async Task<IDictionary<string, object>> GetProfileClaimsAsync(
        this IProfileService profileService,
        string subjectId,
        IEnumerable<string> scopes,
        string protocol,
        CancellationToken cancellationToken = default)
    {
        // Create a minimal client for the context
        var context = new ProfileDataRequestContext
        {
            SubjectId = subjectId,
            Client = new Client { ClientId = "internal" },
            RequestedScopes = scopes,
            Caller = "UserInfoEndpoint",
            Protocol = protocol
        };

        await profileService.GetProfileDataAsync(context);

        return context.IssuedClaims.ToDictionary(
            c => c.Type,
            c => (object)c.Value);
    }

    /// <summary>
    /// Gets profile claims for a user with full context
    /// </summary>
    public static async Task<IDictionary<string, object>> GetProfileClaimsAsync(
        this IProfileService profileService,
        string subjectId,
        Client client,
        IEnumerable<string> scopes,
        string caller,
        CancellationToken cancellationToken = default)
    {
        var context = new ProfileDataRequestContext
        {
            SubjectId = subjectId,
            Client = client,
            RequestedScopes = scopes,
            Caller = caller
        };

        await profileService.GetProfileDataAsync(context);

        return context.IssuedClaims.ToDictionary(
            c => c.Type,
            c => (object)c.Value);
    }

    /// <summary>
    /// Gets profile claims for a user with full context including protocol
    /// </summary>
    public static async Task<IDictionary<string, object>> GetProfileClaimsAsync(
        this IProfileService profileService,
        string subjectId,
        Client client,
        IEnumerable<string> scopes,
        string caller,
        string protocol,
        CancellationToken cancellationToken = default)
    {
        var context = new ProfileDataRequestContext
        {
            SubjectId = subjectId,
            Client = client,
            RequestedScopes = scopes,
            Caller = caller,
            Protocol = protocol
        };

        await profileService.GetProfileDataAsync(context);

        return context.IssuedClaims.ToDictionary(
            c => c.Type,
            c => (object)c.Value);
    }
}

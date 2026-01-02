namespace Oluso.Admin.Controllers;

/// <summary>
/// Interface for request types that contain client collections
/// </summary>
public interface IClientCollections
{
    ICollection<string>? AllowedGrantTypes { get; }
    ICollection<string>? RedirectUris { get; }
    ICollection<string>? PostLogoutRedirectUris { get; }
    ICollection<string>? AllowedScopes { get; }
    ICollection<string>? AllowedCorsOrigins { get; }
    ICollection<ClientClaimDto>? Claims { get; }
    ICollection<ClientPropertyDto>? Properties { get; }
    ICollection<string>? IdentityProviderRestrictions { get; }
    ICollection<string>? AllowedRoles { get; }
    ICollection<AllowedUserDto>? AllowedUsers { get; }
}

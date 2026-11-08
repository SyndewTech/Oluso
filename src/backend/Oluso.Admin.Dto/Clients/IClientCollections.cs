namespace Oluso.Admin.Dto.Clients;

public interface IClientCollections
{
    ICollection<string>? AllowedGrantTypes { get; }
    ICollection<string>? RedirectUris { get; }
    ICollection<string>? PostLogoutRedirectUris { get; }
    ICollection<string>? AllowedScopes { get; }
    ICollection<string>? AllowedCorsOrigins { get; }
    ICollection<ClientClaimDto>? Claims { get; }
    ICollection<ClientAuthorizeParameterDto>? AllowedAuthorizeParameters { get; }
    ICollection<ClientPropertyDto>? Properties { get; }
    ICollection<string>? IdentityProviderRestrictions { get; }
    ICollection<string>? AllowedResources { get; }
    ICollection<string>? IntrospectionResources { get; }
    ICollection<string>? AllowedRoles { get; }
    ICollection<AllowedUserDto>? AllowedUsers { get; }
}

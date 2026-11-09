namespace Oluso.Admin.Dto.Clients;

public class UpdateClientRequest : IClientCollections
{
    public string? ClientName { get; set; }
    public string? Description { get; set; }
    public string? ClientUri { get; set; }
    public string? LogoUri { get; set; }
    public bool? Enabled { get; set; }
    public bool? RequireClientSecret { get; set; }
    public bool? RequirePkce { get; set; }
    public bool? AllowPlainTextPkce { get; set; }
    public bool? RequireRequestObject { get; set; }
    public bool? RequireDPoP { get; set; }
    public bool? RequirePushedAuthorization { get; set; }
    public int? PushedAuthorizationLifetime { get; set; }
    public bool? RequireConsent { get; set; }
    public bool? AllowRememberConsent { get; set; }
    public int? ConsentLifetime { get; set; }
    public bool? AllowOfflineAccess { get; set; }
    public bool? AllowAccessTokensViaBrowser { get; set; }
    public bool? AlwaysIncludeUserClaimsInIdToken { get; set; }
    public int? AccessTokenLifetime { get; set; }
    public int? IdentityTokenLifetime { get; set; }
    public int? AuthorizationCodeLifetime { get; set; }
    public int? AbsoluteRefreshTokenLifetime { get; set; }
    public int? SlidingRefreshTokenLifetime { get; set; }
    public int? RefreshTokenUsage { get; set; }
    public int? RefreshTokenExpiration { get; set; }
    public bool? UpdateAccessTokenClaimsOnRefresh { get; set; }
    public int? AccessTokenType { get; set; }
    public string? AllowedIdentityTokenSigningAlgorithms { get; set; }
    public bool? IncludeJwtId { get; set; }
    public bool? AlwaysSendClientClaims { get; set; }
    public string? ClientClaimsPrefix { get; set; }
    public string? PairWiseSubjectSalt { get; set; }
    public string? FrontChannelLogoutUri { get; set; }
    public bool? FrontChannelLogoutSessionRequired { get; set; }
    public string? BackChannelLogoutUri { get; set; }
    public bool? BackChannelLogoutSessionRequired { get; set; }
    public bool? EnableLocalLogin { get; set; }
    public int? UserSsoLifetime { get; set; }
    public string? UserCodeType { get; set; }
    public int? DeviceCodeLifetime { get; set; }
    public bool? CibaEnabled { get; set; }
    public string? CibaTokenDeliveryMode { get; set; }
    public string? CibaClientNotificationEndpoint { get; set; }
    public int? CibaRequestLifetime { get; set; }
    public int? CibaPollingInterval { get; set; }
    public bool? CibaRequireUserCode { get; set; }
    public bool? UseJourneyFlow { get; set; }
    public bool? ResetUseJourneyFlowToInherit { get; set; }
    public string? RequiredPermission { get; set; }
    public bool? RequireVerifiedEmail { get; set; }

    // mTLS / token endpoint auth settings
    public string? TokenEndpointAuthMethod { get; set; }
    public string? TlsClientAuthSubjectDn { get; set; }
    public string? TlsClientAuthSanDns { get; set; }
    public string? TlsClientAuthSanUri { get; set; }
    public string? TlsClientAuthSanEmail { get; set; }
    public string? TlsClientAuthSanIp { get; set; }
    public ICollection<string>? AllowedGrantTypes { get; set; }
    public ICollection<string>? RedirectUris { get; set; }
    public ICollection<string>? PostLogoutRedirectUris { get; set; }
    public ICollection<string>? AllowedScopes { get; set; }
    public ICollection<string>? AllowedCorsOrigins { get; set; }
    public ICollection<ClientClaimDto>? Claims { get; set; }
    public ICollection<ClientAuthorizeParameterDto>? AllowedAuthorizeParameters { get; set; }
    public ICollection<ClientPropertyDto>? Properties { get; set; }
    public ICollection<string>? IdentityProviderRestrictions { get; set; }

    /// <summary>Resource indicators (RFC 8707) the client may request tokens for; empty means any.</summary>
    public ICollection<string>? AllowedResources { get; set; }

    /// <summary>Resources the client is the resource server for; it may introspect tokens issued for them.</summary>
    public ICollection<string>? IntrospectionResources { get; set; }
    public ICollection<string>? AllowedRoles { get; set; }
    public ICollection<AllowedUserDto>? AllowedUsers { get; set; }
}

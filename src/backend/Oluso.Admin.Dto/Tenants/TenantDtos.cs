namespace Oluso.Admin.Dto.Tenants;

public class TenantDto
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string Identifier { get; set; } = null!;
    public string? Description { get; set; }
    public string? CustomDomain { get; set; }
    public bool Enabled { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string? PlanId { get; set; }
    public DateTime? PlanExpiresAt { get; set; }
    public bool AllowSelfRegistration { get; set; }
    public bool RequireTermsAcceptance { get; set; }
    public string? TermsOfServiceUrl { get; set; }
    public string? PrivacyPolicyUrl { get; set; }
    public bool RequireEmailVerification { get; set; }
    public string? AllowedEmailDomains { get; set; }
    public bool UseJourneyFlow { get; set; }
    public bool JourneyFallbackToStandard { get; set; }
    public bool HideEmbedBranding { get; set; }
    public bool EnableLocalLogin { get; set; }
    public bool EnablePasskey { get; set; }
    public string? DefaultIdpScheme { get; set; }
    public string? DefaultSignInPolicyId { get; set; }
    public string? DefaultSsoUiMode { get; set; }
    public bool AllowPlatformOrgSignIn { get; set; }
}

public class CreateTenantRequest
{
    public string Name { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string Identifier { get; set; } = null!;
    public string? Description { get; set; }
}

public class UpdateTenantRequest
{
    public string? Name { get; set; }
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public bool? Enabled { get; set; }
    public string? Configuration { get; set; }
    public bool? UseJourneyFlow { get; set; }
    public bool? JourneyFallbackToStandard { get; set; }
    public bool? AllowSelfRegistration { get; set; }
    public bool? RequireTermsAcceptance { get; set; }
    public string? TermsOfServiceUrl { get; set; }
    public string? PrivacyPolicyUrl { get; set; }
    public bool? RequireEmailVerification { get; set; }
    public string? AllowedEmailDomains { get; set; }
    public bool? HideEmbedBranding { get; set; }
    public bool? EnableLocalLogin { get; set; }
    public bool? EnablePasskey { get; set; }
    public string? DefaultIdpScheme { get; set; }
    public string? DefaultSignInPolicyId { get; set; }
    public string? DefaultSsoUiMode { get; set; }
    public bool? AllowPlatformOrgSignIn { get; set; }
}

public class TenantProtocolConfigurationDto
{
    public List<string>? AllowedGrantTypes { get; set; }
    public List<string>? AllowedResponseTypes { get; set; }
    public List<string>? AllowedTokenEndpointAuthMethods { get; set; }
    public List<string>? SubjectTypesSupported { get; set; }
    public List<string>? IdTokenSigningAlgValuesSupported { get; set; }
    public List<string>? CodeChallengeMethodsSupported { get; set; }
    public List<string>? DPoPSigningAlgValuesSupported { get; set; }
    public bool RequirePushedAuthorizationRequests { get; set; }
    public bool RequirePkce { get; set; }
    public bool AllowPlainPkce { get; set; }
    public bool EnforceOAuth21 { get; set; }
    public bool RequireDPoP { get; set; }
    public bool ClaimsParameterSupported { get; set; }
    public bool RequestParameterSupported { get; set; } = true;
    public bool RequestUriParameterSupported { get; set; } = true;
    public bool FrontchannelLogoutSupported { get; set; } = true;
    public bool BackchannelLogoutSupported { get; set; } = true;
    public bool EnableDynamicClientRegistration { get; set; }
    public bool AllowOpenDynamicRegistration { get; set; }
    public List<string>? DynamicRegistrationAllowedScopes { get; set; }
    public List<string>? DynamicRegistrationAllowedGrantTypes { get; set; }
    public bool DynamicRegistrationRequirePkce { get; set; } = true;
    public int DynamicRegistrationMaxRedirectUris { get; set; } = 10;
    /// <summary>
    /// The longest suffix a prefix scope accepts when it doesn't set its own maximum (1–2048; default 256).
    /// </summary>
    public int PrefixScopeSuffixMaxLength { get; set; } = 256;
    /// <summary>
    /// Scopes that earn a refresh token without offline_access, for clients allowed the refresh_token grant.
    /// Null means the default (the Matrix client API scopes); empty means none.
    /// </summary>
    public List<string>? RefreshImplyingScopes { get; set; }
    public DateTime Created { get; set; }
    public DateTime? Updated { get; set; }
}

public class UpdateTenantProtocolConfigurationRequest
{
    public List<string>? AllowedGrantTypes { get; set; }
    public List<string>? AllowedResponseTypes { get; set; }
    public List<string>? AllowedTokenEndpointAuthMethods { get; set; }
    public List<string>? SubjectTypesSupported { get; set; }
    public List<string>? IdTokenSigningAlgValuesSupported { get; set; }
    public List<string>? CodeChallengeMethodsSupported { get; set; }
    public List<string>? DPoPSigningAlgValuesSupported { get; set; }
    public bool? RequirePushedAuthorizationRequests { get; set; }
    public bool? RequirePkce { get; set; }
    public bool? AllowPlainPkce { get; set; }
    public bool? EnforceOAuth21 { get; set; }
    public bool? RequireDPoP { get; set; }
    public bool? ClaimsParameterSupported { get; set; }
    public bool? RequestParameterSupported { get; set; }
    public bool? RequestUriParameterSupported { get; set; }
    public bool? FrontchannelLogoutSupported { get; set; }
    public bool? BackchannelLogoutSupported { get; set; }
    public bool? EnableDynamicClientRegistration { get; set; }
    public bool? AllowOpenDynamicRegistration { get; set; }
    public List<string>? DynamicRegistrationAllowedScopes { get; set; }
    public List<string>? DynamicRegistrationAllowedGrantTypes { get; set; }
    public bool? DynamicRegistrationRequirePkce { get; set; }
    public int? DynamicRegistrationMaxRedirectUris { get; set; }
    /// <summary>Default longest prefix-scope suffix (1–2048); null leaves it unchanged.</summary>
    public int? PrefixScopeSuffixMaxLength { get; set; }
    /// <summary>Scopes that earn a refresh token without offline_access; [] turns it off; null leaves it unchanged.</summary>
    public List<string>? RefreshImplyingScopes { get; set; }
    /// <summary>True puts the refresh-implying scopes back to the default (the Matrix client API scopes).</summary>
    public bool? ResetRefreshImplyingScopes { get; set; }
}

public class PasswordPolicyDto
{
    public int MinimumLength { get; set; } = 8;
    public int MaximumLength { get; set; } = 128;
    public bool RequireDigit { get; set; } = true;
    public bool RequireLowercase { get; set; } = true;
    public bool RequireUppercase { get; set; } = true;
    public bool RequireNonAlphanumeric { get; set; } = true;
    public int RequiredUniqueChars { get; set; } = 4;
    public int PasswordHistoryCount { get; set; } = 0;
    public int PasswordExpirationDays { get; set; } = 0;
    public int MaxFailedAttempts { get; set; } = 5;
    public int LockoutDurationMinutes { get; set; } = 15;
    public bool BlockCommonPasswords { get; set; } = true;
    public bool CheckBreachedPasswords { get; set; } = false;

    /// <summary>
    /// When the breach database can't be reached: false (default) accepts the password,
    /// true rejects it and asks the user to try again.
    /// </summary>
    public bool RejectWhenBreachCheckUnavailable { get; set; } = false;
    public string? CustomRegexPattern { get; set; }
    public string? CustomRegexErrorMessage { get; set; }
}

public class UpdatePasswordPolicyRequest
{
    public int? MinimumLength { get; set; }
    public int? MaximumLength { get; set; }
    public bool? RequireDigit { get; set; }
    public bool? RequireLowercase { get; set; }
    public bool? RequireUppercase { get; set; }
    public bool? RequireNonAlphanumeric { get; set; }
    public int? RequiredUniqueChars { get; set; }
    public int? PasswordHistoryCount { get; set; }
    public int? PasswordExpirationDays { get; set; }
    public int? MaxFailedAttempts { get; set; }
    public int? LockoutDurationMinutes { get; set; }
    public bool? BlockCommonPasswords { get; set; }
    public bool? CheckBreachedPasswords { get; set; }
    public bool? RejectWhenBreachCheckUnavailable { get; set; }
    public string? CustomRegexPattern { get; set; }
    public string? CustomRegexErrorMessage { get; set; }
}

public class TenantModuleFeaturesResponse
{
    public string TenantId { get; set; } = null!;
    public bool HasFeaturesConfigured { get; set; }
    public List<TenantFeatureDto> Features { get; set; } = new();
}

public class TenantFeatureDto
{
    public string Key { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string Description { get; set; } = null!;
    public bool Enabled { get; set; }
    public bool OrgEnabled { get; set; }
    public DateTime? GrantedAt { get; set; }
    public int? MaxSeats { get; set; }
    public int? OrgMaxSeats { get; set; }
}

public class SetTenantFeaturesRequest
{
    public List<TenantFeatureUpdateDto> Features { get; set; } = new();
}

public class TenantFeatureUpdateDto
{
    public string FeatureKey { get; set; } = null!;
    public bool Enabled { get; set; }
    public int? MaxSeats { get; set; }
}

namespace Oluso.ExternalAuth;

/// <summary>
/// Sentinel values written into static authentication options at startup so
/// the package validators (which insist on non-empty ClientId / ClientSecret /
/// Authority) accept the registration. The DynamicOAuthPostConfigureOptions
/// pipeline rewrites every options instance whose field still equals one of
/// these sentinels with the real per-tenant value from the database.
///
/// IMPORTANT: every static placeholder set in <c>AddDynamicExternalProviders</c>
/// must be mirrored by an "is-placeholder" check in
/// <see cref="DynamicOAuthPostConfigureOptions"/>. If the two drift, the
/// configurator stops recognising the field as overridable and the placeholder
/// leaks through to live traffic — exactly the OIDC Authority bug we hit
/// before this constants file existed.
/// </summary>
public static class ExternalAuthPlaceholders
{
    /// <summary>
    /// Used for ClientId, ClientSecret, AppId/AppSecret, ConsumerKey/Secret,
    /// TeamId, KeyId, etc. — any string credential field.
    /// </summary>
    public const string Credential = "__dynamic_placeholder__";

    /// <summary>
    /// OIDC Authority sentinel. Picked to be a syntactically-valid HTTPS URL
    /// (so OpenIdConnectOptions.Validate accepts it) on a guaranteed-unresolvable
    /// host (".invalid" per RFC 6761) so a stray fetch can't escape to the
    /// internet.
    /// </summary>
    public const string OidcAuthority = "https://oidc-placeholder.invalid";

    /// <summary>
    /// True when <paramref name="value"/> is null, empty, or the credential
    /// sentinel — i.e. the field was never given a real value and may be
    /// overwritten by the dynamic configurator.
    /// </summary>
    public static bool IsPlaceholderOrEmpty(string? value) =>
        string.IsNullOrEmpty(value) || value == Credential;

    /// <summary>
    /// True when <paramref name="authority"/> equals the OIDC sentinel
    /// (trailing slash insensitive).
    /// </summary>
    public static bool IsOidcPlaceholderAuthority(string? authority) =>
        !string.IsNullOrEmpty(authority) &&
        authority!.TrimEnd('/').Equals(OidcAuthority, System.StringComparison.OrdinalIgnoreCase);
}

using Microsoft.Extensions.Options;
using Oluso.Core.Services;

namespace Oluso.ExternalAuth;

/// <summary>
/// Drops cached state for a single external auth scheme so the next request
/// rebuilds it from the DB. Three layers must be cleared together — leaving
/// any one populated lets a freshly-edited IdP row look like nothing changed.
/// </summary>
public class ExternalSchemeCacheInvalidator : IExternalSchemeCacheInvalidator
{
    private readonly DynamicSchemeCache _schemeCache;
    private readonly DynamicOptionsCache _optionsCache;
    private readonly IOptionsMonitorCache<Microsoft.AspNetCore.Authentication.Google.GoogleOptions> _googleCache;
    private readonly IOptionsMonitorCache<Microsoft.AspNetCore.Authentication.MicrosoftAccount.MicrosoftAccountOptions> _microsoftCache;
    private readonly IOptionsMonitorCache<Microsoft.AspNetCore.Authentication.Facebook.FacebookOptions> _facebookCache;
    private readonly IOptionsMonitorCache<Microsoft.AspNetCore.Authentication.Twitter.TwitterOptions> _twitterCache;
    private readonly IOptionsMonitorCache<AspNet.Security.OAuth.GitHub.GitHubAuthenticationOptions> _githubCache;
    private readonly IOptionsMonitorCache<AspNet.Security.OAuth.LinkedIn.LinkedInAuthenticationOptions> _linkedInCache;
    private readonly IOptionsMonitorCache<AspNet.Security.OAuth.Apple.AppleAuthenticationOptions> _appleCache;
    private readonly IOptionsMonitorCache<Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectOptions> _oidcCache;
    private readonly IOptionsMonitorCache<Microsoft.AspNetCore.Authentication.OAuth.OAuthOptions> _oauthCache;

    public ExternalSchemeCacheInvalidator(
        DynamicSchemeCache schemeCache,
        DynamicOptionsCache optionsCache,
        IOptionsMonitorCache<Microsoft.AspNetCore.Authentication.Google.GoogleOptions> googleCache,
        IOptionsMonitorCache<Microsoft.AspNetCore.Authentication.MicrosoftAccount.MicrosoftAccountOptions> microsoftCache,
        IOptionsMonitorCache<Microsoft.AspNetCore.Authentication.Facebook.FacebookOptions> facebookCache,
        IOptionsMonitorCache<Microsoft.AspNetCore.Authentication.Twitter.TwitterOptions> twitterCache,
        IOptionsMonitorCache<AspNet.Security.OAuth.GitHub.GitHubAuthenticationOptions> githubCache,
        IOptionsMonitorCache<AspNet.Security.OAuth.LinkedIn.LinkedInAuthenticationOptions> linkedInCache,
        IOptionsMonitorCache<AspNet.Security.OAuth.Apple.AppleAuthenticationOptions> appleCache,
        IOptionsMonitorCache<Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectOptions> oidcCache,
        IOptionsMonitorCache<Microsoft.AspNetCore.Authentication.OAuth.OAuthOptions> oauthCache)
    {
        _schemeCache = schemeCache;
        _optionsCache = optionsCache;
        _googleCache = googleCache;
        _microsoftCache = microsoftCache;
        _facebookCache = facebookCache;
        _twitterCache = twitterCache;
        _githubCache = githubCache;
        _linkedInCache = linkedInCache;
        _appleCache = appleCache;
        _oidcCache = oidcCache;
        _oauthCache = oauthCache;
    }

    /// <summary>
    /// Drops cached state for a scheme so the next request resolves a fresh
    /// options instance from the current DB row. Safe to call with
    /// <paramref name="tenantId"/> null for tenant-less / global IdPs — the
    /// per-tenant TTL caches are keyed on a tenant prefix and the no-op
    /// branch handles that.
    /// </summary>
    public void Invalidate(string scheme, string? tenantId)
    {
        if (!string.IsNullOrEmpty(tenantId))
        {
            _schemeCache.InvalidateForTenant(tenantId);
            _optionsCache.InvalidateForTenant(tenantId);
        }
        else
        {
            _schemeCache.InvalidateAll();
        }

        _googleCache.TryRemove(scheme);
        _microsoftCache.TryRemove(scheme);
        _facebookCache.TryRemove(scheme);
        _twitterCache.TryRemove(scheme);
        _githubCache.TryRemove(scheme);
        _linkedInCache.TryRemove(scheme);
        _appleCache.TryRemove(scheme);
        _oidcCache.TryRemove(scheme);
        _oauthCache.TryRemove(scheme);
    }
}

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;

namespace Oluso.ExternalAuth;

/// <summary>
/// Configures OAuth options dynamically based on tenant-specific provider configurations.
/// This is called when authentication handlers need their options.
/// Uses IHttpContextAccessor to resolve scoped services at runtime since this is a singleton.
/// </summary>
public class DynamicOAuthPostConfigureOptions :
    IPostConfigureOptions<Microsoft.AspNetCore.Authentication.Google.GoogleOptions>,
    IPostConfigureOptions<Microsoft.AspNetCore.Authentication.MicrosoftAccount.MicrosoftAccountOptions>,
    IPostConfigureOptions<Microsoft.AspNetCore.Authentication.Facebook.FacebookOptions>,
    IPostConfigureOptions<Microsoft.AspNetCore.Authentication.Twitter.TwitterOptions>,
    IPostConfigureOptions<AspNet.Security.OAuth.GitHub.GitHubAuthenticationOptions>,
    IPostConfigureOptions<AspNet.Security.OAuth.LinkedIn.LinkedInAuthenticationOptions>,
    IPostConfigureOptions<AspNet.Security.OAuth.Apple.AppleAuthenticationOptions>,
    IPostConfigureOptions<OpenIdConnectOptions>,
    IPostConfigureOptions<OAuthOptions>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly DynamicOptionsCache _optionsCache;
    private readonly DynamicExternalProviderOptions _providerOptions;
    private readonly ILogger<DynamicOAuthPostConfigureOptions>? _logger;

    public DynamicOAuthPostConfigureOptions(
        IHttpContextAccessor httpContextAccessor,
        DynamicOptionsCache optionsCache,
        IOptions<DynamicExternalProviderOptions> providerOptions,
        ILogger<DynamicOAuthPostConfigureOptions>? logger = null)
    {
        _httpContextAccessor = httpContextAccessor;
        _optionsCache = optionsCache;
        _providerOptions = providerOptions.Value;
        _logger = logger;
    }

    private ITenantContext? GetTenantContext() =>
        _httpContextAccessor.HttpContext?.RequestServices.GetService<ITenantContext>();

    private IExternalProviderConfigStore? GetProviderStore() =>
        _httpContextAccessor.HttpContext?.RequestServices.GetService<IExternalProviderConfigStore>();

    public void PostConfigure(string? name, Microsoft.AspNetCore.Authentication.Google.GoogleOptions options)
    {
        if (string.IsNullOrEmpty(name)) return;
        ConfigureGoogleOptionsAsync(name, options).GetAwaiter().GetResult();
    }

    public void PostConfigure(string? name, Microsoft.AspNetCore.Authentication.MicrosoftAccount.MicrosoftAccountOptions options)
    {
        if (string.IsNullOrEmpty(name)) return;
        ConfigureMicrosoftOptionsAsync(name, options).GetAwaiter().GetResult();
    }

    public void PostConfigure(string? name, Microsoft.AspNetCore.Authentication.Facebook.FacebookOptions options)
    {
        if (string.IsNullOrEmpty(name)) return;
        ConfigureFacebookOptionsAsync(name, options).GetAwaiter().GetResult();
    }

    public void PostConfigure(string? name, Microsoft.AspNetCore.Authentication.Twitter.TwitterOptions options)
    {
        if (string.IsNullOrEmpty(name)) return;
        ConfigureTwitterOptionsAsync(name, options).GetAwaiter().GetResult();
    }

    public void PostConfigure(string? name, AspNet.Security.OAuth.GitHub.GitHubAuthenticationOptions options)
    {
        if (string.IsNullOrEmpty(name)) return;
        ConfigureGitHubOptionsAsync(name, options).GetAwaiter().GetResult();
    }

    public void PostConfigure(string? name, AspNet.Security.OAuth.LinkedIn.LinkedInAuthenticationOptions options)
    {
        if (string.IsNullOrEmpty(name)) return;
        ConfigureOAuthOptionsAsync(name, options, "linkedin").GetAwaiter().GetResult();
    }

    public void PostConfigure(string? name, AspNet.Security.OAuth.Apple.AppleAuthenticationOptions options)
    {
        if (string.IsNullOrEmpty(name)) return;
        ConfigureAppleOptionsAsync(name, options).GetAwaiter().GetResult();
    }

    public void PostConfigure(string? name, OpenIdConnectOptions options)
    {
        if (string.IsNullOrEmpty(name)) return;
        ConfigureOidcOptionsAsync(name, options).GetAwaiter().GetResult();
    }

    public void PostConfigure(string? name, OAuthOptions options)
    {
        if (string.IsNullOrEmpty(name)) return;
        ConfigureGenericOAuthOptionsAsync(name, options).GetAwaiter().GetResult();
    }

    private const string PlaceholderValue = ExternalAuthPlaceholders.Credential;

    /// <summary>
    /// Per-scheme callback path mirroring the admin UI's advertised
    /// template (<c>/signin-{scheme}</c>). Without this, every Apple
    /// scheme would default to <c>/signin-apple</c>, every Google scheme
    /// to <c>/signin-google</c>, etc. — which collides as soon as a
    /// tenant picks a custom scheme name (e.g. "AcmeAppleSSO") because
    /// the handler listens at the static default but Apple is configured
    /// to redirect to <c>/signin-AcmeAppleSSO</c>.
    /// </summary>
    private static Microsoft.AspNetCore.Http.PathString BuildCallbackPath(string scheme)
        => new($"/signin-{scheme.ToLowerInvariant()}");

    private async Task ConfigureOAuthOptionsAsync<TOptions>(string name, TOptions options, string expectedType)
        where TOptions : OAuthOptions
    {
        var provider = await GetProviderAsync(name);
        if (provider == null)
        {
            _logger?.LogDebug("No provider configuration found for scheme {Scheme}", name);
            return;
        }

        if (!provider.ProviderType.Equals(expectedType, StringComparison.OrdinalIgnoreCase))
        {
            _logger?.LogDebug(
                "Provider type mismatch for scheme {Scheme}: expected {ExpectedType}, got {ActualType}",
                name, expectedType, provider.ProviderType);
            return;
        }

        _logger?.LogDebug(
            "Configuring OAuth options for scheme {Scheme} with provider type {ProviderType}",
            name, provider.ProviderType);

        options.CallbackPath = BuildCallbackPath(name);

        // Apply database config if current value is empty or a placeholder
        // Database configuration always takes precedence over placeholder values
        if (IsPlaceholderOrEmpty(options.ClientId) && !string.IsNullOrEmpty(provider.ClientId))
        {
            options.ClientId = provider.ClientId;
            _logger?.LogDebug("Set ClientId for scheme {Scheme}", name);
        }

        if (IsPlaceholderOrEmpty(options.ClientSecret) && !string.IsNullOrEmpty(provider.ClientSecret))
        {
            options.ClientSecret = provider.ClientSecret;
        }

        options.SaveTokens = provider.CacheExternalTokens || provider.IncludeExternalAccessToken;

        if (provider.Scopes.Any())
        {
            options.Scope.Clear();
            foreach (var scope in provider.Scopes)
            {
                options.Scope.Add(scope);
            }
        }
    }

    private static bool IsPlaceholderOrEmpty(string? value) =>
        ExternalAuthPlaceholders.IsPlaceholderOrEmpty(value);

    private static bool IsOidcPlaceholderAuthority(string? authority) =>
        ExternalAuthPlaceholders.IsOidcPlaceholderAuthority(authority);

    private async Task ConfigureGoogleOptionsAsync(
        string name,
        Microsoft.AspNetCore.Authentication.Google.GoogleOptions options)
    {
        var provider = await GetProviderAsync(name);
        if (provider == null || !provider.ProviderType.Equals("google", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        ApplyCommonOAuthOptions(name, options, provider);

        // Whether Google proved the address, and the Workspace domain it belongs to: what decides if the address
        // is trusted (ExternalEmailTrust). Google's handler maps neither by default.
        options.ClaimActions.MapJsonKey("email_verified", "email_verified");
        options.ClaimActions.MapJsonKey("hd", "hd");

        // hostedDomain restricts sign-in to a single Google Workspace
        // domain. Without this set, anyone with a Google account can
        // authenticate. accessType=offline triggers refresh_token issuance.
        var hostedDomain = LookupProperty(provider, "hostedDomain");
        var accessType = LookupProperty(provider, "accessType");

        if (!string.IsNullOrWhiteSpace(hostedDomain) || !string.IsNullOrWhiteSpace(accessType))
        {
            options.Events.OnRedirectToAuthorizationEndpoint = ctx =>
            {
                var url = ctx.RedirectUri;
                if (!string.IsNullOrWhiteSpace(hostedDomain)) url = AppendQuery(url, "hd", hostedDomain);
                if (!string.IsNullOrWhiteSpace(accessType))   url = AppendQuery(url, "access_type", accessType);
                ctx.Response.Redirect(url);
                return Task.CompletedTask;
            };
        }
    }

    private async Task ConfigureMicrosoftOptionsAsync(
        string name,
        Microsoft.AspNetCore.Authentication.MicrosoftAccount.MicrosoftAccountOptions options)
    {
        var provider = await GetProviderAsync(name);
        if (provider == null || !provider.ProviderType.Equals("microsoft", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        ApplyCommonOAuthOptions(name, options, provider);

        // Graph /me omits otherMails from the default response set — we have
        // to $select it explicitly so the email ClaimAction below can fall
        // back to it when 'mail' is null (typical for unredeemed B2B guests).
        if (!options.UserInformationEndpoint.Contains("$select", StringComparison.Ordinal))
        {
            var separator = options.UserInformationEndpoint.Contains('?') ? "&" : "?";
            options.UserInformationEndpoint += $"{separator}$select=id,displayName,givenName,surname,mail,otherMails,userPrincipalName";
        }

        // Microsoft's MicrosoftAccountHandler ships with
        //   Email <- mail ?? userPrincipalName
        // which stamps the '#EXT#@<resource>.onmicrosoft.com' guest UPN into
        // ClaimTypes.Email whenever a B2B guest's 'mail' attribute is null.
        // The real address for guests lives in Graph's 'otherMails' array;
        // prefer mail, then otherMails[0], and leave the claim absent rather
        // than emit a UPN that isn't a real email.
        options.ClaimActions.Remove(System.Security.Claims.ClaimTypes.Email);
        options.ClaimActions.MapCustomJson(System.Security.Claims.ClaimTypes.Email, user =>
        {
            var mail = user.TryGetProperty("mail", out var mailEl) && mailEl.ValueKind == System.Text.Json.JsonValueKind.String
                ? mailEl.GetString()
                : null;
            if (!string.IsNullOrEmpty(mail)) return mail;

            if (user.TryGetProperty("otherMails", out var otherEl) && otherEl.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                foreach (var item in otherEl.EnumerateArray())
                {
                    if (item.ValueKind != System.Text.Json.JsonValueKind.String) continue;
                    var candidate = item.GetString();
                    if (!string.IsNullOrEmpty(candidate)) return candidate;
                }
            }

            return null!;
        });

        // tenantId restricts the OAuth flow to a specific Azure AD tenant.
        // Without this, the default endpoints point at /common — accepting
        // *any* Microsoft account. Admin's tenantId form value is meaningless
        // until we replace `common` in the authorize/token URLs.
        var tenantIdRaw = LookupProperty(provider, "tenantId");
        var tenantId = string.IsNullOrWhiteSpace(tenantIdRaw) ? "common" : tenantIdRaw;
        var instanceRaw = LookupProperty(provider, "instance");
        var instance = (string.IsNullOrWhiteSpace(instanceRaw) ? "https://login.microsoftonline.com" : instanceRaw).TrimEnd('/');

        options.AuthorizationEndpoint = $"{instance}/{tenantId}/oauth2/v2.0/authorize";
        options.TokenEndpoint        = $"{instance}/{tenantId}/oauth2/v2.0/token";

        // domain_hint and prompt are query-string parameters appended to
        // the authorize URL. Both are optional but useful — domain_hint
        // bypasses Microsoft's home-realm discovery, and prompt forces
        // the consent / login / select-account experience.
        var domainHint = LookupProperty(provider, "domainHint");
        var prompt = LookupProperty(provider, "prompt");
        options.Events.OnRedirectToAuthorizationEndpoint = ctx =>
        {
            var url = ctx.RedirectUri;
            if (!string.IsNullOrWhiteSpace(domainHint)) url = AppendQuery(url, "domain_hint", domainHint);
            if (!string.IsNullOrWhiteSpace(prompt))     url = AppendQuery(url, "prompt", prompt);
            foreach (var key in new[] { "ui_locales", "domain_hint", "login_hint" })
            {
                if (ctx.Properties.Items.TryGetValue(key, out var val) && !string.IsNullOrEmpty(val))
                    url = AppendQuery(url, key, val);
            }
            ctx.Response.Redirect(url);
            return Task.CompletedTask;
        };
    }

    private async Task ConfigureGitHubOptionsAsync(
        string name,
        AspNet.Security.OAuth.GitHub.GitHubAuthenticationOptions options)
    {
        var provider = await GetProviderAsync(name);
        if (provider == null || !provider.ProviderType.Equals("github", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        ApplyCommonOAuthOptions(name, options, provider);

        var enterpriseUrl = LookupProperty(provider, "enterpriseUrl");

        // GitHub Enterprise Server has its own host. Setting EnterpriseDomain
        // automatically rewrites the authorize / token / userinfo endpoints
        // and removes the hard-coded github.com from the package defaults.
        if (!string.IsNullOrWhiteSpace(enterpriseUrl))
        {
            try
            {
                var uri = new Uri(enterpriseUrl);
                options.EnterpriseDomain = uri.Host;
            }
            catch (UriFormatException)
            {
                _logger?.LogWarning(
                    "GitHub enterpriseUrl {EnterpriseUrl} is not a valid absolute URI; ignoring",
                    enterpriseUrl);
            }
        }

        // requestEmail toggles whether the email scope is included.
        // Default is true — GitHub doesn't return email otherwise.
        var requestEmail = !string.Equals(LookupProperty(provider, "requestEmail"), "false", StringComparison.OrdinalIgnoreCase);
        if (requestEmail && !options.Scope.Contains("user:email"))
        {
            options.Scope.Add("user:email");
        }

        // allowedOrganizations gates the user's GitHub-org membership.
        // Enforced post-callback by querying the GitHub API and rejecting
        // users who aren't a member of any listed org.
        var allowedOrgs = ParseStringArray(LookupProperty(provider, "allowedOrganizations"));
        if (allowedOrgs.Count > 0)
        {
            var allowedSet = new HashSet<string>(allowedOrgs, StringComparer.OrdinalIgnoreCase);
            options.SaveTokens = true; // need access_token for the org-membership probe
            var existing = options.Events.OnCreatingTicket;
            options.Events.OnCreatingTicket = async ctx =>
            {
                if (existing != null) await existing(ctx);
                using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user/orgs");
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ctx.AccessToken);
                req.Headers.UserAgent.ParseAdd("Oluso");
                req.Headers.Accept.ParseAdd("application/vnd.github+json");
                using var resp = await ctx.Backchannel.SendAsync(req, ctx.HttpContext.RequestAborted);
                if (!resp.IsSuccessStatusCode)
                {
                    ctx.Fail($"GitHub /user/orgs returned {(int)resp.StatusCode}");
                    return;
                }
                using var stream = await resp.Content.ReadAsStreamAsync(ctx.HttpContext.RequestAborted);
                using var doc = await System.Text.Json.JsonDocument.ParseAsync(stream, cancellationToken: ctx.HttpContext.RequestAborted);
                var memberOf = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var org in doc.RootElement.EnumerateArray())
                {
                    if (org.TryGetProperty("login", out var login) && login.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        memberOf.Add(login.GetString()!);
                    }
                }
                if (!memberOf.Overlaps(allowedSet))
                {
                    ctx.Fail($"User is not a member of an allowed GitHub organisation. Allowed: {string.Join(", ", allowedSet)}");
                }
            };
        }
    }

    private async Task ConfigureFacebookOptionsAsync(string name, Microsoft.AspNetCore.Authentication.Facebook.FacebookOptions options)
    {
        var provider = await GetProviderAsync(name);
        if (provider == null) return;

        if (!provider.ProviderType.Equals("facebook", StringComparison.OrdinalIgnoreCase)) return;

        options.CallbackPath = BuildCallbackPath(name);

        if (IsPlaceholderOrEmpty(options.AppId) && !string.IsNullOrEmpty(provider.ClientId))
        {
            options.AppId = provider.ClientId;
        }

        if (IsPlaceholderOrEmpty(options.AppSecret) && !string.IsNullOrEmpty(provider.ClientSecret))
        {
            options.AppSecret = provider.ClientSecret;
        }

        options.SaveTokens = provider.CacheExternalTokens || provider.IncludeExternalAccessToken;

        if (provider.Scopes.Any())
        {
            options.Scope.Clear();
            foreach (var scope in provider.Scopes)
            {
                options.Scope.Add(scope);
            }
        }

        // Facebook's userinfo (`/me?fields=...`) needs an explicit field
        // list. Admin-supplied list takes precedence; default keeps the
        // package's built-ins.
        var fields = ParseStringArray(LookupProperty(provider, "fields"));
        if (fields.Count > 0)
        {
            options.Fields.Clear();
            foreach (var field in fields)
            {
                options.Fields.Add(field);
            }
        }
    }

    /// <summary>
    /// Common OAuth wiring shared by Google / Microsoft / GitHub / generic.
    /// Sets callback path, client credentials, scopes, and SaveTokens —
    /// the per-provider methods then layer their specific behaviour on top.
    /// </summary>
    private static void ApplyCommonOAuthOptions<TOptions>(
        string name,
        TOptions options,
        ExternalProviderDefinition provider) where TOptions : OAuthOptions
    {
        options.CallbackPath = BuildCallbackPath(name);

        if (IsPlaceholderOrEmpty(options.ClientId) && !string.IsNullOrEmpty(provider.ClientId))
        {
            options.ClientId = provider.ClientId;
        }

        if (IsPlaceholderOrEmpty(options.ClientSecret) && !string.IsNullOrEmpty(provider.ClientSecret))
        {
            options.ClientSecret = provider.ClientSecret;
        }

        options.SaveTokens = provider.CacheExternalTokens || provider.IncludeExternalAccessToken;

        if (provider.Scopes.Any())
        {
            options.Scope.Clear();
            foreach (var scope in provider.Scopes)
            {
                options.Scope.Add(scope);
            }
        }
    }

    /// <summary>
    /// Reads a value from the provider's per-type property bag. Returns
    /// null when the bag is unset or the key is missing — both are
    /// treated as "not configured" by every consumer.
    /// </summary>
    private static string? LookupProperty(ExternalProviderDefinition provider, string key)
    {
        if (provider.Properties == null) return null;
        return provider.Properties.TryGetValue(key, out var value) ? value : null;
    }

    /// <summary>
    /// Parses a comma- or space-separated string into a list, trimming
    /// whitespace and filtering empties. Mirrors how the form serialises
    /// the multi-value text inputs (allowedOrganizations, fields, etc.).
    /// </summary>
    private static List<string> ParseStringArray(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return new List<string>();
        return raw
            .Split(new[] { ',', ' ', '\n', '\r', '\t', ';' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();
    }

    /// <summary>
    /// Appends a query-string parameter to a URL without losing existing
    /// parameters. Used by the OnRedirectToAuthorizationEndpoint events
    /// that splice provider-specific params (domain_hint, prompt, hd,
    /// access_type) into the upstream redirect.
    /// </summary>
    private static string AppendQuery(string url, string key, string value)
    {
        var separator = url.Contains('?') ? "&" : "?";
        return $"{url}{separator}{Uri.EscapeDataString(key)}={Uri.EscapeDataString(value)}";
    }

    private async Task ConfigureTwitterOptionsAsync(string name, Microsoft.AspNetCore.Authentication.Twitter.TwitterOptions options)
    {
        var provider = await GetProviderAsync(name);
        if (provider == null) return;

        if (!provider.ProviderType.Equals("twitter", StringComparison.OrdinalIgnoreCase)) return;

        options.CallbackPath = BuildCallbackPath(name);

        if (IsPlaceholderOrEmpty(options.ConsumerKey) && !string.IsNullOrEmpty(provider.ClientId))
        {
            options.ConsumerKey = provider.ClientId;
        }

        if (IsPlaceholderOrEmpty(options.ConsumerSecret) && !string.IsNullOrEmpty(provider.ClientSecret))
        {
            options.ConsumerSecret = provider.ClientSecret;
        }

        options.SaveTokens = provider.CacheExternalTokens || provider.IncludeExternalAccessToken;
        options.RetrieveUserDetails = true;
    }

    private async Task ConfigureAppleOptionsAsync(string name, AspNet.Security.OAuth.Apple.AppleAuthenticationOptions options)
    {
        // Apple-required fields that satisfy Validate must be set BEFORE
        // any early return — otherwise an Apple-typed scheme without a
        // matching DB row (e.g. mid-deploy, before admin has finished
        // configuring) crashes the auth middleware on every request,
        // taking the login page down. AuthenticationMiddleware probes
        // every IAuthenticationRequestHandler scheme on every request,
        // so a single mis-validating Apple scheme is request-poisoning.
        //
        // Apple's discovery URL is a constant (same for every integration);
        // ClientSecret here is just a placeholder string — Apple's package
        // overrides it with a JWT signed from TeamId/KeyId/PrivateKey at
        // challenge time, before any token-endpoint POST.
        if (string.IsNullOrEmpty(options.MetadataEndpoint))
        {
            options.MetadataEndpoint = "https://appleid.apple.com/.well-known/openid-configuration";
        }
        if (IsPlaceholderOrEmpty(options.ClientSecret))
        {
            options.ClientSecret = "__apple_jwt_minted_at_challenge__";
        }

        var provider = await GetProviderAsync(name);
        if (provider == null) return;

        if (!provider.ProviderType.Equals("apple", StringComparison.OrdinalIgnoreCase)) return;

        options.CallbackPath = BuildCallbackPath(name);

        if (IsPlaceholderOrEmpty(options.ClientId) && !string.IsNullOrEmpty(provider.ClientId))
        {
            options.ClientId = provider.ClientId;
        }

        if (IsPlaceholderOrEmpty(options.TeamId) && !string.IsNullOrEmpty(provider.AppleTeamId))
        {
            options.TeamId = provider.AppleTeamId;
        }

        if (IsPlaceholderOrEmpty(options.KeyId) && !string.IsNullOrEmpty(provider.AppleKeyId))
        {
            options.KeyId = provider.AppleKeyId;
        }

        // Apple's client_secret is a JWT signed with the .p8 private key.
        // Setting GenerateClientSecret = true makes
        // AppleAuthenticationOptions.Validate skip the OAuthOptions
        // ClientSecret-not-empty check (which we'd never satisfy at
        // startup since the JWT is generated per challenge). Always
        // force this on for Apple schemes — even when admin hasn't
        // filled in PrivateKey yet — so options validation passes
        // and the user reaches the "missing config" failure at the
        // actual challenge step (clearer signal than a 500 here).
        options.GenerateClientSecret = true;
        if (!string.IsNullOrEmpty(provider.ApplePrivateKey))
        {
            var privateKey = provider.ApplePrivateKey;
            options.PrivateKey = (keyId, ct) => Task.FromResult(privateKey.AsMemory());
        }
        else if (options.PrivateKey == null)
        {
            // Belt-and-braces: the placeholder AddApple call sets a
            // do-nothing delegate, but if we ever reach this method
            // before the placeholder runs we still want a non-null
            // delegate so Apple's Validate (PrivateKey != null) passes.
            options.PrivateKey = (keyId, ct) => Task.FromResult(ReadOnlyMemory<char>.Empty);
        }

        options.SaveTokens = provider.CacheExternalTokens || provider.IncludeExternalAccessToken;

        if (provider.Scopes.Any())
        {
            options.Scope.Clear();
            foreach (var scope in provider.Scopes)
            {
                options.Scope.Add(scope);
            }
        }
    }

    private async Task ConfigureOidcOptionsAsync(string name, OpenIdConnectOptions options)
    {
        var provider = await GetProviderAsync(name);
        if (provider == null) return;

        if (!provider.ProviderType.Equals("oidc", StringComparison.OrdinalIgnoreCase)) return;

        options.CallbackPath = BuildCallbackPath(name);

        if (IsPlaceholderOrEmpty(options.ClientId) && !string.IsNullOrEmpty(provider.ClientId))
        {
            options.ClientId = provider.ClientId;
        }

        if (IsPlaceholderOrEmpty(options.ClientSecret) && !string.IsNullOrEmpty(provider.ClientSecret))
        {
            options.ClientSecret = provider.ClientSecret;
        }

        // disableDiscovery means "don't fetch /.well-known/openid-configuration".
        // When set, the manual endpoints become the source of truth and the
        // package's automatic discovery is suppressed.
        var disableDiscovery = string.Equals(LookupProperty(provider, "disableDiscovery"), "true", StringComparison.OrdinalIgnoreCase);

        // The placeholder OIDC scheme registers Authority="https://oidc-placeholder.invalid"
        // to satisfy startup validation. Treat it as empty so the real value
        // from the DB takes over here. Authority and MetadataAddress are not
        // interchangeable: Authority is a root URL the handler appends
        // "/.well-known/openid-configuration" to; MetadataAddress is the full
        // discovery URL used as-is. Mixing them up makes the handler fetch
        // the IdP's homepage and try to deserialize HTML.
        if (IsOidcPlaceholderAuthority(options.Authority))
        {
            options.Authority = null;
        }

        if (!disableDiscovery)
        {
            if (string.IsNullOrEmpty(options.Authority) && !string.IsNullOrEmpty(provider.Authority))
            {
                options.Authority = provider.Authority.TrimEnd('/');
            }

            if (string.IsNullOrEmpty(options.MetadataAddress) && !string.IsNullOrEmpty(provider.MetadataAddress))
            {
                options.MetadataAddress = provider.MetadataAddress;
            }
        }

        // Manual endpoints — required when discovery is disabled or the
        // IdP doesn't expose /.well-known. Replaces the OIDC handler's
        // configuration manager with a static one when populated.
        if (disableDiscovery)
        {
            var authzEndpoint = LookupProperty(provider, "authorizationEndpoint");
            var tokenEndpoint = LookupProperty(provider, "tokenEndpoint");
            var userInfoEndpoint = LookupProperty(provider, "userInfoEndpoint");
            var jwksUri = LookupProperty(provider, "jwksUri");
            var endSessionEndpoint = LookupProperty(provider, "endSessionEndpoint");

            options.MetadataAddress = null;
            options.RequireHttpsMetadata = false;
            var oidcConfig = new Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration
            {
                Issuer = options.Authority ?? string.Empty,
                AuthorizationEndpoint = authzEndpoint ?? string.Empty,
                TokenEndpoint = tokenEndpoint ?? string.Empty,
                UserInfoEndpoint = userInfoEndpoint ?? string.Empty,
                JwksUri = jwksUri ?? string.Empty,
                EndSessionEndpoint = endSessionEndpoint ?? string.Empty
            };
            options.Configuration = oidcConfig;
            options.ConfigurationManager = new Microsoft.IdentityModel.Protocols.StaticConfigurationManager<Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration>(oidcConfig);
        }

        // PKCE adds code_challenge to the authorize request and code_verifier
        // to the token request. Required by spec for public clients;
        // recommended for confidential too. Default true unless explicitly
        // disabled by admin (e.g. when the IdP rejects PKCE).
        var usePkceRaw = LookupProperty(provider, "usePkce");
        options.UsePkce = string.IsNullOrEmpty(usePkceRaw)
            ? true
            : !string.Equals(usePkceRaw, "false", StringComparison.OrdinalIgnoreCase);

        options.SaveTokens = provider.CacheExternalTokens || provider.IncludeExternalAccessToken;

        if (provider.Scopes.Any())
        {
            options.Scope.Clear();
            foreach (var scope in provider.Scopes)
            {
                options.Scope.Add(scope);
            }
        }
        else
        {
            options.Scope.Add("email");
        }

        var promptValue = LookupProperty(provider, "prompt");
        if (!string.IsNullOrWhiteSpace(promptValue))
        {
            options.Prompt = promptValue;
        }

        // Forward ui_locales from AuthenticationProperties to the OIDC
        // authorize URL. OAuth handlers do this automatically via
        // Parameters, but OpenIdConnectHandler needs it on the message.
        var existingRedirect = options.Events.OnRedirectToIdentityProvider;
        options.Events.OnRedirectToIdentityProvider = async ctx =>
        {
            if (ctx.Properties.Items.TryGetValue("ui_locales", out var uiLocales) && !string.IsNullOrEmpty(uiLocales))
                ctx.ProtocolMessage.UiLocales = uiLocales;
            if (ctx.Properties.Items.TryGetValue("domain_hint", out var domainHint) && !string.IsNullOrEmpty(domainHint))
                ctx.ProtocolMessage.DomainHint = domainHint;
            if (ctx.Properties.Items.TryGetValue("login_hint", out var loginHint) && !string.IsNullOrEmpty(loginHint))
                ctx.ProtocolMessage.LoginHint = loginHint;
            if (existingRedirect != null)
                await existingRedirect(ctx);
        };
    }

    private async Task ConfigureGenericOAuthOptionsAsync(string name, OAuthOptions options)
    {
        var provider = await GetProviderAsync(name);
        if (provider == null) return;

        if (!provider.ProviderType.Equals("oauth", StringComparison.OrdinalIgnoreCase)) return;

        options.CallbackPath = BuildCallbackPath(name);

        if (IsPlaceholderOrEmpty(options.ClientId) && !string.IsNullOrEmpty(provider.ClientId))
        {
            options.ClientId = provider.ClientId;
        }

        if (IsPlaceholderOrEmpty(options.ClientSecret) && !string.IsNullOrEmpty(provider.ClientSecret))
        {
            options.ClientSecret = provider.ClientSecret;
        }

        if (options.AuthorizationEndpoint == null && !string.IsNullOrEmpty(provider.AuthorizationEndpoint))
        {
            options.AuthorizationEndpoint = provider.AuthorizationEndpoint;
        }

        if (options.TokenEndpoint == null && !string.IsNullOrEmpty(provider.TokenEndpoint))
        {
            options.TokenEndpoint = provider.TokenEndpoint;
        }

        if (options.UserInformationEndpoint == null && !string.IsNullOrEmpty(provider.UserInfoEndpoint))
        {
            options.UserInformationEndpoint = provider.UserInfoEndpoint;
        }

        options.SaveTokens = provider.CacheExternalTokens || provider.IncludeExternalAccessToken;

        if (provider.Scopes.Any())
        {
            options.Scope.Clear();
            foreach (var scope in provider.Scopes)
            {
                options.Scope.Add(scope);
            }
        }

        // Claim path mappings — generic OAuth2 has no standard userinfo
        // shape, so the admin tells us which JSON paths in the userinfo
        // response carry the subject / email / name. Without this, the
        // OAuth handler emits no useful claims for non-OIDC providers.
        var userIdPath = LookupProperty(provider, "userIdClaimPath") ?? "sub";
        var emailPath = LookupProperty(provider, "emailClaimPath") ?? "email";
        var namePath = LookupProperty(provider, "nameClaimPath") ?? "name";

        // Each ClaimAction reads a JSON property at the given path and
        // emits a Claim with the standard type. MapJsonKey is the
        // simplest path-style action; for nested paths (e.g. "data.id")
        // we use the JsonSubKey form.
        ClearClaimActionsForKnownTypes(options.ClaimActions);
        AddJsonPathClaimAction(options.ClaimActions, System.Security.Claims.ClaimTypes.NameIdentifier, userIdPath);
        AddJsonPathClaimAction(options.ClaimActions, System.Security.Claims.ClaimTypes.Email, emailPath);
        AddJsonPathClaimAction(options.ClaimActions, System.Security.Claims.ClaimTypes.Name, namePath);

        // Without an explicit OnCreatingTicket handler, OAuthOptions's
        // default flow doesn't even fetch the userinfo endpoint. Wire a
        // minimal handler that does the GET and runs the claim actions.
        if (!string.IsNullOrEmpty(options.UserInformationEndpoint))
        {
            options.Events.OnCreatingTicket = async ctx =>
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, ctx.Options.UserInformationEndpoint);
                req.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", ctx.AccessToken);
                req.Headers.Accept.ParseAdd("application/json");
                using var resp = await ctx.Backchannel.SendAsync(req, ctx.HttpContext.RequestAborted);
                resp.EnsureSuccessStatusCode();
                using var doc = await System.Text.Json.JsonDocument.ParseAsync(
                    await resp.Content.ReadAsStreamAsync(ctx.HttpContext.RequestAborted),
                    cancellationToken: ctx.HttpContext.RequestAborted);
                ctx.RunClaimActions(doc.RootElement);
            };
        }
    }

    /// <summary>
    /// Removes any pre-existing claim actions for the standard claim
    /// types we're about to add — so admin-supplied paths win over
    /// whatever the package registered by default.
    /// </summary>
    private static void ClearClaimActionsForKnownTypes(Microsoft.AspNetCore.Authentication.OAuth.Claims.ClaimActionCollection actions)
    {
        actions.Remove(System.Security.Claims.ClaimTypes.NameIdentifier);
        actions.Remove(System.Security.Claims.ClaimTypes.Email);
        actions.Remove(System.Security.Claims.ClaimTypes.Name);
    }

    /// <summary>
    /// Adds a claim action that reads a (possibly nested, dot-separated)
    /// JSON path from the userinfo response. Falls back to the simple
    /// MapJsonKey when the path has no dots.
    /// </summary>
    private static void AddJsonPathClaimAction(
        Microsoft.AspNetCore.Authentication.OAuth.Claims.ClaimActionCollection actions,
        string claimType,
        string jsonPath)
    {
        if (string.IsNullOrWhiteSpace(jsonPath)) return;
        if (!jsonPath.Contains('.'))
        {
            actions.MapJsonKey(claimType, jsonPath);
            return;
        }
        // Nested path — split into segments. Microsoft's MapJsonSubKey
        // only supports two levels (parent/child); for deeper paths we
        // walk manually with a custom action.
        var segments = jsonPath.Split('.');
        actions.Add(new JsonPathClaimAction(claimType, segments));
    }

    /// <summary>
    /// ClaimAction that walks an arbitrary dot-separated JSON path on
    /// the userinfo response and emits a single string-valued Claim.
    /// Generic OAuth2 IdPs return wildly different shapes — admins
    /// need this flexibility (e.g. some put email at <c>data.email</c>
    /// or <c>profile.contact.email</c>).
    /// </summary>
    private sealed class JsonPathClaimAction : Microsoft.AspNetCore.Authentication.OAuth.Claims.ClaimAction
    {
        private readonly string[] _path;

        public JsonPathClaimAction(string claimType, string[] path)
            : base(claimType, System.Security.Claims.ClaimValueTypes.String)
        {
            _path = path;
        }

        public override void Run(System.Text.Json.JsonElement userData, System.Security.Claims.ClaimsIdentity identity, string issuer)
        {
            var current = userData;
            foreach (var segment in _path)
            {
                if (current.ValueKind != System.Text.Json.JsonValueKind.Object) return;
                if (!current.TryGetProperty(segment, out current)) return;
            }
            string? value = current.ValueKind switch
            {
                System.Text.Json.JsonValueKind.String => current.GetString(),
                System.Text.Json.JsonValueKind.Number => current.GetRawText(),
                System.Text.Json.JsonValueKind.True => "true",
                System.Text.Json.JsonValueKind.False => "false",
                _ => null
            };
            if (!string.IsNullOrEmpty(value))
            {
                identity.AddClaim(new System.Security.Claims.Claim(ClaimType, value, ValueType, issuer));
            }
        }
    }

    private async Task<ExternalProviderDefinition?> GetProviderAsync(string scheme)
    {
        var providerStore = GetProviderStore();
        if (providerStore == null)
        {
            _logger?.LogDebug(
                "No provider store available (HttpContext may be null). Scheme: {Scheme}",
                scheme);
            return null;
        }

        var allowTenantOverrides = _providerOptions.AllowTenantOverrides;
        var tenantContext = GetTenantContext();
        // When tenant overrides are disabled, every tenant resolves the same
        // global row, so the cache key must collapse to a single bucket
        // regardless of tenant context.
        var tenantId = allowTenantOverrides ? (tenantContext?.TenantId ?? "global") : "global";
        var cacheKey = $"{tenantId}:{scheme}";

        // Check cache first
        if (_optionsCache.TryGetProvider(cacheKey, out var cached))
        {
            _logger?.LogDebug("Found cached provider for {CacheKey}", cacheKey);
            return cached;
        }

        // Load from database
        _logger?.LogDebug("Loading provider from database for scheme {Scheme}, tenant {TenantId} (overrides={Overrides})", scheme, tenantId, allowTenantOverrides);
        var provider = await providerStore.GetBySchemeAsync(scheme, allowTenantOverrides);

        if (provider != null)
        {
            _logger?.LogDebug(
                "Found provider in database: Scheme={Scheme}, ProviderType={ProviderType}, ClientId={HasClientId}",
                provider.Scheme, provider.ProviderType, !string.IsNullOrEmpty(provider.ClientId));
        }
        else
        {
            _logger?.LogDebug("No provider found in database for scheme {Scheme}", scheme);
        }

        // Cache the result (even if null, to avoid repeated lookups)
        _optionsCache.SetProvider(cacheKey, provider);

        return provider;
    }
}

/// <summary>
/// Thread-safe cache for dynamic provider configurations
/// </summary>
public class DynamicOptionsCache
{
    private readonly Dictionary<string, (ExternalProviderDefinition? Provider, DateTime Expiry)> _cache = new();
    private readonly object _lock = new();
    private readonly TimeSpan _defaultExpiry;

    public DynamicOptionsCache() : this(TimeSpan.FromMinutes(5))
    {
    }

    public DynamicOptionsCache(TimeSpan defaultExpiry)
    {
        _defaultExpiry = defaultExpiry;
    }

    public bool TryGetProvider(string key, out ExternalProviderDefinition? provider)
    {
        lock (_lock)
        {
            if (_cache.TryGetValue(key, out var entry))
            {
                if (entry.Expiry > DateTime.UtcNow)
                {
                    provider = entry.Provider;
                    return true;
                }

                _cache.Remove(key);
            }

            provider = null;
            return false;
        }
    }

    public void SetProvider(string key, ExternalProviderDefinition? provider, TimeSpan? expiry = null)
    {
        lock (_lock)
        {
            _cache[key] = (provider, DateTime.UtcNow.Add(expiry ?? _defaultExpiry));
        }
    }

    public void InvalidateForTenant(string tenantId)
    {
        lock (_lock)
        {
            var keysToRemove = _cache.Keys
                .Where(k => k.StartsWith($"{tenantId}:"))
                .ToList();

            foreach (var key in keysToRemove)
            {
                _cache.Remove(key);
            }
        }
    }

    public void InvalidateAll()
    {
        lock (_lock)
        {
            _cache.Clear();
        }
    }
}

using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Core.Services;

namespace Oluso.UI.Pages.Account;

/// <summary>
/// Dedicated callback page for external identity provider authentication.
/// Using a dedicated path (/account/external-callback) instead of query parameter
/// handlers ensures the callback URL is preserved by all OAuth providers.
/// </summary>
public class ExternalCallbackModel : PageModel
{
    private readonly IOlusoUserService _userService;
    private readonly IUserClaimsPrincipalFactory<OlusoUser> _claimsFactory;
    private readonly UserManager<OlusoUser> _userManager;
    private readonly IExternalAuthService? _externalAuthService;
    private readonly ITenantContext? _tenantContext;
    private readonly IRoleStore? _roleStore;
    private readonly IRoleGrantService? _roleGrants;
    private readonly ISignInAuditor? _signInAuditor;
    private readonly ILogger<ExternalCallbackModel> _logger;
    private readonly IProtocolStateStore? _protocolStateStore;

    public ExternalCallbackModel(
        IOlusoUserService userService,
        IUserClaimsPrincipalFactory<OlusoUser> claimsFactory,
        UserManager<OlusoUser> userManager,
        ILogger<ExternalCallbackModel> logger,
        IExternalAuthService? externalAuthService = null,
        ITenantContext? tenantContext = null,
        IRoleStore? roleStore = null,
        ISignInAuditor? signInAuditor = null,
        IRoleGrantService? roleGrants = null,
        IProtocolStateStore? protocolStateStore = null)
    {
        _userService = userService;
        _claimsFactory = claimsFactory;
        _userManager = userManager;
        _externalAuthService = externalAuthService;
        _tenantContext = tenantContext;
        _roleStore = roleStore;
        _roleGrants = roleGrants;
        _logger = logger;
        _signInAuditor = signInAuditor;
        _protocolStateStore = protocolStateStore;
    }

    public string? ErrorMessage { get; set; }

    public async Task<IActionResult> OnGetAsync(
        string? returnUrl = null,
        string? remoteError = null)
    {
        returnUrl ??= "/";

        if (!string.IsNullOrEmpty(remoteError))
        {
            _logger.LogWarning("External login failed with remote error: {Error}", remoteError);
            return RedirectToLoginWithError($"External login failed: {remoteError}", returnUrl);
        }

        // Authenticate from the external cookie scheme
        // The OAuth middleware stores the external provider's result in IdentityConstants.ExternalScheme
        // This is a temporary cookie that holds the external auth result until we process it
        var info = await HttpContext.AuthenticateAsync(IdentityConstants.ExternalScheme);
        if (info?.Principal == null)
        {
            _logger.LogWarning("External callback received but no external authentication found");
            return RedirectToLoginWithError("External login failed. Please try again.", returnUrl);
        }

        var provider = info.Properties?.Items["LoginProvider"] ?? "";
        var isRegistration = info.Properties?.Items.TryGetValue("IsRegistration", out var regValue) == true
            && regValue == "true";

        // Look up provider config once and reuse for proxy mode, auto-
        // provisioning, token caching, and external-token inclusion. SAML
        // schemes arrive prefixed ("Saml.{idpName}") but stored bare in DB.
        ExternalProviderConfig? providerConfig = null;
        if (_externalAuthService != null)
        {
            var lookupScheme = provider.StartsWith("Saml.", StringComparison.OrdinalIgnoreCase)
                ? provider.Substring(5)
                : provider;
            providerConfig = await _externalAuthService.GetProviderConfigAsync(lookupScheme);
            _logger.LogDebug(
                "Provider {Provider} (lookup: {LookupScheme}) config: AutoProvision={AutoProvision} ProxyMode={ProxyMode} StoreUserLocally={StoreUserLocally}",
                provider, lookupScheme,
                providerConfig?.AutoProvisionUsers, providerConfig?.ProxyMode, providerConfig?.StoreUserLocally);
        }

        // Access-control gate (admin-configured per-IdP). Evaluated before
        // the proxy-mode branch so federation-broker logins are gated too.
        // emailAllowlist / claimGate denials end the flow with the same
        // shape as a tenant-level "registration disabled" deny — sign out
        // the external scheme, send the user back to login with a generic
        // error so the IdP isn't enumerable.
        if (providerConfig?.AccessControl != null)
        {
            var decision = ExternalAccessControlEvaluator.Evaluate(info.Principal, providerConfig.AccessControl);
            if (decision != ExternalAccessControlEvaluator.Decision.Allow)
            {
                _logger.LogWarning(
                    "External login denied by IdP access control: provider={Provider} decision={Decision}",
                    provider, decision);
                await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
                return RedirectToLoginWithError("Access denied for this account.", returnUrl);
            }
        }

        // Proxy / federation-broker mode — no local user, claims pass
        // through to the issued session. Mirrors the journey handler's
        // HandleProxyModeAsync path so a tenant's ProxyMode toggle behaves
        // identically across standalone and journey-driven flows.
        if (providerConfig?.ProxyMode == true && providerConfig?.StoreUserLocally == false)
        {
            return await HandleProxyModeAsync(info, provider, providerConfig, returnUrl);
        }

        var autoProvision = providerConfig?.AutoProvisionUsers ?? false;

        // Check if self-registration is allowed for this tenant
        // If registration is disabled, block explicit registration flow via external provider
        // But still allow auto-provisioning (enterprise SSO) - that's intentional provisioning, not self-registration
        var allowSelfRegistration = _tenantContext?.Tenant?.AllowSelfRegistration ?? true;
        if (isRegistration && !allowSelfRegistration)
        {
            _logger.LogWarning("External registration blocked - self-registration is disabled for tenant");
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
            return RedirectToLoginWithError("Registration is not available", returnUrl);
        }

        // Create user if: explicit registration flow OR provider has auto-provisioning enabled
        var shouldCreateUser = isRegistration || autoProvision;

        _logger.LogDebug("Processing external callback from {Provider}, IsRegistration: {IsRegistration}, AutoProvision: {AutoProvision}",
            provider, isRegistration, autoProvision);

        // Process external login - create user if this is a registration flow or auto-provision is enabled
        var externalResult = await _userService.ProcessExternalLoginAsync(
            provider,
            info.Principal,
            createIfNotExists: shouldCreateUser,
            clientId: await AuthFlowGate.GetClientIdAsync(_protocolStateStore, returnUrl));

        if (!externalResult.Succeeded)
        {
            _logger.LogWarning("External login processing failed: {Error}", externalResult.Error);

            // An account has this email but the provider can't vouch for it: the person proves the account is
            // theirs before the login is linked. The external cookie stays; the link page needs it.
            if (externalResult.Error == ProcessExternalLoginResult.AccountLinkRequired)
            {
                return Redirect($"/account/link-account?returnUrl={Uri.EscapeDataString(returnUrl)}");
            }

            // If user not found and not auto-provisioning, OR if email is required,
            // redirect to external registration page to collect missing info
            // But only if self-registration is allowed
            if (externalResult.Error == "user_not_found" && !shouldCreateUser ||
                externalResult.Error == "email_required")
            {
                // If self-registration is disabled, don't allow creating account via external provider
                if (!allowSelfRegistration)
                {
                    _logger.LogWarning("External login blocked - user not found and self-registration is disabled for tenant");
                    await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
                    return RedirectToLoginWithError("No account found. Contact your administrator.", returnUrl);
                }

                // DON'T clear external cookie - we need it for the registration page
                _logger.LogDebug("Redirecting to external registration page for {Provider}: {Error}", provider, externalResult.Error);
                return Redirect($"/account/external-register?returnUrl={Uri.EscapeDataString(returnUrl)}");
            }

            // Clear external cookie before redirecting to error page
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

            var redirectPage = isRegistration ? "/account/register" : "/account/login";
            return RedirectWithError(redirectPage, externalResult.ErrorDescription ?? "External login failed", returnUrl);
        }

        // Apply IdP DefaultRoles on first provision only. Additive — never
        // removed on subsequent logins, so an admin editing the IdP's
        // defaultRoles list later doesn't strip access from existing users.
        if (externalResult.IsNewUser
            && providerConfig?.DefaultRoles != null
            && providerConfig.DefaultRoles.Count > 0
            && _roleStore != null
            && _roleGrants != null)
        {
            var appliedRoleNames = new List<string>();
            foreach (var roleId in providerConfig.DefaultRoles)
            {
                var role = await _roleStore.GetByIdAsync(roleId);
                if (role == null
                    || string.IsNullOrEmpty(role.Name)
                    || (role.TenantId != null && role.TenantId != _tenantContext?.TenantId))
                {
                    _logger.LogWarning(
                        "Default role {RoleId} skipped on user {UserId}: not found or not in tenant {TenantId}",
                        roleId, externalResult.UserId, _tenantContext?.TenantId);
                    continue;
                }
                await _roleGrants.GrantAsync(
                    role.Id, externalResult.UserId!, null, RoleGrantActor.System("External login"));
                appliedRoleNames.Add(role.Name);
            }
            if (appliedRoleNames.Count > 0)
            {
                _logger.LogInformation(
                    "Applied IdP default roles {Roles} to new user {UserId} (provider={Provider})",
                    string.Join(",", appliedRoleNames), externalResult.UserId, provider);
            }
        }

        // Capture external tokens before signing out the external scheme —
        // the auth properties only live on the external cookie we're about
        // to clear, so anything we need from them must be read first.
        var accessToken = info.Properties?.GetTokenValue("access_token");
        var idToken = info.Properties?.GetTokenValue("id_token");
        var refreshToken = info.Properties?.GetTokenValue("refresh_token");

        // Sign in the user with the application cookie. Mirror the journey
        // handler: when IncludeExternalAccessToken / IncludeExternalIdToken
        // is set on the IdP, surface those into the session principal so
        // downstream code (token issuance, userinfo proxy, etc.) can use
        // them without re-reading the external cookie.
        await SignInUserAsync(externalResult.UserId!, provider, returnUrl, providerConfig, accessToken, idToken);

        // Cache external tokens for the local user when configured. Used
        // by the userinfo-proxy endpoint to call back into the IdP on
        // behalf of the user. Same shape as the journey handler.
        if (_externalAuthService != null
            && providerConfig?.CacheExternalTokens == true
            && !string.IsNullOrEmpty(accessToken))
        {
            await _externalAuthService.CacheExternalTokensAsync(
                externalResult.UserId!,
                new ExternalTokenData
                {
                    Provider = provider,
                    ExternalSubject = info.Principal.FindFirstValue(ClaimTypes.NameIdentifier) ?? "",
                    AccessToken = accessToken,
                    IdToken = idToken,
                    RefreshToken = refreshToken
                },
                TimeSpan.FromSeconds(providerConfig.TokenCacheDurationSeconds));
        }

        // Clear external cookie - important to do this after successful processing
        // This ensures the temporary external auth cookie doesn't linger
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

        _logger.LogInformation("User {UserId} signed in via external provider {Provider}",
            externalResult.UserId, provider);

        // Redirect to the return URL
        if (IsAllowedReturnUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }

        return Redirect("/");
    }

    /// <summary>
    /// Federation-broker / pass-through mode: no local user is created or
    /// looked up. The external claims are filtered (ProxyIncludeClaims /
    /// ProxyExcludeClaims) and projected into the session principal under
    /// a synthetic <c>proxy:{provider}:{providerKey}</c> subject. Mirrors
    /// the journey handler's HandleProxyModeAsync so the same IdP toggle
    /// behaves identically across standalone and journey-driven flows.
    /// </summary>
    private async Task<IActionResult> HandleProxyModeAsync(
        AuthenticateResult info,
        string provider,
        ExternalProviderConfig providerConfig,
        string returnUrl)
    {
        var providerKey = info.Principal!.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(providerKey))
        {
            _logger.LogWarning("Proxy-mode external callback missing NameIdentifier for provider {Provider}", provider);
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
            return RedirectToLoginWithError("External login failed: no subject identifier", returnUrl);
        }

        var externalClaims = info.Principal!.Claims
            .GroupBy(c => c.Type)
            .ToDictionary(g => g.Key, g => g.First().Value);

        var email = info.Principal.FindFirstValue(ClaimTypes.Email);
        var name = info.Principal.FindFirstValue(ClaimTypes.Name);
        var firstName = info.Principal.FindFirstValue(ClaimTypes.GivenName);
        var lastName = info.Principal.FindFirstValue(ClaimTypes.Surname);

        var accessToken = info.Properties?.GetTokenValue("access_token");
        var idToken = info.Properties?.GetTokenValue("id_token");
        var refreshToken = info.Properties?.GetTokenValue("refresh_token");

        var outputClaims = ExternalProxyClaimsBuilder.Build(
            provider,
            providerKey,
            externalClaims,
            providerConfig,
            claimMappings: null,
            email,
            name,
            firstName,
            lastName,
            accessToken,
            idToken);

        var proxySubject = ExternalProxyClaimsBuilder.ProxySubjectFor(provider, providerKey);

        // Cache external tokens against the synthetic subject so the
        // userinfo-proxy endpoint can find them later — same key shape
        // the journey handler uses.
        if (_externalAuthService != null
            && providerConfig.CacheExternalTokens
            && !string.IsNullOrEmpty(accessToken))
        {
            await _externalAuthService.CacheExternalTokensAsync(
                proxySubject,
                new ExternalTokenData
                {
                    Provider = provider,
                    ExternalSubject = providerKey,
                    AccessToken = accessToken,
                    IdToken = idToken,
                    RefreshToken = refreshToken
                },
                TimeSpan.FromSeconds(providerConfig.TokenCacheDurationSeconds));
        }

        var claims = new List<Claim>();
        foreach (var kvp in outputClaims)
        {
            var stringValue = kvp.Value switch
            {
                string s => s,
                string[] arr => string.Join(' ', arr),
                bool b => b ? "true" : "false",
                _ => kvp.Value?.ToString() ?? string.Empty
            };
            if (!string.IsNullOrEmpty(stringValue))
            {
                claims.Add(new Claim(kvp.Key, stringValue));
            }
        }

        if (_tenantContext?.HasTenant == true && !string.IsNullOrEmpty(_tenantContext.TenantId))
        {
            claims.RemoveAll(c => c.Type == "tenant_id");
            claims.Add(new Claim("tenant_id", _tenantContext.TenantId));
        }

        var identity = new ClaimsIdentity(claims, "Oluso");
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(IdentityConstants.ApplicationScheme, principal);
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

        if (_signInAuditor != null)
        {
            await _signInAuditor.RecordSuccessAsync(new SignInAudit
            {
                SubjectId = proxySubject,
                Username = providerKey,
                AuthenticationMethod = AuthenticationMethodFor(provider),
                ReturnUrl = returnUrl
            });
        }

        _logger.LogInformation(
            "Proxy-mode sign-in completed for provider {Provider}, external subject {Subject}",
            provider, providerKey);

        if (IsAllowedReturnUrl(returnUrl))
        {
            return Redirect(returnUrl);
        }
        return Redirect("/");
    }

    private async Task SignInUserAsync(
        string userId,
        string provider,
        string returnUrl,
        ExternalProviderConfig? providerConfig = null,
        string? externalAccessToken = null,
        string? externalIdToken = null)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return;

        // Build the principal via the canonical claims factory so the cookie
        // carries the same authorization claims (allowed_tenants, org_ids,
        // org_roles, aggregated permissions) the password and journey signin
        // paths emit. Without this the user signs in but every tenant-scoped
        // API call 403s on the allowed_tenants gate in TenantResolutionMiddleware.
        var principal = await _claimsFactory.CreateAsync(user);
        var identity = (ClaimsIdentity)principal.Identity!;

        // Force tenant_id to the user's home tenant. The factory falls back
        // to the URL-resolved tenant when user.TenantId is null, which can
        // leak the platform host for system-level users.
        if (!string.IsNullOrEmpty(user.TenantId))
        {
            foreach (var stale in identity.FindAll("tenant_id").ToList())
            {
                identity.RemoveClaim(stale);
            }
            identity.AddClaim(new Claim("tenant_id", user.TenantId));
        }

        // Same idp/amr the journey's external step records; an organisation
        // sign-in reads them when handing this session back to the platform.
        identity.AddClaim(new Claim("idp", provider));
        identity.AddClaim(new Claim("amr", "external"));

        // Surface external IdP tokens into the session principal when the
        // provider is configured to include them. Same shape the journey
        // handler emits via output claims, so session looks identical
        // across login paths.
        if (providerConfig?.IncludeExternalAccessToken == true && !string.IsNullOrEmpty(externalAccessToken))
        {
            identity.AddClaim(new Claim("external_access_token", externalAccessToken));
        }
        if (providerConfig?.IncludeExternalIdToken == true && !string.IsNullOrEmpty(externalIdToken))
        {
            identity.AddClaim(new Claim("external_id_token", externalIdToken));
        }

        var authProperties = new AuthenticationProperties
        {
            IsPersistent = false,
            ExpiresUtc = null
        };

        await HttpContext.SignInAsync(IdentityConstants.ApplicationScheme, principal, authProperties);
        await _userService.RecordLoginAsync(userId);

        if (_signInAuditor != null)
        {
            await _signInAuditor.RecordSuccessAsync(new SignInAudit
            {
                SubjectId = userId,
                Username = user.UserName ?? user.Email ?? userId,
                TenantId = string.IsNullOrEmpty(user.TenantId) ? null : user.TenantId,
                AuthenticationMethod = AuthenticationMethodFor(provider),
                ReturnUrl = returnUrl
            });
        }
    }

    // Mirrors the journey step handlers: SAML schemes arrive as "Saml.{idp}" and
    // report as "saml"; everything else reports the provider scheme itself.
    private static string AuthenticationMethodFor(string provider) =>
        provider.StartsWith("Saml.", StringComparison.OrdinalIgnoreCase)
            ? "saml"
            : provider.ToLowerInvariant();

    private IActionResult RedirectToLoginWithError(string error, string returnUrl)
    {
        return RedirectWithError("/account/login", error, returnUrl);
    }

    private IActionResult RedirectWithError(string page, string error, string returnUrl)
    {
        var url = $"{page}?error={Uri.EscapeDataString(error)}";
        if (!string.IsNullOrEmpty(returnUrl))
        {
            url += $"&returnUrl={Uri.EscapeDataString(returnUrl)}";
        }
        return Redirect(url);
    }

    private bool IsAllowedReturnUrl(string url)
    {
        // Allow relative URLs
        if (Url.IsLocalUrl(url))
        {
            return true;
        }

        // Allow absolute URLs to the same host
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var request = HttpContext.Request;
            return string.Equals(uri.Host, request.Host.Host, StringComparison.OrdinalIgnoreCase)
                && uri.Port == (request.Host.Port ?? (request.IsHttps ? 443 : 80));
        }

        return false;
    }
}

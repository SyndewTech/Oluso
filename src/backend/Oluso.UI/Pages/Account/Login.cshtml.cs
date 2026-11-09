using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Authentication.OrganizationSignIn;
using Oluso.Core.Authentication;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Oluso.UI.ViewModels;
using Microsoft.AspNetCore.RateLimiting;
using Oluso.Core.Security;

namespace Oluso.UI.Pages.Account;

/// <summary>
/// Standalone login page for non-journey authentication flows
/// </summary>
[EnableRateLimiting(SignInRateLimitPolicies.SignIn)]
public class LoginModel : PageModel
{
    private readonly IOlusoUserService _userService;
    private readonly IProtocolStateStore _protocolStateStore;
    private readonly ITenantContext? _tenantContext;
    private readonly IAuthenticationMethodRegistry? _authMethodRegistry;
    private readonly IClientStore? _clientStore;
    private readonly ISignInAuditor? _signInAuditor;
    private readonly IOrganizationSignInService? _organizationSignIn;
    private readonly ILogger<LoginModel> _logger;

    public LoginModel(
        IOlusoUserService userService,
        IProtocolStateStore protocolStateStore,
        ILogger<LoginModel> logger,
        ITenantContext? tenantContext = null,
        IAuthenticationMethodRegistry? authMethodRegistry = null,
        IClientStore? clientStore = null,
        ISignInAuditor? signInAuditor = null,
        IOrganizationSignInService? organizationSignIn = null)
    {
        _userService = userService;
        _protocolStateStore = protocolStateStore;
        _tenantContext = tenantContext;
        _authMethodRegistry = authMethodRegistry;
        _clientStore = clientStore;
        _logger = logger;
        _signInAuditor = signInAuditor;
        _organizationSignIn = organizationSignIn;
    }

    [BindProperty]
    public LoginInputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? LoginHint { get; set; }

    /// <summary>
    /// Error string passed in via <c>?error=...</c> on the URL. Bound on GET
    /// only, so post-back error messages set inside <c>OnPostAsync</c>
    /// (invalid credentials, locked account, etc.) still take precedence
    /// after they overwrite the property. The view reads this directly,
    /// which is why it has to be bound here rather than just stashed in
    /// <c>TempData</c> — flow-control redirects from <c>ExternalCallback</c>,
    /// <c>Register</c>, etc. land on this page with the error in the query
    /// string, and were silently dropping it before this binding.
    /// </summary>
    [BindProperty(SupportsGet = true, Name = "error")]
    public string? ErrorMessage { get; set; }
    public Dictionary<string, string> ValidationErrors { get; set; } = new();

    /// <summary>Set when the credentials matched accounts in more than one organization.</summary>
    public List<OrganizationOption>? AvailableOrganizations { get; set; }

    private const string PendingPasswordKey = "login_pending_password";
    public bool EnableLocalLogin { get; set; } = true;
    public bool EnableLocalRegistration { get; set; } = true;
    public IList<ExternalProviderViewModel>? ExternalProviders { get; set; }
    public IList<DirectLoginProviderViewModel>? DirectLoginProviders { get; set; }

    /// <summary>
    /// "Sign in with your organisation" entry point; set only on the platform host
    /// when the option is enabled.
    /// </summary>
    public string? OrgSignInUrl { get; set; }
    public JourneyUiConfiguration? UiConfig { get; set; }

    // Tenant-level legal links. The standalone login footer renders these only
    // when the tenant has set them — no platform fallback, since on a
    // multi-org service the Terms/Privacy belong to the org, not us.
    public string? TermsUrl { get; set; }
    public string? PrivacyUrl { get; set; }

    /// <summary>
    /// Available passwordless authentication methods (passkey, etc.) discovered via registry,
    /// filtered by tenant policy. The platform's Fido2 provider can be configured globally
    /// but each tenant can opt out via <c>Tenant.EnablePasskey</c> — when false, passkey
    /// (provider id "passkey") is hidden from the login page even if the runtime supports it.
    /// </summary>
    public IEnumerable<IAuthenticationMethodProvider> PasswordlessProviders
    {
        get
        {
            var providers = _authMethodRegistry?.GetAvailableLoginProviders()
                .Where(p => p.Category == AuthenticationMethodCategories.Passwordless)
                ?? [];

            if (_tenantContext?.Tenant?.EnablePasskey == false)
            {
                providers = providers.Where(p => !string.Equals(p.Id, "passkey", StringComparison.OrdinalIgnoreCase));
            }

            return providers;
        }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        // /Account/Login is the standalone-mode UI for an *active protocol
        // flow* (OAuth/OIDC authorize, SAML, etc.) — never a directly
        // navigable page. If there's no live protocol state behind the
        // ReturnUrl, return 404 so the page is invisible to drive-by
        // traffic. Authenticated users still get the redirect-out path
        // below in case they followed a stale link.
        if (User.Identity?.IsAuthenticated != true && !await HasActiveProtocolFlowAsync())
        {
            _logger.LogWarning(
                "Direct access to /Account/Login rejected (no protocol flow in flight). ReturnUrl={ReturnUrl}",
                ReturnUrl);
            return NotFound();
        }

        // Check if user is already authenticated - redirect immediately
        if (User.Identity?.IsAuthenticated == true)
        {
            _logger.LogDebug("User already authenticated, redirecting to {ReturnUrl}", ReturnUrl ?? "/");

            if (!string.IsNullOrEmpty(ReturnUrl) && IsAllowedReturnUrl(ReturnUrl))
            {
                return Redirect(ReturnUrl);
            }

            return Redirect("/");
        }

        // Pre-fill username if login hint provided
        if (!string.IsNullOrEmpty(LoginHint))
        {
            Input.Username = LoginHint;
        }

        // Resolve EnableLocalLogin from tenant + client settings
        await ResolveEnableLocalLoginAsync();

        // Set registration availability from tenant settings
        EnableLocalRegistration = _tenantContext?.Tenant?.AllowSelfRegistration ?? true;

        // Tenant legal links for the footer (no-op when unset)
        BindTenantLegalLinks();

        // Load external authentication schemes
        await LoadExternalProvidersAsync();
        await ResolveOrgSignInUrlAsync();

        // domain_hint auto-redirect: SSO-driven sign-ins land here in
        // ui_mode=standalone with the tenant's IdP scheme as domain_hint.
        // If it matches an enabled provider, skip the picker and go straight
        // to the challenge — mirrors what ExternalLoginStepHandler does in
        // the journey path.
        var domainHint = Request.Query["domain_hint"].FirstOrDefault();
        if (!string.IsNullOrEmpty(domainHint) && ExternalProviders is not null)
        {
            var matched = ExternalProviders.FirstOrDefault(p =>
                string.Equals(p.AuthenticationScheme, domainHint, StringComparison.OrdinalIgnoreCase)
                || string.Equals(p.DisplayName, domainHint, StringComparison.OrdinalIgnoreCase));
            if (matched is not null)
            {
                _logger.LogInformation(
                    "domain_hint '{Hint}' matched provider '{Scheme}', auto-redirecting",
                    domainHint, matched.AuthenticationScheme);
                return OnPostExternalLogin(matched.AuthenticationScheme, ReturnUrl, matched.ProviderType);
            }
            _logger.LogDebug(
                "domain_hint '{Hint}' did not match any configured provider, rendering picker",
                domainHint);
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        // Resolve EnableLocalLogin from tenant + client settings
        await ResolveEnableLocalLoginAsync();

        // Set registration availability from tenant settings (needed for re-render)
        EnableLocalRegistration = _tenantContext?.Tenant?.AllowSelfRegistration ?? true;

        // Tenant legal links for the footer (needed for error re-render)
        BindTenantLegalLinks();

        await LoadExternalProvidersAsync();
        await ResolveOrgSignInUrlAsync();

        // Block local login if disabled at tenant or client level
        if (!EnableLocalLogin)
        {
            _logger.LogWarning("Local login attempt blocked — local login is disabled");
            ErrorMessage = "Local login is not available. Please use an external identity provider.";
            return Page();
        }

        // Held server-side across the organization prompt so it never renders
        // into the page as a hidden field.
        if (string.IsNullOrEmpty(Input.Password)
            && !string.IsNullOrWhiteSpace(Input.OrganizationSlug)
            && TempData.TryGetValue(PendingPasswordKey, out var carried)
            && carried is string carriedPassword)
        {
            Input.Password = carriedPassword;
        }

        // Validate input
        if (string.IsNullOrWhiteSpace(Input.Username))
        {
            ValidationErrors["username"] = "Username or email is required";
        }

        if (string.IsNullOrWhiteSpace(Input.Password))
        {
            ValidationErrors["password"] = "Password is required";
        }

        if (ValidationErrors.Any())
        {
            return Page();
        }

        _logger.LogDebug("Login attempt for user {Username}", Input.Username);

        // Use tenant context from subdomain/domain if available
        // This allows users with accounts in multiple tenants to login via tenant-specific URLs
        var tenantId = _tenantContext?.HasTenant == true ? _tenantContext.TenantId : null;
        if (!string.IsNullOrEmpty(tenantId))
        {
            _logger.LogDebug("Using tenant context for login: {TenantId}", tenantId);
        }

        // A chosen organization becomes the same email@slug qualifier a user can
        // type, so both routes share one code path.
        var loginIdentifier = string.IsNullOrWhiteSpace(Input.OrganizationSlug)
            ? Input.Username!
            : $"{Input.Username}@{Input.OrganizationSlug.Trim()}";

        // Authenticate user
        var result = await _userService.ValidateCredentialsAsync(loginIdentifier, Input.Password!, tenantId);

        if (!result.Succeeded)
        {
            _logger.LogWarning("Login failed for user {Username}: {Error}", Input.Username, result.Error);

            if (result.PasswordExpired && await BeginExpiredPasswordChangeAsync(result.User!) is { } changePassword)
            {
                return changePassword;
            }

            if (result.RequiresOrganizationChoice)
            {
                _logger.LogInformation("Multiple accounts matched; prompting for organization");
                AvailableOrganizations = result.AvailableOrganizations;
                TempData[PendingPasswordKey] = Input.Password;
                return Page();
            }
            else if (result.IsLockedOut)
            {
                ErrorMessage = "Your account has been locked due to too many failed attempts. Please try again later.";
            }
            else
            {
                ErrorMessage = result.ErrorDescription ?? "Invalid username or password";
            }

            if (_signInAuditor != null)
            {
                await _signInAuditor.RecordFailureAsync(new SignInFailureAudit
                {
                    Username = loginIdentifier,
                    FailureReason = result.IsLockedOut ? "locked_out" : (result.Error ?? "invalid_credentials"),
                    TenantId = tenantId,
                    ReturnUrl = ReturnUrl
                });
            }

            return Page();
        }

        var userId = result.User!.Id;
        _logger.LogInformation("User {UserId} logged in successfully", userId);

        // Sign in the user
        await SignInUserAsync(userId, Input.RememberMe);
        await _userService.RecordLoginAsync(userId);

        if (_signInAuditor != null)
        {
            await _signInAuditor.RecordSuccessAsync(new SignInAudit
            {
                SubjectId = userId,
                Username = result.User.UserName ?? result.User.Email ?? loginIdentifier,
                TenantId = result.User.TenantId ?? tenantId,
                AuthenticationMethod = "pwd",
                ReturnUrl = ReturnUrl
            });
        }

        // Handle return URL - redirect back to callback (cookie is now set)
        if (!string.IsNullOrEmpty(ReturnUrl) && IsAllowedReturnUrl(ReturnUrl))
        {
            return Redirect(ReturnUrl);
        }

        // Default: redirect to root
        return Redirect("/");
    }

    /// <summary>
    /// The password was right but has expired: no session is issued. Hands the verified account
    /// to the reset-password page (server-side, via TempData) with a fresh reset token, so the
    /// user sets a new password and then signs in with it to resume <see cref="ReturnUrl"/>.
    /// Null when no reset token can be issued, leaving the expired-password error on this page.
    /// </summary>
    private async Task<IActionResult?> BeginExpiredPasswordChangeAsync(ValidatedUser user)
    {
        var token = await _userService.GeneratePasswordResetTokenAsync(user.Id);
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        _logger.LogInformation("Password expired for user {UserId}; redirecting to change it before sign-in", user.Id);
        ExpiredPasswordTicket.Store(TempData, new ExpiredPasswordTicket(user.Id, token, Input.Username?.Trim()));
        return RedirectToPage("/Account/ResetPassword", new { expired = true, returnUrl = ReturnUrl });
    }

    public IActionResult OnPostExternalLogin(string provider, string? returnUrl, string? providerType)
    {
        // SAML providers use controller-based flow, not Challenge
        if (string.Equals(providerType, "Saml2", StringComparison.OrdinalIgnoreCase))
        {
            var samlReturnUrl = Uri.EscapeDataString(returnUrl ?? "/");
            return Redirect($"/saml/login/{Uri.EscapeDataString(provider)}?returnUrl={samlReturnUrl}");
        }

        // OAuth/OIDC providers use ASP.NET Core Challenge
        var redirectUrl = $"/account/external-callback?returnUrl={Uri.EscapeDataString(returnUrl ?? "/")}";
        var properties = new AuthenticationProperties { RedirectUri = redirectUrl };
        properties.Items["LoginProvider"] = provider;

        return Challenge(properties, provider);
    }

    private async Task SignInUserAsync(string userId, bool rememberMe)
    {
        var user = await _userService.FindByIdAsync(userId);
        if (user == null) return;

        var claims = (await _userService.GetClaimsAsync(userId)).ToList();

        // Set tenant_id ONLY from the user's home tenant. System-level users
        // (SuperAdmin/OrgAdmin with user.TenantId=null) get no tenant_id claim
        // — they must use X-Tenant-Id header to operate on a specific tenant.
        // Falling back to the URL-resolved tenant here leaks the platform host
        // into their JWT and causes the middleware's allowed_tenants gate to
        // wrongly evaluate against the platform tenant.
        if (!string.IsNullOrEmpty(user.TenantId))
        {
            claims.RemoveAll(c => c.Type == "tenant_id");
            claims.Add(new System.Security.Claims.Claim("tenant_id", user.TenantId));
        }

        var identity = new System.Security.Claims.ClaimsIdentity(claims, "Oluso");
        var principal = new System.Security.Claims.ClaimsPrincipal(identity);

        var authProperties = new AuthenticationProperties
        {
            IsPersistent = rememberMe,
            ExpiresUtc = rememberMe ? DateTimeOffset.UtcNow.AddDays(30) : null
        };

        await HttpContext.SignInAsync(IdentityConstants.ApplicationScheme, principal, authProperties);
    }

    private async Task LoadExternalProvidersAsync()
    {
        // Try to get providers from the external provider config store first
        // This only returns providers that are actually configured in the database
        var providerStore = HttpContext.RequestServices.GetService<IExternalProviderConfigStore>();
        if (providerStore != null)
        {
            var configuredProviders = await providerStore.GetEnabledProvidersAsync();

            // Split into redirect-based (OAuth/SAML) and direct login (LDAP) providers
            ExternalProviders = configuredProviders
                .Where(p => !p.IsDirectLogin)
                .Select(p => new ExternalProviderViewModel
                {
                    AuthenticationScheme = p.Scheme,
                    DisplayName = p.DisplayName ?? p.Scheme,
                    IconUrl = p.IconUrl,
                    ProviderType = p.ProviderType
                })
                .ToList();

            DirectLoginProviders = configuredProviders
                .Where(p => p.IsDirectLogin)
                .Select(p => new DirectLoginProviderViewModel
                {
                    ProviderId = p.Id,
                    DisplayName = p.DisplayName ?? p.Scheme,
                    // Include scheme in the path so DirectLogin page knows which provider to use
                    LoginPath = $"{p.DirectLoginPath ?? "/account/direct-login"}?scheme={Uri.EscapeDataString(p.Scheme)}",
                    IconUrl = p.IconUrl,
                    Description = null
                })
                .ToList();

            return;
        }

        // Fallback to authentication schemes if no store is registered
        var schemes = await HttpContext.RequestServices
            .GetRequiredService<IAuthenticationSchemeProvider>()
            .GetAllSchemesAsync();

        ExternalProviders = schemes
            .Where(s => !string.IsNullOrEmpty(s.DisplayName))
            .Select(s => new ExternalProviderViewModel
            {
                AuthenticationScheme = s.Name,
                DisplayName = s.DisplayName ?? s.Name
            })
            .ToList();

        DirectLoginProviders = new List<DirectLoginProviderViewModel>();
    }

    private async Task ResolveOrgSignInUrlAsync()
    {
        if (_organizationSignIn == null) return;
        OrgSignInUrl = await _organizationSignIn.GetEntryUrlAsync(HttpContext, ReturnUrl, HttpContext.RequestAborted);
    }

    private async Task ResolveEnableLocalLoginAsync()
    {
        // Start with tenant-level setting
        var enableLocalLogin = _tenantContext?.Tenant?.EnableLocalLogin ?? true;

        // Try to also apply client-level setting from the protocol state in ReturnUrl
        if (enableLocalLogin && !string.IsNullOrEmpty(ReturnUrl) && _clientStore != null)
        {
            try
            {
                var correlationId = ExtractCorrelationId(ReturnUrl);
                if (!string.IsNullOrEmpty(correlationId))
                {
                    var state = await _protocolStateStore.GetAsync(correlationId);
                    if (state != null && !string.IsNullOrEmpty(state.ClientId))
                    {
                        var client = await _clientStore.FindClientByIdAsync(state.ClientId);
                        if (client != null)
                        {
                            enableLocalLogin = client.EnableLocalLogin;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not resolve client EnableLocalLogin from ReturnUrl");
            }
        }

        EnableLocalLogin = enableLocalLogin;
    }

    private static string? ExtractCorrelationId(string returnUrl)
    {
        // ReturnUrl is a callback URL like /oauth/authorize/callback?correlation_id=<value>
        var queryStart = returnUrl.IndexOf('?');
        if (queryStart < 0) return null;

        var query = QueryHelpers.ParseQuery(returnUrl[queryStart..]);
        return query.TryGetValue("correlation_id", out var value) ? value.ToString() : null;
    }

    private void BindTenantLegalLinks()
    {
        var tenant = _tenantContext?.Tenant;
        if (tenant == null) return;
        TermsUrl = tenant.TermsOfServiceUrl;
        PrivacyUrl = tenant.PrivacyPolicyUrl;
    }

    private Task<bool> HasActiveProtocolFlowAsync() =>
        AuthFlowGate.HasActiveFlowAsync(_protocolStateStore, ReturnUrl);

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

public class LoginInputModel
{
    public string? Username { get; set; }
    public string? Password { get; set; }

    public string? OrganizationSlug { get; set; }
    public bool RememberMe { get; set; }
}

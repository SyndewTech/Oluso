using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Authentication;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Oluso.UI.ViewModels;

namespace Oluso.UI.Pages.Account;

/// <summary>
/// Standalone login page for non-journey authentication flows
/// </summary>
public class LoginModel : PageModel
{
    private readonly IOlusoUserService _userService;
    private readonly IProtocolStateStore _protocolStateStore;
    private readonly ITenantContext? _tenantContext;
    private readonly IAuthenticationMethodRegistry? _authMethodRegistry;
    private readonly ILogger<LoginModel> _logger;

    public LoginModel(
        IOlusoUserService userService,
        IProtocolStateStore protocolStateStore,
        ILogger<LoginModel> logger,
        ITenantContext? tenantContext = null,
        IAuthenticationMethodRegistry? authMethodRegistry = null)
    {
        _userService = userService;
        _protocolStateStore = protocolStateStore;
        _tenantContext = tenantContext;
        _authMethodRegistry = authMethodRegistry;
        _logger = logger;
    }

    [BindProperty]
    public LoginInputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? LoginHint { get; set; }

    public string? ErrorMessage { get; set; }
    public Dictionary<string, string> ValidationErrors { get; set; } = new();
    public bool EnableLocalRegistration { get; set; } = true;
    public IList<ExternalProviderViewModel>? ExternalProviders { get; set; }
    public IList<DirectLoginProviderViewModel>? DirectLoginProviders { get; set; }
    public JourneyUiConfiguration? UiConfig { get; set; }

    /// <summary>
    /// Available passwordless authentication methods (passkey, etc.) discovered via registry.
    /// Each provider specifies its own LoginUrl for handling the authentication flow.
    /// </summary>
    public IEnumerable<IAuthenticationMethodProvider> PasswordlessProviders =>
        _authMethodRegistry?.GetAvailableLoginProviders()
            .Where(p => p.Category == AuthenticationMethodCategories.Passwordless) ?? [];

    public async Task<IActionResult> OnGetAsync()
    {
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

        // Load external authentication schemes
        await LoadExternalProvidersAsync();

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadExternalProvidersAsync();

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

        // Authenticate user
        var result = await _userService.ValidateCredentialsAsync(Input.Username!, Input.Password!, tenantId);

        if (!result.Succeeded)
        {
            _logger.LogWarning("Login failed for user {Username}: {Error}", Input.Username, result.Error);

            if (result.RequiresTenantQualifier)
            {
                // User has accounts in multiple tenants - guide them to use tenant-specific URL
                _logger.LogWarning("Multiple accounts found for {Username} across tenants", Input.Username);
                ErrorMessage = "Multiple accounts found with this email. Please access via your organization's login URL.";
                // Could also show available tenants: result.AvailableTenants
            }
            else if (result.IsLockedOut)
            {
                ErrorMessage = "Your account has been locked due to too many failed attempts. Please try again later.";
            }
            else
            {
                ErrorMessage = result.Error ?? "Invalid username or password";
            }

            return Page();
        }

        var userId = result.User!.Id;
        _logger.LogInformation("User {UserId} logged in successfully", userId);

        // Sign in the user
        await SignInUserAsync(userId, Input.RememberMe);

        // Handle return URL - redirect back to callback (cookie is now set)
        if (!string.IsNullOrEmpty(ReturnUrl) && IsAllowedReturnUrl(ReturnUrl))
        {
            return Redirect(ReturnUrl);
        }

        // Default: redirect to root
        return Redirect("/");
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

        // Add tenant_id claim if in a tenant context (required for tenant-scoped cookie validation)
        if (_tenantContext?.HasTenant == true && !string.IsNullOrEmpty(_tenantContext.TenantId))
        {
            // Remove any existing tenant_id claim to avoid duplicates
            claims.RemoveAll(c => c.Type == "tenant_id");
            claims.Add(new System.Security.Claims.Claim("tenant_id", _tenantContext.TenantId));
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
    public bool RememberMe { get; set; }
}

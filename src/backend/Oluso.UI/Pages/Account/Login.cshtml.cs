using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
    private readonly IFido2Service? _fido2Service;
    private readonly ILogger<LoginModel> _logger;

    public LoginModel(
        IOlusoUserService userService,
        IProtocolStateStore protocolStateStore,
        ILogger<LoginModel> logger,
        ITenantContext? tenantContext = null,
        IFido2Service? fido2Service = null)
    {
        _userService = userService;
        _protocolStateStore = protocolStateStore;
        _tenantContext = tenantContext;
        _fido2Service = fido2Service;
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
    /// Whether passkey/FIDO2 login is available
    /// </summary>
    public bool ShowPasskey => _fido2Service != null;

    /// <summary>
    /// FIDO2 assertion view model for passkey login (used to render the partial)
    /// </summary>
    public Fido2AssertionViewModel? Fido2AssertionViewModel { get; set; }

    /// <summary>
    /// FIDO2 assertion options for passkey login (serialized JSON) - kept for backwards compatibility
    /// </summary>
    public string? Fido2AssertionOptions { get; set; }

    /// <summary>
    /// FIDO2 assertion ID for verification
    /// </summary>
    public string? Fido2AssertionId { get; set; }

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

        // Authenticate user
        var result = await _userService.ValidateCredentialsAsync(Input.Username!, Input.Password!);

        if (!result.Succeeded)
        {
            _logger.LogWarning("Login failed for user {Username}: {Error}", Input.Username, result.Error);
            ErrorMessage = result.Error ?? "Invalid username or password";

            if (result.IsLockedOut)
            {
                ErrorMessage = "Your account has been locked due to too many failed attempts. Please try again later.";
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

    public async Task<IActionResult> OnPostPasskeyAsync()
    {
        if (_fido2Service == null)
        {
            ErrorMessage = "Passkey authentication is not available";
            await LoadExternalProvidersAsync();
            return Page();
        }

        try
        {
            // Create assertion options (usernameless - discoverable credentials)
            var options = await _fido2Service.CreateAssertionOptionsAsync(null);

            

            Fido2AssertionId = options.AssertionId;

            // Create view model for the Enterprise _Fido2Assertion partial
            Fido2AssertionViewModel = new Fido2AssertionViewModel
            {
                Options = options,
                AssertionId = options.AssertionId
            };

            // Also serialize for backwards compatibility with inline JavaScript
            Fido2AssertionOptions = System.Text.Json.JsonSerializer.Serialize(options, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            });

            _logger.LogInformation("Generated FIDO2 assertion options with ID {AssertionId}", options.AssertionId);

            await LoadExternalProvidersAsync();
            return Page();
        }
        catch (Fido2Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create FIDO2 assertion options");
            ErrorMessage = ex.Message;
            await LoadExternalProvidersAsync();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostPasskeyVerifyAsync(string assertionId, string assertionResponse)
    {
        if (_fido2Service == null)
        {
            ErrorMessage = "Passkey authentication is not available";
            await LoadExternalProvidersAsync();
            return Page();
        }

        if (string.IsNullOrEmpty(assertionId) || string.IsNullOrEmpty(assertionResponse))
        {
            ErrorMessage = "Invalid passkey response";
            await LoadExternalProvidersAsync();
            return Page();
        }

        try
        {
            var result = await _fido2Service.VerifyAssertionAsync(assertionId, assertionResponse);

            if (!result.Succeeded)
            {
                _logger.LogWarning("FIDO2 assertion verification failed: {Error}", result.Error);
                ErrorMessage = result.ErrorDescription ?? "Passkey verification failed";
                await LoadExternalProvidersAsync();
                return Page();
            }

            var user = await _userService.FindByIdAsync(result.UserId!);
            if (user == null)
            {
                ErrorMessage = "User not found";
                await LoadExternalProvidersAsync();
                return Page();
            }

            if (!user.IsActive)
            {
                ErrorMessage = "Your account has been deactivated";
                await LoadExternalProvidersAsync();
                return Page();
            }

            _logger.LogInformation("User {UserId} logged in via passkey", user.Id);

            // Sign in the user
            await SignInUserAsync(user.Id, Input.RememberMe);

            // Handle return URL
            if (!string.IsNullOrEmpty(ReturnUrl) && IsAllowedReturnUrl(ReturnUrl))
            {
                return Redirect(ReturnUrl);
            }

            return Redirect("/");
        }
        catch (Fido2Exception ex)
        {
            _logger.LogError(ex, "FIDO2 assertion verification error");
            ErrorMessage = ex.Message;
            await LoadExternalProvidersAsync();
            return Page();
        }
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

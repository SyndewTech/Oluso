using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Oluso.UI.ViewModels;

namespace Oluso.UI.Pages.Account;

/// <summary>
/// Standalone registration page for non-journey authentication flows
/// </summary>
public class RegisterModel : PageModel
{
    private readonly IOlusoUserService _userService;
    private readonly ITenantContext? _tenantContext;
    private readonly ILogger<RegisterModel> _logger;

    public RegisterModel(
        IOlusoUserService userService,
        ILogger<RegisterModel> logger,
        ITenantContext? tenantContext = null)
    {
        _userService = userService;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    [BindProperty]
    public RegisterInputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? ErrorMessage { get; set; }
    public Dictionary<string, string> ValidationErrors { get; set; } = new();
    public bool RequireUsername { get; set; } = false;
    public bool RequireTermsAcceptance { get; set; } = true;
    public IList<ExternalProviderViewModel>? ExternalProviders { get; set; }
    public JourneyUiConfiguration? UiConfig { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadExternalProvidersAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        await LoadExternalProvidersAsync();

        // Validate input
        if (string.IsNullOrWhiteSpace(Input.Email))
        {
            ValidationErrors["email"] = "Email address is required";
        }
        else if (!IsValidEmail(Input.Email))
        {
            ValidationErrors["email"] = "Please enter a valid email address";
        }

        if (RequireUsername && string.IsNullOrWhiteSpace(Input.Username))
        {
            ValidationErrors["username"] = "Username is required";
        }

        if (string.IsNullOrWhiteSpace(Input.Password))
        {
            ValidationErrors["password"] = "Password is required";
        }
        else if (Input.Password.Length < 8)
        {
            ValidationErrors["password"] = "Password must be at least 8 characters";
        }

        if (Input.Password != Input.ConfirmPassword)
        {
            ValidationErrors["confirmPassword"] = "Passwords do not match";
        }

        if (RequireTermsAcceptance && !Input.AcceptTerms)
        {
            ValidationErrors["acceptTerms"] = "You must accept the terms to continue";
        }

        if (ValidationErrors.Any())
        {
            return Page();
        }

        _logger.LogDebug("Registration attempt for email {Email}", Input.Email);

        // Check if user already exists
        var existingUser = await _userService.FindByEmailAsync(Input.Email!);
        if (existingUser != null)
        {
            _logger.LogWarning("Registration failed - email already exists: {Email}", Input.Email);
            ValidationErrors["email"] = "An account with this email already exists";
            return Page();
        }

        // Create user
        var createResult = await _userService.CreateUserAsync(new CreateUserRequest
        {
            Email = Input.Email!,
            Username = Input.Username ?? Input.Email!,
            Password = Input.Password!,
            FirstName = Input.FirstName,
            LastName = Input.LastName
        });

        if (!createResult.Succeeded)
        {
            _logger.LogWarning("Registration failed for {Email}: {Errors}",
                Input.Email, string.Join(", ", createResult.Errors));

            ErrorMessage = string.Join(". ", createResult.Errors);
            return Page();
        }

        _logger.LogInformation("User {UserId} registered successfully", createResult.UserId);

        // Sign in the new user
        await SignInUserAsync(createResult.UserId!);

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
            // isRegistration=true tells SAML controller this is a registration flow
            return Redirect($"/saml/login/{Uri.EscapeDataString(provider)}?returnUrl={samlReturnUrl}&isRegistration=true");
        }

        // OAuth/OIDC providers use ASP.NET Core Challenge
        // Mark as registration so the callback page knows to create user if not exists
        var redirectUrl = $"/account/external-callback?returnUrl={Uri.EscapeDataString(returnUrl ?? "/")}";
        var properties = new AuthenticationProperties { RedirectUri = redirectUrl };
        properties.Items["LoginProvider"] = provider;
        properties.Items["IsRegistration"] = "true";

        return Challenge(properties, provider);
    }

    private async Task SignInUserAsync(string userId)
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

        await HttpContext.SignInAsync(IdentityConstants.ApplicationScheme, principal);
    }

    private async Task LoadExternalProvidersAsync()
    {
        // Try to get providers from the external provider config store first (includes ProviderType)
        var providerStore = HttpContext.RequestServices.GetService<IExternalProviderConfigStore>();
        if (providerStore != null)
        {
            var configuredProviders = await providerStore.GetEnabledProvidersAsync();

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

            return;
        }

        // Fallback to authentication schemes (no ProviderType available)
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
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
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

public class RegisterInputModel
{
    public string? Email { get; set; }
    public string? Username { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Password { get; set; }
    public string? ConfirmPassword { get; set; }
    public bool AcceptTerms { get; set; }
}

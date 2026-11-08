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
using Microsoft.AspNetCore.RateLimiting;
using Oluso.Core.Security;

namespace Oluso.UI.Pages.Account;

/// <summary>
/// Standalone registration page for non-journey authentication flows
/// </summary>
[EnableRateLimiting(SignInRateLimitPolicies.SignIn)]
public class RegisterModel : PageModel
{
    private readonly IOlusoUserService _userService;
    private readonly IProtocolStateStore _protocolStateStore;
    private readonly ITenantContext? _tenantContext;
    private readonly ISignInAuditor? _signInAuditor;
    private readonly ILogger<RegisterModel> _logger;

    public RegisterModel(
        IOlusoUserService userService,
        IProtocolStateStore protocolStateStore,
        ILogger<RegisterModel> logger,
        ITenantContext? tenantContext = null,
        ISignInAuditor? signInAuditor = null)
    {
        _userService = userService;
        _protocolStateStore = protocolStateStore;
        _tenantContext = tenantContext;
        _logger = logger;
        _signInAuditor = signInAuditor;
    }

    [BindProperty]
    public RegisterInputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? ErrorMessage { get; set; }
    public Dictionary<string, string> ValidationErrors { get; set; } = new();
    public bool RequireUsername { get; set; } = false;
    // RequireTermsAcceptance / TermsUrl / PrivacyUrl are bound from the
    // tenant on every render so admins set them once at the tenant level
    // and the standalone register page mirrors the journey signup step's
    // behaviour. Defaults match a permissive tenant (false / null).
    public bool RequireTermsAcceptance { get; set; }
    public string? TermsUrl { get; set; }
    public string? PrivacyUrl { get; set; }
    public IList<ExternalProviderViewModel>? ExternalProviders { get; set; }
    public JourneyUiConfiguration? UiConfig { get; set; }

    /// <summary>
    /// Pulls the tenant-level registration settings into the page model so
    /// the view and the POST validator share one source of truth. Called
    /// from both OnGet and OnPost — OnPost can't trust BindProperty because
    /// these aren't form-posted, they come from <see cref="ITenantContext"/>.
    /// </summary>
    private void BindTenantRegistrationSettings()
    {
        var tenant = _tenantContext?.Tenant;
        if (tenant == null) return;
        RequireTermsAcceptance = tenant.RequireTermsAcceptance;
        TermsUrl = tenant.TermsOfServiceUrl;
        PrivacyUrl = tenant.PrivacyPolicyUrl;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        // /Account/Register is the standalone-mode UI for an in-flight
        // protocol flow — never directly navigable.
        if (!await AuthFlowGate.HasActiveFlowAsync(_protocolStateStore, ReturnUrl))
        {
            _logger.LogWarning("Direct access to /Account/Register rejected (no protocol flow in flight). ReturnUrl={ReturnUrl}", ReturnUrl);
            return NotFound();
        }

        // Check if self-registration is allowed for this tenant
        if (_tenantContext?.Tenant?.AllowSelfRegistration == false)
        {
            _logger.LogWarning("Registration attempt blocked - self-registration is disabled for tenant");
            return RedirectToPage("/Account/Login", new { ReturnUrl, error = "registration_disabled" });
        }

        BindTenantRegistrationSettings();
        await LoadExternalProvidersAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        // Check if self-registration is allowed for this tenant
        if (_tenantContext?.Tenant?.AllowSelfRegistration == false)
        {
            _logger.LogWarning("Registration POST blocked - self-registration is disabled for tenant");
            return RedirectToPage("/Account/Login", new { ReturnUrl, error = "registration_disabled" });
        }

        BindTenantRegistrationSettings();
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
        else if (!IsEmailDomainAllowed(Input.Email))
        {
            // Tenant-level allowlist — when set, registration is restricted
            // to the listed domains. Mirrors the journey SignUpStepHandler
            // domain check so both surfaces enforce the same policy.
            ValidationErrors["email"] = "Registrations from this email domain are not allowed.";
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

        // Create user. RequireEmailVerification flows from the tenant
        // setting so admins can flip "verify after sign-up" on/off without
        // touching code, and so the standalone register flow stays
        // consistent with the journey signup step.
        var createResult = await _userService.CreateUserAsync(new CreateUserRequest
        {
            Email = Input.Email!,
            Username = Input.Username ?? Input.Email!,
            Password = Input.Password!,
            FirstName = Input.FirstName,
            LastName = Input.LastName,
            RequireEmailVerification = _tenantContext?.Tenant?.RequireEmailVerification ?? true,
            ClientId = await AuthFlowGate.GetClientIdAsync(_protocolStateStore, ReturnUrl)
        });

        if (!createResult.Succeeded)
        {
            _logger.LogWarning("Registration failed for {Email}: {Errors}", Input.Email, createResult.Message);

            ErrorMessage = createResult.MessageOr("Your account could not be created.");
            return Page();
        }

        _logger.LogInformation("User {UserId} registered successfully", createResult.UserId);

        // Sign in the new user
        await SignInUserAsync(createResult.UserId!);
        await _userService.RecordLoginAsync(createResult.UserId!);

        if (_signInAuditor != null)
        {
            await _signInAuditor.RecordSuccessAsync(new SignInAudit
            {
                SubjectId = createResult.UserId!,
                Username = Input.Email,
                TenantId = _tenantContext?.HasTenant == true ? _tenantContext.TenantId : null,
                AuthenticationMethod = "signup",
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

    /// <summary>
    /// Enforces the tenant's <c>AllowedEmailDomains</c> allowlist (comma /
    /// whitespace-separated). Empty / unset list means "any". The
    /// SignUpStepHandler does the same check for journey-based signup;
    /// both paths must agree so a tenant policy can't be bypassed by
    /// pointing the protocol flow at the standalone register page.
    /// </summary>
    private bool IsEmailDomainAllowed(string email)
    {
        var allowed = _tenantContext?.Tenant?.AllowedEmailDomains;
        if (string.IsNullOrWhiteSpace(allowed)) return true;

        var atIndex = email.IndexOf('@');
        if (atIndex < 0 || atIndex == email.Length - 1) return false;
        var domain = email[(atIndex + 1)..].ToLowerInvariant();

        var domains = allowed
            .Split(new[] { ',', ' ', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(d => d.ToLowerInvariant());

        return domains.Contains(domain);
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

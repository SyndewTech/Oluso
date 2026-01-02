using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;

namespace Oluso.UI.Pages.Account;

/// <summary>
/// Unified direct login page for providers that authenticate credentials server-side
/// (LDAP, RADIUS, etc.) rather than redirecting to an external IdP.
///
/// Flow:
/// 1. User clicks "Sign in with Corporate Directory" on login page
/// 2. Redirected here with provider scheme in URL
/// 3. User enters credentials
/// 4. Credentials validated against provider (e.g., LDAP server)
/// 5. On success, signs into ExternalScheme (like OAuth callback does)
/// 6. Redirects to /account/external-callback (same as OAuth/SAML)
/// 7. ExternalCallback processes and signs into application
/// </summary>
public class DirectLoginModel : PageModel
{
    private readonly IExternalProviderConfigStore? _providerStore;
    private readonly IEnumerable<IDirectIdentityProvider> _identityProviders;
    private readonly ITenantContext? _tenantContext;
    private readonly ILogger<DirectLoginModel> _logger;

    public DirectLoginModel(
        ILogger<DirectLoginModel> logger,
        IEnumerable<IDirectIdentityProvider> identityProviders,
        IExternalProviderConfigStore? providerStore = null,
        ITenantContext? tenantContext = null)
    {
        _logger = logger;
        _identityProviders = identityProviders;
        _providerStore = providerStore;
        _tenantContext = tenantContext;
    }

    [BindProperty]
    public DirectLoginInputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? Scheme { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? ErrorMessage { get; set; }
    public string? ProviderDisplayName { get; set; }
    public string? TenantName { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (string.IsNullOrEmpty(Scheme))
        {
            return RedirectToPage("/Account/Login", new { returnUrl = ReturnUrl, error = "No provider specified" });
        }

        // Get provider info
        var providerInfo = await GetProviderInfoAsync(Scheme);
        if (providerInfo == null)
        {
            _logger.LogWarning("Direct login requested for unknown provider: {Scheme}", Scheme);
            return RedirectToPage("/Account/Login", new { returnUrl = ReturnUrl, error = "Unknown provider" });
        }

        if (!providerInfo.IsDirectLogin)
        {
            _logger.LogWarning("Direct login requested for non-direct provider: {Scheme}", Scheme);
            return RedirectToPage("/Account/Login", new { returnUrl = ReturnUrl, error = "Invalid provider type" });
        }

        ProviderDisplayName = providerInfo.DisplayName ?? Scheme;

        if (_tenantContext?.HasTenant == true)
        {
            TenantName = _tenantContext.Tenant?.Name;
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (string.IsNullOrEmpty(Scheme))
        {
            return RedirectToPage("/Account/Login", new { returnUrl = ReturnUrl, error = "No provider specified" });
        }

        var providerInfo = await GetProviderInfoAsync(Scheme);
        if (providerInfo == null || !providerInfo.IsDirectLogin)
        {
            return RedirectToPage("/Account/Login", new { returnUrl = ReturnUrl, error = "Invalid provider" });
        }

        ProviderDisplayName = providerInfo.DisplayName ?? Scheme;

        if (_tenantContext?.HasTenant == true)
        {
            TenantName = _tenantContext.Tenant?.Name;
        }

        if (string.IsNullOrEmpty(Input.Username) || string.IsNullOrEmpty(Input.Password))
        {
            ErrorMessage = "Username and password are required";
            return Page();
        }

        // Find the identity provider for this type
        var identityProvider = FindIdentityProvider(providerInfo.ProviderType);
        if (identityProvider == null)
        {
            _logger.LogError("No IDirectIdentityProvider found for type: {ProviderType}", providerInfo.ProviderType);
            ErrorMessage = "Authentication provider not configured";
            return Page();
        }

        _logger.LogDebug("Attempting direct authentication for user {Username} via {Provider}",
            Input.Username, providerInfo.ProviderType);

        // Authenticate against the provider
        var authResult = await identityProvider.AuthenticateAsync(Input.Username, Input.Password);

        if (!authResult.Succeeded || authResult.Principal == null)
        {
            _logger.LogWarning("Direct authentication failed for {Username}: {Error}",
                Input.Username, authResult.Error);
            ErrorMessage = "Invalid username or password";
            return Page();
        }

        _logger.LogInformation("Direct authentication successful for {Username} via {Provider}",
            Input.Username, providerInfo.ProviderType);

        // Use the principal from the identity provider directly
        var principal = authResult.Principal;

        // Set up auth properties (like OAuth middleware does)
        var authProperties = new AuthenticationProperties
        {
            RedirectUri = $"/account/external-callback?returnUrl={Uri.EscapeDataString(ReturnUrl ?? "/")}",
            IsPersistent = Input.RememberMe
        };
        authProperties.Items["LoginProvider"] = Scheme;
        authProperties.Items["scheme"] = Scheme;

        // Sign into the external scheme (like OAuth callback does)
        await HttpContext.SignInAsync(IdentityConstants.ExternalScheme, principal, authProperties);

        _logger.LogDebug("Signed into external scheme, redirecting to callback");

        // Redirect to external callback (same path as OAuth/SAML)
        return Redirect(authProperties.RedirectUri);
    }

    private async Task<ExternalProviderDefinition?> GetProviderInfoAsync(string scheme)
    {
        if (_providerStore == null)
        {
            return null;
        }

        return await _providerStore.GetBySchemeAsync(scheme);
    }

    private IDirectIdentityProvider? FindIdentityProvider(string providerType)
    {
        // Match by provider type (e.g., "ldap" -> LdapExternalIdentityProvider)
        return _identityProviders.FirstOrDefault(p =>
            p.ProviderType.Equals(providerType, StringComparison.OrdinalIgnoreCase));
    }
}

public class DirectLoginInputModel
{
    public string? Username { get; set; }
    public string? Password { get; set; }
    public bool RememberMe { get; set; }
}

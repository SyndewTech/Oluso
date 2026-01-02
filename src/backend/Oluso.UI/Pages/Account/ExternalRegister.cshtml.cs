using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;

namespace Oluso.UI.Pages.Account;

/// <summary>
/// Registration completion page for external logins (SAML, OAuth, OIDC).
/// Shown when an external login doesn't have all required fields (e.g., missing email).
/// </summary>
public class ExternalRegisterModel : PageModel
{
    private readonly IOlusoUserService _userService;
    private readonly ITenantContext? _tenantContext;
    private readonly ILogger<ExternalRegisterModel> _logger;

    public ExternalRegisterModel(
        IOlusoUserService userService,
        ILogger<ExternalRegisterModel> logger,
        ITenantContext? tenantContext = null)
    {
        _userService = userService;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    [BindProperty]
    public ExternalRegisterInputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? ProviderDisplayName { get; set; }
    public string? ErrorMessage { get; set; }
    public Dictionary<string, string> ValidationErrors { get; set; } = new();

    /// <summary>
    /// Shows the registration completion form with pre-filled data from external claims
    /// </summary>
    public async Task<IActionResult> OnGetAsync()
    {
        // Get external login info from the external cookie
        var info = await HttpContext.AuthenticateAsync(IdentityConstants.ExternalScheme);
        if (info?.Principal == null)
        {
            _logger.LogWarning("ExternalRegister accessed without external authentication");
            return RedirectToPage("/Account/Login");
        }

        var provider = info.Properties?.Items["LoginProvider"] ?? "External";
        ProviderDisplayName = provider;

        // Pre-fill available data from claims
        Input.Email = info.Principal.FindFirstValue(ClaimTypes.Email);
        Input.FirstName = info.Principal.FindFirstValue(ClaimTypes.GivenName);
        Input.LastName = info.Principal.FindFirstValue(ClaimTypes.Surname);

        // Try to get name if first/last not available
        if (string.IsNullOrEmpty(Input.FirstName))
        {
            var name = info.Principal.FindFirstValue("name") ?? info.Principal.FindFirstValue(ClaimTypes.Name);
            if (!string.IsNullOrEmpty(name))
            {
                var nameParts = name.Split(' ', 2);
                Input.FirstName = nameParts[0];
                Input.LastName = nameParts.Length > 1 ? nameParts[1] : null;
            }
        }

        return Page();
    }

    /// <summary>
    /// Completes registration with the provided data
    /// </summary>
    public async Task<IActionResult> OnPostAsync()
    {
        // Get external login info
        var info = await HttpContext.AuthenticateAsync(IdentityConstants.ExternalScheme);
        if (info?.Principal == null)
        {
            _logger.LogWarning("ExternalRegister POST without external authentication");
            return RedirectToPage("/Account/Login");
        }

        var provider = info.Properties?.Items["LoginProvider"] ?? "External";
        ProviderDisplayName = provider;

        var providerKey = info.Principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(providerKey))
        {
            ErrorMessage = "Invalid external login - no identifier found";
            return Page();
        }

        // Validate input
        if (string.IsNullOrWhiteSpace(Input.Email))
        {
            ValidationErrors["email"] = "Email address is required";
        }
        else if (!IsValidEmail(Input.Email))
        {
            ValidationErrors["email"] = "Please enter a valid email address";
        }

        if (ValidationErrors.Any())
        {
            return Page();
        }

        // Check if email already exists
        var existingUser = await _userService.FindByEmailAsync(Input.Email!);
        if (existingUser != null)
        {
            // Email exists - offer to link instead
            ValidationErrors["email"] = "An account with this email already exists. Please sign in to link your external account.";
            return Page();
        }

        // Check if external login already linked to another user
        var existingLogin = await _userService.FindByExternalLoginAsync(provider, providerKey);
        if (existingLogin != null)
        {
            ErrorMessage = "This external account is already linked to another user.";
            return Page();
        }

        _logger.LogDebug("Creating user from external login: provider={Provider}, email={Email}", provider, Input.Email);

        // Create user with external login
        var createResult = await _userService.CreateUserFromExternalLoginAsync(new CreateExternalUserRequest
        {
            Email = Input.Email!,
            FirstName = Input.FirstName,
            LastName = Input.LastName,
            Provider = provider,
            ProviderKey = providerKey,
            EmailConfirmed = true // Trust external provider
        });

        if (!createResult.Succeeded)
        {
            _logger.LogWarning("Failed to create user from external login: {Errors}",
                string.Join(", ", createResult.Errors ?? Array.Empty<string>()));
            ErrorMessage = string.Join(". ", createResult.Errors ?? new[] { "Failed to create account" });
            return Page();
        }

        _logger.LogInformation("User {UserId} registered via external provider {Provider}", createResult.UserId, provider);

        // Sign in the new user
        await SignInUserAsync(createResult.UserId!);

        // Clear external cookie
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

        // Redirect to return URL
        if (!string.IsNullOrEmpty(ReturnUrl) && IsAllowedReturnUrl(ReturnUrl))
        {
            return Redirect(ReturnUrl);
        }

        return Redirect("/");
    }

    private async Task SignInUserAsync(string userId)
    {
        var user = await _userService.FindByIdAsync(userId);
        if (user == null) return;

        var claims = (await _userService.GetClaimsAsync(userId)).ToList();

        // Add tenant_id claim if in a tenant context
        if (_tenantContext?.HasTenant == true && !string.IsNullOrEmpty(_tenantContext.TenantId))
        {
            claims.RemoveAll(c => c.Type == "tenant_id");
            claims.Add(new Claim("tenant_id", _tenantContext.TenantId));
        }

        var identity = new ClaimsIdentity(claims, "Oluso");
        var principal = new ClaimsPrincipal(identity);

        await HttpContext.SignInAsync(IdentityConstants.ApplicationScheme, principal);
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
        if (Url.IsLocalUrl(url))
        {
            return true;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var request = HttpContext.Request;
            return string.Equals(uri.Host, request.Host.Host, StringComparison.OrdinalIgnoreCase)
                && uri.Port == (request.Host.Port ?? (request.IsHttps ? 443 : 80));
        }

        return false;
    }
}

public class ExternalRegisterInputModel
{
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
}

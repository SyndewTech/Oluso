using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
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
    private readonly IExternalAuthService? _externalAuthService;
    private readonly ITenantContext? _tenantContext;
    private readonly ILogger<ExternalCallbackModel> _logger;

    public ExternalCallbackModel(
        IOlusoUserService userService,
        ILogger<ExternalCallbackModel> logger,
        IExternalAuthService? externalAuthService = null,
        ITenantContext? tenantContext = null)
    {
        _userService = userService;
        _externalAuthService = externalAuthService;
        _tenantContext = tenantContext;
        _logger = logger;
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

        // Check if provider is configured for auto-provisioning (JIT user creation)
        // This is common for enterprise SSO where users should be created on first login
        var autoProvision = false;
        if (_externalAuthService != null)
        {
            // For SAML providers, the scheme is "Saml.{idpName}" but stored in DB as just "{idpName}"
            var lookupScheme = provider;
            if (provider.StartsWith("Saml.", StringComparison.OrdinalIgnoreCase))
            {
                lookupScheme = provider.Substring(5); // Strip "Saml." prefix
            }

            var providerConfig = await _externalAuthService.GetProviderConfigAsync(lookupScheme);
            autoProvision = providerConfig?.AutoProvisionUsers ?? false;
            _logger.LogDebug("Provider {Provider} (lookup: {LookupScheme}) config: AutoProvisionUsers={AutoProvision}",
                provider, lookupScheme, autoProvision);
        }

        // Create user if: explicit registration flow OR provider has auto-provisioning enabled
        var shouldCreateUser = isRegistration || autoProvision;

        _logger.LogDebug("Processing external callback from {Provider}, IsRegistration: {IsRegistration}, AutoProvision: {AutoProvision}",
            provider, isRegistration, autoProvision);

        // Process external login - create user if this is a registration flow or auto-provision is enabled
        var externalResult = await _userService.ProcessExternalLoginAsync(
            provider,
            info.Principal,
            createIfNotExists: shouldCreateUser);

        if (!externalResult.Succeeded)
        {
            _logger.LogWarning("External login processing failed: {Error}", externalResult.Error);

            // If user not found and not auto-provisioning, OR if email is required,
            // redirect to external registration page to collect missing info
            if (externalResult.Error == "user_not_found" && !shouldCreateUser ||
                externalResult.Error == "email_required")
            {
                // DON'T clear external cookie - we need it for the registration page
                _logger.LogDebug("Redirecting to external registration page for {Provider}: {Error}", provider, externalResult.Error);
                return Redirect($"/account/external-register?returnUrl={Uri.EscapeDataString(returnUrl)}");
            }

            // Clear external cookie before redirecting to error page
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

            var redirectPage = isRegistration ? "/account/register" : "/account/login";
            return RedirectWithError(redirectPage, externalResult.Error ?? "External login failed", returnUrl);
        }

        // Sign in the user with the application cookie
        await SignInUserAsync(externalResult.UserId!);

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

    private async Task SignInUserAsync(string userId)
    {
        var user = await _userService.FindByIdAsync(userId);
        if (user == null) return;

        var claims = (await _userService.GetClaimsAsync(userId)).ToList();

        // Add tenant_id claim if in a tenant context
        if (_tenantContext?.HasTenant == true && !string.IsNullOrEmpty(_tenantContext.TenantId))
        {
            claims.RemoveAll(c => c.Type == "tenant_id");
            claims.Add(new System.Security.Claims.Claim("tenant_id", _tenantContext.TenantId));
        }

        var identity = new System.Security.Claims.ClaimsIdentity(claims, "Oluso");
        var principal = new System.Security.Claims.ClaimsPrincipal(identity);

        var authProperties = new AuthenticationProperties
        {
            IsPersistent = false,
            ExpiresUtc = null
        };

        await HttpContext.SignInAsync(IdentityConstants.ApplicationScheme, principal, authProperties);
    }

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

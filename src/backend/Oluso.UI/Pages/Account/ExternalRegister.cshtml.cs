using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
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
    private readonly ISignInAuditor? _signInAuditor;
    private readonly ILogger<ExternalRegisterModel> _logger;
    private readonly IProtocolStateStore? _protocolStateStore;

    public ExternalRegisterModel(
        IOlusoUserService userService,
        ITenantEmailDomainStore emailDomains,
        ILogger<ExternalRegisterModel> logger,
        ITenantContext? tenantContext = null,
        ISignInAuditor? signInAuditor = null,
        IProtocolStateStore? protocolStateStore = null,
        IExternalProviderConfigStore? providers = null)
    {
        _userService = userService;
        _emailDomains = emailDomains;
        _providers = providers;
        _tenantContext = tenantContext;
        _logger = logger;
        _signInAuditor = signInAuditor;
        _protocolStateStore = protocolStateStore;
    }

    [BindProperty]
    public ExternalRegisterInputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? ProviderDisplayName { get; set; }
    public string? ErrorMessage { get; set; }
    public Dictionary<string, string> ValidationErrors { get; set; } = new();

    /// <summary>
    /// True when the IdP supplied an email claim. The view uses this to
    /// render the email input as readonly so the user can't silently swap
    /// it for a different address before account creation.
    /// </summary>
    public bool EmailFromIdp { get; set; }

    /// <summary>
    /// Tenant registration policy mirrored on the page model so the view +
    /// validator share one source of truth — same pattern as
    /// <c>Register.cshtml.cs</c>. Defaults match a permissive tenant.
    /// </summary>
    public bool RequireTermsAcceptance { get; set; }
    public string? TermsUrl { get; set; }
    public string? PrivacyUrl { get; set; }

    /// <summary>
    /// True when the IdP itself supplied the email claim (so we can mark
    /// it as confirmed). False when the user typed it in this form — the
    /// IdP didn't verify that address, so it should still go through the
    /// tenant's email-verification flow when one is required.
    /// </summary>
    private bool _emailFromIdp;
    private readonly ITenantEmailDomainStore _emailDomains;
    private readonly IExternalProviderConfigStore? _providers;

    private void BindTenantRegistrationSettings()
    {
        var tenant = _tenantContext?.Tenant;
        if (tenant == null) return;
        RequireTermsAcceptance = tenant.RequireTermsAcceptance;
        TermsUrl = tenant.TermsOfServiceUrl;
        PrivacyUrl = tenant.PrivacyPolicyUrl;
    }

    /// <summary>
    /// Shows the registration completion form with pre-filled data from external claims
    /// </summary>
    public async Task<IActionResult> OnGetAsync()
    {
        // Check if self-registration is allowed for this tenant
        if (_tenantContext?.Tenant?.AllowSelfRegistration == false)
        {
            _logger.LogWarning("External registration blocked - self-registration is disabled for tenant");
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
            return RedirectToPage("/Account/Login", new { error = "registration_disabled" });
        }

        BindTenantRegistrationSettings();

        // Get external login info from the external cookie
        var info = await HttpContext.AuthenticateAsync(IdentityConstants.ExternalScheme);
        if (info?.Principal == null)
        {
            _logger.LogWarning("ExternalRegister accessed without external authentication");
            return RedirectToPage("/Account/Login");
        }

        var provider = info.Properties?.Items["LoginProvider"] ?? "External";
        ProviderDisplayName = provider;

        // Pre-fill available data from claims. The IdP-supplied flag is what
        // gates EmailConfirmed at create-time: if we read the email from the
        // external claims here, the IdP attests to it. If the user types it
        // in the form (because the IdP didn't supply one — the whole reason
        // we're on this page), the IdP attests to nothing.
        Input.Email = info.Principal.FindFirstValue(ClaimTypes.Email);
        _emailFromIdp = !string.IsNullOrEmpty(Input.Email);
        EmailFromIdp = _emailFromIdp;
        Input.FirstName = info.Principal.FindFirstValue(ClaimTypes.GivenName);
        Input.LastName = info.Principal.FindFirstValue(ClaimTypes.Surname);

        // Try to get name if first/last not available. Skip when the name
        // claim is just the email (some IdPs default `name` to the email
        // when no display name is set) — otherwise we'd shove the email
        // into the First Name field.
        if (string.IsNullOrEmpty(Input.FirstName))
        {
            var name = info.Principal.FindFirstValue("name") ?? info.Principal.FindFirstValue(ClaimTypes.Name);
            if (!string.IsNullOrEmpty(name) && !LooksLikeEmail(name))
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
        // Check if self-registration is allowed for this tenant
        if (_tenantContext?.Tenant?.AllowSelfRegistration == false)
        {
            _logger.LogWarning("External registration POST blocked - self-registration is disabled for tenant");
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
            return RedirectToPage("/Account/Login", new { error = "registration_disabled" });
        }

        BindTenantRegistrationSettings();

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

        // The IdP-attested-email flag has to be re-derived from the external
        // claims on POST too — page model state isn't preserved across the
        // round-trip. When the IdP supplied an email, that's the only one
        // we'll accept on POST: the form renders it readonly and we ignore
        // any tampered value rather than silently treating an edited email
        // as user-typed (which would let an attacker register a different
        // address while bypassing the IdP attestation).
        var idpEmail = info.Principal.FindFirstValue(ClaimTypes.Email);
        if (!string.IsNullOrEmpty(idpEmail))
        {
            Input.Email = idpEmail;
            _emailFromIdp = true;
            EmailFromIdp = true;
        }
        else
        {
            _emailFromIdp = false;
            EmailFromIdp = false;
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
        else if (!IsEmailDomainAllowed(Input.Email))
        {
            // Tenant allowlist applies to SSO too — admin's enterprise IdP
            // doesn't override "only @acme.com employees" rules.
            ValidationErrors["email"] = "Registrations from this email domain are not allowed.";
        }

        if (RequireTermsAcceptance && !Input.AcceptTerms)
        {
            ValidationErrors["acceptTerms"] = "You must accept the terms to continue";
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

        // Create user with external login. EmailConfirmed only when the IdP supplied the email and can vouch for
        // it (ExternalEmailTrust): an address typed into this fallback form proves nothing, and neither does one an
        // IdP lets its users set freely. Otherwise the tenant's email-verification flow confirms it.
        var emailConfirmed = _emailFromIdp && await ExternalEmailTrust.IsTrustedAsync(
            provider,
            type => info.Principal.FindFirstValue(type),
            Input.Email,
            _tenantContext?.TenantId,
            _providers,
            _emailDomains);

        var createResult = await _userService.CreateUserFromExternalLoginAsync(new CreateExternalUserRequest
        {
            Email = Input.Email!,
            FirstName = Input.FirstName,
            LastName = Input.LastName,
            Provider = provider,
            ProviderKey = providerKey,
            EmailConfirmed = emailConfirmed,
            ClientId = await AuthFlowGate.GetClientIdAsync(_protocolStateStore, ReturnUrl)
        });

        if (!createResult.Succeeded)
        {
            _logger.LogWarning("Failed to create user from external login: {Errors}",
                string.Join(", ", createResult.Errors ?? Array.Empty<string>()));
            ErrorMessage = createResult.MessageOr("Failed to create account");
            return Page();
        }

        _logger.LogInformation("User {UserId} registered via external provider {Provider}", createResult.UserId, provider);

        // Sign in the new user
        await SignInUserAsync(createResult.UserId!);
        await _userService.RecordLoginAsync(createResult.UserId!);

        if (_signInAuditor != null)
        {
            await _signInAuditor.RecordSuccessAsync(new SignInAudit
            {
                SubjectId = createResult.UserId!,
                Username = createResult.UserId!,
                TenantId = _tenantContext?.HasTenant == true ? _tenantContext.TenantId : null,
                AuthenticationMethod = provider.StartsWith("Saml.", StringComparison.OrdinalIgnoreCase) ? "saml" : provider.ToLowerInvariant(),
                ReturnUrl = ReturnUrl
            });
        }

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

    private static bool LooksLikeEmail(string value)
        => value.Contains('@') && !value.Contains(' ');

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
    /// Mirrors the AllowedEmailDomains check in <c>Register.cshtml.cs</c>.
    /// SSO-via-form has to honour the same allowlist — the admin's enterprise
    /// IdP doesn't get to override "only listed domains may register".
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
    public bool AcceptTerms { get; set; }
}

using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Core.UserJourneys;
using Oluso.Protocols.Device;

namespace Oluso.UI.Pages.Account;

/// <summary>
/// The device authorization grant's verification page (RFC 8628 §3.3), the issuer's <c>verification_uri</c>
/// (<c>/device</c>) and <c>verification_uri_complete</c> (<c>/device?user_code=…</c>). The user signs in here
/// (through the tenant's usual sign-in, journey or standard pages) if they aren't already, checks the code and
/// what the other device asks for, and approves or denies. The device's token request then gets tokens or
/// <c>access_denied</c>.
/// </summary>
public class DeviceModel : PageModel
{
    /// <summary>The protocol name the sign-in runs under; its callback is <c>/device/callback</c>.</summary>
    public const string ProtocolName = "device";

    /// <summary>Protocol-state property holding where to come back to after sign-in.</summary>
    public const string ReturnPathProperty = "DeviceReturnPath";

    private readonly IDeviceVerificationService _verification;
    private readonly IAuthenticationCoordinator _coordinator;
    private readonly IClientStore _clientStore;
    private readonly ITenantContext _tenantContext;

    public DeviceModel(
        IDeviceVerificationService verification,
        IAuthenticationCoordinator coordinator,
        IClientStore clientStore,
        ITenantContext tenantContext)
    {
        _verification = verification;
        _coordinator = coordinator;
        _clientStore = clientStore;
        _tenantContext = tenantContext;
    }

    [BindProperty(SupportsGet = true, Name = "user_code")]
    public string? UserCode { get; set; }

    public DeviceVerificationLookup? Request { get; private set; }

    public string? UserDisplay { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var principal = await SignedInUserAsync();
        if (principal == null)
        {
            return await StartSignInAsync();
        }

        UserDisplay = Display(principal.Principal!);
        if (string.IsNullOrWhiteSpace(UserCode))
        {
            return Page();
        }

        Request = await _verification.FindAsync(UserCode, HttpContext.RequestAborted);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? button)
    {
        var signedIn = await SignedInUserAsync();
        if (signedIn == null)
        {
            return await StartSignInAsync();
        }

        var principal = signedIn.Principal!;
        UserDisplay = Display(principal);
        var subjectId = SubjectId(principal)!;

        if (button == "approve")
        {
            Request = await _verification.ApproveAsync(UserCode, new DeviceApproval
            {
                SubjectId = subjectId,
                SessionId = principal.FindFirst("sid")?.Value,
                Claims = SessionClaims(principal, signedIn.Properties),
                Amr = principal.FindAll("amr").Select(c => c.Value).Distinct(StringComparer.Ordinal).ToList(),
                Acr = principal.FindFirst("acr")?.Value,
            }, HttpContext.RequestAborted);
        }
        else if (button == "deny")
        {
            Request = await _verification.DenyAsync(UserCode, subjectId, HttpContext.RequestAborted);
        }
        else
        {
            Request = await _verification.FindAsync(UserCode, HttpContext.RequestAborted);
        }

        return Page();
    }

    /// <summary>The signed-in session, or null when there is none (or it has no user id).</summary>
    private async Task<AuthenticateResult?> SignedInUserAsync()
    {
        var result = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        return result.Succeeded && result.Principal != null && SubjectId(result.Principal) != null ? result : null;
    }

    /// <summary>
    /// Sends the user through the tenant's sign-in and back to this page (with the code they followed), the same
    /// way an authorization request does: a journey when the tenant (or the device's client) uses journeys, else
    /// the standard pages.
    /// </summary>
    private async Task<IActionResult> StartSignInAsync()
    {
        var returnPath = string.IsNullOrWhiteSpace(UserCode)
            ? "/device"
            : $"/device?user_code={Uri.EscapeDataString(DeviceVerificationService.NormalizeUserCode(UserCode))}";

        var context = new ProtocolContext
        {
            HttpContext = HttpContext,
            ProtocolName = ProtocolName,
            EndpointType = EndpointType.Authorize,
        };
        context.Properties[ReturnPathProperty] = returnPath;

        var tenant = _tenantContext.Tenant;
        if (tenant != null)
        {
            context.Properties["TenantId"] = tenant.Id;
            context.Properties["TenantUseJourneyFlow"] = tenant.UseJourneyFlow;
            context.Properties["TenantJourneyFallbackToStandard"] = tenant.JourneyFallbackToStandard;
        }

        // The device's client may opt out of journeys, as it would for its own authorization requests.
        var lookup = string.IsNullOrWhiteSpace(UserCode) ? null : await _verification.FindAsync(UserCode, HttpContext.RequestAborted);
        if (lookup?.ClientId != null)
        {
            context.Properties["ClientId"] = lookup.ClientId;
            var client = await _clientStore.FindClientByIdAsync(lookup.ClientId, HttpContext.RequestAborted);
            if (client?.UseJourneyFlow is bool useJourney)
            {
                context.Properties["ClientUseJourneyFlow"] = useJourney;
            }
        }

        return await _coordinator.StartAuthenticationAsync(
            context,
            new AuthenticationRequirement { SuggestedPolicyType = JourneyType.SignIn },
            HttpContext.RequestAborted);
    }

    private static string? SubjectId(ClaimsPrincipal principal) =>
        principal.FindFirst("sub")?.Value ?? principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    private static string? Display(ClaimsPrincipal principal) =>
        principal.FindFirst("email")?.Value ?? principal.FindFirst(ClaimTypes.Email)?.Value ?? principal.Identity?.Name;

    /// <summary>What describes the session, as an authorization code carries it: idp and remember-me.</summary>
    private static IReadOnlyDictionary<string, string> SessionClaims(ClaimsPrincipal principal, AuthenticationProperties? properties)
    {
        var claims = new Dictionary<string, string>();
        if (principal.FindFirst("idp")?.Value is { Length: > 0 } idp)
        {
            claims["idp"] = idp;
        }
        if (properties?.IsPersistent == true)
        {
            claims[Oluso.Core.Authorization.ReservedClaimKeys.RememberMe] = "true";
        }
        return claims;
    }
}

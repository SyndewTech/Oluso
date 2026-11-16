using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Oluso.Core.Domain.Interfaces;
using Oluso.Protocols.Oidc;

namespace Oluso.UI.Pages.Account;

/// <summary>
/// Asks the user to confirm signing out (OpenID Connect RP-Initiated Logout 1.0 section 2) when the end session
/// endpoint could not tell from a verified id_token_hint that the signed-in user asked for it. The page only
/// shows the request; confirming posts it back to the end session endpoint with an anti-forgery token, which
/// signs the user out.
/// </summary>
public class SignOutModel : PageModel
{
    private readonly IClientStore _clientStore;
    private readonly ITimeLimitedDataProtector _pendingLogouts;

    public SignOutModel(IClientStore clientStore, IDataProtectionProvider dataProtectionProvider)
    {
        _clientStore = clientStore;
        _pendingLogouts = dataProtectionProvider
            .CreateProtector(OidcEndSessionController.LogoutRequestPurpose)
            .ToTimeLimitedDataProtector();
    }

    /// <summary>The protected logout request, posted back as is.</summary>
    public string? LogoutId { get; private set; }

    /// <summary>The application that asked for the sign-out, when it is known.</summary>
    public string? ClientName { get; private set; }

    /// <summary>Where confirming posts to.</summary>
    public string EndSessionPath { get; private set; } = "/connect/endsession";

    public bool Expired { get; private set; }

    public async Task<IActionResult> OnGetAsync(
        [FromQuery(Name = OidcEndSessionController.LogoutIdField)] string? logoutId,
        [FromServices] Microsoft.Extensions.Options.IOptions<Oluso.Core.Protocols.OidcEndpointConfiguration> endpoints)
    {
        EndSessionPath = endpoints.Value.EndSessionEndpoint;

        OidcEndSessionController.PendingLogout? pending = null;
        if (!string.IsNullOrEmpty(logoutId))
        {
            try
            {
                pending = JsonSerializer.Deserialize<OidcEndSessionController.PendingLogout>(_pendingLogouts.Unprotect(logoutId));
            }
            catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or JsonException or FormatException)
            {
                pending = null;
            }
        }

        if (pending == null)
        {
            Expired = true;
            return Page();
        }

        LogoutId = logoutId;
        if (!string.IsNullOrEmpty(pending.ClientId))
        {
            var client = await _clientStore.FindClientByIdAsync(pending.ClientId, HttpContext.RequestAborted);
            ClientName = client?.ClientName ?? client?.ClientId;
        }

        return Page();
    }
}

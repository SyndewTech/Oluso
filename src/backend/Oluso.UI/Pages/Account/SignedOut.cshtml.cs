using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Oluso.Protocols.Oidc;

namespace Oluso.UI.Pages.Account;

/// <summary>
/// Front-Channel Logout 1.0: shown once the user is signed out, it loads each front-channel logout URI of the
/// clients that took part in the session in a hidden iframe, so they can end their own sessions, then continues to
/// the client's post_logout_redirect_uri (or the default signed-out page). Its content comes from the end session
/// endpoint, protected, so the page frames nothing and sends the user nowhere that endpoint did not validate.
/// </summary>
public class SignedOutModel : PageModel
{
    /// <summary>How long the page waits for the frames before going on regardless.</summary>
    public const int FrameTimeoutMilliseconds = 4000;

    private readonly ITimeLimitedDataProtector _signedOutPages;

    public SignedOutModel(IDataProtectionProvider dataProtectionProvider)
    {
        _signedOutPages = dataProtectionProvider
            .CreateProtector(OidcEndSessionController.SignedOutPurpose)
            .ToTimeLimitedDataProtector();
    }

    public IReadOnlyList<string> Frames { get; private set; } = Array.Empty<string>();

    public string Continue { get; private set; } = OidcEndSessionController.DefaultSignedOutPath;

    public IActionResult OnGet([FromQuery(Name = OidcEndSessionController.LogoutIdField)] string? logoutId)
    {
        OidcEndSessionController.SignedOutPage? page = null;
        if (!string.IsNullOrEmpty(logoutId))
        {
            try
            {
                page = JsonSerializer.Deserialize<OidcEndSessionController.SignedOutPage>(_signedOutPages.Unprotect(logoutId));
            }
            catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or JsonException or FormatException)
            {
                page = null;
            }
        }

        if (page == null)
        {
            // Nothing (left) to load: the user is signed out either way.
            return Redirect(OidcEndSessionController.DefaultSignedOutPath);
        }

        Frames = page.Frames.Where(IsWebUrl).ToList();
        // A relative path of this site, or an http(s) URL: never another scheme (javascript:, data:).
        Continue = page.Continue.StartsWith('/') && !page.Continue.StartsWith("//", StringComparison.Ordinal) || IsWebUrl(page.Continue)
            ? page.Continue
            : OidcEndSessionController.DefaultSignedOutPath;

        // Only the clients' own origins may be framed by this page (frame-src). ActiveContentHeadersMiddleware writes
        // every HTML page's policy unless the page sets one, and it has no frame-src; so this page sets the whole
        // policy, keeping the middleware's framing rule for it (frame-ancestors 'self').
        var origins = Frames
            .Select(f => Uri.TryCreate(f, UriKind.Absolute, out var u) ? u.GetLeftPart(UriPartial.Authority) : null)
            .Where(o => o != null)
            .Distinct();
        Response.Headers["Content-Security-Policy"] = $"frame-src {string.Join(' ', origins)}; frame-ancestors 'self'";
        Response.Headers.CacheControl = "no-store";

        return Page();
    }

    private static bool IsWebUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
}

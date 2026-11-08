using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Oluso.Authentication.OrganizationSignIn;

namespace Oluso.UI.Pages.Account;

/// <summary>
/// Platform host: "Sign in with your organisation". The user names their
/// organisation (tenant identifier) and is sent to that tenant's own login.
/// Reached from a standalone platform login (<c>returnUrl</c> = the authorize
/// callback) or a journey platform login (<c>journey</c> = the journey to resume);
/// never navigable outside an in-flight protocol flow.
/// </summary>
public class OrgSignInModel : PageModel
{
    private readonly IOrganizationSignInService? _organizationSignIn;

    public OrgSignInModel(IOrganizationSignInService? organizationSignIn = null)
    {
        _organizationSignIn = organizationSignIn;
    }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    [BindProperty(SupportsGet = true, Name = "journey")]
    public string? JourneyId { get; set; }

    [BindProperty]
    public string? Identifier { get; set; }

    /// <summary>Localization key of the error to show, if any.</summary>
    public string? ErrorKey { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (!await IsAvailableAsync()) return NotFound();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!await IsAvailableAsync()) return NotFound();

        var result = await _organizationSignIn!.BeginAsync(HttpContext, Identifier, ReturnUrl, JourneyId, HttpContext.RequestAborted);
        if (result.Succeeded)
        {
            return Redirect(result.RedirectUrl!);
        }

        ErrorKey = result.Error switch
        {
            OrganizationSignInErrors.TooManyAttempts => "org_signin_error_too_many",
            OrganizationSignInErrors.NotOffered or OrganizationSignInErrors.InvalidReturnUrl => null,
            _ => "org_signin_error_generic"
        };
        return ErrorKey == null ? NotFound() : Page();
    }

    private async Task<bool> IsAvailableAsync() =>
        _organizationSignIn != null
        && await _organizationSignIn.CanBeginAsync(HttpContext, ReturnUrl, JourneyId, HttpContext.RequestAborted);
}

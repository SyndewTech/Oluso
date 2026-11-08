using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Oluso.Authentication.OrganizationSignIn;

namespace Oluso.UI.Pages.Account;

/// <summary>
/// Platform host: an organisation sign-in came back after its pending request
/// (and the authorize request it resumes) expired. Offers to start the original
/// authorize request again, but only to the browser that started it.
/// </summary>
public class OrgSignInExpiredModel : PageModel
{
    private readonly IOrganizationSignInService? _organizationSignIn;

    public OrgSignInExpiredModel(IOrganizationSignInService? organizationSignIn = null)
    {
        _organizationSignIn = organizationSignIn;
    }

    [BindProperty(SupportsGet = true, Name = "request")]
    public string? RequestId { get; set; }

    public string? RestartUrl { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (_organizationSignIn == null || !_organizationSignIn.IsOffered(HttpContext)) return NotFound();

        var restart = await _organizationSignIn.TakeRestartUrlAsync(HttpContext, RequestId, HttpContext.RequestAborted);
        RestartUrl = restart != null && Url.IsLocalUrl(restart) ? restart : null;
        return Page();
    }
}

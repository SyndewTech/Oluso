using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Oluso.UI.Pages.Journey;

/// <summary>
/// Terminal page shown to the user after a successful magic-link click.
/// Renders on the device that opened the email; the device that originally
/// started the journey advances on its own once its status poll detects
/// the step has completed.
/// </summary>
public class VerificationCompleteModel : PageModel
{
    public void OnGet()
    {
    }
}

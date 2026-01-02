using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Oluso.Core.Services;

namespace Oluso.UI.Pages.Account;

/// <summary>
/// Account security settings page - manage passkeys and other security options
/// </summary>
[Authorize]
public class SecurityModel : PageModel
{
    private readonly IFido2Service? _fido2Service;
    private readonly IOlusoUserService _userService;
    private readonly ILogger<SecurityModel> _logger;

    public SecurityModel(
        IOlusoUserService userService,
        ILogger<SecurityModel> logger,
        IFido2Service? fido2Service = null)
    {
        _userService = userService;
        _fido2Service = fido2Service;
        _logger = logger;
    }

    public IEnumerable<Fido2Credential> Passkeys { get; set; } = [];
    public bool PasskeysEnabled => _fido2Service != null;
    public string? ErrorMessage { get; set; }
    public string? SuccessMessage { get; set; }

    // Registration state
    public Fido2RegistrationViewModel? RegistrationViewModel { get; set; }

    public async Task<IActionResult> OnGetAsync()
    {
        await LoadPasskeysAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostRegisterPasskeyAsync(string? authenticatorType)
    {
        if (_fido2Service == null)
        {
            ErrorMessage = "Passkey support is not available";
            return Page();
        }

        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var user = await _userService.FindByIdAsync(userId);
        if (user == null)
        {
            return Unauthorized();
        }

        try
        {
            var options = await _fido2Service.CreateRegistrationOptionsAsync(
                user.Id,
                user.Username ?? user.Email ?? user.Id,
                user.DisplayName ?? user.Username ?? user.Email ?? "User",
                authenticatorType,
                requireResidentKey: true);

            // Store in session for verification
            HttpContext.Session.SetString("fido2.registrationId", options.RegistrationId);

            RegistrationViewModel = new Fido2RegistrationViewModel
            {
                Options = options,
                RegistrationId = options.RegistrationId
            };

            await LoadPasskeysAsync();
            return Page();
        }
        catch (Fido2Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create registration options");
            ErrorMessage = ex.Message;
            await LoadPasskeysAsync();
            return Page();
        }
    }

    public async Task<IActionResult> OnPostCompleteRegistrationAsync(string registrationId, string attestationResponse)
    {
        if (_fido2Service == null)
        {
            ErrorMessage = "Passkey support is not available";
            return Page();
        }

        // Verify the registration ID matches what we stored
        var storedId = HttpContext.Session.GetString("fido2.registrationId");
        if (string.IsNullOrEmpty(storedId) || storedId != registrationId)
        {
            ErrorMessage = "Registration session expired. Please try again.";
            await LoadPasskeysAsync();
            return Page();
        }

        try
        {
            var result = await _fido2Service.VerifyRegistrationAsync(registrationId, attestationResponse);

            HttpContext.Session.Remove("fido2.registrationId");

            if (!result.Succeeded)
            {
                ErrorMessage = result.ErrorDescription ?? "Passkey registration failed";
                await LoadPasskeysAsync();
                return Page();
            }

            SuccessMessage = "Passkey registered successfully!";
            _logger.LogInformation("Passkey registered for user");
        }
        catch (Fido2Exception ex)
        {
            _logger.LogError(ex, "Passkey registration error");
            ErrorMessage = ex.Message;
        }

        await LoadPasskeysAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostDeletePasskeyAsync(string credentialId)
    {
        if (_fido2Service == null)
        {
            ErrorMessage = "Passkey support is not available";
            return Page();
        }

        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var success = await _fido2Service.DeleteCredentialAsync(userId, credentialId);

        if (success)
        {
            SuccessMessage = "Passkey deleted successfully";
            _logger.LogInformation("Passkey {CredentialId} deleted", credentialId);
        }
        else
        {
            ErrorMessage = "Failed to delete passkey";
        }

        await LoadPasskeysAsync();
        return Page();
    }

    private async Task LoadPasskeysAsync()
    {
        if (_fido2Service == null)
        {
            Passkeys = [];
            return;
        }

        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? User.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            Passkeys = [];
            return;
        }

        Passkeys = await _fido2Service.GetCredentialsAsync(userId);
    }
}

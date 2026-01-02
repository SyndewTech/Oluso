using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;

namespace Oluso.UI.Pages.Account;

/// <summary>
/// Standalone forgot password page
/// </summary>
public class ForgotPasswordModel : PageModel
{
    private readonly IOlusoUserService _userService;
    private readonly IMessagingService? _messagingService;
    private readonly ILogger<ForgotPasswordModel> _logger;

    public ForgotPasswordModel(
        IOlusoUserService userService,
        ILogger<ForgotPasswordModel> logger,
        IMessagingService? messagingService = null)
    {
        _userService = userService;
        _messagingService = messagingService;
        _logger = logger;
    }

    [BindProperty]
    public ForgotPasswordInputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? ErrorMessage { get; set; }
    public Dictionary<string, string> ValidationErrors { get; set; } = new();
    public bool EmailSent { get; set; }
    public JourneyUiConfiguration? UiConfig { get; set; }

    public IActionResult OnGet()
    {
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        // Validate input
        if (string.IsNullOrWhiteSpace(Input.Email))
        {
            ValidationErrors["email"] = "Email address is required";
            return Page();
        }

        if (!IsValidEmail(Input.Email))
        {
            ValidationErrors["email"] = "Please enter a valid email address";
            return Page();
        }

        _logger.LogDebug("Password reset requested for email {Email}", Input.Email);

        // Find user - but don't reveal if user exists or not
        var user = await _userService.FindByEmailAsync(Input.Email);

        if (user != null && _messagingService != null)
        {
            // Generate reset token
            var token = await _userService.GeneratePasswordResetTokenAsync(user.Id);

            // Build reset URL
            var request = HttpContext.Request;
            var resetUrl = $"{request.Scheme}://{request.Host}/account/reset-password?token={Uri.EscapeDataString(token)}&email={Uri.EscapeDataString(Input.Email)}";

            // Send email
            await _messagingService.SendEmailAsync(
                Input.Email,
                "Reset Your Password",
                $"Click the link below to reset your password:\n\n{resetUrl}\n\nThis link will expire in 1 hour.\n\nIf you didn't request this, please ignore this email.");

            _logger.LogInformation("Password reset email sent to {Email}", Input.Email);
        }
        else
        {
            _logger.LogDebug("Password reset requested for non-existent email {Email}", Input.Email);
        }

        // Always show success to prevent email enumeration
        EmailSent = true;
        return Page();
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
}

public class ForgotPasswordInputModel
{
    public string? Email { get; set; }
}

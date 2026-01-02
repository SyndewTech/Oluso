using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Diagnostics;

namespace Oluso.UI.Pages;

/// <summary>
/// Error page model
/// </summary>
public class ErrorModel : PageModel
{
    [BindProperty(SupportsGet = true)]
    public string? Error { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? Message { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? ErrorCode { get; set; }
    public string? RequestId { get; set; }
    public bool ShowRetry { get; set; } = true;

    public void OnGet()
    {
        RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier;

        // Map common OAuth error codes to friendly messages
        ErrorCode = Error;
        if (string.IsNullOrEmpty(Message))
        {
            Message = Error switch
            {
                "access_denied" => "Access was denied. You may have cancelled the authentication or don't have permission.",
                "invalid_request" => "The request was invalid. Please try again.",
                "unauthorized_client" => "The application is not authorized to request authentication.",
                "unsupported_response_type" => "The authentication type is not supported.",
                "invalid_scope" => "The requested permissions are invalid.",
                "server_error" => "A server error occurred. Please try again later.",
                "temporarily_unavailable" => "The service is temporarily unavailable. Please try again later.",
                "login_required" => "Please sign in to continue.",
                "consent_required" => "Your consent is required to continue.",
                "interaction_required" => "User interaction is required to continue.",
                _ => Error != null ? $"An error occurred: {Error}" : "An unexpected error occurred."
            };
        }

        // Don't show retry for certain error types
        ShowRetry = Error switch
        {
            "access_denied" => false,
            "unauthorized_client" => false,
            _ => true
        };
    }
}

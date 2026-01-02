using Microsoft.AspNetCore.Http;
using Oluso.Core.Common;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Protocols.Models;

namespace Oluso.Core.Protocols.Validation;

/// <summary>
/// Result of authorize request validation including validated client and scopes
/// </summary>
public class AuthorizeValidationResult : ValidationResult
{
    public AuthorizeRequest? Request { get; set; }
    public Client? Client { get; set; }
    public ICollection<string> ValidScopes { get; set; } = new List<string>();

    /// <summary>
    /// Whether the redirect_uri has been validated against the client's registered redirect URIs.
    /// If true, errors can be safely redirected to the redirect_uri per OAuth 2.0 spec.
    /// If false, errors must be shown on the authorization server (don't redirect).
    /// </summary>
    public bool RedirectUriValidated { get; set; }

    /// <summary>
    /// The validated redirect URI (if RedirectUriValidated is true).
    /// This is the redirect_uri that can be used for error redirects.
    /// </summary>
    public string? ValidatedRedirectUri { get; set; }

    public static AuthorizeValidationResult Success(AuthorizeRequest request, Client client, ICollection<string> validScopes) => new()
    {
        Request = request,
        Client = client,
        ValidScopes = validScopes,
        RedirectUriValidated = true,
        ValidatedRedirectUri = request?.RedirectUri
    };

    /// <summary>
    /// Create a failure result. If the error occurred after redirect_uri was validated,
    /// pass redirectUriValidated=true and the validatedRedirectUri so the error can be
    /// safely redirected to the client.
    /// </summary>
    public static AuthorizeValidationResult Failure(
        string error,
        string? description = null,
        bool redirectUriValidated = false,
        string? validatedRedirectUri = null) => new()
    {
        Error = error,
        ErrorDescription = description,
        RedirectUriValidated = redirectUriValidated,
        ValidatedRedirectUri = validatedRedirectUri
    };

    public new static AuthorizeValidationResult Failure(string error, string? description = null) => new()
    {
        Error = error,
        ErrorDescription = description,
        RedirectUriValidated = false
    };
}

/// <summary>
/// Validates authorization endpoint requests
/// </summary>
public interface IAuthorizeRequestValidator
{
    Task<AuthorizeValidationResult> ValidateAsync(
        HttpRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates an already-parsed authorize request (for PAR)
    /// </summary>
    Task<AuthorizeValidationResult> ValidateAsync(
        AuthorizeRequest request,
        CancellationToken cancellationToken = default);
}

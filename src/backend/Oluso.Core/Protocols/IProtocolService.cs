using Microsoft.AspNetCore.Mvc;

namespace Oluso.Core.Protocols;

/// <summary>
/// Base interface for protocol services (OIDC, SAML, WS-Fed, etc.)
/// </summary>
public interface IProtocolService
{
    /// <summary>
    /// Protocol identifier (e.g., "oidc", "saml", "wsfed")
    /// </summary>
    string ProtocolName { get; }

    /// <summary>
    /// Build protocol-specific response after successful authentication
    /// </summary>
    Task<IActionResult> BuildAuthenticatedResponseAsync(
        ProtocolContext context,
        AuthenticationResult authResult,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Build protocol-specific error response
    /// </summary>
    IActionResult BuildErrorResponse(ProtocolContext? context, ProtocolError error);
}

/// <summary>
/// Marker interface for protocol-specific request types
/// </summary>
public interface IProtocolRequest
{
    /// <summary>
    /// Client/relying party identifier
    /// </summary>
    string? ClientId { get; }

    /// <summary>
    /// Requested policy ID (from query param or request body)
    /// </summary>
    string? PolicyId { get; }

    /// <summary>
    /// UI mode preference (journey, standalone, headless)
    /// </summary>
    string? UiMode { get; }
}

/// <summary>
/// Result of processing a protocol request
/// </summary>
public class ProtocolRequestResult
{
    public ProtocolResultType Type { get; init; }
    public AuthenticationRequirement? AuthRequirement { get; init; }
    public ConsentRequirement? ConsentRequirement { get; init; }
    public IActionResult? Response { get; init; }
    public ProtocolError? Error { get; init; }

    public static ProtocolRequestResult RequiresAuth(AuthenticationRequirement requirement) => new()
    {
        Type = ProtocolResultType.RequiresAuthentication,
        AuthRequirement = requirement
    };

    public static ProtocolRequestResult RequiresConsent(ConsentRequirement requirement) => new()
    {
        Type = ProtocolResultType.RequiresConsent,
        ConsentRequirement = requirement
    };

    public static ProtocolRequestResult Success() => new()
    {
        Type = ProtocolResultType.Success
    };

    public static ProtocolRequestResult DirectResponse(IActionResult response) => new()
    {
        Type = ProtocolResultType.DirectResponse,
        Response = response
    };

    public static ProtocolRequestResult Failed(string error, string? description = null) => new()
    {
        Type = ProtocolResultType.Failed,
        Error = new ProtocolError { Code = error, Description = description }
    };

    public static ProtocolRequestResult Failed(
        string error,
        string? description,
        bool redirectUriValidated,
        string? validatedRedirectUri) => new()
    {
        Type = ProtocolResultType.Failed,
        Error = new ProtocolError
        {
            Code = error,
            Description = description,
            RedirectUriValidated = redirectUriValidated,
            ValidatedRedirectUri = validatedRedirectUri
        }
    };

    public static ProtocolRequestResult Failed(ProtocolError error) => new()
    {
        Type = ProtocolResultType.Failed,
        Error = error
    };
}

/// <summary>
/// Describes consent requirements for a protocol request
/// </summary>
public class ConsentRequirement
{
    /// <summary>
    /// Client ID requesting consent
    /// </summary>
    public string ClientId { get; init; } = null!;

    /// <summary>
    /// Client display name
    /// </summary>
    public string? ClientName { get; init; }

    /// <summary>
    /// Scopes that need consent
    /// </summary>
    public IReadOnlyList<string> RequestedScopes { get; init; } = Array.Empty<string>();

    /// <summary>
    /// User being asked for consent
    /// </summary>
    public string SubjectId { get; init; } = null!;
}

public enum ProtocolResultType
{
    /// <summary>
    /// Request requires user authentication (journey or standalone)
    /// </summary>
    RequiresAuthentication,

    /// <summary>
    /// Request requires user consent
    /// </summary>
    RequiresConsent,

    /// <summary>
    /// Request processed successfully - ready to issue response
    /// </summary>
    Success,

    /// <summary>
    /// Direct response (token endpoint, metadata, etc.)
    /// </summary>
    DirectResponse,

    /// <summary>
    /// Request validation or processing failed
    /// </summary>
    Failed
}

/// <summary>
/// Protocol error details
/// </summary>
public class ProtocolError
{
    public string Code { get; init; } = null!;
    public string? Description { get; init; }
    public string? Uri { get; init; }

    /// <summary>
    /// Whether the redirect URI has been validated and can be trusted for error redirects.
    /// Per OAuth 2.0 spec, errors should only redirect to the client if the redirect_uri is valid.
    /// </summary>
    public bool RedirectUriValidated { get; init; }

    /// <summary>
    /// The validated redirect URI to use for error redirects (if RedirectUriValidated is true).
    /// </summary>
    public string? ValidatedRedirectUri { get; init; }
}

using Microsoft.AspNetCore.Mvc;

namespace Oluso.Core.Protocols;

/// <summary>
/// Coordinates authentication between protocol services and UI (journey or standalone)
/// </summary>
public interface IAuthenticationCoordinator
{
    /// <summary>
    /// Starts the authentication process - routes to journey, standalone, or returns headless response
    /// </summary>
    Task<IActionResult> StartAuthenticationAsync(
        ProtocolContext context,
        AuthenticationRequirement requirement,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Handles callback from journey or standalone authentication.
    /// For standalone flow, the user must be authenticated via cookie.
    /// </summary>
    /// <param name="correlationId">Protocol correlation ID</param>
    /// <param name="journeyId">Journey ID (for journey flow, null for standalone)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task<AuthenticationResult> HandleCallbackAsync(
        string correlationId,
        string? journeyId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the UI mode for a given context (request > client > tenant > default)
    /// </summary>
    UiMode ResolveUiMode(ProtocolContext context);

    /// <summary>
    /// Handles consent requirement - routes to journey with consent step or standalone consent page.
    /// In journey mode, this starts/continues a journey that includes consent and any post-consent steps.
    /// </summary>
    Task<IActionResult> HandleConsentRequiredAsync(
        ProtocolContext context,
        ConsentRequirement requirement,
        CancellationToken cancellationToken = default);
}

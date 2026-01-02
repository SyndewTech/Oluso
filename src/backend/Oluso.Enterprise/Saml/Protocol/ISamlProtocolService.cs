using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Oluso.Core.Protocols;

namespace Oluso.Enterprise.Saml.Protocol;

/// <summary>
/// SAML protocol service for handling SAML SSO flows.
/// Implements both SP (consuming IdPs) and IdP (issuing assertions) roles.
/// </summary>
public interface ISamlProtocolService : IProtocolService
{
    /// <summary>
    /// Builds context from an incoming SAML request (IdP mode)
    /// </summary>
    Task<ProtocolContext> BuildContextFromRequestAsync(HttpContext httpContext, CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes a SAML AuthnRequest when acting as IdP
    /// </summary>
    Task<ProtocolResult> ProcessAuthnRequestAsync(ProtocolContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Builds an authenticated SAML response when acting as IdP
    /// </summary>
    Task<IActionResult> BuildSamlResponseAsync(ProtocolContext context, AuthenticationResult authResult, CancellationToken cancellationToken = default);

    /// <summary>
    /// Initiates SAML authentication with an external IdP (SP mode)
    /// </summary>
    Task<IActionResult> InitiateExternalAuthAsync(string idpName, string returnUrl, string correlationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Processes a SAML response from an external IdP (SP mode)
    /// </summary>
    Task<SamlExternalAuthResult> ProcessExternalResponseAsync(HttpContext httpContext, CancellationToken cancellationToken = default);
}

/// <summary>
/// Result of processing external SAML authentication
/// </summary>
public class SamlExternalAuthResult
{
    public bool Succeeded { get; init; }
    public string? SubjectId { get; init; }
    public string? IdpName { get; init; }
    public string? SessionIndex { get; init; }
    public string? RelayState { get; init; }
    public string? Error { get; init; }
    public string? ErrorDescription { get; init; }
    public System.Security.Claims.ClaimsPrincipal? Principal { get; init; }
    public IDictionary<string, string>? Claims { get; init; }

    public static SamlExternalAuthResult Success(
        string subjectId,
        string idpName,
        System.Security.Claims.ClaimsPrincipal principal,
        string? sessionIndex = null,
        string? relayState = null)
    {
        return new SamlExternalAuthResult
        {
            Succeeded = true,
            SubjectId = subjectId,
            IdpName = idpName,
            Principal = principal,
            SessionIndex = sessionIndex,
            RelayState = relayState
        };
    }

    public static SamlExternalAuthResult Failed(string error, string? description = null)
    {
        return new SamlExternalAuthResult
        {
            Succeeded = false,
            Error = error,
            ErrorDescription = description
        };
    }
}

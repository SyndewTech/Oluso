using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Oluso.Core.Protocols;

namespace Oluso.Protocols.Oidc;

/// <summary>
/// OIDC-specific protocol service
/// </summary>
public interface IOidcProtocolService : IProtocolService
{
    /// <summary>
    /// Build context from HTTP request
    /// </summary>
    Task<ProtocolContext> BuildContextAsync(HttpContext http, CancellationToken cancellationToken = default);

    /// <summary>
    /// Process authorization request
    /// </summary>
    Task<ProtocolRequestResult> ProcessAuthorizeAsync(ProtocolContext context, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restore context from protocol state
    /// </summary>
    Task<ProtocolContext> RestoreContextAsync(string correlationId, CancellationToken cancellationToken = default);
}

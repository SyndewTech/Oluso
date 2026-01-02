using Microsoft.AspNetCore.Http;
using Oluso.Core.Common;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Protocols.Models;

namespace Oluso.Core.Protocols.Validation;

/// <summary>
/// Result of client authentication
/// </summary>
public class ClientAuthenticationResult : ValidationResult
{
    public Client? Client { get; set; }
    public ClientAuthenticationMethod Method { get; set; }
}

/// <summary>
/// Authenticates clients using various methods
/// </summary>
public interface IClientAuthenticator
{
    /// <summary>
    /// Authenticates a client from the HTTP request
    /// </summary>
    Task<ClientAuthenticationResult> AuthenticateAsync(
        HttpRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Authenticates a client using client_id and client_secret directly
    /// Used for JSON body authentication in PAR and other endpoints
    /// </summary>
    Task<ClientAuthenticationResult> AuthenticateAsync(
        string clientId,
        string? clientSecret,
        CancellationToken cancellationToken = default);
}

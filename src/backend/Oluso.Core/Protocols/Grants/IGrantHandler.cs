using Oluso.Core.Protocols.Models;

namespace Oluso.Core.Protocols.Grants;

/// <summary>
/// Base interface for grant type handlers
/// </summary>
public interface IGrantHandler
{
    /// <summary>
    /// The grant type this handler supports
    /// </summary>
    string GrantType { get; }

    /// <summary>
    /// Process a token request for this grant type
    /// </summary>
    Task<GrantResult> HandleAsync(TokenRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// Registry for grant handlers
/// </summary>
public interface IGrantHandlerRegistry
{
    IGrantHandler? GetHandler(string grantType);
    IEnumerable<string> SupportedGrantTypes { get; }
}

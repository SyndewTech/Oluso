using Microsoft.IdentityModel.Tokens;
using Oluso.Core.Protocols.Models;

namespace Oluso.Protocols.Validation;

/// <summary>
/// The JWT <c>typ</c> check every place that accepts one of our JWTs as an access token uses: tokens we
/// sign for another purpose (token-exchange tickets, logout tokens) are refused.
/// </summary>
public static class AccessTokenTypes
{
    /// <summary>A <see cref="TokenValidationParameters.TypeValidator"/>.</summary>
    public static string Validate(string? type, SecurityToken securityToken, TokenValidationParameters validationParameters)
    {
        if (OidcConstants.JwtTypes.IsNotAccessToken(type))
            throw new SecurityTokenInvalidTypeException($"A '{type}' token is not an access token.") { InvalidType = type };
        return type ?? string.Empty;
    }
}

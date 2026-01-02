using Oluso.Core.Common;
using Oluso.Core.Protocols.Models;

namespace Oluso.Core.Protocols.Validation;

/// <summary>
/// Validates PKCE (Proof Key for Code Exchange) parameters
/// </summary>
public interface IPkceValidator
{
    /// <summary>
    /// Validates code_challenge and code_challenge_method during authorization
    /// </summary>
    ValidationResult ValidateCodeChallenge(string? codeChallenge, string? codeChallengeMethod, bool pkceRequired, bool allowPlainText);

    /// <summary>
    /// Validates code_verifier against stored code_challenge during token exchange
    /// </summary>
    ValidationResult ValidateCodeVerifier(string? codeVerifier, string storedCodeChallenge, string storedCodeChallengeMethod);

    /// <summary>
    /// Generates a secure code_verifier
    /// </summary>
    string GenerateCodeVerifier();

    /// <summary>
    /// Generates code_challenge from code_verifier
    /// </summary>
    string GenerateCodeChallenge(string codeVerifier, string method = CodeChallengeMethods.Sha256);
}

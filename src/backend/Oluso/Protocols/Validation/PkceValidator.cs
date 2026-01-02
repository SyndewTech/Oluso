using System.Security.Cryptography;
using System.Text;
using Oluso.Core.Common;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Protocols.Validation;

namespace Oluso.Protocols.Validation;

/// <summary>
/// Default implementation of PKCE validator
/// </summary>
public class PkceValidator : IPkceValidator
{
    private const int MinCodeVerifierLength = 43;
    private const int MaxCodeVerifierLength = 128;

    public ValidationResult ValidateCodeChallenge(string? codeChallenge, string? codeChallengeMethod, bool pkceRequired, bool allowPlainText)
    {
        // If PKCE is required, code_challenge must be present
        if (pkceRequired && string.IsNullOrEmpty(codeChallenge))
        {
            return ValidationResult.Failure(
                OidcConstants.Errors.InvalidRequest,
                "code_challenge is required");
        }

        // If code_challenge is provided, validate it
        if (!string.IsNullOrEmpty(codeChallenge))
        {
            // Validate length (base64url encoded, 43-128 characters)
            if (codeChallenge.Length < MinCodeVerifierLength || codeChallenge.Length > MaxCodeVerifierLength)
            {
                return ValidationResult.Failure(
                    OidcConstants.Errors.InvalidRequest,
                    $"code_challenge must be between {MinCodeVerifierLength} and {MaxCodeVerifierLength} characters");
            }

            // Validate characters (unreserved URI characters)
            if (!IsValidCodeChallengeCharacters(codeChallenge))
            {
                return ValidationResult.Failure(
                    OidcConstants.Errors.InvalidRequest,
                    "code_challenge contains invalid characters");
            }

            // Validate method
            var method = codeChallengeMethod ?? CodeChallengeMethods.Plain;

            if (method != CodeChallengeMethods.Plain && method != CodeChallengeMethods.Sha256)
            {
                return ValidationResult.Failure(
                    OidcConstants.Errors.InvalidRequest,
                    "Unsupported code_challenge_method. Supported: plain, S256");
            }

            // Check if plain is allowed
            if (method == CodeChallengeMethods.Plain && !allowPlainText)
            {
                return ValidationResult.Failure(
                    OidcConstants.Errors.InvalidRequest,
                    "plain code_challenge_method is not allowed for this client");
            }
        }

        return ValidationResult.Success();
    }

    public ValidationResult ValidateCodeVerifier(string? codeVerifier, string storedCodeChallenge, string storedCodeChallengeMethod)
    {
        if (string.IsNullOrEmpty(codeVerifier))
        {
            return ValidationResult.Failure(
                OidcConstants.Errors.InvalidGrant,
                "code_verifier is required");
        }

        // Validate length
        if (codeVerifier.Length < MinCodeVerifierLength || codeVerifier.Length > MaxCodeVerifierLength)
        {
            return ValidationResult.Failure(
                OidcConstants.Errors.InvalidGrant,
                $"code_verifier must be between {MinCodeVerifierLength} and {MaxCodeVerifierLength} characters");
        }

        // Validate characters
        if (!IsValidCodeVerifierCharacters(codeVerifier))
        {
            return ValidationResult.Failure(
                OidcConstants.Errors.InvalidGrant,
                "code_verifier contains invalid characters");
        }

        // Compute challenge from verifier
        var computedChallenge = storedCodeChallengeMethod == CodeChallengeMethods.Sha256
            ? ComputeS256Challenge(codeVerifier)
            : codeVerifier;

        // Compare with stored challenge
        if (!SecureCompare(computedChallenge, storedCodeChallenge))
        {
            return ValidationResult.Failure(
                OidcConstants.Errors.InvalidGrant,
                "code_verifier does not match code_challenge");
        }

        return ValidationResult.Success();
    }

    public string GenerateCodeVerifier()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Base64UrlEncode(bytes);
    }

    public string GenerateCodeChallenge(string codeVerifier, string method = CodeChallengeMethods.Sha256)
    {
        if (method == CodeChallengeMethods.Plain)
        {
            return codeVerifier;
        }

        return ComputeS256Challenge(codeVerifier);
    }

    private static string ComputeS256Challenge(string codeVerifier)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.ASCII.GetBytes(codeVerifier);
        var hash = sha256.ComputeHash(bytes);
        return Base64UrlEncode(hash);
    }

    private static string Base64UrlEncode(byte[] input)
    {
        var output = Convert.ToBase64String(input);
        output = output.Replace('+', '-').Replace('/', '_');
        output = output.TrimEnd('=');
        return output;
    }

    private static bool IsValidCodeVerifierCharacters(string value)
    {
        // [A-Z] / [a-z] / [0-9] / "-" / "." / "_" / "~"
        foreach (var c in value)
        {
            if (!char.IsLetterOrDigit(c) && c != '-' && c != '.' && c != '_' && c != '~')
            {
                return false;
            }
        }
        return true;
    }

    private static bool IsValidCodeChallengeCharacters(string value)
    {
        // Base64url characters: [A-Z] / [a-z] / [0-9] / "-" / "_"
        foreach (var c in value)
        {
            if (!char.IsLetterOrDigit(c) && c != '-' && c != '_')
            {
                return false;
            }
        }
        return true;
    }

    private static bool SecureCompare(string a, string b)
    {
        if (a.Length != b.Length)
            return false;

        var result = 0;
        for (var i = 0; i < a.Length; i++)
        {
            result |= a[i] ^ b[i];
        }
        return result == 0;
    }
}

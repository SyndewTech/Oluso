using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Authentication;

/// <summary>
/// Tenant-aware password validator that enforces tenant-specific password policies.
/// This replaces the built-in Identity password options with runtime tenant settings.
/// </summary>
public class TenantPasswordValidator : IPasswordValidator<OlusoUser>
{
    private readonly ITenantSettingsProvider _tenantSettingsProvider;
    private readonly ILogger<TenantPasswordValidator> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    // Common weak passwords to block (can be extended)
    private static readonly HashSet<string> CommonPasswords = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "123456", "12345678", "qwerty", "abc123", "monkey", "1234567",
        "letmein", "trustno1", "dragon", "baseball", "iloveyou", "master", "sunshine",
        "ashley", "bailey", "shadow", "123123", "654321", "superman", "qazwsx",
        "michael", "football", "password1", "password123", "welcome", "welcome1",
        "admin", "login", "passw0rd", "pass@123", "p@ssw0rd", "p@ssword"
    };

    public TenantPasswordValidator(
        ITenantSettingsProvider tenantSettingsProvider,
        ILogger<TenantPasswordValidator> logger,
        IHttpClientFactory httpClientFactory)
    {
        _tenantSettingsProvider = tenantSettingsProvider;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
    }

    /// <summary>
    /// Checks if a password has been exposed in known data breaches using the Have I Been Pwned API.
    /// Uses k-anonymity: only the first 5 characters of the SHA-1 hash are sent to the API,
    /// so the actual password is never transmitted.
    /// </summary>
    /// <param name="password">The password to check</param>
    /// <returns>True if the password appears in breach databases, false otherwise</returns>
    private async Task<bool> IsPasswordInBreachAsync(string password)
    {
        try
        {
            // SHA-1 hash the password
            var sha1Hash = ComputeSha1Hash(password);

            // Split into prefix (first 5 chars) and suffix (remaining chars)
            var prefix = sha1Hash[..5];
            var newPref = sha1Hash.Substring(0, 5);
            var newSuffix = sha1Hash.Substring(5);
            var suffix = sha1Hash[5..];

            // Query the HIBP API with the prefix (k-anonymity)
            using var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Add("User-Agent", "Oluso-PasswordValidator");

            var response = await httpClient.GetAsync($"https://api.pwnedpasswords.com/range/{prefix}");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Have I Been Pwned API returned {StatusCode}", response.StatusCode);
                return false; // Don't block users if API is unavailable
            }

            var responseBody = await response.Content.ReadAsStringAsync();

            // Response format: each line is "SUFFIX:COUNT" (suffix is uppercase)
            // Check if our password's suffix appears in the response
            var lines = responseBody.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            foreach (var line in lines)
            {
                var parts = line.Split(':');
                if (parts.Length >= 1 && parts[0].Trim().Equals(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    // Password found in breach database
                    if (parts.Length >= 2 && int.TryParse(parts[1].Trim(), out var count))
                    {
                        _logger.LogInformation("Password found in {Count} breaches", count);
                    }
                    return true;
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking Have I Been Pwned API");
            return false; // Don't block users if there's an error
        }
    }

    /// <summary>
    /// Computes the SHA-1 hash of a string and returns it as an uppercase hex string.
    /// </summary>
    private static string ComputeSha1Hash(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hashBytes = SHA1.HashData(bytes);
        return Convert.ToHexString(hashBytes);
    }

    public async Task<IdentityResult> ValidateAsync(UserManager<OlusoUser> manager, OlusoUser user, string? password)
    {
        if (string.IsNullOrEmpty(password))
        {
            return IdentityResult.Failed(new IdentityError
            {
                Code = "PasswordRequired",
                Description = "Password is required."
            });
        }

        var settings = await _tenantSettingsProvider.GetPasswordSettingsAsync();
        var errors = new List<IdentityError>();

        // Minimum length
        if (password.Length < settings.MinimumLength)
        {
            errors.Add(new IdentityError
            {
                Code = "PasswordTooShort",
                Description = $"Password must be at least {settings.MinimumLength} characters."
            });
        }

        // Maximum length
        if (settings.MaximumLength > 0 && password.Length > settings.MaximumLength)
        {
            errors.Add(new IdentityError
            {
                Code = "PasswordTooLong",
                Description = $"Password must not exceed {settings.MaximumLength} characters."
            });
        }

        // Require digit
        if (settings.RequireDigit && !password.Any(char.IsDigit))
        {
            errors.Add(new IdentityError
            {
                Code = "PasswordRequiresDigit",
                Description = "Password must contain at least one digit (0-9)."
            });
        }

        // Require lowercase
        if (settings.RequireLowercase && !password.Any(char.IsLower))
        {
            errors.Add(new IdentityError
            {
                Code = "PasswordRequiresLower",
                Description = "Password must contain at least one lowercase letter (a-z)."
            });
        }

        // Require uppercase
        if (settings.RequireUppercase && !password.Any(char.IsUpper))
        {
            errors.Add(new IdentityError
            {
                Code = "PasswordRequiresUpper",
                Description = "Password must contain at least one uppercase letter (A-Z)."
            });
        }

        // Require non-alphanumeric
        if (settings.RequireNonAlphanumeric && !password.Any(c => !char.IsLetterOrDigit(c)))
        {
            errors.Add(new IdentityError
            {
                Code = "PasswordRequiresNonAlphanumeric",
                Description = "Password must contain at least one special character (!@#$%^&* etc.)."
            });
        }

        // Required unique characters
        if (settings.RequiredUniqueChars > 0 && password.Distinct().Count() < settings.RequiredUniqueChars)
        {
            errors.Add(new IdentityError
            {
                Code = "PasswordRequiresUniqueChars",
                Description = $"Password must contain at least {settings.RequiredUniqueChars} unique characters."
            });
        }

        // Block common passwords
        if (settings.BlockCommonPasswords && CommonPasswords.Contains(password))
        {
            errors.Add(new IdentityError
            {
                Code = "PasswordTooCommon",
                Description = "This password is too common. Please choose a stronger password."
            });
        }

        // Check if password contains username
        if (user.UserName != null && password.Contains(user.UserName, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(new IdentityError
            {
                Code = "PasswordContainsUsername",
                Description = "Password cannot contain your username."
            });
        }

        // Custom regex pattern
        if (!string.IsNullOrEmpty(settings.CustomRegexPattern))
        {
            try
            {
                if (!Regex.IsMatch(password, settings.CustomRegexPattern))
                {
                    errors.Add(new IdentityError
                    {
                        Code = "PasswordCustomValidation",
                        Description = settings.CustomRegexErrorMessage ?? "Password does not meet custom requirements."
                    });
                }
            }
            catch (RegexParseException ex)
            {
                _logger.LogWarning(ex, "Invalid custom password regex pattern: {Pattern}", settings.CustomRegexPattern);
                // Don't fail validation due to bad regex, just log the warning
            }
        }

        // Check against Have I Been Pwned breach database
        if (settings.CheckBreachedPasswords)
        {
            var isBreached = await IsPasswordInBreachAsync(password);
            if (isBreached)
            {
                errors.Add(new IdentityError
                {
                    Code = "PasswordBreached",
                    Description = "This password has appeared in a known data breach. Please choose a different password."
                });
            }
        }

        if (errors.Count > 0)
        {
            return IdentityResult.Failed(errors.ToArray());
        }

        return IdentityResult.Success;
    }
}

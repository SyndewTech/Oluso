using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Locale;

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
    private readonly IPasswordHistoryStore _passwordHistory;

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
        IHttpClientFactory httpClientFactory,
        IPasswordHistoryStore passwordHistory)
    {
        _tenantSettingsProvider = tenantSettingsProvider;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _passwordHistory = passwordHistory;
    }

    /// <summary>Named HttpClient for the Pwned Passwords range API (see <see cref="AddPwnedPasswordsClient"/>).</summary>
    public const string PwnedPasswordsClient = "Oluso.PwnedPasswords";

    /// <summary>
    /// Registers the Pwned Passwords client: a short timeout, so a slow API can't stall a
    /// password change, and response padding, so response sizes don't hint at the range queried.
    /// </summary>
    public static IServiceCollection AddPwnedPasswordsClient(IServiceCollection services)
    {
        services.AddHttpClient(PwnedPasswordsClient, client =>
        {
            client.BaseAddress = new Uri("https://api.pwnedpasswords.com/");
            client.Timeout = TimeSpan.FromSeconds(5);
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Oluso-PasswordValidator");
            client.DefaultRequestHeaders.Add("Add-Padding", "true");
        });
        return services;
    }

    /// <summary>Outcome of a breached-password lookup.</summary>
    public enum BreachCheckResult
    {
        NotBreached,
        Breached,

        /// <summary>The service couldn't be reached or answered with an error.</summary>
        Unavailable
    }

    /// <summary>
    /// Checks if a password has been exposed in known data breaches using the Have I Been Pwned
    /// Pwned Passwords API. Uses k-anonymity: only the first 5 characters of the SHA-1 hash are
    /// sent, so the password is never transmitted. A slow or failing API reports
    /// <see cref="BreachCheckResult.Unavailable"/>; the tenant policy decides what that means
    /// (<see cref="TenantPasswordSettings.RejectWhenBreachCheckUnavailable"/>).
    /// </summary>
    private async Task<BreachCheckResult> CheckBreachAsync(string password)
    {
        try
        {
            var sha1Hash = ComputeSha1Hash(password);
            var prefix = sha1Hash[..5];
            var suffix = sha1Hash[5..];

            var httpClient = _httpClientFactory.CreateClient(PwnedPasswordsClient);
            using var response = await httpClient.GetAsync($"range/{prefix}");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Have I Been Pwned API returned {StatusCode}", response.StatusCode);
                return BreachCheckResult.Unavailable;
            }

            var responseBody = await response.Content.ReadAsStringAsync();

            // Each line is "SUFFIX:COUNT". Padding lines have a count of 0 and are not breaches.
            foreach (var line in responseBody.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split(':');
                if (parts.Length == 2
                    && parts[0].Trim().Equals(suffix, StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(parts[1].Trim(), out var count)
                    && count > 0)
                {
                    _logger.LogInformation("Password found in {Count} breaches", count);
                    return BreachCheckResult.Breached;
                }
            }

            return BreachCheckResult.NotBreached;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Have I Been Pwned API could not be reached");
            return BreachCheckResult.Unavailable;
        }
    }

    /// <summary>
    /// True when <paramref name="password"/> matches the user's current password or, with a
    /// history policy, one of their last <see cref="TenantPasswordSettings.PasswordHistoryCount"/>
    /// passwords. The current password always counts when either history or expiry is on —
    /// otherwise an expired password could simply be set again.
    /// </summary>
    private async Task<bool> IsReusedAsync(UserManager<OlusoUser> manager, OlusoUser user, string password, TenantPasswordSettings settings)
    {
        var historyCount = Math.Max(settings.PasswordHistoryCount, 0);
        if (historyCount == 0 && settings.PasswordExpirationDays <= 0)
        {
            return false;
        }

        var hashes = new List<string>();
        if (!string.IsNullOrEmpty(user.PasswordHash))
        {
            hashes.Add(user.PasswordHash);
        }
        if (historyCount > 0 && !string.IsNullOrEmpty(user.Id))
        {
            var recent = await _passwordHistory.GetRecentHashesAsync(user.Id, historyCount);
            hashes.AddRange(recent.Where(h => !string.Equals(h, user.PasswordHash, StringComparison.Ordinal)));
        }

        return hashes.Any(hash =>
            manager.PasswordHasher.VerifyHashedPassword(user, hash, password) != PasswordVerificationResult.Failed);
    }

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

        var settings = string.IsNullOrEmpty(user.TenantId)
            ? await _tenantSettingsProvider.GetPasswordSettingsAsync()
            : await _tenantSettingsProvider.GetPasswordSettingsAsync(user.TenantId);
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

        if (await IsReusedAsync(manager, user, password, settings))
        {
            errors.Add(new IdentityError
            {
                Code = "PasswordReused",
                Description = settings.PasswordHistoryCount > 0
                    ? UiStringDefaults.Resolve("password_error_reused_history", settings.PasswordHistoryCount)
                    : UiStringDefaults.Resolve("password_error_reused_current")
            });
        }

        // Check against Have I Been Pwned breach database
        if (settings.CheckBreachedPasswords)
        {
            switch (await CheckBreachAsync(password))
            {
                case BreachCheckResult.Breached:
                    errors.Add(new IdentityError
                    {
                        Code = "PasswordBreached",
                        Description = UiStringDefaults.Resolve("password_error_breached")
                    });
                    break;
                case BreachCheckResult.Unavailable when settings.RejectWhenBreachCheckUnavailable:
                    errors.Add(new IdentityError
                    {
                        Code = "PasswordBreachCheckUnavailable",
                        Description = UiStringDefaults.Resolve("password_error_breach_check_unavailable")
                    });
                    break;
                case BreachCheckResult.Unavailable:
                    _logger.LogWarning("Breached-password check unavailable; accepting the password as the tenant policy allows");
                    break;
            }
        }

        if (errors.Count > 0)
        {
            return IdentityResult.Failed(errors.ToArray());
        }

        return IdentityResult.Success;
    }
}

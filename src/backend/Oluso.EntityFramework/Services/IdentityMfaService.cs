using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Services;

namespace Oluso.EntityFramework.Services;

/// <summary>
/// Identity-based implementation of IMfaService.
/// Uses ASP.NET Core Identity's built-in MFA capabilities for TOTP,
/// and simple code generation for SMS and email verification.
/// </summary>
public class IdentityMfaService : IMfaService
{
    private readonly UserManager<OlusoUser> _userManager;
    private readonly IDistributedCache _cache;
    private readonly ILogger<IdentityMfaService> _logger;
    private readonly IEmailSender? _emailSender;
    private readonly ISmsSender? _smsSender;

    private const string CacheKeyPrefix = "oluso:mfa:code:";
    private static readonly TimeSpan CodeExpiration = TimeSpan.FromMinutes(10);

    public IdentityMfaService(
        UserManager<OlusoUser> userManager,
        IDistributedCache cache,
        ILogger<IdentityMfaService> logger,
        IEmailSender? emailSender = null,
        ISmsSender? smsSender = null)
    {
        _userManager = userManager;
        _cache = cache;
        _logger = logger;
        _emailSender = emailSender;
        _smsSender = smsSender;
    }

    public async Task<MfaSetupResult> GenerateTotpSetupAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return MfaSetupResult.Failed("User not found");
        }

        try
        {
            // Reset the authenticator key to generate a new one
            await _userManager.ResetAuthenticatorKeyAsync(user);

            var unformattedKey = await _userManager.GetAuthenticatorKeyAsync(user);
            if (string.IsNullOrEmpty(unformattedKey))
            {
                return MfaSetupResult.Failed("Failed to generate authenticator key");
            }

            var email = user.Email ?? user.UserName ?? "user";
            var authenticatorUri = GenerateQrCodeUri(email, unformattedKey);

            _logger.LogInformation("Generated TOTP setup for user {UserId}", userId);

            return MfaSetupResult.Success(unformattedKey, authenticatorUri);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating TOTP setup for user {UserId}", userId);
            return MfaSetupResult.Failed("Failed to generate authenticator setup");
        }
    }

    public async Task<bool> VerifyTotpCodeAsync(string userId, string code, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return false;
        }

        var isValid = await _userManager.VerifyTwoFactorTokenAsync(
            user,
            _userManager.Options.Tokens.AuthenticatorTokenProvider,
            code);

        if (isValid)
        {
            _logger.LogInformation("TOTP code verified for user {UserId}", userId);
        }
        else
        {
            _logger.LogWarning("Invalid TOTP code for user {UserId}", userId);
        }

        return isValid;
    }

    public async Task<bool> SendVerificationCodeAsync(string userId, string provider, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return false;
        }

        // Generate a 6-digit code
        var code = GenerateCode();
        var cacheKey = GetCacheKey(userId, provider);

        // Store the code in distributed cache
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = CodeExpiration
        };
        await _cache.SetStringAsync(cacheKey, code, options, cancellationToken);

        _logger.LogInformation("Generated verification code for user {UserId} via {Provider}", userId, provider);

        // Send the code via the appropriate channel
        switch (provider.ToLower())
        {
            case "email":
                if (_emailSender != null && !string.IsNullOrEmpty(user.Email))
                {
                    var subject = "Your verification code";
                    var body = GenerateVerificationEmailBody(code);
                    var result = await _emailSender.SendAsync(user.Email, subject, body, cancellationToken);
                    if (!result.Success)
                    {
                        _logger.LogWarning("Failed to send email verification code to {Email}: {Error}", user.Email, result.Error);
                        return false;
                    }
                    _logger.LogInformation("Email verification code sent to {Email}", user.Email);
                }
                else
                {
                    _logger.LogDebug("Email sender not configured or email not set. Code: {Code}", code);
                }
                break;

            case "sms":
            case "phone":
                if (_smsSender != null && !string.IsNullOrEmpty(user.PhoneNumber))
                {
                    var message = $"Your verification code is: {code}. Valid for 10 minutes.";
                    var result = await _smsSender.SendAsync(user.PhoneNumber, message, cancellationToken);
                    if (!result.Success)
                    {
                        _logger.LogWarning("Failed to send SMS verification code to {Phone}: {Error}", user.PhoneNumber, result.Error);
                        return false;
                    }
                    _logger.LogInformation("SMS verification code sent to {Phone}", MaskPhoneNumber(user.PhoneNumber));
                }
                else
                {
                    _logger.LogDebug("SMS sender not configured or phone not set. Code: {Code}", code);
                }
                break;
        }

        return true;
    }

    private static string GenerateVerificationEmailBody(string code)
    {
        return $@"
<div style='font-family: Arial, sans-serif; max-width: 600px; margin: 0 auto;'>
    <h2>Verification Code</h2>
    <p>Your verification code is:</p>
    <div style='font-size: 32px; font-weight: bold; letter-spacing: 8px; padding: 20px; background: #f5f5f5; text-align: center; margin: 20px 0;'>
        {code}
    </div>
    <p>This code will expire in 10 minutes.</p>
    <p style='color: #666; font-size: 12px;'>If you didn't request this code, you can safely ignore this email.</p>
</div>";
    }

    private static string MaskPhoneNumber(string phone)
    {
        if (string.IsNullOrEmpty(phone) || phone.Length < 6)
            return "***";
        return phone[..4] + "****" + phone[^2..];
    }

    public async Task<bool> VerifyEmailCodeAsync(string userId, string code, CancellationToken cancellationToken = default)
    {
        return await VerifyCodeAsync(userId, "email", code, cancellationToken);
    }

    public async Task<bool> VerifySmsCodeAsync(string userId, string code, CancellationToken cancellationToken = default)
    {
        return await VerifyCodeAsync(userId, "sms", code, cancellationToken);
    }

    public async Task<MfaEnableResult> EnableMfaAsync(string userId, string provider, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return MfaEnableResult.Failed("User not found");
        }

        try
        {
            // Enable two-factor authentication
            var result = await _userManager.SetTwoFactorEnabledAsync(user, true);
            if (!result.Succeeded)
            {
                var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                _logger.LogWarning("Failed to enable MFA for user {UserId}: {Errors}", userId, errors);
                return MfaEnableResult.Failed(errors);
            }

            // Generate recovery codes
            var recoveryCodes = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, 10);

            _logger.LogInformation("Enabled MFA for user {UserId} with provider {Provider}", userId, provider);

            return MfaEnableResult.Success(recoveryCodes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error enabling MFA for user {UserId}", userId);
            return MfaEnableResult.Failed("Failed to enable MFA");
        }
    }

    public async Task<bool> DisableMfaAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return false;
        }

        try
        {
            // Disable two-factor authentication
            var result = await _userManager.SetTwoFactorEnabledAsync(user, false);
            if (!result.Succeeded)
            {
                var errors = string.Join(", ", result.Errors.Select(e => e.Description));
                _logger.LogWarning("Failed to disable MFA for user {UserId}: {Errors}", userId, errors);
                return false;
            }

            // Reset authenticator key
            await _userManager.ResetAuthenticatorKeyAsync(user);

            _logger.LogInformation("Disabled MFA for user {UserId}", userId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error disabling MFA for user {UserId}", userId);
            return false;
        }
    }

    public async Task<IEnumerable<string>> GetEnabledProvidersAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return Enumerable.Empty<string>();
        }

        var providers = new List<string>();

        if (await _userManager.GetTwoFactorEnabledAsync(user))
        {
            // Check if authenticator key is set (TOTP)
            var key = await _userManager.GetAuthenticatorKeyAsync(user);
            if (!string.IsNullOrEmpty(key))
            {
                providers.Add("totp");
            }

            // Could add logic here to check for phone/email MFA if stored in user claims
            // For now, if 2FA is enabled with an authenticator key, we assume TOTP
        }

        return providers;
    }

    public async Task<IEnumerable<string>> GenerateRecoveryCodesAsync(string userId, int count = 10, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return Enumerable.Empty<string>();
        }

        try
        {
            var codes = await _userManager.GenerateNewTwoFactorRecoveryCodesAsync(user, count);
            _logger.LogInformation("Generated {Count} recovery codes for user {UserId}", count, userId);
            return codes ?? Enumerable.Empty<string>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating recovery codes for user {UserId}", userId);
            return Enumerable.Empty<string>();
        }
    }

    public async Task<bool> VerifyRecoveryCodeAsync(string userId, string code, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return false;
        }

        var result = await _userManager.RedeemTwoFactorRecoveryCodeAsync(user, code);

        if (result.Succeeded)
        {
            _logger.LogInformation("Recovery code redeemed for user {UserId}", userId);
        }
        else
        {
            _logger.LogWarning("Invalid recovery code for user {UserId}", userId);
        }

        return result.Succeeded;
    }

    private async Task<bool> VerifyCodeAsync(string userId, string provider, string code, CancellationToken cancellationToken)
    {
        var cacheKey = GetCacheKey(userId, provider);
        var storedCode = await _cache.GetStringAsync(cacheKey, cancellationToken);

        if (!string.IsNullOrEmpty(storedCode) &&
            string.Equals(code, storedCode, StringComparison.OrdinalIgnoreCase))
        {
            // Remove the code after successful verification
            await _cache.RemoveAsync(cacheKey, cancellationToken);
            _logger.LogInformation("{Provider} code verified for user {UserId}", provider, userId);
            return true;
        }

        _logger.LogWarning("Invalid {Provider} code for user {UserId}", provider, userId);
        return false;
    }

    private static string GenerateCode()
    {
        return Random.Shared.Next(100000, 999999).ToString();
    }

    private static string GetCacheKey(string userId, string provider)
    {
        return $"{CacheKeyPrefix}{provider}:{userId}";
    }

    private static string GenerateQrCodeUri(string email, string unformattedKey)
    {
        const string issuer = "Oluso";
        return $"otpauth://totp/{Uri.EscapeDataString(issuer)}:{Uri.EscapeDataString(email)}" +
               $"?secret={unformattedKey}&issuer={Uri.EscapeDataString(issuer)}&digits=6";
    }
}

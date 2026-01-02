using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Oluso.Core.Api;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Services;

namespace Oluso.Account.Controllers;

/// <summary>
/// Account API for managing user's security settings (password, MFA, passkeys)
/// </summary>
[Route("api/account/security")]
public class SecurityController : AccountBaseController
{
    private readonly IOlusoUserService _userService;
    private readonly IMfaService? _mfaService;
    private readonly IOlusoEventService _eventService;
    private readonly ILogger<SecurityController> _logger;

    public SecurityController(
        ITenantContext tenantContext,
        IOlusoUserService userService,
        IOlusoEventService eventService,
        ILogger<SecurityController> logger,
        IMfaService? mfaService = null) : base(tenantContext)
    {
        _userService = userService;
        _mfaService = mfaService;
        _eventService = eventService;
        _logger = logger;
    }

    /// <summary>
    /// Get security overview for the current user
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<SecurityOverviewDto>> GetSecurityOverview(CancellationToken cancellationToken)
    {
        var user = await _userService.FindByIdAsync(UserId, cancellationToken);
        if (user == null)
            return NotFound();

        var hasMfa = await _userService.HasMfaEnabledAsync(UserId, cancellationToken);

        return Ok(new SecurityOverviewDto
        {
            HasPassword = true, // Assume true if they can log in
            TwoFactorEnabled = user.TwoFactorEnabled,
            MfaEnabled = hasMfa,
            EmailVerified = user.EmailVerified,
            PhoneNumberVerified = user.PhoneNumberVerified,
            LastPasswordChange = null, // Would need to track this
            PasskeyCount = 0 // Would come from FIDO2 store
        });
    }

    /// <summary>
    /// Change the user's password
    /// </summary>
    [HttpPost("password")]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordRequest request,
        CancellationToken cancellationToken)
    {
        // Validate current password first
        var validationResult = await _userService.ValidateCredentialsAsync(
            UserEmail ?? UserId, request.CurrentPassword, TenantId, cancellationToken);

        if (!validationResult.Succeeded)
        {
            return BadRequest(new { error = "Current password is incorrect" });
        }

        var result = await _userService.ResetPasswordAsync(
            UserId, request.NewPassword, cancellationToken: cancellationToken);

        if (!result.Succeeded)
        {
            return BadRequest(new { errors = result.Errors ?? new[] { result.Error ?? "Password change failed" } });
        }

        _logger.LogInformation("User {UserId} changed their password", UserId);

        await _eventService.RaiseAsync(new AccountPasswordChangedEvent
        {
            SubjectId = UserId,
            TenantId = TenantId,
            IpAddress = ClientIp,
            UserAgent = UserAgent
        }, cancellationToken);

        return Ok(new { message = "Password changed successfully" });
    }

    /// <summary>
    /// Get MFA status and available methods
    /// </summary>
    [HttpGet("mfa")]
    public async Task<ActionResult<MfaStatusDto>> GetMfaStatus(CancellationToken cancellationToken)
    {
        var user = await _userService.FindByIdAsync(UserId, cancellationToken);
        if (user == null)
            return NotFound();

        var hasMfa = await _userService.HasMfaEnabledAsync(UserId, cancellationToken);
        var enabledProviders = _mfaService != null
            ? (await _mfaService.GetEnabledProvidersAsync(UserId, cancellationToken)).ToList()
            : new List<string>();

        // Determine available methods based on user data
        var availableMethods = new List<string> { "totp" };
        if (!string.IsNullOrEmpty(user.Email) && user.EmailVerified)
            availableMethods.Add("email");
        if (!string.IsNullOrEmpty(user.PhoneNumber) && user.PhoneNumberVerified)
            availableMethods.Add("sms");

        return Ok(new MfaStatusDto
        {
            Enabled = hasMfa,
            TwoFactorEnabled = user.TwoFactorEnabled,
            AvailableMethods = availableMethods,
            EnabledMethods = enabledProviders,
            HasTotp = enabledProviders.Contains("totp"),
            HasSms = enabledProviders.Contains("sms"),
            HasEmail = enabledProviders.Contains("email"),
            PhoneNumber = !string.IsNullOrEmpty(user.PhoneNumber) ? MaskPhone(user.PhoneNumber) : null,
            Email = !string.IsNullOrEmpty(user.Email) ? MaskEmail(user.Email) : null
        });
    }

    /// <summary>
    /// Start TOTP enrollment - generates secret and QR code
    /// </summary>
    [HttpPost("mfa/totp/setup")]
    public async Task<ActionResult<TotpSetupDto>> StartTotpSetup(CancellationToken cancellationToken)
    {
        if (_mfaService == null)
            return BadRequest(new { error = "MFA service not available" });

        var result = await _mfaService.GenerateTotpSetupAsync(UserId, cancellationToken);
        if (!result.Succeeded)
            return BadRequest(new { error = result.Error ?? "Failed to generate TOTP setup" });

        return Ok(new TotpSetupDto
        {
            Secret = result.SharedKey!,
            QrCodeUri = result.AuthenticatorUri!
        });
    }

    /// <summary>
    /// Verify and enable TOTP
    /// </summary>
    [HttpPost("mfa/totp/verify")]
    public async Task<ActionResult<MfaEnableResultDto>> VerifyAndEnableTotp(
        [FromBody] VerifyTotpRequest request,
        CancellationToken cancellationToken)
    {
        if (_mfaService == null)
            return BadRequest(new { error = "MFA service not available" });

        // Verify the code first
        var isValid = await _mfaService.VerifyTotpCodeAsync(UserId, request.Code, cancellationToken);
        if (!isValid)
            return BadRequest(new { error = "Invalid verification code" });

        // Enable TOTP MFA
        var result = await _mfaService.EnableMfaAsync(UserId, "totp", cancellationToken);
        if (!result.Succeeded)
            return BadRequest(new { error = result.Error ?? "Failed to enable TOTP" });

        _logger.LogInformation("User {UserId} enabled TOTP MFA", UserId);

        await _eventService.RaiseAsync(new UserMfaEnabledEvent
        {
            SubjectId = UserId,
            TenantId = TenantId,
            Method = "totp",
            IpAddress = ClientIp
        }, cancellationToken);

        return Ok(new MfaEnableResultDto
        {
            Success = true,
            RecoveryCodes = result.RecoveryCodes?.ToList()
        });
    }

    /// <summary>
    /// Disable TOTP MFA
    /// </summary>
    [HttpDelete("mfa/totp")]
    public async Task<IActionResult> DisableTotp(
        [FromBody] DisableMfaRequest request,
        CancellationToken cancellationToken)
    {
        // Verify password first
        var validationResult = await _userService.ValidateCredentialsAsync(
            UserEmail ?? UserId, request.Password, TenantId, cancellationToken);

        if (!validationResult.Succeeded)
            return BadRequest(new { error = "Invalid password" });

        if (_mfaService != null)
        {
            await _mfaService.DisableMfaAsync(UserId, cancellationToken);
        }

        _logger.LogWarning("User {UserId} disabled TOTP MFA", UserId);

        await _eventService.RaiseAsync(new UserMfaDisabledEvent
        {
            SubjectId = UserId,
            TenantId = TenantId,
            IpAddress = ClientIp
        }, cancellationToken);

        return Ok(new { message = "TOTP MFA disabled" });
    }

    /// <summary>
    /// Start SMS MFA setup - sends verification code
    /// </summary>
    [HttpPost("mfa/sms/setup")]
    public async Task<ActionResult<SmsSetupDto>> StartSmsSetup(
        [FromBody] SmsSetupRequest? request,
        CancellationToken cancellationToken)
    {
        if (_mfaService == null)
            return BadRequest(new { error = "MFA service not available" });

        var user = await _userService.FindByIdAsync(UserId, cancellationToken);
        if (user == null)
            return NotFound();

        var phoneNumber = request?.PhoneNumber ?? user.PhoneNumber;
        if (string.IsNullOrEmpty(phoneNumber))
            return BadRequest(new { error = "Phone number is required" });

        // Send verification code
        var sent = await _mfaService.SendVerificationCodeAsync(UserId, "sms", cancellationToken);
        if (!sent)
            return BadRequest(new { error = "Failed to send verification code" });

        _logger.LogInformation("SMS verification code sent to user {UserId}", UserId);

        return Ok(new SmsSetupDto
        {
            MaskedPhone = MaskPhone(phoneNumber),
            CodeSent = true
        });
    }

    /// <summary>
    /// Verify and enable SMS MFA
    /// </summary>
    [HttpPost("mfa/sms/verify")]
    public async Task<ActionResult<MfaEnableResultDto>> VerifyAndEnableSms(
        [FromBody] VerifySmsRequest request,
        CancellationToken cancellationToken)
    {
        if (_mfaService == null)
            return BadRequest(new { error = "MFA service not available" });

        // Verify the code first
        var isValid = await _mfaService.VerifySmsCodeAsync(UserId, request.Code, cancellationToken);
        if (!isValid)
            return BadRequest(new { error = "Invalid verification code" });

        // Enable SMS MFA
        var result = await _mfaService.EnableMfaAsync(UserId, "sms", cancellationToken);
        if (!result.Succeeded)
            return BadRequest(new { error = result.Error ?? "Failed to enable SMS MFA" });

        _logger.LogInformation("User {UserId} enabled SMS MFA", UserId);

        await _eventService.RaiseAsync(new UserMfaEnabledEvent
        {
            SubjectId = UserId,
            TenantId = TenantId,
            Method = "sms",
            IpAddress = ClientIp
        }, cancellationToken);

        return Ok(new MfaEnableResultDto
        {
            Success = true,
            RecoveryCodes = result.RecoveryCodes?.ToList()
        });
    }

    /// <summary>
    /// Disable SMS MFA
    /// </summary>
    [HttpDelete("mfa/sms")]
    public async Task<IActionResult> DisableSms(
        [FromBody] DisableMfaRequest request,
        CancellationToken cancellationToken)
    {
        var validationResult = await _userService.ValidateCredentialsAsync(
            UserEmail ?? UserId, request.Password, TenantId, cancellationToken);

        if (!validationResult.Succeeded)
            return BadRequest(new { error = "Invalid password" });

        // Note: This disables all MFA - you may want method-specific disable
        if (_mfaService != null)
        {
            await _mfaService.DisableMfaAsync(UserId, cancellationToken);
        }

        _logger.LogWarning("User {UserId} disabled SMS MFA", UserId);

        await _eventService.RaiseAsync(new UserMfaDisabledEvent
        {
            SubjectId = UserId,
            TenantId = TenantId,
            IpAddress = ClientIp
        }, cancellationToken);

        return Ok(new { message = "SMS MFA disabled" });
    }

    /// <summary>
    /// Start Email MFA setup - sends verification code
    /// </summary>
    [HttpPost("mfa/email/setup")]
    public async Task<ActionResult<EmailSetupDto>> StartEmailSetup(CancellationToken cancellationToken)
    {
        if (_mfaService == null)
            return BadRequest(new { error = "MFA service not available" });

        var user = await _userService.FindByIdAsync(UserId, cancellationToken);
        if (user == null)
            return NotFound();

        if (string.IsNullOrEmpty(user.Email))
            return BadRequest(new { error = "Email address is required" });

        // Send verification code
        var sent = await _mfaService.SendVerificationCodeAsync(UserId, "email", cancellationToken);
        if (!sent)
            return BadRequest(new { error = "Failed to send verification code" });

        _logger.LogInformation("Email verification code sent to user {UserId}", UserId);

        return Ok(new EmailSetupDto
        {
            MaskedEmail = MaskEmail(user.Email),
            CodeSent = true
        });
    }

    /// <summary>
    /// Verify and enable Email MFA
    /// </summary>
    [HttpPost("mfa/email/verify")]
    public async Task<ActionResult<MfaEnableResultDto>> VerifyAndEnableEmail(
        [FromBody] VerifyEmailRequest request,
        CancellationToken cancellationToken)
    {
        if (_mfaService == null)
            return BadRequest(new { error = "MFA service not available" });

        // Verify the code first
        var isValid = await _mfaService.VerifyEmailCodeAsync(UserId, request.Code, cancellationToken);
        if (!isValid)
            return BadRequest(new { error = "Invalid verification code" });

        // Enable Email MFA
        var result = await _mfaService.EnableMfaAsync(UserId, "email", cancellationToken);
        if (!result.Succeeded)
            return BadRequest(new { error = result.Error ?? "Failed to enable Email MFA" });

        _logger.LogInformation("User {UserId} enabled Email MFA", UserId);

        await _eventService.RaiseAsync(new UserMfaEnabledEvent
        {
            SubjectId = UserId,
            TenantId = TenantId,
            Method = "email",
            IpAddress = ClientIp
        }, cancellationToken);

        return Ok(new MfaEnableResultDto
        {
            Success = true,
            RecoveryCodes = result.RecoveryCodes?.ToList()
        });
    }

    /// <summary>
    /// Disable Email MFA
    /// </summary>
    [HttpDelete("mfa/email")]
    public async Task<IActionResult> DisableEmail(
        [FromBody] DisableMfaRequest request,
        CancellationToken cancellationToken)
    {
        var validationResult = await _userService.ValidateCredentialsAsync(
            UserEmail ?? UserId, request.Password, TenantId, cancellationToken);

        if (!validationResult.Succeeded)
            return BadRequest(new { error = "Invalid password" });

        if (_mfaService != null)
        {
            await _mfaService.DisableMfaAsync(UserId, cancellationToken);
        }

        _logger.LogWarning("User {UserId} disabled Email MFA", UserId);

        await _eventService.RaiseAsync(new UserMfaDisabledEvent
        {
            SubjectId = UserId,
            TenantId = TenantId,
            IpAddress = ClientIp
        }, cancellationToken);

        return Ok(new { message = "Email MFA disabled" });
    }

    /// <summary>
    /// Generate new recovery codes
    /// </summary>
    [HttpPost("mfa/recovery-codes")]
    public async Task<ActionResult<RecoveryCodesDto>> GenerateRecoveryCodes(
        [FromBody] DisableMfaRequest request,
        CancellationToken cancellationToken)
    {
        // Verify password first
        var validationResult = await _userService.ValidateCredentialsAsync(
            UserEmail ?? UserId, request.Password, TenantId, cancellationToken);

        if (!validationResult.Succeeded)
            return BadRequest(new { error = "Invalid password" });

        if (_mfaService == null)
            return BadRequest(new { error = "MFA service not available" });

        var codes = await _mfaService.GenerateRecoveryCodesAsync(UserId, 10, cancellationToken);

        _logger.LogInformation("User {UserId} generated new recovery codes", UserId);

        return Ok(new RecoveryCodesDto
        {
            Codes = codes.ToList()
        });
    }

    /// <summary>
    /// Send email verification code
    /// </summary>
    [HttpPost("email/send-verification")]
    public async Task<ActionResult<EmailVerificationSentDto>> SendEmailVerification(CancellationToken cancellationToken)
    {
        var user = await _userService.FindByIdAsync(UserId, cancellationToken);
        if (user == null)
            return NotFound();

        if (string.IsNullOrEmpty(user.Email))
            return BadRequest(new { error = "No email address configured" });

        if (user.EmailVerified)
            return BadRequest(new { error = "Email already verified" });

        if (_mfaService == null)
            return BadRequest(new { error = "Verification service not available" });

        var sent = await _mfaService.SendVerificationCodeAsync(UserId, "email", cancellationToken);
        if (!sent)
            return BadRequest(new { error = "Failed to send verification code" });

        _logger.LogInformation("Email verification code sent to user {UserId}", UserId);

        return Ok(new EmailVerificationSentDto
        {
            MaskedEmail = MaskEmail(user.Email),
            CodeSent = true
        });
    }

    /// <summary>
    /// Verify email with code
    /// </summary>
    [HttpPost("email/verify")]
    public async Task<IActionResult> VerifyEmail(
        [FromBody] VerifyCodeRequest request,
        CancellationToken cancellationToken)
    {
        var user = await _userService.FindByIdAsync(UserId, cancellationToken);
        if (user == null)
            return NotFound();

        if (user.EmailVerified)
            return BadRequest(new { error = "Email already verified" });

        if (_mfaService == null)
            return BadRequest(new { error = "Verification service not available" });

        var isValid = await _mfaService.VerifyEmailCodeAsync(UserId, request.Code, cancellationToken);
        if (!isValid)
            return BadRequest(new { error = "Invalid verification code" });

        // Mark email as verified
        await _userService.SetEmailVerifiedAsync(UserId, true, cancellationToken);

        _logger.LogInformation("User {UserId} verified their email", UserId);

        await _eventService.RaiseAsync(new EmailVerifiedEvent
        {
            SubjectId = UserId,
            TenantId = TenantId,
            Email = user.Email,
            IpAddress = ClientIp
        }, cancellationToken);

        return Ok(new { message = "Email verified successfully" });
    }

    /// <summary>
    /// Send phone verification code
    /// </summary>
    [HttpPost("phone/send-verification")]
    public async Task<ActionResult<PhoneVerificationSentDto>> SendPhoneVerification(
        [FromBody] UpdatePhoneRequest? request,
        CancellationToken cancellationToken)
    {
        var user = await _userService.FindByIdAsync(UserId, cancellationToken);
        if (user == null)
            return NotFound();

        var phoneNumber = request?.PhoneNumber ?? user.PhoneNumber;
        if (string.IsNullOrEmpty(phoneNumber))
            return BadRequest(new { error = "No phone number configured" });

        // If updating to a new phone number, store it temporarily
        if (!string.IsNullOrEmpty(request?.PhoneNumber) && request.PhoneNumber != user.PhoneNumber)
        {
            await _userService.SetPhoneNumberAsync(UserId, request.PhoneNumber, cancellationToken);
        }

        if (_mfaService == null)
            return BadRequest(new { error = "Verification service not available" });

        var sent = await _mfaService.SendVerificationCodeAsync(UserId, "sms", cancellationToken);
        if (!sent)
            return BadRequest(new { error = "Failed to send verification code" });

        _logger.LogInformation("Phone verification code sent to user {UserId}", UserId);

        return Ok(new PhoneVerificationSentDto
        {
            MaskedPhone = MaskPhone(phoneNumber),
            CodeSent = true
        });
    }

    /// <summary>
    /// Verify phone with code
    /// </summary>
    [HttpPost("phone/verify")]
    public async Task<IActionResult> VerifyPhone(
        [FromBody] VerifyCodeRequest request,
        CancellationToken cancellationToken)
    {
        var user = await _userService.FindByIdAsync(UserId, cancellationToken);
        if (user == null)
            return NotFound();

        if (user.PhoneNumberVerified)
            return BadRequest(new { error = "Phone number already verified" });

        if (_mfaService == null)
            return BadRequest(new { error = "Verification service not available" });

        var isValid = await _mfaService.VerifySmsCodeAsync(UserId, request.Code, cancellationToken);
        if (!isValid)
            return BadRequest(new { error = "Invalid verification code" });

        // Mark phone as verified
        await _userService.SetPhoneNumberVerifiedAsync(UserId, true, cancellationToken);

        _logger.LogInformation("User {UserId} verified their phone number", UserId);

        await _eventService.RaiseAsync(new PhoneVerifiedEvent
        {
            SubjectId = UserId,
            TenantId = TenantId,
            PhoneNumber = MaskPhone(user.PhoneNumber ?? ""),
            IpAddress = ClientIp
        }, cancellationToken);

        return Ok(new { message = "Phone number verified successfully" });
    }

    /// <summary>
    /// Update email address (requires re-verification)
    /// </summary>
    [HttpPut("email")]
    public async Task<ActionResult<EmailVerificationSentDto>> UpdateEmail(
        [FromBody] UpdateEmailRequest request,
        CancellationToken cancellationToken)
    {
        var user = await _userService.FindByIdAsync(UserId, cancellationToken);
        if (user == null)
            return NotFound();

        if (string.IsNullOrEmpty(request.Email))
            return BadRequest(new { error = "Email is required" });

        // Update email and mark as unverified
        await _userService.SetEmailAsync(UserId, request.Email, cancellationToken);
        await _userService.SetEmailVerifiedAsync(UserId, false, cancellationToken);

        // Send verification code
        if (_mfaService != null)
        {
            await _mfaService.SendVerificationCodeAsync(UserId, "email", cancellationToken);
        }

        _logger.LogInformation("User {UserId} updated their email to {Email}", UserId, MaskEmail(request.Email));

        return Ok(new EmailVerificationSentDto
        {
            MaskedEmail = MaskEmail(request.Email),
            CodeSent = _mfaService != null
        });
    }

    /// <summary>
    /// Update phone number (requires re-verification)
    /// </summary>
    [HttpPut("phone")]
    public async Task<ActionResult<PhoneVerificationSentDto>> UpdatePhone(
        [FromBody] UpdatePhoneRequest request,
        CancellationToken cancellationToken)
    {
        var user = await _userService.FindByIdAsync(UserId, cancellationToken);
        if (user == null)
            return NotFound();

        if (string.IsNullOrEmpty(request.PhoneNumber))
            return BadRequest(new { error = "Phone number is required" });

        // Update phone and mark as unverified
        await _userService.SetPhoneNumberAsync(UserId, request.PhoneNumber, cancellationToken);
        await _userService.SetPhoneNumberVerifiedAsync(UserId, false, cancellationToken);

        // Send verification code
        if (_mfaService != null)
        {
            await _mfaService.SendVerificationCodeAsync(UserId, "sms", cancellationToken);
        }

        _logger.LogInformation("User {UserId} updated their phone number", UserId);

        return Ok(new PhoneVerificationSentDto
        {
            MaskedPhone = MaskPhone(request.PhoneNumber),
            CodeSent = _mfaService != null
        });
    }

    /// <summary>
    /// Get all external logins linked to the current user's account
    /// </summary>
    [HttpGet("external-logins")]
    public async Task<ActionResult<IEnumerable<ExternalLoginDto>>> GetExternalLogins(CancellationToken cancellationToken)
    {
        var externalLogins = await _userService.GetExternalLoginsAsync(UserId, cancellationToken);

        return Ok(externalLogins.Select(el => new ExternalLoginDto
        {
            Provider = el.Provider,
            ProviderKey = el.ProviderKey,
            DisplayName = el.DisplayName ?? el.Provider
        }));
    }

    /// <summary>
    /// Remove an external login from the current user's account
    /// </summary>
    [HttpDelete("external-logins/{provider}")]
    public async Task<IActionResult> RemoveExternalLogin(
        string provider,
        [FromQuery] string providerKey,
        CancellationToken cancellationToken)
    {
        // Check if user has a password or other external logins before allowing removal
        var hasPassword = await _userService.HasPasswordAsync(UserId, cancellationToken);
        var externalLogins = await _userService.GetExternalLoginsAsync(UserId, cancellationToken);
        var loginCount = externalLogins.Count();

        if (!hasPassword && loginCount <= 1)
        {
            return BadRequest(new { error = "Cannot remove your only sign-in method. Please set a password first." });
        }

        var result = await _userService.RemoveExternalLoginAsync(UserId, provider, providerKey, cancellationToken);

        if (!result.Succeeded)
        {
            return BadRequest(new { error = result.Error ?? "Failed to remove external login" });
        }

        _logger.LogInformation("User {UserId} removed external login {Provider}", UserId, provider);

        await _eventService.RaiseAsync(new ExternalLoginRemovedEvent
        {
            SubjectId = UserId,
            TenantId = TenantId,
            Provider = provider,
            IpAddress = ClientIp
        }, cancellationToken);

        return Ok(new { message = "External login removed" });
    }

    private static string MaskPhone(string phoneNumber)
    {
        if (string.IsNullOrEmpty(phoneNumber)) return "";
        if (phoneNumber.Length <= 4) return "***" + phoneNumber;
        return "***" + phoneNumber[^4..];
    }

    private static string MaskEmail(string email)
    {
        if (string.IsNullOrEmpty(email)) return "";
        var atIndex = email.IndexOf('@');
        if (atIndex <= 1) return email;

        var localPart = email[..atIndex];
        var domain = email[atIndex..];

        if (localPart.Length <= 2)
            return localPart[0] + "***" + domain;

        return localPart[0] + "***" + localPart[^1] + domain;
    }
}

#region DTOs

public class SecurityOverviewDto
{
    public bool HasPassword { get; set; }
    public bool TwoFactorEnabled { get; set; }
    public bool MfaEnabled { get; set; }
    public bool EmailVerified { get; set; }
    public bool PhoneNumberVerified { get; set; }
    public DateTime? LastPasswordChange { get; set; }
    public int PasskeyCount { get; set; }
}

public class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = null!;
    public string NewPassword { get; set; } = null!;
}

public class MfaStatusDto
{
    public bool Enabled { get; set; }
    public bool TwoFactorEnabled { get; set; }
    public List<string> AvailableMethods { get; set; } = new();
    public List<string> EnabledMethods { get; set; } = new();
    public bool HasTotp { get; set; }
    public bool HasSms { get; set; }
    public bool HasEmail { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
}

public class TotpSetupDto
{
    public string Secret { get; set; } = null!;
    public string QrCodeUri { get; set; } = null!;
}

public class VerifyTotpRequest
{
    public string Code { get; set; } = null!;
}

public class SmsSetupRequest
{
    public string? PhoneNumber { get; set; }
}

public class SmsSetupDto
{
    public string MaskedPhone { get; set; } = null!;
    public bool CodeSent { get; set; }
}

public class VerifySmsRequest
{
    public string Code { get; set; } = null!;
}

public class EmailSetupDto
{
    public string MaskedEmail { get; set; } = null!;
    public bool CodeSent { get; set; }
}

public class VerifyEmailRequest
{
    public string Code { get; set; } = null!;
}

public class MfaEnableResultDto
{
    public bool Success { get; set; }
    public List<string>? RecoveryCodes { get; set; }
}

public class RecoveryCodesDto
{
    public List<string> Codes { get; set; } = new();
}

public class DisableMfaRequest
{
    public string Password { get; set; } = null!;
}

public class VerifyCodeRequest
{
    public string Code { get; set; } = null!;
}

public class UpdateEmailRequest
{
    public string Email { get; set; } = null!;
}

public class UpdatePhoneRequest
{
    public string PhoneNumber { get; set; } = null!;
}

public class EmailVerificationSentDto
{
    public string MaskedEmail { get; set; } = null!;
    public bool CodeSent { get; set; }
}

public class PhoneVerificationSentDto
{
    public string MaskedPhone { get; set; } = null!;
    public bool CodeSent { get; set; }
}

public class ExternalLoginDto
{
    public string Provider { get; set; } = null!;
    public string ProviderKey { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
}

#endregion

#region Events

// Note: We use Account-specific events here to include additional context
// like IpAddress and UserAgent. The core UserPasswordChangedEvent is simpler.

public class AccountPasswordChangedEvent : OlusoEvent
{
    public override string Category => "Security";
    public override string EventType => "AccountPasswordChanged";
    public string? SubjectId { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
}

public class UserMfaEnabledEvent : OlusoEvent
{
    public override string Category => "Security";
    public override string EventType => "UserMfaEnabled";
    public string? SubjectId { get; set; }
    public string? Method { get; set; }
    public string? IpAddress { get; set; }
}

public class UserMfaDisabledEvent : OlusoEvent
{
    public override string Category => "Security";
    public override string EventType => "UserMfaDisabled";
    public string? SubjectId { get; set; }
    public string? IpAddress { get; set; }
}

public class EmailVerifiedEvent : OlusoEvent
{
    public override string Category => "Security";
    public override string EventType => "EmailVerified";
    public string? SubjectId { get; set; }
    public string? TenantId { get; set; }
    public string? Email { get; set; }
    public string? IpAddress { get; set; }
}

public class PhoneVerifiedEvent : OlusoEvent
{
    public override string Category => "Security";
    public override string EventType => "PhoneVerified";
    public string? SubjectId { get; set; }
    public string? TenantId { get; set; }
    public string? PhoneNumber { get; set; }
    public string? IpAddress { get; set; }
}

public class ExternalLoginRemovedEvent : OlusoEvent
{
    public override string Category => "Security";
    public override string EventType => "ExternalLoginRemoved";
    public string? SubjectId { get; set; }
    public string? TenantId { get; set; }
    public string? Provider { get; set; }
    public string? IpAddress { get; set; }
}

#endregion

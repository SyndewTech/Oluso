using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Oluso.Core.Api;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Services;

namespace Oluso.Enterprise.Fido2.Controllers;

/// <summary>
/// Account API for managing user's passkeys (FIDO2/WebAuthn credentials).
/// Provides endpoints for listing, registering, and deleting passkeys.
/// This controller is part of the FIDO2 Enterprise module and is added
/// to the Account UI via the fido2-ui plugin.
/// </summary>
[Route("api/account/passkeys")]
public class AccountPasskeysController : AccountBaseController
{
    private readonly IFido2Service _fido2Service;
    private readonly IOlusoEventService _eventService;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<AccountPasskeysController> _logger;

    public AccountPasskeysController(
        IFido2Service fido2Service,
        IOlusoEventService eventService,
        ITenantContext tenantContext,
        ILogger<AccountPasskeysController> logger) : base(tenantContext)
    {
        _fido2Service = fido2Service;
        _eventService = eventService;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    private string? UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub)
        ?? User.FindFirstValue("sub");

    private string? UserEmail => User.FindFirstValue(ClaimTypes.Email);
    private string? UserName => User.FindFirstValue(ClaimTypes.Name)
        ?? User.FindFirstValue("name");
    private string? TenantId => _tenantContext.TenantId;
    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();
    private string? UserAgent => HttpContext.Request.Headers.UserAgent.ToString();

    /// <summary>
    /// Get all passkeys registered for the current user
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<AccountPasskeyListDto>> GetPasskeys(CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(UserId))
        {
            return Unauthorized();
        }

        var credentials = await _fido2Service.GetCredentialsAsync(UserId, cancellationToken);

        var passkeys = credentials.Select(c => new AccountPasskeyDto
        {
            Id = c.Id,
            CredentialId = c.CredentialId,
            Name = c.Name ?? GetDefaultName(c),
            AuthenticatorType = c.AuthenticatorType,
            CreatedAt = c.CreatedAt,
            LastUsedAt = c.LastUsedAt,
            IsResidentKey = c.IsResidentKey
        }).ToList();

        return Ok(new AccountPasskeyListDto
        {
            Passkeys = passkeys,
            IsEnabled = true
        });
    }

    /// <summary>
    /// Start passkey registration - returns WebAuthn options for navigator.credentials.create()
    /// </summary>
    [HttpPost("register/start")]
    public async Task<ActionResult<AccountPasskeyRegistrationStartDto>> StartRegistration(
        [FromBody] StartAccountPasskeyRegistrationRequest? request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(UserId))
        {
            return Unauthorized();
        }

        var options = await _fido2Service.CreateRegistrationOptionsAsync(
            userId: UserId,
            userName: UserEmail ?? UserId,
            displayName: UserName ?? UserEmail ?? UserId,
            authenticatorType: request?.AuthenticatorType,
            requireResidentKey: request?.RequireDiscoverableCredential,
            cancellationToken: cancellationToken);

        return Ok(new AccountPasskeyRegistrationStartDto
        {
            RegistrationId = options.RegistrationId,
            Options = new AccountWebAuthnRegistrationOptions
            {
                Challenge = options.Challenge,
                Rp = new AccountWebAuthnRpEntity
                {
                    Id = options.RelyingParty.Id,
                    Name = options.RelyingParty.Name
                },
                User = new AccountWebAuthnUserEntity
                {
                    Id = options.User.Id,
                    Name = options.User.Name,
                    DisplayName = options.User.DisplayName
                },
                PubKeyCredParams = options.PublicKeyCredentialParameters?.Select(p => new AccountWebAuthnPubKeyCredParam
                {
                    Type = p.Type,
                    Alg = p.Algorithm
                }).ToList(),
                Timeout = options.Timeout,
                Attestation = options.AttestationConveyance,
                AuthenticatorSelection = options.AuthenticatorSelection != null ? new AccountWebAuthnAuthenticatorSelection
                {
                    AuthenticatorAttachment = options.AuthenticatorSelection.AuthenticatorAttachment,
                    ResidentKey = options.AuthenticatorSelection.ResidentKey,
                    UserVerification = options.AuthenticatorSelection.UserVerification
                } : null,
                ExcludeCredentials = options.ExcludeCredentials?.Select(c => new AccountWebAuthnCredentialDescriptor
                {
                    Type = c.Type,
                    Id = c.Id,
                    Transports = c.Transports?.ToList()
                }).ToList()
            }
        });
    }

    /// <summary>
    /// Complete passkey registration - verifies the credential and stores it
    /// </summary>
    [HttpPost("register/complete")]
    public async Task<ActionResult<AccountPasskeyRegistrationCompleteDto>> CompleteRegistration(
        [FromBody] CompleteAccountPasskeyRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(UserId))
        {
            return Unauthorized();
        }

        var result = await _fido2Service.VerifyRegistrationAsync(
            request.RegistrationId,
            request.AttestationResponse,
            request.Name,
            cancellationToken);

        if (!result.Succeeded)
        {
            _logger.LogWarning("Passkey registration failed for user {UserId}: {Error}", UserId, result.Error);
            return BadRequest(new { error = result.Error, description = result.ErrorDescription });
        }

        _logger.LogInformation("User {UserId} registered a new passkey {CredentialId}", UserId, result.CredentialId);

        await _eventService.RaiseAsync(new PasskeyRegisteredEvent
        {
            SubjectId = UserId,
            TenantId = TenantId,
            CredentialId = result.CredentialId,
            PasskeyName = request.Name,
            IpAddress = ClientIp,
            UserAgent = UserAgent
        }, cancellationToken);

        return Ok(new AccountPasskeyRegistrationCompleteDto
        {
            Success = true,
            CredentialId = result.CredentialId,
            Message = "Passkey registered successfully"
        });
    }

    /// <summary>
    /// Rename a passkey
    /// </summary>
    [HttpPatch("{passkeyId}")]
    public async Task<IActionResult> UpdatePasskey(
        string passkeyId,
        [FromBody] UpdateAccountPasskeyRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(UserId))
        {
            return Unauthorized();
        }

        // Verify the passkey belongs to this user
        var credentials = await _fido2Service.GetCredentialsAsync(UserId, cancellationToken);
        var passkey = credentials.FirstOrDefault(c => c.Id == passkeyId);

        if (passkey == null)
        {
            return NotFound(new { error = "Passkey not found" });
        }

        var updated = await _fido2Service.UpdateCredentialNameAsync(UserId, passkeyId, request.Name, cancellationToken);
        if (!updated)
        {
            return BadRequest(new { error = "Failed to update passkey name" });
        }

        _logger.LogInformation("User {UserId} renamed passkey {PasskeyId} to {Name}", UserId, passkeyId, request.Name);

        return Ok(new { message = "Passkey updated successfully" });
    }

    /// <summary>
    /// Delete a passkey
    /// </summary>
    [HttpDelete("{passkeyId}")]
    public async Task<IActionResult> DeletePasskey(
        string passkeyId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(UserId))
        {
            return Unauthorized();
        }

        // Verify the passkey belongs to this user before deleting
        var credentials = await _fido2Service.GetCredentialsAsync(UserId, cancellationToken);
        var passkey = credentials.FirstOrDefault(c => c.Id == passkeyId);

        if (passkey == null)
        {
            return NotFound(new { error = "Passkey not found" });
        }

        // Check if this is the user's last passkey - warn but don't prevent
        if (credentials.Count() == 1)
        {
            _logger.LogWarning("User {UserId} is deleting their last passkey", UserId);
        }

        var deleted = await _fido2Service.DeleteCredentialAsync(UserId, passkey.CredentialId, cancellationToken);

        if (!deleted)
        {
            return BadRequest(new { error = "Failed to delete passkey" });
        }

        _logger.LogInformation("User {UserId} deleted passkey {PasskeyId}", UserId, passkeyId);

        await _eventService.RaiseAsync(new PasskeyDeletedEvent
        {
            SubjectId = UserId,
            TenantId = TenantId,
            CredentialId = passkey.CredentialId,
            IpAddress = ClientIp
        }, cancellationToken);

        return Ok(new { message = "Passkey deleted successfully" });
    }

    private static string GetDefaultName(Fido2Credential credential)
    {
        var type = credential.AuthenticatorType?.ToLowerInvariant() switch
        {
            "platform" => "Built-in authenticator",
            "cross-platform" => "Security key",
            _ => "Passkey"
        };

        return $"{type} ({credential.CreatedAt:MMM d, yyyy})";
    }
}

#region DTOs

public class AccountPasskeyListDto
{
    public List<AccountPasskeyDto> Passkeys { get; set; } = new();
    public bool IsEnabled { get; set; }
    public string? Message { get; set; }
}

public class AccountPasskeyDto
{
    public string Id { get; set; } = null!;
    public string CredentialId { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? AuthenticatorType { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public bool IsResidentKey { get; set; }
}

public class StartAccountPasskeyRegistrationRequest
{
    /// <summary>
    /// Optional: "platform" for built-in authenticator, "cross-platform" for security key
    /// </summary>
    public string? AuthenticatorType { get; set; }

    /// <summary>
    /// Whether to require a discoverable credential (passkey).
    /// Default is true for modern passkey experience.
    /// </summary>
    public bool? RequireDiscoverableCredential { get; set; } = true;
}

public class AccountPasskeyRegistrationStartDto
{
    public string RegistrationId { get; set; } = null!;
    public AccountWebAuthnRegistrationOptions Options { get; set; } = null!;
}

public class AccountWebAuthnRegistrationOptions
{
    public string Challenge { get; set; } = null!;
    public AccountWebAuthnRpEntity Rp { get; set; } = null!;
    public AccountWebAuthnUserEntity User { get; set; } = null!;
    public List<AccountWebAuthnPubKeyCredParam>? PubKeyCredParams { get; set; }
    public int? Timeout { get; set; }
    public string? Attestation { get; set; }
    public AccountWebAuthnAuthenticatorSelection? AuthenticatorSelection { get; set; }
    public List<AccountWebAuthnCredentialDescriptor>? ExcludeCredentials { get; set; }
}

public class AccountWebAuthnRpEntity
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
}

public class AccountWebAuthnUserEntity
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
}

public class AccountWebAuthnPubKeyCredParam
{
    public string Type { get; set; } = null!;
    public int Alg { get; set; }
}

public class AccountWebAuthnAuthenticatorSelection
{
    public string? AuthenticatorAttachment { get; set; }
    public string? ResidentKey { get; set; }
    public string? UserVerification { get; set; }
}

public class AccountWebAuthnCredentialDescriptor
{
    public string Type { get; set; } = null!;
    public string Id { get; set; } = null!;
    public List<string>? Transports { get; set; }
}

public class CompleteAccountPasskeyRegistrationRequest
{
    public string RegistrationId { get; set; } = null!;

    /// <summary>
    /// JSON string from navigator.credentials.create() response
    /// </summary>
    public string AttestationResponse { get; set; } = null!;

    /// <summary>
    /// Optional friendly name for the passkey
    /// </summary>
    public string? Name { get; set; }
}

public class AccountPasskeyRegistrationCompleteDto
{
    public bool Success { get; set; }
    public string? CredentialId { get; set; }
    public string? Message { get; set; }
}

public class UpdateAccountPasskeyRequest
{
    public string Name { get; set; } = null!;
}

#endregion

#region Events

public class PasskeyRegisteredEvent : OlusoEvent
{
    public override string Category => "Security";
    public override string EventType => "PasskeyRegistered";
    public string? SubjectId { get; set; }
    public string? CredentialId { get; set; }
    public string? PasskeyName { get; set; }
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
}

public class PasskeyDeletedEvent : OlusoEvent
{
    public override string Category => "Security";
    public override string EventType => "PasskeyDeleted";
    public string? SubjectId { get; set; }
    public string? CredentialId { get; set; }
    public string? IpAddress { get; set; }
}

#endregion

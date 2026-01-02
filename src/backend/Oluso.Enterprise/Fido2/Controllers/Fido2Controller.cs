using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Oluso.Core.Api;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;

namespace Oluso.Enterprise.Fido2.Controllers;

/// <summary>
/// User-facing controller for FIDO2/WebAuthn credential management.
/// Allows authenticated users to manage their own passkeys.
/// </summary>
[Route("api/fido2")]
public class Fido2Controller : AccountBaseController
{
    private readonly IFido2Service _fido2Service;
    private readonly IOlusoUserService _userService;
    private readonly ILogger<Fido2Controller> _logger;

    public Fido2Controller(
        IFido2Service fido2Service,
        IOlusoUserService userService,
        ILogger<Fido2Controller> logger,
        ITenantContext tenantContext) : base(tenantContext)
    {
        _fido2Service = fido2Service;
        _userService = userService;
        _logger = logger;
    }

    #region Registration

    /// <summary>
    /// Begin FIDO2 credential registration for the current user
    /// </summary>
    [HttpPost("register/options")]
    public async Task<ActionResult<Fido2RegistrationOptions>> GetRegistrationOptions(
        [FromBody] RegistrationOptionsRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var user = await _userService.FindByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            return Unauthorized();
        }

        var options = await _fido2Service.CreateRegistrationOptionsAsync(
            user.Id,
            user.Username ?? user.Email ?? user.Id,
            user.DisplayName ?? user.Username ?? user.Email ?? user.Id,
            request?.AuthenticatorType,
            request?.RequireResidentKey,
            cancellationToken);

        // Store registration ID in session for verification
        HttpContext.Session.SetString("fido2.registrationId", options.RegistrationId);

        _logger.LogDebug("Created registration options for user {UserId}", userId);

        return Ok(options);
    }

    /// <summary>
    /// Complete FIDO2 credential registration for the current user
    /// </summary>
    [HttpPost("register/complete")]
    public async Task<ActionResult<RegistrationResponse>> CompleteRegistration(
        [FromBody] RegistrationCompleteRequest request,
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        // Get registration ID from session
        var registrationId = HttpContext.Session.GetString("fido2.registrationId");
        if (string.IsNullOrEmpty(registrationId))
        {
            return BadRequest(new { error = "No registration session found. Please start over." });
        }

        var result = await _fido2Service.VerifyRegistrationAsync(
            registrationId,
            request.Response,
            credentialName: null,
            cancellationToken);

        // Clear session
        HttpContext.Session.Remove("fido2.registrationId");

        if (!result.Succeeded)
        {
            _logger.LogWarning("FIDO2 registration failed for user {UserId}: {Error}", userId, result.Error);
            return BadRequest(new { error = result.Error, description = result.ErrorDescription });
        }

        _logger.LogInformation("FIDO2 credential registered for user {UserId}", userId);

        return Ok(new RegistrationResponse
        {
            CredentialId = result.CredentialId!,
            Success = true
        });
    }

    #endregion

    #region Authentication

    /// <summary>
    /// Begin FIDO2 assertion (authentication) - anonymous access allowed
    /// </summary>
    [HttpPost("assert/options")]
    [AllowAnonymous]
    public async Task<ActionResult<Fido2AssertionOptions>> GetAssertionOptions(
        [FromBody] AssertionOptionsRequest? request = null,
        CancellationToken cancellationToken = default)
    {
        var options = await _fido2Service.CreateAssertionOptionsAsync(
            request?.Username,
            cancellationToken);

        // Store assertion ID in session for verification
        HttpContext.Session.SetString("fido2.assertionId", options.AssertionId);

        return Ok(options);
    }

    /// <summary>
    /// Complete FIDO2 assertion (authentication) - anonymous access allowed
    /// </summary>
    [HttpPost("assert/complete")]
    [AllowAnonymous]
    public async Task<ActionResult<AssertionResponse>> CompleteAssertion(
        [FromBody] AssertionCompleteRequest request,
        CancellationToken cancellationToken = default)
    {
        // Get assertion ID from session
        var assertionId = HttpContext.Session.GetString("fido2.assertionId");
        if (string.IsNullOrEmpty(assertionId))
        {
            return BadRequest(new { error = "No assertion session found. Please start over." });
        }

        var result = await _fido2Service.VerifyAssertionAsync(
            assertionId,
            request.Response,
            cancellationToken);

        // Clear session
        HttpContext.Session.Remove("fido2.assertionId");

        if (!result.Succeeded)
        {
            _logger.LogWarning("FIDO2 assertion failed: {Error}", result.Error);
            return BadRequest(new { error = result.Error, description = result.ErrorDescription });
        }

        // Get user for response
        var user = await _userService.FindByIdAsync(result.UserId!, cancellationToken);

        return Ok(new AssertionResponse
        {
            Success = true,
            UserId = result.UserId!,
            Username = user?.Username,
            DisplayName = user?.DisplayName ?? user?.Username
        });
    }

    #endregion

    #region Credential Management

    /// <summary>
    /// Get current user's FIDO2 credentials
    /// </summary>
    [HttpGet("credentials")]
    public async Task<ActionResult<IEnumerable<CredentialInfo>>> GetCredentials(
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var credentials = await _fido2Service.GetCredentialsAsync(userId, cancellationToken);

        return Ok(credentials.Select(c => new CredentialInfo
        {
            Id = c.Id,
            CredentialId = c.CredentialId,
            DisplayName = c.Name,
            CreatedAt = c.CreatedAt,
            LastUsedAt = c.LastUsedAt,
            AuthenticatorType = c.AuthenticatorType ?? "unknown",
            IsResidentKey = c.IsResidentKey
        }));
    }

    /// <summary>
    /// Delete a credential owned by the current user
    /// </summary>
    [HttpDelete("credentials/{id}")]
    public async Task<ActionResult> DeleteCredential(
        string id,
        CancellationToken cancellationToken = default)
    {
        var userId = GetCurrentUserId();
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var success = await _fido2Service.DeleteCredentialAsync(userId, id, cancellationToken);

        if (!success)
        {
            return NotFound();
        }

        _logger.LogInformation("FIDO2 credential {CredentialId} deleted by user {UserId}", id, userId);

        return NoContent();
    }

    #endregion

    #region Helpers

    private string? GetCurrentUserId()
    {
        return User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? User.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? User.FindFirstValue("sub");
    }

    #endregion
}

#region DTOs

public class RegistrationOptionsRequest
{
    /// <summary>
    /// Authenticator type: "platform" (built-in) or "cross-platform" (security key)
    /// </summary>
    public string? AuthenticatorType { get; set; }

    /// <summary>
    /// Whether to require discoverable credentials (passkeys)
    /// </summary>
    public bool? RequireResidentKey { get; set; }
}

public class RegistrationCompleteRequest
{
    /// <summary>
    /// JSON response from navigator.credentials.create()
    /// </summary>
    public string Response { get; set; } = null!;

    /// <summary>
    /// Optional display name for the credential
    /// </summary>
    public string? DisplayName { get; set; }
}

public class RegistrationResponse
{
    public bool Success { get; set; }
    public string CredentialId { get; set; } = null!;
}

public class AssertionOptionsRequest
{
    /// <summary>
    /// Username for username-based flow, null for usernameless (discoverable credential) flow
    /// </summary>
    public string? Username { get; set; }
}

public class AssertionCompleteRequest
{
    /// <summary>
    /// JSON response from navigator.credentials.get()
    /// </summary>
    public string Response { get; set; } = null!;
}

public class AssertionResponse
{
    public bool Success { get; set; }
    public string UserId { get; set; } = null!;
    public string? Username { get; set; }
    public string? DisplayName { get; set; }
}

public class CredentialInfo
{
    public string Id { get; set; } = null!;
    public string CredentialId { get; set; } = null!;
    public string? DisplayName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public string AuthenticatorType { get; set; } = null!;
    public bool IsResidentKey { get; set; }
}

#endregion

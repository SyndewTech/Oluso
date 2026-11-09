using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Microsoft.AspNetCore.RateLimiting;
using Oluso.Core.Security;

namespace Oluso.Protocols.WebAuthn;

/// <summary>
/// WebAuthn/Passkey API endpoints for SDK-based passkey authentication (<c>signInWithPasskey</c>).
/// The passkey is verified by the same <see cref="IFido2Service"/> as the journey passkey step and the
/// page-based Fido2LoginController; the sign-in then completes through <see cref="IAuthorizationCodeIssuer"/>,
/// like every other sign-in that returns a code to a page: the authorize endpoint's client rules (enabled
/// client, allowed scopes, PKCE S256, a registered origin), its code shape (session, amr, auth_time) and the
/// sign-in audit. The token endpoint builds the tokens with the profile service.
/// </summary>
[ApiController]
[Route("connect/webauthn")]
[ApiExplorerSettings(IgnoreApi = true)]
[EnableRateLimiting(SignInRateLimitPolicies.SignIn)]
public class WebAuthnApiController : ControllerBase
{
    /// <summary>The method the journey passkey steps record, so every passkey sign-in reads the same.</summary>
    public const string AuthenticationMethod = "fido2";

    private static readonly string[] PasskeyAmr = { "hwk" };

    private readonly IFido2Service _fido2Service;
    private readonly IOlusoUserService _userService;
    private readonly IAuthorizationCodeIssuer _codes;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<WebAuthnApiController> _logger;
    private readonly ISignInAuditor? _signInAuditor;

    public WebAuthnApiController(
        IFido2Service fido2Service,
        IOlusoUserService userService,
        IAuthorizationCodeIssuer codes,
        ITenantContext tenantContext,
        ILogger<WebAuthnApiController> logger,
        ISignInAuditor? signInAuditor = null)
    {
        _fido2Service = fido2Service;
        _userService = userService;
        _codes = codes;
        _tenantContext = tenantContext;
        _logger = logger;
        _signInAuditor = signInAuditor;
    }

    /// <summary>
    /// Get WebAuthn assertion options for passkey authentication.
    /// The SDK calls this to get the challenge and allowed credentials,
    /// then passes them to navigator.credentials.get(). The client and page origin are checked first,
    /// and the whole request too when the SDK sends its scope and PKCE challenge, so the user is never
    /// prompted for a passkey that can't sign them in.
    /// </summary>
    [HttpPost("assertion-options")]
    public async Task<IActionResult> GetAssertionOptions(
        [FromBody] AssertionOptionsRequest request,
        CancellationToken cancellationToken)
    {
        if (PasskeyDisabled() is { } disabled) return disabled;

        var origin = HeadlessJourneyData.PageOrigin(Request);
        var validation = string.IsNullOrEmpty(request.CodeChallenge)
            ? await _codes.ValidateClientAsync(request.ClientId, origin, cancellationToken: cancellationToken)
            : await _codes.ValidateAsync(ToAuthorizationRequest(request.ClientId, request.Scope, request.CodeChallenge, request.CodeChallengeMethod, origin), cancellationToken);
        if (!validation.IsValid) return Rejected(validation.Error!, validation.ErrorDescription, validation.OriginRejected);

        // Create assertion options (username is optional — null enables usernameless/discoverable flow)
        var options = await _fido2Service.CreateAssertionOptionsAsync(
            request.Username,
            cancellationToken);

        _logger.LogDebug(
            "WebAuthn assertion options created for client {ClientId}, assertionId {AssertionId}, username {Username}",
            request.ClientId, options.AssertionId, request.Username ?? "(discoverable)");

        // Return options in WebAuthn-compatible format
        return Ok(new AssertionOptionsResponse
        {
            AssertionId = options.AssertionId,
            Challenge = options.Challenge,
            Timeout = options.Timeout ?? 60000,
            RpId = options.RpId,
            AllowCredentials = options.AllowCredentials?.Select(c => new CredentialDescriptorDto
            {
                Type = c.Type,
                Id = c.Id,
                Transports = c.Transports
            }).ToList(),
            UserVerification = options.UserVerification ?? "preferred"
        });
    }

    /// <summary>
    /// Verify a WebAuthn assertion and complete the sign-in with an authorization code.
    /// The SDK calls this after navigator.credentials.get() succeeds, passing the authenticator response,
    /// and exchanges the code (with its PKCE verifier and <c>redirect_uri=postmessage</c>) at the token endpoint.
    /// </summary>
    [HttpPost("assertion")]
    public async Task<IActionResult> VerifyAssertion(
        [FromBody] AssertionVerifyRequest request,
        CancellationToken cancellationToken)
    {
        if (PasskeyDisabled() is { } disabled) return disabled;

        if (string.IsNullOrEmpty(request.AssertionId) || string.IsNullOrEmpty(request.AssertionResponse))
        {
            return BadRequest(new { error = "invalid_request", error_description = "assertionId and assertionResponse are required" });
        }

        // Validate the authorization request before the assertion is spent on it.
        var authorization = ToAuthorizationRequest(
            request.ClientId, request.Scope, request.CodeChallenge, request.CodeChallengeMethod, HeadlessJourneyData.PageOrigin(Request));
        var validation = await _codes.ValidateAsync(authorization, cancellationToken);
        if (!validation.IsValid) return Rejected(validation.Error!, validation.ErrorDescription, validation.OriginRejected);

        // Verify the assertion
        var result = await _fido2Service.VerifyAssertionAsync(
            request.AssertionId,
            request.AssertionResponse,
            cancellationToken);

        if (!result.Succeeded)
        {
            _logger.LogWarning(
                "WebAuthn assertion verification failed for client {ClientId}: {Error}",
                request.ClientId, result.Error);
            await RecordFailureAsync(result.UserId, result.Error ?? "passkey_verification_failed", request.ClientId, cancellationToken);

            return Unauthorized(new { error = "access_denied", error_description = result.ErrorDescription ?? "Passkey verification failed" });
        }

        // Same checks as the journey passkey step.
        var user = await _userService.FindByIdAsync(result.UserId!, cancellationToken);
        if (user == null || !user.IsActive)
        {
            _logger.LogWarning("WebAuthn assertion succeeded but user {UserId} is missing or inactive", result.UserId);
            await RecordFailureAsync(result.UserId, user == null ? "user_not_found" : "user_deactivated", request.ClientId, cancellationToken);
            return Unauthorized(new { error = "access_denied", error_description = user == null ? "User not found" : "Account has been deactivated" });
        }

        var issued = await _codes.IssueAsync(authorization, new SignInCompletion
        {
            UserId = user.Id,
            AuthenticationMethod = AuthenticationMethod,
            Amr = PasskeyAmr
        }, cancellationToken);
        if (!issued.Succeeded) return Rejected(issued.Error!, issued.ErrorDescription, issued.OriginRejected);

        await _userService.RecordLoginAsync(user.Id, cancellationToken);

        _logger.LogInformation(
            "WebAuthn sign-in completed for client {ClientId}, user {UserId}, credential {CredentialId}",
            request.ClientId, user.Id, result.CredentialId);

        return Ok(new { code = issued.Code });
    }

    private static DirectAuthorizationRequest ToAuthorizationRequest(
        string? clientId, string? scope, string? codeChallenge, string? codeChallengeMethod, string? origin) => new()
    {
        ClientId = clientId,
        Scope = scope,
        CodeChallenge = codeChallenge,
        CodeChallengeMethod = codeChallengeMethod,
        Origin = origin,
        RedirectUri = DirectAuthorizationRequest.PostMessage
    };

    /// <summary>The tenant turned passkeys off (Tenant.EnablePasskey), as the journey passkey step honours.</summary>
    private IActionResult? PasskeyDisabled() =>
        _tenantContext.Tenant?.EnablePasskey == false
            ? BadRequest(new { error = "invalid_request", error_description = "Passkey sign-in is disabled" })
            : null;

    private IActionResult Rejected(string error, string? description, bool forbidden)
    {
        var body = new { error, error_description = description };
        return forbidden ? StatusCode(StatusCodes.Status403Forbidden, body) : BadRequest(body);
    }

    private Task RecordFailureAsync(string? userId, string reason, string? clientId, CancellationToken cancellationToken) =>
        _signInAuditor?.RecordFailureAsync(new SignInFailureAudit
        {
            Username = userId ?? "passkey",
            FailureReason = reason,
            ClientId = clientId
        }, cancellationToken) ?? Task.CompletedTask;
}

#region Request/Response DTOs

public class AssertionOptionsRequest
{
    /// <summary>OAuth2 client ID</summary>
    public string? ClientId { get; set; }

    /// <summary>Optional username for username-based flow. Omit for discoverable credentials (passkeys).</summary>
    public string? Username { get; set; }

    /// <summary>Optional: the scopes the sign-in will ask for, validated before the passkey prompt.</summary>
    public string? Scope { get; set; }

    /// <summary>Optional: the PKCE challenge the sign-in will use, validated before the passkey prompt.</summary>
    public string? CodeChallenge { get; set; }

    /// <summary>Optional: S256.</summary>
    public string? CodeChallengeMethod { get; set; }
}

public class AssertionVerifyRequest
{
    /// <summary>OAuth2 client ID</summary>
    public string? ClientId { get; set; }

    /// <summary>Assertion ID from the assertion-options response</summary>
    public string AssertionId { get; set; } = null!;

    /// <summary>JSON-serialized authenticator assertion response from navigator.credentials.get()</summary>
    public string AssertionResponse { get; set; } = null!;

    /// <summary>PKCE code challenge (required, S256)</summary>
    public string? CodeChallenge { get; set; }

    /// <summary>PKCE code challenge method: S256 (the default)</summary>
    public string? CodeChallengeMethod { get; set; }

    /// <summary>Requested scopes, each allowed for the client (default: "openid profile email", trimmed to those allowed)</summary>
    public string? Scope { get; set; }
}

public class AssertionOptionsResponse
{
    public string AssertionId { get; set; } = null!;
    public string Challenge { get; set; } = null!;
    public int Timeout { get; set; }
    public string? RpId { get; set; }
    public List<CredentialDescriptorDto>? AllowCredentials { get; set; }
    public string UserVerification { get; set; } = "preferred";
}

public class CredentialDescriptorDto
{
    public string Type { get; set; } = "public-key";
    public string Id { get; set; } = null!;
    public IList<string>? Transports { get; set; }
}

#endregion

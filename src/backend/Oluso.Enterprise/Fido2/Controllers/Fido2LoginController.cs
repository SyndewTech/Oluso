using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Oluso.Core.Authentication;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Locale;
using Oluso.Core.Services;
using Microsoft.AspNetCore.RateLimiting;
using Oluso.Core.Security;

namespace Oluso.Enterprise.Fido2.Controllers;

/// <summary>
/// Handles the passkey login flow with a self-contained UI page.
/// This decouples passkey authentication from the main login page. The passkey is verified by the same
/// <see cref="IFido2Service"/> as the journey passkey steps and the SDK endpoint, and the session it issues records
/// the same sign-in (amr <c>fido2</c>/<c>hwk</c>, auth_time) and audit as a journey passkey sign-in.
/// </summary>
[Route("fido2")]
[AllowAnonymous]
[EnableRateLimiting(SignInRateLimitPolicies.SignIn)]
public class Fido2LoginController : Controller
{
    /// <summary>The method and amr every passkey sign-in records.</summary>
    private const string AuthenticationMethod = "fido2";
    private static readonly string[] PasskeyMethods = { AuthenticationMethod, "hwk" };

    private readonly IFido2Service _fido2Service;
    private readonly IOlusoUserService _userService;
    private readonly ITenantContext? _tenantContext;
    private readonly ISignInAuditor? _signInAuditor;
    private readonly IClientStore? _clients;
    private readonly ILogger<Fido2LoginController> _logger;

    public Fido2LoginController(
        IFido2Service fido2Service,
        IOlusoUserService userService,
        ILogger<Fido2LoginController> logger,
        ITenantContext? tenantContext = null,
        ISignInAuditor? signInAuditor = null,
        IClientStore? clients = null)
    {
        _fido2Service = fido2Service;
        _userService = userService;
        _tenantContext = tenantContext;
        _logger = logger;
        _signInAuditor = signInAuditor;
        _clients = clients;
    }

    /// <summary>
    /// Display the passkey login page and initiate WebAuthn assertion
    /// </summary>
    [HttpGet("login")]
    public async Task<IActionResult> Login(
        [FromQuery] string? returnUrl = null,
        CancellationToken cancellationToken = default)
    {
        returnUrl = await SafeReturnUrlAsync(returnUrl, cancellationToken);

        // If already authenticated, redirect
        if (User.Identity?.IsAuthenticated == true)
        {
            return Redirect(returnUrl);
        }

        if (_tenantContext?.Tenant?.EnablePasskey == false)
        {
            return Content(GenerateErrorPage("Passkey sign-in is not available.", returnUrl), "text/html");
        }

        try
        {
            // Create assertion options for usernameless (discoverable credential) flow
            var options = await _fido2Service.CreateAssertionOptionsAsync(null, cancellationToken);

            // Store assertion ID in session
            HttpContext.Session.SetString("fido2.assertionId", options.AssertionId);

            // Serialize options for JavaScript (the default encoder escapes <, > and &, so it is safe in a script)
            var optionsJson = System.Text.Json.JsonSerializer.Serialize(options, new System.Text.Json.JsonSerializerOptions
            {
                PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase
            });

            // Return a self-contained HTML page that handles the WebAuthn ceremony
            return Content(GenerateLoginPage(optionsJson, returnUrl), "text/html");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create passkey assertion options");
            return Content(GenerateErrorPage("Failed to initialize passkey authentication. Please try again.", returnUrl), "text/html");
        }
    }

    /// <summary>
    /// Verify the passkey assertion and sign in the user
    /// </summary>
    [HttpPost("login")]
    public async Task<IActionResult> VerifyLogin(
        [FromForm] string assertionResponse,
        [FromForm] string? returnUrl = null,
        CancellationToken cancellationToken = default)
    {
        returnUrl = await SafeReturnUrlAsync(returnUrl, cancellationToken);
        var outcome = await CompleteSignInAsync(assertionResponse, returnUrl, cancellationToken);
        return outcome.Error == null
            ? Redirect(returnUrl)
            : Content(GenerateErrorPage(outcome.Error, returnUrl), "text/html");
    }

    /// <summary>
    /// API endpoint for JavaScript-based verification (returns JSON)
    /// </summary>
    [HttpPost("login/verify")]
    public async Task<IActionResult> VerifyLoginJson(
        [FromBody] PasskeyVerifyRequest request,
        CancellationToken cancellationToken = default)
    {
        var returnUrl = await SafeReturnUrlAsync(request.ReturnUrl, cancellationToken);
        var outcome = await CompleteSignInAsync(request.AssertionResponse, returnUrl, cancellationToken);
        if (outcome.SessionExpired)
        {
            return BadRequest(new { error = "Session expired", redirect = $"/fido2/login?returnUrl={Uri.EscapeDataString(returnUrl)}" });
        }
        return outcome.Error == null
            ? Ok(new { success = true, redirect = returnUrl })
            : BadRequest(new { error = outcome.Error });
    }

    private sealed record SignInOutcome(string? Error, bool SessionExpired = false);

    /// <summary>Both forms of the page end here: verify, sign in, record the login and audit it.</summary>
    private async Task<SignInOutcome> CompleteSignInAsync(string assertionResponse, string returnUrl, CancellationToken cancellationToken)
    {
        // Get assertion ID from session; it is used once.
        var assertionId = HttpContext.Session.GetString("fido2.assertionId");
        if (string.IsNullOrEmpty(assertionId))
        {
            return new SignInOutcome("Session expired. Please try again.", SessionExpired: true);
        }
        HttpContext.Session.Remove("fido2.assertionId");

        if (_tenantContext?.Tenant?.EnablePasskey == false)
        {
            return new SignInOutcome("Passkey sign-in is not available.");
        }

        try
        {
            var result = await _fido2Service.VerifyAssertionAsync(assertionId, assertionResponse, cancellationToken);
            if (!result.Succeeded)
            {
                _logger.LogWarning("Passkey verification failed: {Error}", result.Error);
                await RecordFailureAsync(result.UserId ?? "passkey", result.Error ?? "passkey_verification_failed", returnUrl, cancellationToken);
                return new SignInOutcome(result.ErrorDescription ?? "Passkey verification failed.");
            }

            var user = await _userService.FindByIdAsync(result.UserId!, cancellationToken);
            if (user == null || !user.IsActive)
            {
                await RecordFailureAsync(result.UserId!, user == null ? "user_not_found" : "user_deactivated", returnUrl, cancellationToken);
                return new SignInOutcome(user == null ? "User not found." : "Your account has been deactivated.");
            }

            _logger.LogInformation("User {UserId} logged in via passkey", user.Id);

            await SignInUserAsync(user.Id);
            await _userService.RecordLoginAsync(user.Id, cancellationToken);

            if (_signInAuditor != null)
            {
                await _signInAuditor.RecordSuccessAsync(new SignInAudit
                {
                    SubjectId = user.Id,
                    Username = user.Username ?? user.Email ?? user.Id,
                    TenantId = user.TenantId,
                    AuthenticationMethod = AuthenticationMethod,
                    ReturnUrl = returnUrl
                }, cancellationToken);
            }

            return new SignInOutcome(null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Passkey verification error");
            return new SignInOutcome("An error occurred during authentication. Please try again.");
        }
    }

    private Task RecordFailureAsync(string username, string reason, string returnUrl, CancellationToken cancellationToken) =>
        _signInAuditor?.RecordFailureAsync(new SignInFailureAudit
        {
            Username = username,
            FailureReason = reason,
            ReturnUrl = returnUrl
        }, cancellationToken) ?? Task.CompletedTask;

    /// <summary>
    /// Issues the session cookie the way a journey sign-in does (SignInManager and the claims principal factory),
    /// stamped with how and when it signed in, so <c>max_age</c>, session MFA reuse and the tokens see a passkey.
    /// </summary>
    private async Task SignInUserAsync(string userId)
    {
        var signInManager = HttpContext.RequestServices.GetService<SignInManager<OlusoUser>>();
        var userManager = HttpContext.RequestServices.GetService<UserManager<OlusoUser>>();
        var identityUser = userManager == null ? null : await userManager.FindByIdAsync(userId);
        var signInClaims = SessionAuthenticationClaims.Create(PasskeyMethods, DateTimeOffset.UtcNow);

        if (signInManager != null && identityUser != null)
        {
            await signInManager.SignInWithClaimsAsync(identityUser, isPersistent: true, signInClaims);
            return;
        }

        // Hosts without ASP.NET Identity: build the principal from the user service.
        var claims = (await _userService.GetClaimsAsync(userId)).ToList();
        if (_tenantContext?.HasTenant == true && !string.IsNullOrEmpty(_tenantContext.TenantId))
        {
            claims.RemoveAll(c => c.Type == "tenant_id");
            claims.Add(new Claim("tenant_id", _tenantContext.TenantId));
        }
        claims.RemoveAll(c => c.Type is "amr" or "auth_time");
        claims.AddRange(signInClaims);

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Oluso"));
        await HttpContext.SignInAsync(IdentityConstants.ApplicationScheme, principal, new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30)
        });
    }

    /// <summary>
    /// Where to go after signing in. A local path, or an absolute URL on this host with its scheme (the
    /// sign-in page links here with the authorize callback, which then checks the client's redirect_uri
    /// exactly). Otherwise only an absolute URL that is exactly a registered redirect URI of an enabled client
    /// in the tenant. Anything else goes home.
    /// </summary>
    private async Task<string> SafeReturnUrlAsync(string? returnUrl, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(returnUrl))
            return "/";

        if (Url.IsLocalUrl(returnUrl))
            return returnUrl;

        if (Uri.TryCreate(returnUrl, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp))
        {
            var request = HttpContext.Request;
            if (string.Equals(uri.Scheme, request.Scheme, StringComparison.OrdinalIgnoreCase)
                && string.Equals(uri.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase))
                return returnUrl;

            if (_clients != null && await _clients.IsRegisteredRedirectUriAsync(returnUrl, cancellationToken))
                return returnUrl;
        }

        _logger.LogWarning("Passkey login return URL {ReturnUrl} is not local or a registered redirect URI; using /", returnUrl);
        return "/";
    }

    private static string Html(string? value) => System.Net.WebUtility.HtmlEncode(value ?? string.Empty);

    private static string GenerateLoginPage(string optionsJson, string returnUrl)
    {
        var escapedReturnUrl = Html(Uri.EscapeDataString(returnUrl));
        var returnUrlValue = Html(returnUrl);
        return $$"""
<!DOCTYPE html>
<html>
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>Sign in with Passkey</title>
    <style>
        * { box-sizing: border-box; margin: 0; padding: 0; }
        body {
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
            background: linear-gradient(135deg, #0f172a 0%, #1e293b 100%);
            min-height: 100vh;
            display: flex;
            align-items: center;
            justify-content: center;
            padding: 1rem;
        }
        .card {
            background: white;
            border-radius: 16px;
            padding: 2.5rem;
            max-width: 400px;
            width: 100%;
            text-align: center;
            box-shadow: 0 25px 50px -12px rgba(0, 0, 0, 0.25);
        }
        .icon {
            width: 64px;
            height: 64px;
            margin: 0 auto 1.5rem;
            color: #f97316;
        }
        h1 { font-size: 1.5rem; margin-bottom: 0.5rem; color: #1a1a2e; }
        .subtitle { color: #6c757d; margin-bottom: 2rem; }
        .status { padding: 1rem; border-radius: 8px; margin-bottom: 1rem; }
        .status.loading { background: #f0f9ff; color: #0369a1; }
        .status.error { background: #fef2f2; color: #991b1b; }
        .spinner {
            width: 24px; height: 24px;
            border: 3px solid #e5e7eb;
            border-top-color: #f97316;
            border-radius: 50%;
            animation: spin 1s linear infinite;
            margin: 0 auto 1rem;
        }
        @keyframes spin { to { transform: rotate(360deg); } }
        .btn {
            display: inline-block;
            padding: 0.75rem 1.5rem;
            border-radius: 8px;
            text-decoration: none;
            font-weight: 500;
            cursor: pointer;
            border: none;
            font-size: 1rem;
        }
        .btn-primary {
            background: linear-gradient(135deg, #f97316 0%, #c2410c 100%);
            color: white;
        }
        .btn-secondary {
            background: #f3f4f6;
            color: #374151;
            margin-top: 0.5rem;
        }
        .hidden { display: none; }
    </style>
</head>
<body>
    <div class="card">
        <svg class="icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
            <path stroke-linecap="round" stroke-linejoin="round" d="M21 2l-2 2m-7.61 7.61a5.5 5.5 0 1 1-7.778 7.778 5.5 5.5 0 0 1 7.777-7.777zm0 0L15.5 7.5m0 0l3 3L22 7l-3-3m-3.5 3.5L19 4" />
        </svg>
        <h1>{{UiStringDefaults.Resolve("passkey_assert_title")}}</h1>
        <p class="subtitle">{{UiStringDefaults.Resolve("passkey_assert_description")}}</p>

        <div id="loading" class="status loading">
            <div class="spinner"></div>
            {{UiStringDefaults.Resolve("passkey_assert_waiting")}}
        </div>

        <div id="error" class="status error hidden"></div>

        <button id="retryBtn" class="btn btn-primary hidden" onclick="startAssertion()">{{UiStringDefaults.Resolve("passkey_assert_retry")}}</button>
        <a href="/account/login?returnUrl={{escapedReturnUrl}}" class="btn btn-secondary" style="display: block;">{{UiStringDefaults.Resolve("fido2_login_password_fallback")}}</a>

        <form id="verifyForm" method="post" action="/fido2/login" class="hidden">
            <input type="hidden" name="assertionResponse" id="assertionResponse" />
            <input type="hidden" name="returnUrl" value="{{returnUrlValue}}" />
        </form>
    </div>

    <script>
        const options = {{optionsJson}};

        function base64UrlToArrayBuffer(base64url) {
            const base64 = base64url.replace(/-/g, '+').replace(/_/g, '/');
            const pad = base64.length % 4;
            const padded = pad ? base64 + '===='.slice(pad) : base64;
            const binary = atob(padded);
            const bytes = new Uint8Array(binary.length);
            for (let i = 0; i < binary.length; i++) bytes[i] = binary.charCodeAt(i);
            return bytes.buffer;
        }

        function arrayBufferToBase64Url(buffer) {
            const bytes = new Uint8Array(buffer);
            let binary = '';
            for (let i = 0; i < bytes.byteLength; i++) binary += String.fromCharCode(bytes[i]);
            return btoa(binary).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
        }

        async function startAssertion() {
            const loading = document.getElementById('loading');
            const error = document.getElementById('error');
            const retryBtn = document.getElementById('retryBtn');

            loading.classList.remove('hidden');
            error.classList.add('hidden');
            retryBtn.classList.add('hidden');

            try {
                const publicKeyOptions = {
                    challenge: base64UrlToArrayBuffer(options.challenge),
                    timeout: options.timeout || 60000,
                    rpId: options.rpId,
                    userVerification: options.userVerification || 'preferred'
                };

                if (options.allowCredentials) {
                    publicKeyOptions.allowCredentials = options.allowCredentials.map(c => ({
                        type: c.type,
                        id: base64UrlToArrayBuffer(c.id),
                        transports: c.transports
                    }));
                }

                const credential = await navigator.credentials.get({ publicKey: publicKeyOptions });

                const response = {
                    id: credential.id,
                    rawId: arrayBufferToBase64Url(credential.rawId),
                    type: credential.type,
                    response: {
                        authenticatorData: arrayBufferToBase64Url(credential.response.authenticatorData),
                        clientDataJSON: arrayBufferToBase64Url(credential.response.clientDataJSON),
                        signature: arrayBufferToBase64Url(credential.response.signature),
                        userHandle: credential.response.userHandle ? arrayBufferToBase64Url(credential.response.userHandle) : null
                    }
                };

                document.getElementById('assertionResponse').value = JSON.stringify(response);
                document.getElementById('verifyForm').submit();

            } catch (err) {
                loading.classList.add('hidden');
                error.classList.remove('hidden');
                retryBtn.classList.remove('hidden');

                if (err.name === 'NotAllowedError') {
                    error.textContent = 'Authentication was cancelled or timed out. Please try again.';
                } else if (err.name === 'SecurityError') {
                    error.textContent = 'Security error. Please ensure you are on a secure connection.';
                } else {
                    error.textContent = 'Failed to authenticate: ' + err.message;
                }
            }
        }

        // Auto-start on page load
        if (window.PublicKeyCredential) {
            startAssertion();
        } else {
            document.getElementById('loading').classList.add('hidden');
            document.getElementById('error').classList.remove('hidden');
            document.getElementById('error').textContent = 'Passkeys are not supported in this browser.';
        }
    </script>
</body>
</html>
""";
    }

    private static string GenerateErrorPage(string message, string returnUrl)
    {
        var escapedReturnUrl = Html(Uri.EscapeDataString(returnUrl));
        var encodedMessage = Html(message);
        return $$"""
<!DOCTYPE html>
<html>
<head>
    <meta charset="utf-8" />
    <meta name="viewport" content="width=device-width, initial-scale=1" />
    <title>Authentication Error</title>
    <style>
        * { box-sizing: border-box; margin: 0; padding: 0; }
        body {
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif;
            background: linear-gradient(135deg, #0f172a 0%, #1e293b 100%);
            min-height: 100vh;
            display: flex;
            align-items: center;
            justify-content: center;
            padding: 1rem;
        }
        .card {
            background: white;
            border-radius: 16px;
            padding: 2.5rem;
            max-width: 400px;
            width: 100%;
            text-align: center;
            box-shadow: 0 25px 50px -12px rgba(0, 0, 0, 0.25);
        }
        .icon { width: 64px; height: 64px; margin: 0 auto 1.5rem; color: #dc2626; }
        h1 { font-size: 1.5rem; margin-bottom: 1rem; color: #1a1a2e; }
        .message { color: #6c757d; margin-bottom: 2rem; }
        .btn {
            display: inline-block;
            padding: 0.75rem 1.5rem;
            border-radius: 8px;
            text-decoration: none;
            font-weight: 500;
            margin: 0.25rem;
        }
        .btn-primary {
            background: linear-gradient(135deg, #f97316 0%, #c2410c 100%);
            color: white;
        }
        .btn-secondary { background: #f3f4f6; color: #374151; }
    </style>
</head>
<body>
    <div class="card">
        <svg class="icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5">
            <path stroke-linecap="round" stroke-linejoin="round" d="M12 9v3.75m9-.75a9 9 0 11-18 0 9 9 0 0118 0zm-9 3.75h.008v.008H12v-.008z" />
        </svg>
        <h1>{{UiStringDefaults.Resolve("error_title")}}</h1>
        <p class="message">{{encodedMessage}}</p>
        <a href="/fido2/login?returnUrl={{escapedReturnUrl}}" class="btn btn-primary">Try Again</a>
        <a href="/account/login?returnUrl={{escapedReturnUrl}}" class="btn btn-secondary">Back to Login</a>
    </div>
</body>
</html>
""";
    }
}

public class PasskeyVerifyRequest
{
    public string AssertionResponse { get; set; } = null!;
    public string? ReturnUrl { get; set; }
}

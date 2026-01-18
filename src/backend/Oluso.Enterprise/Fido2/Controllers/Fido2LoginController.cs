using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;

namespace Oluso.Enterprise.Fido2.Controllers;

/// <summary>
/// Handles the passkey login flow with a self-contained UI page.
/// This decouples passkey authentication from the main login page.
/// </summary>
[Route("fido2")]
[AllowAnonymous]
public class Fido2LoginController : Controller
{
    private readonly IFido2Service _fido2Service;
    private readonly IOlusoUserService _userService;
    private readonly ITenantContext? _tenantContext;
    private readonly ILogger<Fido2LoginController> _logger;

    public Fido2LoginController(
        IFido2Service fido2Service,
        IOlusoUserService userService,
        ILogger<Fido2LoginController> logger,
        ITenantContext? tenantContext = null)
    {
        _fido2Service = fido2Service;
        _userService = userService;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>
    /// Display the passkey login page and initiate WebAuthn assertion
    /// </summary>
    [HttpGet("login")]
    public async Task<IActionResult> Login(
        [FromQuery] string? returnUrl = null,
        CancellationToken cancellationToken = default)
    {
        // If already authenticated, redirect
        if (User.Identity?.IsAuthenticated == true)
        {
            return Redirect(GetSafeReturnUrl(returnUrl));
        }

        try
        {
            // Create assertion options for usernameless (discoverable credential) flow
            var options = await _fido2Service.CreateAssertionOptionsAsync(null, cancellationToken);

            // Store assertion ID in session
            HttpContext.Session.SetString("fido2.assertionId", options.AssertionId);

            // Serialize options for JavaScript
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
        // Get assertion ID from session
        var assertionId = HttpContext.Session.GetString("fido2.assertionId");
        if (string.IsNullOrEmpty(assertionId))
        {
            return Content(GenerateErrorPage("Session expired. Please try again.", returnUrl), "text/html");
        }

        try
        {
            var result = await _fido2Service.VerifyAssertionAsync(assertionId, assertionResponse, cancellationToken);

            // Clear session
            HttpContext.Session.Remove("fido2.assertionId");

            if (!result.Succeeded)
            {
                _logger.LogWarning("Passkey verification failed: {Error}", result.Error);
                return Content(GenerateErrorPage(result.ErrorDescription ?? "Passkey verification failed.", returnUrl), "text/html");
            }

            // Get user
            var user = await _userService.FindByIdAsync(result.UserId!, cancellationToken);
            if (user == null)
            {
                return Content(GenerateErrorPage("User not found.", returnUrl), "text/html");
            }

            if (!user.IsActive)
            {
                return Content(GenerateErrorPage("Your account has been deactivated.", returnUrl), "text/html");
            }

            _logger.LogInformation("User {UserId} logged in via passkey", user.Id);

            // Sign in the user
            await SignInUserAsync(user.Id);

            // Redirect to return URL
            return Redirect(GetSafeReturnUrl(returnUrl));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Passkey verification error");
            return Content(GenerateErrorPage("An error occurred during authentication. Please try again.", returnUrl), "text/html");
        }
    }

    /// <summary>
    /// API endpoint for JavaScript-based verification (returns JSON)
    /// </summary>
    [HttpPost("login/verify")]
    public async Task<IActionResult> VerifyLoginJson(
        [FromBody] PasskeyVerifyRequest request,
        CancellationToken cancellationToken = default)
    {
        // Get assertion ID from session
        var assertionId = HttpContext.Session.GetString("fido2.assertionId");
        if (string.IsNullOrEmpty(assertionId))
        {
            return BadRequest(new { error = "Session expired", redirect = $"/fido2/login?returnUrl={Uri.EscapeDataString(request.ReturnUrl ?? "/")}" });
        }

        try
        {
            var result = await _fido2Service.VerifyAssertionAsync(assertionId, request.AssertionResponse, cancellationToken);

            // Clear session
            HttpContext.Session.Remove("fido2.assertionId");

            if (!result.Succeeded)
            {
                _logger.LogWarning("Passkey verification failed: {Error}", result.Error);
                return BadRequest(new { error = result.ErrorDescription ?? "Passkey verification failed" });
            }

            // Get user
            var user = await _userService.FindByIdAsync(result.UserId!, cancellationToken);
            if (user == null)
            {
                return BadRequest(new { error = "User not found" });
            }

            if (!user.IsActive)
            {
                return BadRequest(new { error = "Your account has been deactivated" });
            }

            _logger.LogInformation("User {UserId} logged in via passkey", user.Id);

            // Sign in the user
            await SignInUserAsync(user.Id);

            return Ok(new { success = true, redirect = GetSafeReturnUrl(request.ReturnUrl) });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Passkey verification error");
            return BadRequest(new { error = "An error occurred during authentication" });
        }
    }

    private async Task SignInUserAsync(string userId)
    {
        var user = await _userService.FindByIdAsync(userId);
        if (user == null) return;

        var claims = (await _userService.GetClaimsAsync(userId)).ToList();

        // Add tenant_id claim if in a tenant context
        if (_tenantContext?.HasTenant == true && !string.IsNullOrEmpty(_tenantContext.TenantId))
        {
            claims.RemoveAll(c => c.Type == "tenant_id");
            claims.Add(new Claim("tenant_id", _tenantContext.TenantId));
        }

        var identity = new ClaimsIdentity(claims, "Oluso");
        var principal = new ClaimsPrincipal(identity);

        var authProperties = new AuthenticationProperties
        {
            IsPersistent = true,
            ExpiresUtc = DateTimeOffset.UtcNow.AddDays(30)
        };

        await HttpContext.SignInAsync(IdentityConstants.ApplicationScheme, principal, authProperties);
    }

    private string GetSafeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrEmpty(returnUrl))
            return "/";

        // Only allow relative URLs or same-host URLs
        if (Url.IsLocalUrl(returnUrl))
            return returnUrl;

        if (Uri.TryCreate(returnUrl, UriKind.Absolute, out var uri))
        {
            var request = HttpContext.Request;
            if (string.Equals(uri.Host, request.Host.Host, StringComparison.OrdinalIgnoreCase))
                return returnUrl;
        }

        return "/";
    }

    private static string GenerateLoginPage(string optionsJson, string? returnUrl)
    {
        var escapedReturnUrl = Uri.EscapeDataString(returnUrl ?? "/");
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
        <h1>Sign in with Passkey</h1>
        <p class="subtitle">Use your fingerprint, face, or security key</p>

        <div id="loading" class="status loading">
            <div class="spinner"></div>
            Waiting for passkey...
        </div>

        <div id="error" class="status error hidden"></div>

        <button id="retryBtn" class="btn btn-primary hidden" onclick="startAssertion()">Try Again</button>
        <a href="/account/login?returnUrl={{escapedReturnUrl}}" class="btn btn-secondary" style="display: block;">Back to Login</a>

        <form id="verifyForm" method="post" action="/fido2/login" class="hidden">
            <input type="hidden" name="assertionResponse" id="assertionResponse" />
            <input type="hidden" name="returnUrl" value="{{returnUrl ?? "/"}}" />
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

    private static string GenerateErrorPage(string message, string? returnUrl)
    {
        var escapedReturnUrl = Uri.EscapeDataString(returnUrl ?? "/");
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
        <h1>Authentication Error</h1>
        <p class="message">{{message}}</p>
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

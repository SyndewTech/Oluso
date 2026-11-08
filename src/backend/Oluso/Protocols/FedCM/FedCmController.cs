using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Core.Services;
using Microsoft.AspNetCore.RateLimiting;
using Oluso.Core.Security;

namespace Oluso.Protocols.FedCM;

/// <summary>
/// FedCM (Federated Credential Management) API endpoints.
/// Enables browser-native account picker for federated sign-in without
/// third-party cookies or full-page redirects.
///
/// Spec: https://fedidcg.github.io/FedCM/
/// </summary>
[ApiController]
[ApiExplorerSettings(IgnoreApi = true)]
public class FedCmController : ControllerBase
{
    private readonly IOlusoUserService _userService;
    private readonly IAuthorizationCodeIssuer _codes;
    private readonly ITenantSettingsProvider _settingsProvider;
    private readonly IIssuerResolver _issuerResolver;
    private readonly ILogger<FedCmController> _logger;

    public FedCmController(
        IOlusoUserService userService,
        IAuthorizationCodeIssuer codes,
        ITenantSettingsProvider settingsProvider,
        IIssuerResolver issuerResolver,
        ILogger<FedCmController> logger)
    {
        _userService = userService;
        _codes = codes;
        _settingsProvider = settingsProvider;
        _issuerResolver = issuerResolver;
        _logger = logger;
    }

    /// <summary>
    /// Well-known manifest for FedCM.
    /// Browsers check this to discover the IdP config URL.
    /// </summary>
    [HttpGet("/.well-known/web-identity")]
    public async Task<IActionResult> GetWebIdentity(CancellationToken cancellationToken)
    {
        var issuer = await _issuerResolver.GetIssuerAsync(cancellationToken);
        var baseUrl = issuer.TrimEnd('/');

        return Ok(new
        {
            provider_urls = new[] { $"{baseUrl}/fedcm/config.json" }
        });
    }

    /// <summary>
    /// IdP configuration for FedCM.
    /// Contains endpoint URLs, branding, and login URL.
    /// </summary>
    [HttpGet("fedcm/config.json")]
    public async Task<IActionResult> GetConfig(CancellationToken cancellationToken)
    {
        var issuer = await _issuerResolver.GetIssuerAsync(cancellationToken);
        var baseUrl = issuer.TrimEnd('/');
        var branding = await _settingsProvider.GetBrandingSettingsAsync(cancellationToken);

        // Set FedCM-required headers
        Response.Headers["Access-Control-Allow-Origin"] = "*";

        return Ok(new
        {
            accounts_endpoint = $"{baseUrl}/fedcm/accounts",
            client_metadata_endpoint = $"{baseUrl}/fedcm/client-metadata",
            id_assertion_endpoint = $"{baseUrl}/fedcm/assertion",
            login_url = $"{baseUrl}/account/login",
            branding = new
            {
                background_color = branding?.BackgroundColor ?? "#ffffff",
                color = branding?.PrimaryColor ?? "#1a56db",
                icons = branding?.LogoUrl != null
                    ? new[] { new { url = branding.LogoUrl, size = 32 } }
                    : Array.Empty<object>()
            }
        });
    }

    /// <summary>
    /// Returns accounts for the authenticated user.
    /// The browser sends this request with cookies (SameSite=None, Secure).
    /// The browser displays these accounts in the FedCM account picker.
    /// </summary>
    [HttpGet("fedcm/accounts")]
    public async Task<IActionResult> GetAccounts(CancellationToken cancellationToken)
    {
        // FedCM requires Sec-Fetch-Dest: webidentity header
        if (!IsFedCmRequest())
        {
            return StatusCode(403, new { error = "invalid_request", error_description = "Not a FedCM request" });
        }

        // Authenticate from cookie
        var authResult = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (!authResult.Succeeded || authResult.Principal == null)
        {
            // Return empty accounts — browser will show "Sign in to IdP" button
            return Ok(new { accounts = Array.Empty<object>() });
        }

        var userId = authResult.Principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? authResult.Principal.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userId))
        {
            return Ok(new { accounts = Array.Empty<object>() });
        }

        var user = await _userService.FindByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            return Ok(new { accounts = Array.Empty<object>() });
        }

        _logger.LogDebug("FedCM accounts requested for user {UserId}", userId);

        return Ok(new
        {
            accounts = new[]
            {
                new
                {
                    id = user.Id,
                    name = user.DisplayName ?? user.Username ?? user.Email ?? "",
                    email = user.Email ?? "",
                    given_name = user.FirstName ?? "",
                    picture = user.Picture ?? "",
                    approved_clients = Array.Empty<string>(),
                    login_hints = new[] { user.Email, user.Username }
                        .Where(h => !string.IsNullOrEmpty(h)).ToArray()
                }
            }
        });
    }

    /// <summary>
    /// Client metadata endpoint.
    /// Returns privacy policy and terms of service URLs for the RP.
    /// </summary>
    [HttpGet("fedcm/client-metadata")]
    public async Task<IActionResult> GetClientMetadata(
        [FromQuery] string client_id,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(client_id))
        {
            return BadRequest(new { error = "invalid_request" });
        }

        // Only for clients that can sign in with FedCM: existing, enabled, allowed the code flow and with a
        // registered page origin (the RP's, when the browser names it). Anything else looks like no client.
        var origin = Request.Headers.Origin.ToString();
        var validation = await _codes.ValidatePageClientAsync(
            client_id, string.IsNullOrEmpty(origin) ? null : origin, DirectAuthorizationRequest.FedCm, cancellationToken);
        if (!validation.IsValid)
        {
            return NotFound(new { error = "invalid_client" });
        }
        var client = validation.Client!;

        return Ok(new
        {
            privacy_policy_url = client.ClientUri ?? "",
            terms_of_service_url = client.ClientUri ?? ""
        });
    }

    /// <summary>
    /// ID assertion endpoint — the core of FedCM.
    /// Called by the browser after the user selects an account. The token it returns is an authorization
    /// code issued through <see cref="IAuthorizationCodeIssuer"/>, like every sign-in that returns a code
    /// to a page: the RP's origin (the request's Origin) must be registered for the client, the scopes
    /// allowed for it, and the code bound to a PKCE S256 challenge the SDK passes in the provider's
    /// <c>params</c> (<c>code_challenge</c>, <c>code_challenge_method</c>, <c>scope</c>). The SDK redeems it
    /// with <c>redirect_uri=fedcm</c>. The existing session's id, methods and time travel with the code.
    /// </summary>
    [HttpPost("fedcm/assertion")]
    [EnableRateLimiting(SignInRateLimitPolicies.SignIn)]
    public async Task<IActionResult> IdAssertion(CancellationToken cancellationToken)
    {
        // FedCM requires Sec-Fetch-Dest: webidentity header
        if (!IsFedCmRequest())
        {
            return StatusCode(403, new { error = "invalid_request", error_description = "Not a FedCM request" });
        }

        // Read form-encoded body (FedCM spec uses application/x-www-form-urlencoded)
        var clientId = Request.Form["client_id"].FirstOrDefault();
        var accountId = Request.Form["account_id"].FirstOrDefault();
        var nonce = Request.Form["nonce"].FirstOrDefault();
        var parameters = ReadParams(Request.Form["params"].FirstOrDefault());

        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(accountId))
        {
            return BadRequest(new { error = "invalid_request", error_description = "client_id and account_id are required" });
        }

        var authorization = new DirectAuthorizationRequest
        {
            ClientId = clientId,
            Scope = parameters.GetValueOrDefault("scope"),
            CodeChallenge = parameters.GetValueOrDefault("code_challenge"),
            CodeChallengeMethod = parameters.GetValueOrDefault("code_challenge_method"),
            Origin = Request.Headers.Origin.ToString(),
            RedirectUri = DirectAuthorizationRequest.FedCm,
            Nonce = nonce
        };
        var validation = await _codes.ValidateAsync(authorization, cancellationToken);
        if (!validation.IsValid)
        {
            var body = new { error = validation.Error, error_description = validation.ErrorDescription };
            return validation.OriginRejected ? StatusCode(403, body) : BadRequest(body);
        }

        // Authenticate from cookie (browser sends cookies with FedCM requests)
        var authResult = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (!authResult.Succeeded || authResult.Principal == null)
        {
            return Unauthorized(new { error = "access_denied", error_description = "Not authenticated" });
        }

        var session = authResult.Principal;
        var userId = session.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
            ?? session.FindFirst("sub")?.Value;

        if (string.IsNullOrEmpty(userId) || userId != accountId)
        {
            return Unauthorized(new { error = "access_denied", error_description = "Account mismatch" });
        }

        DateTimeOffset? authTime = long.TryParse(session.FindFirst("auth_time")?.Value, out var unix)
            ? DateTimeOffset.FromUnixTimeSeconds(unix)
            : null;

        // The user signed in earlier (that sign-in was audited); this reuses the session, like /authorize does.
        var issued = await _codes.IssueAsync(authorization, new SignInCompletion
        {
            UserId = userId,
            AuthenticationMethod = "fedcm",
            Amr = session.FindAll("amr").Select(c => c.Value).ToList(),
            Acr = session.FindFirst("acr")?.Value,
            AuthTime = authTime,
            SessionId = session.FindFirst("sid")?.Value,
            IsNewSignIn = false
        }, cancellationToken);

        if (!issued.Succeeded)
        {
            var body = new { error = issued.Error, error_description = issued.ErrorDescription };
            return issued.OriginRejected ? StatusCode(403, body) : BadRequest(body);
        }

        // FedCM spec: return { token: "..." } — the token is the auth code, which the SDK exchanges
        // for tokens at the token endpoint.
        return Ok(new { token = issued.Code });
    }

    /// <summary>The provider's <c>params</c> (a JSON object the RP passed to navigator.credentials.get), as strings.</summary>
    private static Dictionary<string, string> ReadParams(string? json)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(json)) return values;
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object) return values;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    values[property.Name] = property.Value.GetString()!;
                }
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Unreadable params: treated as none, so the PKCE check rejects the request.
        }
        return values;
    }

    private bool IsFedCmRequest()
    {
        // FedCM requests include Sec-Fetch-Dest: webidentity
        var secFetchDest = Request.Headers["Sec-Fetch-Dest"].FirstOrDefault();
        return string.Equals(secFetchDest, "webidentity", StringComparison.OrdinalIgnoreCase);
    }
}

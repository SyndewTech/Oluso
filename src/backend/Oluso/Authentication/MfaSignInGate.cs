using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Oluso.Core.Authentication;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Core.Services;

namespace Oluso.Authentication;

/// <summary>
/// Stands between a first-factor sign-in (password, external provider) and the session cookie. When the sign-in still
/// needs a second factor (<see cref="SecondFactorRequirement"/>), no cookie is issued: the principal is held
/// server-side and the browser goes to <c>/account/mfa</c>, which verifies or sets up a second factor and then issues
/// the session. The browser holds only the random id of the held sign-in, in an HTTP-only cookie.
/// </summary>
public sealed class MfaSignInGate
{
    public const string ProtocolName = "mfa_sign_in";
    public const string CookieName = ".Oluso.MfaSignIn";
    public const string PagePath = "/account/mfa";

    /// <summary>How long a held sign-in waits for its second factor.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    private readonly IProtocolStateStore _states;
    private readonly IOlusoUserService _users;
    private readonly ITenantContext? _tenantContext;
    private readonly ILogger<MfaSignInGate> _logger;

    public MfaSignInGate(
        IProtocolStateStore states,
        IOlusoUserService users,
        ILogger<MfaSignInGate> logger,
        ITenantContext? tenantContext = null)
    {
        _states = states;
        _users = users;
        _logger = logger;
        _tenantContext = tenantContext;
    }

    /// <summary>
    /// Whether the user signing in as <paramref name="principal"/> must pass a second factor first: the tenant
    /// requires one, or it is a password sign-in by a user who turned MFA on, and the sign-in has none.
    /// </summary>
    public async Task<bool> IsSecondFactorMissingAsync(ClaimsPrincipal principal, CancellationToken cancellationToken = default)
    {
        var userId = UserIdOf(principal);
        var user = string.IsNullOrEmpty(userId) ? null : await _users.FindByIdAsync(userId, cancellationToken);
        return SecondFactorRequirement.IsMissing(
            _tenantContext?.Tenant?.RequireMfa == true,
            user?.TwoFactorEnabled == true,
            principal.FindAll("amr").Select(c => c.Value));
    }

    /// <summary>
    /// Holds the sign-in and returns the redirect to the MFA page when a second factor is missing; null when the
    /// caller may issue the session itself.
    /// </summary>
    public async Task<IActionResult?> HoldIfSecondFactorMissingAsync(
        HttpContext http,
        ClaimsPrincipal principal,
        AuthenticationProperties properties,
        string? returnUrl,
        string authenticationMethod,
        string? clientId = null,
        CancellationToken cancellationToken = default,
        bool passwordChangeRequired = false,
        string? username = null)
    {
        if (!await IsSecondFactorMissingAsync(principal, cancellationToken))
        {
            return null;
        }

        var identity = (ClaimsIdentity)principal.Identity!;
        var pending = new PendingMfaSignIn
        {
            UserId = UserIdOf(principal)!,
            AuthenticationType = identity.AuthenticationType ?? IdentityConstants.ApplicationScheme,
            Claims = principal.Claims.Select(c => new PendingMfaSignIn.HeldClaim(c.Type, c.Value, c.ValueType)).ToList(),
            IsPersistent = properties.IsPersistent,
            ExpiresUtc = properties.ExpiresUtc,
            ReturnUrl = string.IsNullOrEmpty(returnUrl) ? "/" : returnUrl,
            AuthenticationMethod = authenticationMethod,
            ClientId = clientId,
            PasswordChangeRequired = passwordChangeRequired,
            Username = username,
        };

        await SaveAsync(http, pending, previousId: null, cancellationToken);
        _logger.LogInformation("Sign-in for user {UserId} held for a second factor", pending.UserId);
        return new RedirectResult(PagePath);
    }

    /// <summary>The held sign-in this browser is completing, or null when there is none (or it expired).</summary>
    public async Task<(string Id, PendingMfaSignIn Pending)?> FindAsync(HttpContext http, CancellationToken cancellationToken = default)
    {
        if (!http.Request.Cookies.TryGetValue(CookieName, out var id) || string.IsNullOrEmpty(id))
        {
            return null;
        }

        var state = await _states.GetAsync(id, cancellationToken);
        if (state == null || state.ProtocolName != ProtocolName)
        {
            return null;
        }

        var pending = JsonSerializer.Deserialize<PendingMfaSignIn>(state.SerializedRequest);
        return pending == null ? null : (id, pending);
    }

    /// <summary>
    /// Stores <paramref name="pending"/> under a new id, replacing <paramref name="previousId"/>, so an id is good for
    /// one step only.
    /// </summary>
    public async Task SaveAsync(HttpContext http, PendingMfaSignIn pending, string? previousId, CancellationToken cancellationToken = default)
    {
        var id = await _states.StoreAsync(new ProtocolState
        {
            ProtocolName = ProtocolName,
            SerializedRequest = JsonSerializer.Serialize(pending),
            ClientId = pending.ClientId ?? string.Empty,
            TenantId = _tenantContext?.HasTenant == true ? _tenantContext.TenantId : null,
        }, Lifetime, cancellationToken);

        if (!string.IsNullOrEmpty(previousId))
        {
            await _states.RemoveAsync(previousId, cancellationToken);
        }

        http.Response.Cookies.Append(CookieName, id, new CookieOptions
        {
            HttpOnly = true,
            Secure = http.Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = PagePath,
            MaxAge = Lifetime,
            IsEssential = true,
        });
    }

    /// <summary>Ends the held sign-in, completed or abandoned.</summary>
    public async Task ClearAsync(HttpContext http, string id, CancellationToken cancellationToken = default)
    {
        await _states.RemoveAsync(id, cancellationToken);
        http.Response.Cookies.Delete(CookieName, new CookieOptions { Path = PagePath });
    }

    /// <summary>The session principal for the held sign-in once the second factor passed: its claims plus amr mfa.</summary>
    public static ClaimsPrincipal CompletedPrincipal(PendingMfaSignIn pending)
    {
        var claims = pending.Claims.Select(c => new Claim(c.Type, c.Value, c.ValueType)).ToList();
        if (!claims.Any(c => c.Type == "amr" && c.Value == "mfa"))
        {
            claims.Add(new Claim("amr", "mfa"));
        }
        return new ClaimsPrincipal(new ClaimsIdentity(claims, pending.AuthenticationType));
    }

    /// <summary>
    /// Whether an external provider's sign-in reports multi-factor authentication in its amr (RFC 8176): that sign-in
    /// had its second factor.
    /// </summary>
    public static bool ExternalProviderReportsMfa(ClaimsPrincipal externalPrincipal) =>
        externalPrincipal.Claims.Any(c =>
            (c.Type == "amr" || c.Type == "http://schemas.microsoft.com/claims/authnmethodsreferences")
            && c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains("mfa", StringComparer.OrdinalIgnoreCase));

    private static string? UserIdOf(ClaimsPrincipal principal) =>
        principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? principal.FindFirst("sub")?.Value;
}

/// <summary>A first-factor sign-in waiting for its second factor (<see cref="MfaSignInGate"/>).</summary>
public sealed class PendingMfaSignIn
{
    public required string UserId { get; init; }
    public required string AuthenticationType { get; init; }
    public required List<HeldClaim> Claims { get; init; }
    public bool IsPersistent { get; init; }
    public DateTimeOffset? ExpiresUtc { get; init; }
    public required string ReturnUrl { get; init; }

    /// <summary>The first factor's method for the sign-in audit (pwd, the provider's scheme, journey, ...).</summary>
    public required string AuthenticationMethod { get; init; }

    public string? ClientId { get; init; }

    /// <summary>
    /// The password matched but has expired: once the second factor passes, the user changes it instead of getting a
    /// session, and then signs in with the new one.
    /// </summary>
    public bool PasswordChangeRequired { get; init; }

    /// <summary>What the user signed in with, for the password change (password managers).</summary>
    public string? Username { get; init; }

    /// <summary>The MFA step's data between requests (setup in progress, the method just verified).</summary>
    public Dictionary<string, object> StepData { get; init; } = new();

    public sealed record HeldClaim(string Type, string Value, string ValueType);
}

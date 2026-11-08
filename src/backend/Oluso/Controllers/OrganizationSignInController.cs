using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Authentication.OrganizationSignIn;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Protocols;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Oluso.Protocols.Oidc;

namespace Oluso.Controllers;

/// <summary>
/// The three browser hops of "Sign in with your organisation":
/// <list type="number">
///   <item><c>start</c> on the tenant host sends the user through the tenant's
///     own sign-in (its identity providers, journey or standalone page, MFA)
///     with <c>complete</c> as the return URL;</item>
///   <item><c>complete</c> on the tenant host turns the tenant session into a
///     single-use hand-off code for the platform;</item>
///   <item><c>callback</c> on the platform host redeems the code and resumes the
///     platform login the way it started: a standalone login gets a platform
///     session and returns to the authorize callback; a journey login returns to
///     its journey, whose login step completes with the result (later steps such
///     as consent still run and the journey issues the session).</item>
/// </list>
/// </summary>
[ApiExplorerSettings(IgnoreApi = true)]
[AllowAnonymous]
[Route("account/org-handoff")]
public class OrganizationSignInController : Controller
{
    private const string GenericError = "This sign-in link is invalid or has expired. Please start again.";

    private readonly IAuthenticationCoordinator _coordinator;
    private readonly IJourneyOrchestrator _journeys;
    private readonly IJourneyPolicyStore _policies;
    private readonly IJourneyStateStore _journeyStates;
    private readonly ITenantContext _tenantContext;
    private readonly IOlusoUserService _users;
    private readonly ILogger<OrganizationSignInController> _logger;
    private readonly IOrganizationSignInService? _signIn;
    private readonly ISignInAuditor? _signInAuditor;

    public OrganizationSignInController(
        IAuthenticationCoordinator coordinator,
        IJourneyOrchestrator journeys,
        IJourneyPolicyStore policies,
        IJourneyStateStore journeyStates,
        ITenantContext tenantContext,
        IOlusoUserService users,
        ILogger<OrganizationSignInController> logger,
        IOrganizationSignInService? signIn = null,
        ISignInAuditor? signInAuditor = null)
    {
        _coordinator = coordinator;
        _journeys = journeys;
        _policies = policies;
        _journeyStates = journeyStates;
        _tenantContext = tenantContext;
        _users = users;
        _logger = logger;
        _signIn = signIn;
        _signInAuditor = signInAuditor;
    }

    /// <summary>Tenant host: run the tenant's normal sign-in for a pending platform request.</summary>
    [HttpGet("start")]
    public async Task<IActionResult> Start([FromQuery(Name = "request")] string? requestId, CancellationToken cancellationToken)
    {
        if (_signIn == null) return NotFound();

        var lookup = await _signIn.GetRequestForTenantHostAsync(HttpContext, requestId, OrganizationSignInEvent.StageStart, cancellationToken);
        if (lookup.IsExpired) return Redirect(_signIn.BuildExpiredUrl(lookup.Request!.Id));
        if (!lookup.Succeeded) return ErrorRedirect();
        var request = lookup.Request!;

        var completeUrl = _signIn.BuildCompleteUrl(request);

        var existing = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        if (existing.Succeeded && existing.Principal != null)
        {
            if (request.ForceFreshLogin || IsOlderThanMaxAge(existing, request.MaxAge))
            {
                await HttpContext.SignOutAsync(IdentityConstants.ApplicationScheme);
            }
            else
            {
                return Redirect(completeUrl);
            }
        }

        var tenant = _tenantContext.Tenant!;
        if (ResolveTenantUiMode(tenant) != UiMode.Journey)
        {
            return Redirect(BuildStandaloneLoginUrl(tenant, request, completeUrl));
        }

        var policy = !string.IsNullOrEmpty(tenant.DefaultSignInPolicyId)
            ? await _policies.GetBySlugAsync(tenant.DefaultSignInPolicyId, cancellationToken)
            : null;
        policy ??= await _policies.GetByTypeAsync(JourneyType.SignIn, cancellationToken);
        if (policy == null)
        {
            if (tenant.JourneyFallbackToStandard)
            {
                _logger.LogInformation("Organisation sign-in for tenant {TenantId}: no sign-in journey policy; using the standard login", tenant.Id);
                return Redirect(BuildStandaloneLoginUrl(tenant, request, completeUrl));
            }

            _logger.LogWarning("Organisation sign-in for tenant {TenantId}: no sign-in journey policy and fallback is off", tenant.Id);
            return Redirect("/error?error=server_error&message=" + Uri.EscapeDataString(Oluso.Protocols.AuthenticationCoordinator.NoJourneyMessage));
        }

        var properties = new Dictionary<string, object> { ["TenantId"] = tenant.Id };
        if (!string.IsNullOrEmpty(tenant.DefaultIdpScheme)) properties["DomainHint"] = tenant.DefaultIdpScheme;
        if (!string.IsNullOrEmpty(request.UiLocales)) properties["UiLocales"] = request.UiLocales;

        var journey = await _journeys.StartJourneyAsync(policy, new JourneyStartContext
        {
            HttpContext = HttpContext,
            ProtocolName = OrganizationSignInDefaults.ProtocolName,
            CorrelationId = request.Id,
            CallbackUrl = completeUrl,
            LoginHint = request.LoginHint,
            Properties = properties,
        }, cancellationToken);

        // The journey id is only known once it exists; carrying it on the return
        // URL lets completion read how the user authenticated (identity provider, MFA).
        await _journeyStates.SaveAsync(journey with { CallbackUrl = _signIn.BuildCompleteUrl(request, journey.Id) }, cancellationToken);

        return Redirect($"/journey/{journey.Id}");
    }

    /// <summary>Tenant host: the tenant sign-in finished; hand the user back to the platform.</summary>
    [HttpGet("complete")]
    public async Task<IActionResult> Complete(
        [FromQuery(Name = "correlation_id")] string? requestId,
        [FromQuery(Name = "journey")] string? journeyId,
        CancellationToken cancellationToken)
    {
        if (_signIn == null) return NotFound();

        // A tenant sign-in that outlasted the pending request (slow MFA, abandoned
        // tab) gets the "took too long" page rather than a code the platform could
        // no longer resume with.
        var lookup = await _signIn.GetRequestForTenantHostAsync(HttpContext, requestId, OrganizationSignInEvent.StageComplete, cancellationToken);
        if (lookup.IsExpired) return Redirect(_signIn.BuildExpiredUrl(lookup.Request!.Id));
        if (!lookup.Succeeded) return ErrorRedirect();
        var request = lookup.Request!;

        var session = await HttpContext.AuthenticateAsync(IdentityConstants.ApplicationScheme);
        var userId = session.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? session.Principal?.FindFirst("sub")?.Value;
        if (!session.Succeeded || string.IsNullOrEmpty(userId))
        {
            await _signIn.RecordCompleteFailureAsync(HttpContext, request, OrganizationSignInErrors.NotAuthenticated, cancellationToken);
            return ErrorRedirect();
        }

        var authentication = await DescribeAuthenticationAsync(request, userId, session.Principal!, journeyId, cancellationToken);
        var callbackUrl = await _signIn.IssueHandoffAsync(HttpContext, request, authentication, cancellationToken);
        return Redirect(callbackUrl);
    }

    /// <summary>Platform host: redeem the hand-off code and resume the original request.</summary>
    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery(Name = "code")] string? code, CancellationToken cancellationToken)
    {
        if (_signIn == null) return NotFound();

        var result = await _signIn.RedeemAsync(HttpContext, code, cancellationToken);
        if (result.Error == OrganizationSignInErrors.RequestExpired)
        {
            return Redirect($"{OrganizationSignInDefaults.ExpiredPath}?request={Uri.EscapeDataString(result.Request!.Id)}");
        }
        if (!result.Succeeded) return ErrorRedirect();

        var request = result.Request!;
        if (request.JourneyId != null)
        {
            await _signIn.FinishAsync(HttpContext, result, cancellationToken);
            return Url.IsLocalUrl(request.ReturnUrl) ? Redirect(request.ReturnUrl) : ErrorRedirect();
        }

        var userManager = HttpContext.RequestServices.GetRequiredService<UserManager<OlusoUser>>();
        var claimsFactory = HttpContext.RequestServices.GetRequiredService<IUserClaimsPrincipalFactory<OlusoUser>>();

        var user = await userManager.FindByIdAsync(result.UserId!);
        if (user == null) return ErrorRedirect();

        // Same principal the platform-host password and external sign-ins build:
        // the canonical claims factory (allowed_tenants, org claims, permissions)
        // with tenant_id pinned to the user's home tenant.
        var principal = await claimsFactory.CreateAsync(user);
        var identity = (ClaimsIdentity)principal.Identity!;
        if (!string.IsNullOrEmpty(user.TenantId))
        {
            foreach (var stale in identity.FindAll("tenant_id").ToList()) identity.RemoveClaim(stale);
            identity.AddClaim(new Claim("tenant_id", user.TenantId));
        }
        if (!string.IsNullOrEmpty(result.IdentityProvider))
        {
            identity.AddClaim(new Claim("idp", result.IdentityProvider));
        }
        foreach (var method in result.AuthenticationMethods.Prepend("external").Distinct(StringComparer.Ordinal))
        {
            identity.AddClaim(new Claim("amr", method));
        }

        await HttpContext.SignInAsync(IdentityConstants.ApplicationScheme, principal,
            new AuthenticationProperties { IsPersistent = false });
        await _users.RecordLoginAsync(user.Id, cancellationToken);

        if (_signInAuditor != null)
        {
            await _signInAuditor.RecordSuccessAsync(new SignInAudit
            {
                SubjectId = user.Id,
                Username = user.UserName ?? user.Email ?? user.Id,
                TenantId = string.IsNullOrEmpty(user.TenantId) ? null : user.TenantId,
                AuthenticationMethod = "organization",
                ClientId = request.ClientId,
                ReturnUrl = request.ReturnUrl,
            }, cancellationToken);
        }

        await _signIn.FinishAsync(HttpContext, result, cancellationToken);

        return Url.IsLocalUrl(request.ReturnUrl) ? Redirect(request.ReturnUrl) : ErrorRedirect();
    }

    /// <summary>
    /// The tenant's own journey/standalone choice, resolved by the same precedence
    /// as an SSO authorize request on its host: the tenant's journey setting is
    /// authoritative, and its SSO UI mode stands in for the <c>ui_mode</c> that
    /// "Login with SSO" landings forward. There is no client preference here, since
    /// the client belongs to the platform request, not to this tenant sign-in.
    /// </summary>
    private UiMode ResolveTenantUiMode(Tenant tenant) =>
        _coordinator.ResolveUiMode(new ProtocolContext
        {
            HttpContext = HttpContext,
            ProtocolName = OrganizationSignInDefaults.ProtocolName,
            Request = new OidcAuthorizeRequest { UiMode = tenant.DefaultSsoUiMode },
            Properties = new Dictionary<string, object> { ["TenantUseJourneyFlow"] = tenant.UseJourneyFlow },
        });

    private static string BuildStandaloneLoginUrl(Tenant tenant, OrganizationHandoffRequest request, string completeUrl)
    {
        var url = $"/account/login?returnUrl={Uri.EscapeDataString(completeUrl)}";
        if (!string.IsNullOrEmpty(request.LoginHint)) url += $"&loginHint={Uri.EscapeDataString(request.LoginHint)}";
        if (!string.IsNullOrEmpty(tenant.DefaultIdpScheme)) url += $"&domain_hint={Uri.EscapeDataString(tenant.DefaultIdpScheme)}";
        if (!string.IsNullOrEmpty(request.UiLocales)) url += $"&ui_locales={Uri.EscapeDataString(request.UiLocales)}";
        return url;
    }

    private static bool IsOlderThanMaxAge(AuthenticateResult session, int? maxAge)
    {
        if (!maxAge.HasValue) return false;

        DateTimeOffset? authTime = null;
        if (long.TryParse(session.Principal?.FindFirst("auth_time")?.Value, out var unix))
        {
            authTime = DateTimeOffset.FromUnixTimeSeconds(unix);
        }
        authTime ??= session.Properties?.IssuedUtc;

        return authTime.HasValue && DateTimeOffset.UtcNow - authTime.Value > TimeSpan.FromSeconds(maxAge.Value);
    }

    /// <summary>
    /// How the tenant authenticated the user. A journey records it in its state
    /// (identity provider, MFA); the standalone pages record it on the session.
    /// </summary>
    private async Task<OrganizationAuthentication> DescribeAuthenticationAsync(
        OrganizationHandoffRequest request, string userId, ClaimsPrincipal session, string? journeyId, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(journeyId))
        {
            var journey = await _journeyStates.GetAsync(journeyId, cancellationToken);
            if (journey != null
                && journey.Status == JourneyStatus.Completed
                && string.Equals(journey.TenantId, request.TenantId, StringComparison.Ordinal)
                && string.Equals(journey.CorrelationId, request.Id, StringComparison.Ordinal)
                && (journey.AuthenticatedUserId == userId || journey.UserId == userId))
            {
                return new OrganizationAuthentication(
                    userId,
                    ReadString(journey.Data, "idp"),
                    ReadStrings(journey.Data, "amr"));
            }
        }

        return new OrganizationAuthentication(
            userId,
            session.FindFirst("idp")?.Value,
            session.FindAll("amr").Select(c => c.Value).Distinct(StringComparer.Ordinal).ToList());
    }

    private static string? ReadString(IDictionary<string, object>? data, string key)
    {
        if (data == null || !data.TryGetValue(key, out var value)) return null;
        return value switch
        {
            string s when s.Length > 0 => s,
            JsonElement { ValueKind: JsonValueKind.String } el => el.GetString(),
            _ => null
        };
    }

    private static IReadOnlyList<string> ReadStrings(IDictionary<string, object>? data, string key)
    {
        if (data == null || !data.TryGetValue(key, out var value)) return Array.Empty<string>();
        IEnumerable<string?> values = value switch
        {
            string s => new[] { s },
            IEnumerable<string> list => list,
            JsonElement { ValueKind: JsonValueKind.String } el => new[] { el.GetString() },
            JsonElement { ValueKind: JsonValueKind.Array } el => el.EnumerateArray()
                .Where(e => e.ValueKind == JsonValueKind.String)
                .Select(e => e.GetString()),
            _ => Array.Empty<string>()
        };
        return values.Where(v => !string.IsNullOrEmpty(v)).Select(v => v!).Distinct(StringComparer.Ordinal).ToList();
    }

    // The link was invalid or expired: the way back is this host's sign-in page, to start again.
    private RedirectResult ErrorRedirect() =>
        Redirect("/error?error=access_denied&message=" + Uri.EscapeDataString(GenericError)
            + "&returnUrl=" + Uri.EscapeDataString("/account/login"));
}

using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Middleware;

/// <summary>
/// Layer 2 of 3 in the tenant-context pipeline. Sets the tenant from
/// a <b>bearer token's</b> <c>tenant_id</c> claim before authentication
/// runs, for the case where the host doesn't carry the tenant signal
/// (e.g. a SPA on the bare platform host calling its API).
///
/// <para>How it stays safe pre-auth: the JWT is parsed (not validated)
/// solely to <i>pick</i> which tenant's signing key + expected issuer
/// the downstream JWT bearer handler should use. The cryptographic
/// checks inside <c>UseAuthentication</c> still gate the request — an
/// attacker who flipped <c>tenant_id</c> would route us to the wrong
/// tenant's keys, the signature would fail, and the request would 401.</para>
///
/// <para>Activation guard rails — bail out and let the next layer act:
/// <list type="bullet">
///   <item><b>Tenant already set</b> (Layer 1 won via subdomain /
///     custom-domain) → nothing to do.</item>
///   <item><b>No bearer token</b> (cookie / anonymous request) →
///     not our path.</item>
///   <item><b>Unparseable token / missing tenant_id</b> → leave it
///     for the JWT bearer handler to reject with proper 401.</item>
///   <item><b>Platform-issued token</b> (token's <c>iss</c> ==
///     configured <c>Oluso:IssuerUri</c>, no subdomain) → leave context
///     alone. The token was minted under platform scope; switching
///     context to the user's home tenant here would make the signing-
///     key / iss lookups resolve against the wrong scope and break
///     validation. Layer 3 will set context from claims after auth.</item>
/// </list></para>
///
/// <para>Only when all guards pass and the token's iss is a tenant
/// subdomain do we look up the tenant from <c>tenant_id</c> and call
/// <see cref="ITenantContextAccessor.SetTenant"/>.</para>
///
/// <para>Cooperates with <see cref="EarlyTenantHostMiddleware"/> (Layer 1,
/// host-based, runs immediately before this) and
/// <see cref="TenantResolutionMiddleware"/> (Layer 3, post-auth, runs
/// after <c>UseAuthentication</c>).</para>
/// </summary>
public class BearerTokenTenantResolverMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<BearerTokenTenantResolverMiddleware> _logger;

    public BearerTokenTenantResolverMiddleware(
        RequestDelegate next,
        ILogger<BearerTokenTenantResolverMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var accessor = context.RequestServices.GetService<ITenantContextAccessor>();
        if (accessor == null || accessor.HasTenant)
        {
            await _next(context);
            return;
        }

        var token = ExtractBearerToken(context);
        if (string.IsNullOrEmpty(token))
        {
            await _next(context);
            return;
        }

        var handler = new JwtSecurityTokenHandler();
        if (!handler.CanReadToken(token))
        {
            await _next(context);
            return;
        }

        string? tenantId;
        string? tokenIssuer;
        try
        {
            var jwt = handler.ReadJwtToken(token);
            tenantId = jwt.Claims.FirstOrDefault(c => c.Type == "tenant_id")?.Value;
            tokenIssuer = jwt.Issuer;
        }
        catch
        {
            await _next(context);
            return;
        }

        if (string.IsNullOrEmpty(tenantId))
        {
            await _next(context);
            return;
        }

        // Platform-issued token: leave context untouched and let
        // TenantResolutionMiddleware set it from the authenticated user
        // after JWT validation completes. Switching to the token's
        // tenant_id here would make the signing-key / issuer lookups
        // resolve against the wrong tenant.
        var configuredBase = context.RequestServices
            .GetService<IConfiguration>()?["Oluso:IssuerUri"]
            ?.TrimEnd('/');
        if (!string.IsNullOrEmpty(configuredBase)
            && string.Equals(tokenIssuer, configuredBase, StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        var tenantStore = context.RequestServices.GetService<ITenantStore>();
        if (tenantStore == null)
        {
            await _next(context);
            return;
        }

        var tenant = await tenantStore.GetByIdAsync(tenantId);
        if (tenant != null && tenant.Enabled)
        {
            accessor.SetTenant(tenant);
            _logger.LogDebug(
                "Tenant context set from bearer token: {TenantId} ({Identifier}) for {Path}",
                tenant.Id, tenant.Identifier, context.Request.Path);
        }

        await _next(context);
    }

    private static string? ExtractBearerToken(HttpContext context)
    {
        if (Protocols.DPoP.PresentedAccessToken.FromAuthorizationHeader(context.Request.Headers.Authorization.FirstOrDefault()) is { } presented)
        {
            return presented.Token;
        }

        // SignalR WebSocket: token rides in the access_token query string when
        // the browser can't set headers on an upgrade. Mirror what JwtBearer's
        // OnMessageReceived does so the tenant context is set consistently.
        if (context.Request.Path.StartsWithSegments("/hubs"))
        {
            var qsToken = context.Request.Query["access_token"].FirstOrDefault();
            if (!string.IsNullOrEmpty(qsToken)) return qsToken;
        }

        return null;
    }
}

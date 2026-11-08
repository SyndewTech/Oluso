using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Middleware;

/// <summary>
/// Layer 1 of 3 in the tenant-context pipeline. Resolves the tenant from
/// the request <b>host</b> before authentication runs.
///
/// <para>Why pre-auth: the JWT bearer handler, the OAuth / OIDC external
/// scheme provider, the dynamic-scheme cache and the Stripe webhook
/// verifier all need <see cref="ITenantContext"/> populated when they
/// fire — they sit inside <c>UseAuthentication</c>'s pipeline and
/// short-circuit before any post-auth middleware could populate the
/// context. Resolving from host alone is safe pre-auth because the host
/// is a public, unforgeable-enough signal (an attacker can only point
/// at hosts the platform owner has wired up).</para>
///
/// <para>Scope is deliberately narrow:
/// <list type="bullet">
///   <item><b>Subdomain → CustomDomain only.</b> No header / query
///     resolution — those are admin-tenant-switcher concerns and
///     belong post-auth where allowed_tenants gates them.</item>
///   <item><b>No 404 / 403.</b> Unknown hosts pass through; the
///     post-auth <see cref="TenantResolutionMiddleware"/> applies the
///     strict gate after auth has populated claims.</item>
///   <item><b>Idempotent.</b> Skips entirely if upstream code (test
///     harness, custom handler) has already populated the context.</item>
/// </list></para>
///
/// <para>Cooperates with:
/// <list type="number">
///   <item><b>Layer 2 —
///     <see cref="BearerTokenTenantResolverMiddleware"/>:</b> runs
///     immediately after this filter. Handles bearer-token API calls on
///     hosts where the subdomain doesn't tell us the tenant.</item>
///   <item><b>Layer 3 —
///     <see cref="TenantResolutionMiddleware"/>:</b> runs after
///     <c>UseAuthentication</c>. Picks up unset contexts, honours
///     <c>X-Tenant-Id</c> for org/super admins, and enforces
///     allowed_tenants on authenticated API paths.</item>
/// </list></para>
/// </summary>
public class EarlyTenantHostMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<EarlyTenantHostMiddleware> _logger;

    public EarlyTenantHostMiddleware(RequestDelegate next, ILogger<EarlyTenantHostMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var accessor = context.RequestServices.GetService<ITenantContextAccessor>();
        if (accessor == null || accessor.HasTenant)
        {
            // Multi-tenancy not enabled, or some upstream already set context — do nothing.
            await _next(context);
            return;
        }

        var tenantStore = context.RequestServices.GetService<ITenantStore>();
        if (tenantStore == null)
        {
            await _next(context);
            return;
        }

        var options = context.RequestServices.GetService<IOptions<MultiTenancyOptions>>()?.Value
            ?? new MultiTenancyOptions();
        EnsureBaseDomain(options, context.RequestServices.GetService<IConfiguration>());

        var tenant = await ResolveBySubdomainAsync(context, options, tenantStore)
            ?? await ResolveByCustomDomainAsync(context, tenantStore);

        if (tenant != null)
        {
            accessor.SetTenant(tenant);
            _logger.LogDebug(
                "Pre-auth tenant resolved from host: {TenantId} ({Identifier}) for {Path}",
                tenant.Id, tenant.Identifier, context.Request.Path);
        }

        await _next(context);
    }

    /// <summary>
    /// Mirrors the BaseDomain auto-derivation in
    /// <see cref="TenantResolutionMiddleware"/> — when the platform
    /// admin doesn't set BaseDomain explicitly, fall back to the
    /// host portion of the configured IssuerUri so subdomain
    /// resolution works out of the box.
    /// </summary>
    private static void EnsureBaseDomain(MultiTenancyOptions options, IConfiguration? configuration)
    {
        if (!string.IsNullOrEmpty(options.BaseDomain)) return;
        var issuerUri = configuration?["Oluso:IssuerUri"];
        if (!string.IsNullOrEmpty(issuerUri) && Uri.TryCreate(issuerUri, UriKind.Absolute, out var uri))
        {
            options.BaseDomain = uri.Host;
        }
    }

    private static async Task<Tenant?> ResolveBySubdomainAsync(
        HttpContext context, MultiTenancyOptions options, ITenantStore tenantStore)
    {
        if (string.IsNullOrEmpty(options.BaseDomain)) return null;

        var host = context.Request.Host.Host;
        if (string.IsNullOrEmpty(host)) return null;

        // Strip the BaseDomain suffix to get the subdomain candidate.
        var baseDomain = options.BaseDomain.TrimStart('.').ToLowerInvariant();
        var lowerHost = host.ToLowerInvariant();
        if (!lowerHost.EndsWith("." + baseDomain, StringComparison.Ordinal)) return null;

        var subdomain = lowerHost[..^(baseDomain.Length + 1)]; // strip ".{baseDomain}"
        if (string.IsNullOrEmpty(subdomain)) return null;
        if (options.ReservedSubdomains?.Contains(subdomain) == true) return null;

        // Try identifier match first, then ID — same precedence the existing
        // TenantResolutionMiddleware uses via LookupTenant.
        var tenant = await tenantStore.GetByIdentifierAsync(subdomain)
            ?? await tenantStore.GetByIdAsync(subdomain);

        return tenant;
    }

    private static async Task<Tenant?> ResolveByCustomDomainAsync(HttpContext context, ITenantStore tenantStore)
    {
        var host = context.Request.Host.Host;
        if (string.IsNullOrEmpty(host) || host.Equals("localhost", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        return await tenantStore.GetByCustomDomainAsync(host);
    }
}

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Controllers;

/// <summary>
/// Internal endpoint for Caddy's On-Demand TLS "ask" callback.
/// Returns 200 if a domain should receive a TLS certificate, 404 otherwise.
/// Only accessible from the internal Docker network — not exposed publicly.
/// </summary>
[AllowAnonymous]
[ApiController]
[Route(".internal/tls")]
[ApiExplorerSettings(IgnoreApi = true)]
public class TlsValidationController : ControllerBase
{
    private readonly ITenantStore _tenantStore;
    private readonly IConfiguration _configuration;
    private readonly ICustomDomainVerificationStore? _domainStore;

    public TlsValidationController(
        ITenantStore tenantStore,
        IConfiguration configuration,
        ICustomDomainVerificationStore? domainStore = null)
    {
        _tenantStore = tenantStore;
        _configuration = configuration;
        _domainStore = domainStore;
    }

    /// <summary>
    /// Caddy calls: GET /.internal/tls/check?domain=login.customer.com
    /// Returns 200 if domain is a valid tenant subdomain or active custom domain.
    /// Returns 404 if not — Caddy will not obtain a certificate for this domain.
    /// </summary>
    [HttpGet("check")]
    public async Task<IActionResult> CheckDomain([FromQuery] string? domain, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(domain))
            return NotFound();

        // 1. Check if domain is a tenant's active custom domain
        var tenant = await _tenantStore.GetByCustomDomainAsync(domain);
        if (tenant is { Enabled: true })
            return Ok();

        // 1b. Fall back to the verification records. Tenants.CustomDomain is only
        // written once a domain is Active, and a domain cannot reach Active until
        // it has a certificate — which is what this endpoint authorises. Verified
        // is the right threshold: DNS ownership is proven by then.
        if (_domainStore != null)
        {
            var servable = await _domainStore.GetServableDomainsAsync(ct);
            if (servable.Any(d => string.Equals(d.Domain, domain, StringComparison.OrdinalIgnoreCase)))
                return Ok();
        }

        // 2. Check if domain is a valid tenant subdomain
        var issuerUri = _configuration["Oluso:IssuerUri"];
        if (!string.IsNullOrEmpty(issuerUri) && Uri.TryCreate(issuerUri, UriKind.Absolute, out var uri))
        {
            var issuerHost = uri.Host;
            var suffix = "." + issuerHost;
            if (domain.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && domain.Length > suffix.Length)
            {
                var subdomain = domain[..^suffix.Length];
                if (!subdomain.Contains('.'))
                {
                    var subTenant = await _tenantStore.GetByIdentifierAsync(subdomain);
                    if (subTenant is { Enabled: true })
                        return Ok();
                }
            }

            // 3. Check if it's the base issuer domain itself
            if (domain.Equals(issuerHost, StringComparison.OrdinalIgnoreCase))
                return Ok();
        }

        return NotFound();
    }
}

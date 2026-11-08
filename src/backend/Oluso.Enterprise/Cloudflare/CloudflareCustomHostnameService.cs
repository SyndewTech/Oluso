using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Oluso.Core.Services;

namespace Oluso.Enterprise.Cloudflare;

/// <summary>
/// Provisions custom domains via Cloudflare Custom Hostnames API (SSL for SaaS).
/// Requires Cloudflare zone ID + API token with Custom Hostnames permissions.
///
/// Configuration:
///   Oluso:Cloudflare:ZoneId            — The Cloudflare zone ID for the base domain
///   Oluso:Cloudflare:ApiToken          — API token with Custom Hostname:Edit permission
///   Oluso:Cloudflare:UseCustomMetadata — Optional, default false. Sends
///       <c>custom_metadata.tenant_id</c> on the create call. Requires an
///       Enterprise SSL for SaaS allocation; without it Cloudflare returns
///       error code 1413 ("No custom metadata access has been allocated").
///       Off by default since Oluso resolves the tenant from the host header
///       in TenantResolutionMiddleware — there's no app-side need for it.
///   Oluso:Cloudflare:CustomOriginServer — Optional. Origin this deployment's
///       custom hostnames route to, sent as <c>custom_origin_server</c>.
///       Defaults to Oluso:CustomDomains:CnameTarget. When neither is set,
///       Cloudflare routes to the zone's fallback origin — correct for a
///       single-deployment zone, wrong when dev/staging/prod share one, since
///       the fallback is a single host for the whole zone.
/// </summary>
public class CloudflareCustomHostnameService : ICustomDomainProvisioningService
{
    private const string BaseUrl = "https://api.cloudflare.com/client/v4";

    private readonly HttpClient _httpClient;
    private readonly string? _zoneId;
    private readonly bool _isConfigured;
    private readonly bool _useCustomMetadata;
    private readonly string? _customOriginServer;
    private readonly ILogger<CloudflareCustomHostnameService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public CloudflareCustomHostnameService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<CloudflareCustomHostnameService> logger)
    {
        _logger = logger;
        _zoneId = configuration["Oluso:Cloudflare:ZoneId"];
        var apiToken = configuration["Oluso:Cloudflare:ApiToken"];
        _isConfigured = !string.IsNullOrEmpty(_zoneId) && !string.IsNullOrEmpty(apiToken);
        _useCustomMetadata = configuration.GetValue("Oluso:Cloudflare:UseCustomMetadata", false);

        // Falls back to the host tenants are told to CNAME at: routing traffic
        // somewhere other than the advertised target would be incoherent.
        var originServer = configuration["Oluso:Cloudflare:CustomOriginServer"]
            ?? configuration["Oluso:CustomDomains:CnameTarget"];
        _customOriginServer = string.IsNullOrWhiteSpace(originServer) ? null : originServer.Trim();

        _httpClient = httpClientFactory.CreateClient("Cloudflare");
        if (!string.IsNullOrEmpty(apiToken))
        {
            _httpClient.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiToken);
        }

        if (!_isConfigured)
        {
            _logger.LogWarning("Cloudflare Custom Hostnames not configured — domain provisioning disabled");
        }
    }

    public async Task<DomainProvisioningResult> ProvisionAsync(string domain, string tenantId, CancellationToken ct = default)
    {
        if (!_isConfigured)
            return DomainProvisioningResult.Failed("Cloudflare not configured");

        try
        {
            // Null fields are omitted (JsonIgnoreCondition.WhenWritingNull), so
            // both optional fields hang off one payload rather than a branch
            // per combination.
            //
            // custom_metadata is gated behind an Enterprise SSL for SaaS
            // allocation; including it on a non-Enterprise zone returns
            // Cloudflare error code 1413. Send the field only when the
            // operator opts in, otherwise the standard tier accepts the
            // call. The tenant lookup at request time uses the Host header
            // via TenantResolutionMiddleware, so the metadata is purely a
            // convenience (e.g. for a Cloudflare Worker), not a requirement.
            //
            // custom_origin_server pins the hostname to this deployment. Without
            // it Cloudflare routes to the zone's fallback origin, which is a
            // single host for the whole zone — so on a zone shared by several
            // environments, every custom domain provisioned anywhere lands on
            // whichever environment owns the fallback.
            object payload = new
            {
                hostname = domain,
                ssl = new
                {
                    method = "http",
                    type = "dv",
                    settings = new { min_tls_version = "1.2" }
                },
                custom_metadata = _useCustomMetadata ? (object?)new { tenant_id = tenantId } : null,
                custom_origin_server = _customOriginServer
            };

            var response = await _httpClient.PostAsJsonAsync(
                $"{BaseUrl}/zones/{_zoneId}/custom_hostnames",
                payload, JsonOptions, ct);

            var body = await response.Content.ReadAsStringAsync(ct);

            if (response.IsSuccessStatusCode)
            {
                var result = JsonSerializer.Deserialize<CloudflareResponse>(body, JsonOptions);
                var id = result?.Result?.Id;
                _logger.LogInformation("Cloudflare Custom Hostname created for {Domain}: {Id}", domain, id);
                return DomainProvisioningResult.Ok(id);
            }

            _logger.LogWarning("Cloudflare Custom Hostname creation failed for {Domain}: {StatusCode} {Body}",
                domain, response.StatusCode, body);
            return DomainProvisioningResult.Failed($"Cloudflare API: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error provisioning Cloudflare Custom Hostname for {Domain}", domain);
            return DomainProvisioningResult.Failed(ex.Message);
        }
    }

    public async Task<DomainProvisioningStatus> GetStatusAsync(string domain, CancellationToken ct = default)
    {
        if (!_isConfigured)
            return DomainProvisioningStatus.Failed("Cloudflare not configured");

        try
        {
            var hostname = await FindHostnameAsync(domain, ct);
            if (hostname == null)
                return DomainProvisioningStatus.NotFound();

            // Two independent gates: the hostname itself must validate
            // (ownership/routing) and the DV certificate must issue. Both
            // report "active" when done; everything else is either still in
            // flight or terminal.
            var hostStatus = hostname.Status ?? "unknown";
            var sslStatus = hostname.Ssl?.Status ?? "unknown";
            var detail = $"hostname={hostStatus}, ssl={sslStatus}";

            if (IsActive(hostStatus) && IsActive(sslStatus))
                return DomainProvisioningStatus.Active(hostname.Id);

            if (IsTerminalFailure(hostStatus) || IsTerminalFailure(sslStatus))
            {
                var errors = hostname.Ssl?.ValidationErrors?
                    .Select(e => e.Message)
                    .Where(m => !string.IsNullOrWhiteSpace(m))
                    .ToList();

                if (errors is { Count: > 0 })
                    detail = $"{detail} — {string.Join("; ", errors)}";

                return DomainProvisioningStatus.Failed(detail, hostname.Id);
            }

            return DomainProvisioningStatus.Pending(detail, hostname.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reading Cloudflare Custom Hostname status for {Domain}", domain);
            return DomainProvisioningStatus.Failed(ex.Message);
        }
    }

    private static bool IsActive(string status) =>
        status.Equals("active", StringComparison.OrdinalIgnoreCase);

    // Cloudflare's terminal states. "pending_*", "initializing" and
    // "active_redeploying" are all still in flight and must not be
    // treated as failures — they resolve on their own.
    private static bool IsTerminalFailure(string status) =>
        status.Equals("moved", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("deleted", StringComparison.OrdinalIgnoreCase) ||
        status.Equals("timed_out", StringComparison.OrdinalIgnoreCase) ||
        status.StartsWith("validation_timed_out", StringComparison.OrdinalIgnoreCase);

    private async Task<CloudflareHostname?> FindHostnameAsync(string domain, CancellationToken ct)
    {
        var response = await _httpClient.GetAsync(
            $"{BaseUrl}/zones/{_zoneId}/custom_hostnames?hostname={Uri.EscapeDataString(domain)}", ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            _logger.LogWarning("Cloudflare Custom Hostname lookup failed for {Domain}: {StatusCode} {Body}",
                domain, response.StatusCode, errorBody);
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(ct);
        var list = JsonSerializer.Deserialize<CloudflareListResponse>(body, JsonOptions);

        // The hostname filter is a prefix match on Cloudflare's side, so
        // pick the exact hostname rather than trusting the first row.
        return list?.Result?.FirstOrDefault(h =>
            string.Equals(h.Hostname, domain, StringComparison.OrdinalIgnoreCase));
    }

    public async Task DeprovisionAsync(string domain, CancellationToken ct = default)
    {
        if (!_isConfigured) return;

        try
        {
            // Find the custom hostname by domain
            var response = await _httpClient.GetAsync(
                $"{BaseUrl}/zones/{_zoneId}/custom_hostnames?hostname={domain}", ct);

            if (!response.IsSuccessStatusCode) return;

            var body = await response.Content.ReadAsStringAsync(ct);
            var list = JsonSerializer.Deserialize<CloudflareListResponse>(body, JsonOptions);

            if (list?.Result == null) return;

            foreach (var hostname in list.Result)
            {
                if (string.IsNullOrEmpty(hostname.Id)) continue;

                var deleteResponse = await _httpClient.DeleteAsync(
                    $"{BaseUrl}/zones/{_zoneId}/custom_hostnames/{hostname.Id}", ct);

                _logger.LogInformation("Cloudflare Custom Hostname {Id} deleted for {Domain}: {StatusCode}",
                    hostname.Id, domain, deleteResponse.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deprovisioning Cloudflare Custom Hostname for {Domain}", domain);
        }
    }

    private class CloudflareResponse
    {
        public CloudflareHostname? Result { get; set; }
    }

    private class CloudflareListResponse
    {
        public List<CloudflareHostname>? Result { get; set; }
    }

    private class CloudflareHostname
    {
        public string? Id { get; set; }
        public string? Hostname { get; set; }

        /// <summary>Hostname validation state: pending, active, moved, deleted…</summary>
        public string? Status { get; set; }

        public CloudflareSsl? Ssl { get; set; }
    }

    private class CloudflareSsl
    {
        /// <summary>Certificate state: initializing, pending_validation, pending_issuance, pending_deployment, active…</summary>
        public string? Status { get; set; }

        public List<CloudflareValidationError>? ValidationErrors { get; set; }
    }

    private class CloudflareValidationError
    {
        public string? Message { get; set; }
    }
}

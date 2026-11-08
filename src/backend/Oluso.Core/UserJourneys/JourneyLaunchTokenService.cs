using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.DataProtection;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;

namespace Oluso.Core.UserJourneys;

public sealed class JourneyLaunchTokenService : IJourneyLaunchTokenService
{
    public const string ProtectorPurpose = "Oluso.UserJourneys.Launch.v1";

    private readonly IDataProtector _protector;
    private readonly IJourneyPolicyStore _policies;
    private readonly ITenantStore _tenants;
    private readonly IIssuerResolver _issuer;
    private readonly ITenantAppOrigins _origins;
    private readonly TimeProvider _clock;

    public JourneyLaunchTokenService(
        IDataProtectionProvider dataProtection,
        IJourneyPolicyStore policies,
        ITenantStore tenants,
        IIssuerResolver issuer,
        ITenantAppOrigins origins,
        TimeProvider? clock = null)
    {
        _protector = dataProtection.CreateProtector(ProtectorPurpose);
        _policies = policies;
        _tenants = tenants;
        _issuer = issuer;
        _origins = origins;
        _clock = clock ?? TimeProvider.System;
    }

    public string Protect(JourneyLaunchTicket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        var payload = new Payload
        {
            TenantId = ticket.TenantId,
            PolicyId = ticket.PolicyId,
            Context = new Dictionary<string, string>(ticket.Context),
            ReturnUrl = ticket.ReturnUrl,
            ExpiresAt = ticket.ExpiresAt.ToUnixTimeSeconds(),
            Module = ticket.Module,
        };
        return _protector.Protect(JsonSerializer.Serialize(payload));
    }

    public JourneyLaunchReadResult Read(string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return JourneyLaunchReadResult.Invalid;

        Payload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<Payload>(_protector.Unprotect(token));
        }
        catch (CryptographicException)
        {
            return JourneyLaunchReadResult.Invalid;
        }
        catch (FormatException)
        {
            return JourneyLaunchReadResult.Invalid;
        }
        catch (JsonException)
        {
            return JourneyLaunchReadResult.Invalid;
        }

        if (payload == null || string.IsNullOrEmpty(payload.TenantId) || string.IsNullOrEmpty(payload.PolicyId))
            return JourneyLaunchReadResult.Invalid;

        var expiresAt = DateTimeOffset.FromUnixTimeSeconds(payload.ExpiresAt);
        if (expiresAt <= _clock.GetUtcNow()) return JourneyLaunchReadResult.Expired;

        return new JourneyLaunchReadResult(JourneyLaunchReadStatus.Valid, new JourneyLaunchTicket(
            payload.TenantId,
            payload.PolicyId,
            payload.Context ?? new Dictionary<string, string>(),
            payload.ReturnUrl,
            expiresAt,
            string.IsNullOrEmpty(payload.Module) ? null : payload.Module));
    }

    public async Task<bool> IsAllowedReturnUrlAsync(string tenantId, string returnUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(returnUrl)) return false;
        if (IsLocalPath(returnUrl)) return true;

        if (!Uri.TryCreate(returnUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        var origin = uri.GetLeftPart(UriPartial.Authority);

        var tenant = await _tenants.GetByIdAsync(tenantId, cancellationToken);
        if (tenant == null) return false;
        if (Uri.TryCreate(await _issuer.GetIssuerForTenantAsync(tenant, cancellationToken), UriKind.Absolute, out var issuer)
            && string.Equals(issuer.GetLeftPart(UriPartial.Authority), origin, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var registered = await _origins.GetOriginsAsync(tenantId, cancellationToken);
        return registered.Contains(origin);
    }

    public async Task<string> CreateLaunchUrlAsync(
        string tenantId,
        string policyId,
        IDictionary<string, string> context,
        string? returnUrl,
        TimeSpan lifetime,
        CancellationToken cancellationToken = default,
        string? module = null)
    {
        if (string.IsNullOrWhiteSpace(tenantId)) throw new ArgumentException("tenantId required", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(policyId)) throw new ArgumentException("policyId required", nameof(policyId));
        ArgumentNullException.ThrowIfNull(context);
        if (lifetime <= TimeSpan.Zero) throw new ArgumentException("lifetime must be positive", nameof(lifetime));

        var policy = await _policies.GetAsync(policyId, cancellationToken);
        if (policy == null || (policy.TenantId != null && policy.TenantId != tenantId))
            throw new InvalidOperationException($"Journey '{policyId}' is not available to tenant '{tenantId}'");
        if (!policy.Enabled)
            throw new InvalidOperationException($"Journey '{policy.Name}' is disabled");
        if (string.IsNullOrEmpty(policy.Slug))
            throw new InvalidOperationException($"Journey '{policy.Name}' has no slug");
        if (policy.IsModuleOwned && string.IsNullOrEmpty(module))
            throw new InvalidOperationException($"Journey '{policy.Name}' belongs to a module and is started by a module");
        if (!string.IsNullOrEmpty(module) && !policy.UsableByModule(module))
            throw new InvalidOperationException($"Journey '{policy.Name}' belongs to another module and is not shared");

        if (!string.IsNullOrEmpty(returnUrl) && !await IsAllowedReturnUrlAsync(tenantId, returnUrl, cancellationToken))
            throw new InvalidOperationException("The return URL is not an address registered for the tenant");

        var tenant = await _tenants.GetByIdAsync(tenantId, cancellationToken)
            ?? throw new InvalidOperationException($"Tenant '{tenantId}' not found");
        var issuer = (await _issuer.GetIssuerForTenantAsync(tenant, cancellationToken)).TrimEnd('/');

        var token = Protect(new JourneyLaunchTicket(
            tenantId,
            policy.Id,
            new Dictionary<string, string>(context),
            string.IsNullOrEmpty(returnUrl) ? null : returnUrl,
            _clock.GetUtcNow() + lifetime,
            string.IsNullOrEmpty(module) ? null : module));

        return $"{issuer}/journey/start/{Uri.EscapeDataString(policy.Slug)}?{JourneyLaunchData.QueryParameter}={Uri.EscapeDataString(token)}";
    }

    private static bool IsLocalPath(string url) =>
        url.Length > 0
        && url[0] == '/'
        && (url.Length == 1 || (url[1] != '/' && url[1] != '\\'))
        && !url.Contains('\\');

    private sealed class Payload
    {
        [JsonPropertyName("t")] public string TenantId { get; set; } = "";
        [JsonPropertyName("p")] public string PolicyId { get; set; } = "";
        [JsonPropertyName("c")] public Dictionary<string, string>? Context { get; set; }
        [JsonPropertyName("r")] public string? ReturnUrl { get; set; }
        [JsonPropertyName("e")] public long ExpiresAt { get; set; }
        [JsonPropertyName("m")] public string? Module { get; set; }
    }
}

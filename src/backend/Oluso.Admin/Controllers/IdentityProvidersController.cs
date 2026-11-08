using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oluso.Admin.Authorization;
using Oluso.Core.Authorization;
using Oluso.Core.Api;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Services;
using Oluso.Admin.Dto.IdentityProviders;
using Oluso.Admin.Services.ProviderIcons;
using Oluso.Imaging;

namespace Oluso.Admin.Controllers;

/// <summary>
/// Admin API for managing external identity providers
/// </summary>
[ApiController]
[Route("api/admin/identity-providers")]
[Authorize(Policy = "AdminApi")]
public class IdentityProvidersController : AdminBaseController
{
    private readonly IIdentityProviderStore _providerStore;
    private readonly ITenantContext _tenantContext;
    private readonly IIssuerResolver _issuerResolver;
    private readonly IOlusoEventService _eventService;
    private readonly ITenantStore _tenantStore;
    private readonly IRoleStore _roleStore;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IExternalSchemeCacheInvalidator? _schemeCacheInvalidator;
    private readonly IProviderIconDiscoveryService _iconDiscovery;
    private readonly IIconImageNormalizer _iconNormalizer;
    private readonly ILogger<IdentityProvidersController> _logger;

    public IdentityProvidersController(
        IIdentityProviderStore providerStore,
        ITenantContext tenantContext,
        IIssuerResolver issuerResolver,
        IOlusoEventService eventService,
        ITenantStore tenantStore,
        IRoleStore roleStore,
        IHttpClientFactory httpClientFactory,
        IProviderIconDiscoveryService iconDiscovery,
        IIconImageNormalizer iconNormalizer,
        ILogger<IdentityProvidersController> logger,
        IExternalSchemeCacheInvalidator? schemeCacheInvalidator = null) : base(tenantContext)
    {
        _iconDiscovery = iconDiscovery;
        _iconNormalizer = iconNormalizer;
        _providerStore = providerStore;
        _tenantContext = tenantContext;
        _issuerResolver = issuerResolver;
        _eventService = eventService;
        _tenantStore = tenantStore;
        _roleStore = roleStore;
        _httpClientFactory = httpClientFactory;
        _schemeCacheInvalidator = schemeCacheInvalidator;
        _logger = logger;
    }

    private new string? TenantId => _tenantContext.TenantId ?? User.FindFirst("tenant_id")?.Value;
    private new string AdminUserId => User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "unknown";
    private new string? AdminUserName => User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? User.FindFirst("name")?.Value;

    /// <summary>
    /// Get server info for callback URLs. Returns:
    /// <list type="bullet">
    ///   <item><c>IssuerUri</c> — what <see cref="IIssuerResolver"/> resolves
    ///     for the target tenant. CustomDomain wins over auto-subdomain so
    ///     this is the URL external IdPs (Apple, Google, Stripe webhooks)
    ///     should register as the callback origin.</item>
    ///   <item><c>AutoSubdomain</c> — the subdomain-based URL regardless of
    ///     CustomDomain, so the admin "Automatic subdomain" panel always
    ///     shows the canonical <c>{identifier}.{base-host}</c> form.</item>
    /// </list>
    /// Pass <c>?forTenantId=</c> to resolve URLs for a tenant other than
    /// the request's own (e.g. platform admin viewing tenant "acme" sees
    /// acme's URLs, not the platform's).
    /// </summary>
    [HttpGet("server-info")]
    [RequirePermission(AdminPermissions.IdentityProvidersRead)]
    public async Task<ActionResult<ServerInfoResponse>> GetServerInfo(
        [FromQuery] string? forTenantId = null,
        CancellationToken cancellationToken = default)
    {
        Tenant? targetTenant = null;
        if (!string.IsNullOrEmpty(forTenantId))
        {
            targetTenant = await _tenantStore.GetByIdAsync(forTenantId, cancellationToken)
                ?? await _tenantStore.GetByIdentifierAsync(forTenantId, cancellationToken);
        }

        var resolved = targetTenant != null
            ? await _issuerResolver.GetResolvedIssuerForTenantAsync(targetTenant, cancellationToken)
            : await _issuerResolver.GetResolvedIssuerAsync(cancellationToken);

        return Ok(new ServerInfoResponse
        {
            IssuerUri = resolved.IssuerUri.TrimEnd('/'),
            AutoSubdomain = resolved.AutoSubdomain,
            CallbackPathTemplate = "/signin-{scheme}"
        });
    }

    /// <summary>
    /// Check if a scheme name is available (globally unique across all tenants)
    /// </summary>
    [HttpGet("check-scheme/{scheme}")]
    [RequirePermission(AdminPermissions.IdentityProvidersRead)]
    public async Task<ActionResult<SchemeAvailabilityResponse>> CheckSchemeAvailability(
        string scheme,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(scheme))
        {
            return Ok(new SchemeAvailabilityResponse { Available = false, Message = "Scheme name is required" });
        }

        var exists = await _providerStore.SchemeExistsGloballyAsync(scheme, cancellationToken);
        return Ok(new SchemeAvailabilityResponse
        {
            Available = !exists,
            Message = exists ? "This scheme name is already in use" : null
        });
    }

    /// <summary>
    /// Get all identity providers
    /// </summary>
    [HttpGet]
    [RequirePermission(AdminPermissions.IdentityProvidersRead)]
    public async Task<ActionResult<IEnumerable<IdentityProviderDto>>> GetAll(
        [FromQuery] ExternalProviderType? type,
        CancellationToken cancellationToken)
    {
        var providers = await _providerStore.GetAllAsync(cancellationToken);
        var dtos = providers.Where(p => type == null || p.ProviderType == type).Select(MapToDto);
        return Ok(dtos);
    }

    /// <summary>
    /// Identity providers of a specific tenant, for admins managing that tenant
    /// (Organizations → tenant → settings) rather than the tenant active in their
    /// own context. Gated like the other tenants/{tenantId} settings reads.
    /// </summary>
    [HttpGet("~/api/admin/tenants/{tenantId}/identity-providers")]
    [RequirePermission(AdminPermissions.TenantsRead)]
    public async Task<ActionResult<IEnumerable<IdentityProviderDto>>> GetAllForTenant(
        string tenantId,
        CancellationToken cancellationToken)
    {
        var tenant = await _tenantStore.GetByIdAsync(tenantId, cancellationToken);
        if (tenant == null)
            return NotFound();
        if (!CanAccessTenant(tenant))
            return Forbid();

        var providers = await _providerStore.GetAllForTenantAsync(tenantId, cancellationToken);
        return Ok(providers.Select(MapToDto));
    }

    /// <summary>
    /// Get identity provider by ID with configuration for editing.
    /// Secrets are masked - use GET /{id}/secret/{key} to reveal individual secrets.
    /// </summary>
    [HttpGet("{id:int}/edit")]
    [RequirePermission(AdminPermissions.IdentityProvidersRead)]
    public async Task<ActionResult<IdentityProviderEditDto>> GetForEdit(int id, CancellationToken cancellationToken)
    {
        var provider = await _providerStore.GetByIdAsync(id, cancellationToken);
        if (provider == null)
            return NotFound();

        return Ok(new IdentityProviderEditDto
        {
            Id = provider.Id,
            Scheme = provider.Scheme,
            DisplayName = provider.DisplayName,
            Enabled = provider.Enabled,
            ProviderType = provider.ProviderType,
            IconUrl = provider.IconUrl,
            DisplayOrder = provider.DisplayOrder,
            AllowedClientIds = provider.AllowedClientIds,
            Configuration = MaskSensitiveFields(provider.Properties),
            NonEditable = provider.NonEditable,
            Created = provider.Created,
            Updated = provider.Updated,
            LastAccessed = provider.LastAccessed
        });
    }

    /// <summary>
    /// Reveal a specific secret from provider configuration.
    /// Requires admin authentication. Access is logged.
    /// </summary>
    [HttpGet("{id:int}/secret/{secretKey}")]
    [RequirePermission(AdminPermissions.IdentityProvidersWrite)]
    public async Task<ActionResult<SecretRevealResponse>> RevealSecret(
        int id,
        string secretKey,
        CancellationToken cancellationToken)
    {
        var provider = await _providerStore.GetByIdAsync(id, cancellationToken);
        if (provider == null)
            return NotFound();

        if (string.IsNullOrEmpty(provider.Properties))
            return NotFound(new { error = "No configuration found" });

        var config = JsonSerializer.Deserialize<Dictionary<string, object>>(provider.Properties);
        if (config == null || !config.TryGetValue(secretKey, out var value))
            return NotFound(new { error = $"Secret '{secretKey}' not found" });

        // Only allow revealing actual sensitive fields
        if (!SensitiveFields.Contains(secretKey))
            return BadRequest(new { error = $"'{secretKey}' is not a secret field" });

        _logger.LogWarning(
            "Secret '{SecretKey}' revealed for provider {Scheme} by admin",
            secretKey, provider.Scheme);

        return Ok(new SecretRevealResponse { Value = value?.ToString() ?? "" });
    }

    /// <summary>
    /// Get identity provider by ID
    /// </summary>
    [HttpGet("{id:int}")]
    [RequirePermission(AdminPermissions.IdentityProvidersRead)]
    public async Task<ActionResult<IdentityProviderDto>> GetById(int id, CancellationToken cancellationToken)
    {
        var provider = await _providerStore.GetByIdAsync(id, cancellationToken);
        if (provider == null)
            return NotFound();

        return Ok(MapToDto(provider));
    }

    /// <summary>
    /// Get identity provider by scheme
    /// </summary>
    [HttpGet("by-scheme/{scheme}")]
    [RequirePermission(AdminPermissions.IdentityProvidersRead)]
    public async Task<ActionResult<IdentityProviderDto>> GetByScheme(string scheme, CancellationToken cancellationToken)
    {
        var provider = await _providerStore.GetBySchemeAsync(scheme, cancellationToken);
        if (provider == null)
            return NotFound();

        return Ok(MapToDto(provider));
    }

    /// <summary>
    /// Get available provider types with their configuration requirements
    /// </summary>
    [HttpGet("types")]
    [RequirePermission(AdminPermissions.IdentityProvidersRead)]
    public ActionResult<IEnumerable<ProviderTypeInfo>> GetProviderTypes()
    {
        var types = new List<ProviderTypeInfo>
        {
            new()
            {
                Type = ExternalProviderType.Google,
                Name = "Google",
                Description = "Google Sign-In with pre-configured endpoints",
                RequiredFields = new[] { "clientId", "clientSecret" },
                OptionalFields = new[] { "hostedDomain", "accessType" }
            },
            new()
            {
                Type = ExternalProviderType.Microsoft,
                Name = "Microsoft / Azure AD",
                Description = "Microsoft, Azure AD, or Entra ID authentication",
                RequiredFields = new[] { "clientId", "clientSecret" },
                OptionalFields = new[] { "tenantId", "instance", "domainHint", "prompt" }
            },
            new()
            {
                Type = ExternalProviderType.Facebook,
                Name = "Facebook",
                Description = "Facebook Login",
                RequiredFields = new[] { "clientId", "clientSecret" },
                OptionalFields = new[] { "fields" }
            },
            new()
            {
                Type = ExternalProviderType.Apple,
                Name = "Sign in with Apple",
                Description = "Apple Sign-In with team / key / private-key authentication",
                RequiredFields = new[] { "clientId", "teamId", "keyId", "privateKey" },
                OptionalFields = Array.Empty<string>()
            },
            new()
            {
                Type = ExternalProviderType.GitHub,
                Name = "GitHub",
                Description = "GitHub OAuth (supports Enterprise)",
                RequiredFields = new[] { "clientId", "clientSecret" },
                OptionalFields = new[] { "enterpriseUrl", "allowedOrganizations", "requestEmail" }
            },
            new()
            {
                Type = ExternalProviderType.LinkedIn,
                Name = "LinkedIn",
                Description = "LinkedIn OpenID Connect",
                RequiredFields = new[] { "clientId", "clientSecret" },
                OptionalFields = new[] { "scopes" }
            },
            new()
            {
                Type = ExternalProviderType.Twitter,
                Name = "Twitter / X",
                Description = "Twitter / X OAuth 2.0",
                RequiredFields = new[] { "clientId", "clientSecret" },
                OptionalFields = new[] { "scopes" }
            },
            new()
            {
                Type = ExternalProviderType.Oidc,
                Name = "OpenID Connect",
                Description = "Generic OIDC provider - works with any compliant provider",
                RequiredFields = new[] { "authority", "clientId" },
                OptionalFields = new[] { "clientSecret", "responseType", "responseMode", "usePkce", "scopes", "claimMappings" }
            },
            new()
            {
                Type = ExternalProviderType.OAuth2,
                Name = "OAuth 2.0",
                Description = "Generic OAuth 2.0 provider (non-OIDC)",
                RequiredFields = new[] { "authorizationEndpoint", "tokenEndpoint", "clientId" },
                OptionalFields = new[] { "clientSecret", "userInfoEndpoint", "scopes", "userIdClaimPath", "emailClaimPath", "nameClaimPath" }
            },
            new()
            {
                Type = ExternalProviderType.Saml2,
                Name = "SAML 2.0",
                Description = "Enterprise SAML 2.0 Identity Provider",
                RequiredFields = new[] { "entityId" },
                OptionalFields = new[] { "metadataUrl", "singleSignOnServiceUrl", "singleSignOnBinding", "singleLogoutServiceUrl", "signingCertificate", "nameIdFormat", "wantAssertionsSigned", "signAuthenticationRequests", "autoProvisionUsers" }
            },
            new()
            {
                Type = ExternalProviderType.Ldap,
                Name = "LDAP / Active Directory",
                Description = "Authenticate against LDAP directory (OpenLDAP, Active Directory)",
                RequiredFields = new[] { "server", "baseDn" },
                OptionalFields = new[] { "port", "useSsl", "useStartTls", "bindDn", "bindPassword", "userSearchBase", "userSearchFilter", "groupSearchBase", "groupSearchFilter", "uidAttribute", "emailAttribute", "firstNameAttribute", "lastNameAttribute", "displayNameAttribute", "phoneAttribute", "memberOfAttribute", "autoProvisionUsers", "syncGroupsToRoles", "connectionTimeout", "searchTimeout" }
            }
        };

        return Ok(types);
    }

    /// <summary>
    /// Create a new identity provider
    /// </summary>
    [HttpPost]
    [RequirePermission(AdminPermissions.IdentityProvidersWrite)]
    public async Task<ActionResult<IdentityProviderDto>> Create(
        [FromBody] CreateIdentityProviderRequest request,
        CancellationToken cancellationToken)
    {
        // Validate scheme doesn't exist globally (auth schemes must be unique across all tenants)
        if (await _providerStore.SchemeExistsGloballyAsync(request.Scheme, cancellationToken))
        {
            return Conflict(new { error = $"Provider with scheme '{request.Scheme}' already exists. Authentication schemes must be unique across all tenants." });
        }

        var configError = await ValidateAccessControlAndDefaultRolesAsync(request.Configuration, cancellationToken);
        if (configError != null)
        {
            return BadRequest(new { error = configError });
        }

        var provider = new IdentityProvider
        {
            Scheme = request.Scheme,
            DisplayName = request.DisplayName ?? request.Scheme,
            Enabled = request.Enabled ?? true,
            ProviderType = request.ProviderType,
            IconUrl = request.IconUrl,
            DisplayOrder = request.DisplayOrder ?? 0,
            Properties = request.Configuration != null
                ? JsonSerializer.Serialize(request.Configuration)
                : null,
            AllowedClientIds = request.AllowedClientIds ?? new List<string>()
        };

        var created = await _providerStore.AddAsync(provider, cancellationToken);

        _schemeCacheInvalidator?.Invalidate(created.Scheme, created.TenantId);

        _logger.LogInformation(
            "Created identity provider: {Scheme} ({Type})",
            created.Scheme, created.ProviderType);

        // Raise audit event
        await _eventService.RaiseAsync(new AdminIdpCreatedEvent
        {
            TenantId = TenantId,
            AdminUserId = AdminUserId!,
            AdminUserName = AdminUserName,
            IpAddress = ClientIp,
            ResourceId = created.Id.ToString(),
            ResourceName = created.DisplayName ?? created.Scheme,
            Scheme = created.Scheme,
            ProviderType = created.ProviderType.ToString()
        }, cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = created.Id }, MapToDto(created));
    }

    /// <summary>
    /// Update an identity provider
    /// </summary>
    [HttpPut("{id:int}")]
    [RequirePermission(AdminPermissions.IdentityProvidersWrite)]
    public async Task<ActionResult<IdentityProviderDto>> Update(
        int id,
        [FromBody] UpdateIdentityProviderRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await _providerStore.GetByIdAsync(id, cancellationToken);
        if (existing == null)
            return NotFound();

        if (existing.NonEditable)
            return BadRequest(new { error = "This provider cannot be modified" });

        // If this update would disable a currently-enabled provider, make sure
        // the tenant won't be left without a sign-in path and that the
        // provider isn't currently the tenant's SSO default.
        if (request.Enabled == false && existing.Enabled)
        {
            var disableError = await CheckIdpDisableOrDeleteAsync(
                existing, willStillBeEnabled: false, willStillExist: true, "disabling", cancellationToken);
            if (disableError != null) return BadRequest(new { error = disableError });
        }

        var configError = await ValidateAccessControlAndDefaultRolesAsync(request.Configuration, cancellationToken);
        if (configError != null)
        {
            return BadRequest(new { error = configError });
        }

        // Update properties
        if (request.DisplayName != null) existing.DisplayName = request.DisplayName;
        if (request.Enabled.HasValue) existing.Enabled = request.Enabled.Value;
        if (request.ProviderType.HasValue) existing.ProviderType = request.ProviderType.Value;
        if (request.IconUrl != null) existing.IconUrl = request.IconUrl;
        if (request.DisplayOrder.HasValue) existing.DisplayOrder = request.DisplayOrder.Value;

        if (request.Configuration != null)
        {
            // Merge configuration, preserving secrets that weren't changed
            var newConfig = MergeConfigurationPreservingSecrets(existing.Properties, request.Configuration);
            existing.Properties = JsonSerializer.Serialize(newConfig);
        }

        if (request.AllowedClientIds != null)
        {
            existing.AllowedClientIds = request.AllowedClientIds;
        }

        var updated = await _providerStore.UpdateAsync(existing, cancellationToken);

        _schemeCacheInvalidator?.Invalidate(updated.Scheme, updated.TenantId);

        _logger.LogInformation("Updated identity provider: {Scheme}", updated.Scheme);

        // Raise audit event
        await _eventService.RaiseAsync(new AdminIdpUpdatedEvent
        {
            TenantId = TenantId,
            AdminUserId = AdminUserId!,
            AdminUserName = AdminUserName,
            IpAddress = ClientIp,
            ResourceId = updated.Id.ToString(),
            ResourceName = updated.DisplayName ?? updated.Scheme,
            Scheme = updated.Scheme
        }, cancellationToken);

        return Ok(MapToDto(updated));
    }

    /// <summary>
    /// Delete an identity provider
    /// </summary>
    [HttpDelete("{id:int}")]
    [RequirePermission(AdminPermissions.IdentityProvidersDelete)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var existing = await _providerStore.GetByIdAsync(id, cancellationToken);
        if (existing == null)
            return NotFound();

        if (existing.NonEditable)
            return BadRequest(new { error = "This provider cannot be deleted" });

        var deleteError = await CheckIdpDisableOrDeleteAsync(
            existing, willStillBeEnabled: false, willStillExist: false, "deleting", cancellationToken);
        if (deleteError != null) return BadRequest(new { error = deleteError });

        await _providerStore.DeleteAsync(id, cancellationToken);

        _schemeCacheInvalidator?.Invalidate(existing.Scheme, existing.TenantId);

        _logger.LogInformation("Deleted identity provider: {Scheme}", existing.Scheme);

        // Raise audit event
        await _eventService.RaiseAsync(new AdminIdpDeletedEvent
        {
            TenantId = TenantId,
            AdminUserId = AdminUserId!,
            AdminUserName = AdminUserName,
            IpAddress = ClientIp,
            ResourceId = id.ToString(),
            ResourceName = existing.DisplayName ?? existing.Scheme,
            Scheme = existing.Scheme
        }, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Toggle provider enabled status
    /// </summary>
    [HttpPost("{id:int}/toggle")]
    [RequirePermission(AdminPermissions.IdentityProvidersWrite)]
    public async Task<ActionResult<IdentityProviderDto>> Toggle(int id, CancellationToken cancellationToken)
    {
        var existing = await _providerStore.GetByIdAsync(id, cancellationToken);
        if (existing == null)
            return NotFound();

        // Going from enabled → disabled has the same lockout / default-scheme
        // hazards as disabling via PUT, so route through the same check.
        if (existing.Enabled)
        {
            var disableError = await CheckIdpDisableOrDeleteAsync(
                existing, willStillBeEnabled: false, willStillExist: true, "disabling", cancellationToken);
            if (disableError != null) return BadRequest(new { error = disableError });
        }

        existing.Enabled = !existing.Enabled;
        var updated = await _providerStore.UpdateAsync(existing, cancellationToken);

        _schemeCacheInvalidator?.Invalidate(updated.Scheme, updated.TenantId);

        _logger.LogInformation(
            "{Action} identity provider: {Scheme}",
            existing.Enabled ? "Enabled" : "Disabled", existing.Scheme);

        return Ok(MapToDto(updated));
    }

    /// <summary>
    /// Test provider configuration. The depth of the test depends on the
    /// provider type:
    /// <list type="bullet">
    ///   <item>OIDC / Google / Microsoft / LinkedIn / Apple — fetches the
    ///     <c>/.well-known/openid-configuration</c> document at the
    ///     configured (or known) authority and confirms it parses.</item>
    ///   <item>OAuth2 / GitHub / Facebook / Twitter — checks required
    ///     credentials are present and pings the authorization endpoint
    ///     for reachability (any HTTP response counts; refusal/DNS failure
    ///     does not).</item>
    ///   <item>SAML2 — fetches the IdP metadata XML when a metadata URL is
    ///     configured; otherwise validates the manual endpoints are
    ///     well-formed URLs.</item>
    ///   <item>LDAP — opens a TCP connection to <c>server:port</c> with a
    ///     5-second timeout to confirm the directory is reachable.</item>
    /// </list>
    /// </summary>
    /// <summary>
    /// Looks for icons on the provider's own site (SAML metadata logo, icon
    /// links, apple-touch-icon, favicon). Every image is fetched through an
    /// SSRF-guarded client, identified by its bytes rather than its name or
    /// declared type, decoded and re-encoded as PNG. Nothing the remote site
    /// sent is stored or returned as-is. Changes nothing until the admin
    /// picks one.
    /// </summary>
    [HttpPost("{id:int}/icon/discover")]
    [RequirePermission(AdminPermissions.IdentityProvidersWrite)]
    public async Task<ActionResult<ProviderIconDiscoveryResponse>> DiscoverIcons(int id, CancellationToken cancellationToken)
    {
        var provider = await _providerStore.GetByIdAsync(id, cancellationToken);
        if (provider == null)
            return NotFound();

        var outcome = await _iconDiscovery.DiscoverAsync(provider, cancellationToken);
        return Ok(new ProviderIconDiscoveryResponse
        {
            Origin = outcome.Origin,
            Candidates = outcome.Candidates.Select(c => new ProviderIconCandidateDto
            {
                Source = c.Source,
                SourceUrl = c.SourceUrl,
                DataUrl = c.DataUrl,
                Width = c.Width,
                Height = c.Height,
                SourceFormat = c.SourceFormat,
            }).ToList(),
            Notes = outcome.Notes.ToList(),
        });
    }

    /// <summary>
    /// Stores a discovered (or pasted) PNG data URL as the provider icon.
    /// The bytes are decoded and re-encoded again here, so the stored value
    /// is always an image the server produced.
    /// </summary>
    [HttpPut("{id:int}/icon")]
    [RequirePermission(AdminPermissions.IdentityProvidersWrite)]
    public async Task<ActionResult<IdentityProviderDto>> SetIcon(int id, [FromBody] SetProviderIconRequest request, CancellationToken cancellationToken)
    {
        var provider = await _providerStore.GetByIdAsync(id, cancellationToken);
        if (provider == null)
            return NotFound();
        if (provider.NonEditable)
            return BadRequest("This provider is not editable");

        var normalized = NormalizeIconDataUrl(request.DataUrl, out var rejection);
        if (normalized == null)
            return BadRequest($"Icon rejected: {rejection}");

        provider.IconUrl = normalized.ToDataUrl();
        provider.Updated = DateTime.UtcNow;
        var updated = await _providerStore.UpdateAsync(provider, cancellationToken);
        _schemeCacheInvalidator?.Invalidate(updated.Scheme, updated.TenantId);

        _logger.LogInformation("Identity provider {Scheme} icon set ({Width}x{Height} from {Format})",
            updated.Scheme, normalized.Width, normalized.Height, normalized.SourceFormat);
        return Ok(MapToDto(updated));
    }

    private NormalizedIcon? NormalizeIconDataUrl(string? dataUrl, out string? rejection)
    {
        rejection = null;
        if (string.IsNullOrWhiteSpace(dataUrl))
        {
            rejection = "no image supplied";
            return null;
        }

        var comma = dataUrl.IndexOf(',');
        if (!dataUrl.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase) || comma < 0
            || !dataUrl[..comma].EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
        {
            rejection = "expected a base64 image data URL";
            return null;
        }

        var payload = dataUrl[(comma + 1)..];
        if (payload.Length > IconImageNormalizer.MaxInputBytes * 4 / 3 + 4)
        {
            rejection = "image too large";
            return null;
        }

        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(payload);
        }
        catch (FormatException)
        {
            rejection = "invalid base64";
            return null;
        }

        return _iconNormalizer.Normalize(bytes, ProviderIconDiscoveryService.IconEdge, out rejection);
    }

    [HttpPost("{id:int}/test")]
    [RequirePermission(AdminPermissions.IdentityProvidersWrite)]
    public async Task<ActionResult<TestResult>> TestProvider(int id, CancellationToken cancellationToken)
    {
        var provider = await _providerStore.GetByIdAsync(id, cancellationToken);
        if (provider == null)
            return NotFound();

        var config = string.IsNullOrEmpty(provider.Properties)
            ? new Dictionary<string, object>()
            : (JsonSerializer.Deserialize<Dictionary<string, object>>(provider.Properties)
                ?? new Dictionary<string, object>());

        try
        {
            var result = provider.ProviderType switch
            {
                ExternalProviderType.Oidc => await TestOidcDiscoveryAsync(config, knownAuthority: null, cancellationToken),
                ExternalProviderType.Google => await TestOidcDiscoveryAsync(config, knownAuthority: "https://accounts.google.com", cancellationToken),
                ExternalProviderType.Microsoft => await TestOidcDiscoveryAsync(config, knownAuthority: BuildMicrosoftAuthority(config), cancellationToken),
                ExternalProviderType.LinkedIn => await TestOidcDiscoveryAsync(config, knownAuthority: "https://www.linkedin.com/oauth", cancellationToken),
                ExternalProviderType.Apple => await TestAppleAsync(config, cancellationToken),
                ExternalProviderType.GitHub => await TestFixedOAuthAsync(config, "https://github.com/login/oauth/authorize", cancellationToken),
                ExternalProviderType.Facebook => await TestFixedOAuthAsync(config, "https://www.facebook.com/v18.0/dialog/oauth", cancellationToken),
                ExternalProviderType.Twitter => await TestFixedOAuthAsync(config, "https://twitter.com/i/oauth2/authorize", cancellationToken),
                ExternalProviderType.OAuth2 => await TestOAuth2GenericAsync(config, cancellationToken),
                ExternalProviderType.Saml2 => await TestSamlAsync(config, cancellationToken),
                ExternalProviderType.Ldap => await TestLdapAsync(config, cancellationToken),
                _ => new TestResult { Success = false, Message = $"Unknown provider type: {provider.ProviderType}" }
            };
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Provider test failed for {Scheme}", provider.Scheme);
            return Ok(new TestResult
            {
                Success = false,
                Message = $"Test failed: {ex.Message}"
            });
        }
    }

    /// Microsoft Entra stores <c>instance</c> + <c>tenantId</c> instead of a
    /// flat <c>authority</c>, mirroring how DynamicOAuthOptionsConfigurator
    /// builds the runtime endpoints. Combine them into the v2 OIDC authority
    /// root so the shared discovery test can fetch the metadata document.
    private static string BuildMicrosoftAuthority(Dictionary<string, object> config)
    {
        var explicitAuthority = GetString(config, "authority");
        if (!string.IsNullOrWhiteSpace(explicitAuthority))
            return explicitAuthority;

        var instance = GetString(config, "instance");
        if (string.IsNullOrWhiteSpace(instance))
            instance = "https://login.microsoftonline.com";

        var tenantId = GetString(config, "tenantId");
        if (string.IsNullOrWhiteSpace(tenantId))
            tenantId = "common";

        return $"{instance.TrimEnd('/')}/{tenantId}/v2.0";
    }

    /// <summary>
    /// Fetch the OIDC discovery document at <c>authority + /.well-known/openid-configuration</c>
    /// (or the metadataUrl override) and confirm it deserialises. Validates
    /// clientId presence first since a working discovery doc + missing client
    /// credentials still wouldn't sign anyone in.
    /// </summary>
    private async Task<TestResult> TestOidcDiscoveryAsync(
        Dictionary<string, object> config,
        string? knownAuthority,
        CancellationToken cancellationToken)
    {
        var clientId = GetString(config, "clientId");
        if (string.IsNullOrWhiteSpace(clientId))
            return new TestResult { Success = false, Message = "Client ID is required." };

        var authority = knownAuthority ?? GetString(config, "authority");
        var metadataUrl = GetString(config, "metadataUrl");
        var disableDiscovery = GetBool(config, "disableDiscovery");

        if (disableDiscovery)
        {
            return ValidateManualOidcEndpoints(config);
        }

        var discoveryUrl = !string.IsNullOrWhiteSpace(metadataUrl)
            ? metadataUrl
            : !string.IsNullOrWhiteSpace(authority)
                ? authority.TrimEnd('/') + "/.well-known/openid-configuration"
                : null;

        if (string.IsNullOrWhiteSpace(discoveryUrl))
            return new TestResult { Success = false, Message = "Authority or Metadata URL is required when discovery is enabled." };

        var http = _httpClientFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(8);

        using var response = await http.GetAsync(discoveryUrl, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return new TestResult { Success = false, Message = $"Discovery URL returned {(int)response.StatusCode} {response.StatusCode}: {discoveryUrl}" };
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("issuer", out _)
                || !root.TryGetProperty("authorization_endpoint", out _)
                || !root.TryGetProperty("token_endpoint", out _))
            {
                return new TestResult { Success = false, Message = "Discovery document is missing required fields (issuer / authorization_endpoint / token_endpoint)." };
            }
        }
        catch (JsonException ex)
        {
            return new TestResult { Success = false, Message = $"Discovery URL did not return valid JSON: {ex.Message}" };
        }

        return new TestResult { Success = true, Message = $"Fetched and parsed discovery document at {discoveryUrl}." };
    }

    /// <summary>
    /// When OIDC discovery is disabled, the admin must supply each endpoint
    /// manually. Verify they're populated and well-formed URLs.
    /// </summary>
    private static TestResult ValidateManualOidcEndpoints(Dictionary<string, object> config)
    {
        string[] required = { "authorizationEndpoint", "tokenEndpoint", "jwksUri" };
        foreach (var key in required)
        {
            var value = GetString(config, key);
            if (string.IsNullOrWhiteSpace(value))
                return new TestResult { Success = false, Message = $"Manual OIDC config requires {key}." };
            if (!Uri.TryCreate(value, UriKind.Absolute, out _))
                return new TestResult { Success = false, Message = $"{key} is not a valid absolute URL." };
        }
        return new TestResult { Success = true, Message = "Manual OIDC endpoint configuration is well-formed." };
    }

    /// <summary>
    /// Apple has its own metadata at appleid.apple.com — also verify
    /// TeamId / KeyId / PrivateKey are populated since the OIDC discovery
    /// alone isn't enough to authenticate (Apple uses a JWT client_secret).
    /// </summary>
    private async Task<TestResult> TestAppleAsync(Dictionary<string, object> config, CancellationToken cancellationToken)
    {
        var clientId = GetString(config, "clientId");
        var teamId = GetString(config, "teamId");
        var keyId = GetString(config, "keyId");
        var privateKey = GetString(config, "privateKey");

        if (string.IsNullOrWhiteSpace(clientId))
            return new TestResult { Success = false, Message = "Client ID (Services ID) is required." };
        if (string.IsNullOrWhiteSpace(teamId))
            return new TestResult { Success = false, Message = "Apple Team ID is required." };
        if (string.IsNullOrWhiteSpace(keyId))
            return new TestResult { Success = false, Message = "Apple Key ID is required." };
        if (string.IsNullOrWhiteSpace(privateKey))
            return new TestResult { Success = false, Message = "Apple Private Key is required." };

        return await TestOidcDiscoveryAsync(config, knownAuthority: "https://appleid.apple.com", cancellationToken);
    }

    /// <summary>
    /// OAuth-only providers with fixed authorization endpoints (GitHub,
    /// Facebook, Twitter). Verify credentials are present, then ping the
    /// authorize URL — any HTTP response (200, 302, 400) means the host
    /// is reachable; only DNS / connect failures mark the test as failed.
    /// </summary>
    private async Task<TestResult> TestFixedOAuthAsync(
        Dictionary<string, object> config,
        string authorizeUrl,
        CancellationToken cancellationToken)
    {
        var clientId = GetString(config, "clientId");
        if (string.IsNullOrWhiteSpace(clientId))
            return new TestResult { Success = false, Message = "Client ID is required." };

        var clientSecret = GetString(config, "clientSecret");
        if (string.IsNullOrWhiteSpace(clientSecret))
            return new TestResult { Success = false, Message = "Client Secret is required." };

        return await PingHostAsync(authorizeUrl, "OAuth authorization endpoint", cancellationToken);
    }

    /// <summary>
    /// Generic OAuth2 (the admin-defined kind) — admin supplies the
    /// authorization and token endpoints. Validate format then ping.
    /// </summary>
    private async Task<TestResult> TestOAuth2GenericAsync(Dictionary<string, object> config, CancellationToken cancellationToken)
    {
        var clientId = GetString(config, "clientId");
        if (string.IsNullOrWhiteSpace(clientId))
            return new TestResult { Success = false, Message = "Client ID is required." };

        var authorizeUrl = GetString(config, "authorizationEndpoint");
        var tokenUrl = GetString(config, "tokenEndpoint");

        if (string.IsNullOrWhiteSpace(authorizeUrl))
            return new TestResult { Success = false, Message = "Authorization Endpoint is required." };
        if (string.IsNullOrWhiteSpace(tokenUrl))
            return new TestResult { Success = false, Message = "Token Endpoint is required." };
        if (!Uri.TryCreate(authorizeUrl, UriKind.Absolute, out _))
            return new TestResult { Success = false, Message = "Authorization Endpoint is not a valid absolute URL." };
        if (!Uri.TryCreate(tokenUrl, UriKind.Absolute, out _))
            return new TestResult { Success = false, Message = "Token Endpoint is not a valid absolute URL." };

        return await PingHostAsync(authorizeUrl, "Authorization Endpoint", cancellationToken);
    }

    /// <summary>
    /// SAML — fetch and parse metadata XML if a URL is configured. When
    /// endpoints are entered manually, validate URL format only.
    /// </summary>
    private async Task<TestResult> TestSamlAsync(Dictionary<string, object> config, CancellationToken cancellationToken)
    {
        var entityId = GetString(config, "entityId");
        if (string.IsNullOrWhiteSpace(entityId))
            return new TestResult { Success = false, Message = "Entity ID is required." };

        var metadataUrl = GetString(config, "metadataUrl");
        if (!string.IsNullOrWhiteSpace(metadataUrl))
        {
            if (!Uri.TryCreate(metadataUrl, UriKind.Absolute, out _))
                return new TestResult { Success = false, Message = "Metadata URL is not a valid absolute URL." };

            var http = _httpClientFactory.CreateClient();
            http.Timeout = TimeSpan.FromSeconds(8);
            using var response = await http.GetAsync(metadataUrl, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new TestResult { Success = false, Message = $"Metadata URL returned {(int)response.StatusCode} {response.StatusCode}." };
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            try
            {
                var xml = new System.Xml.XmlDocument();
                xml.LoadXml(body);
                var localName = xml.DocumentElement?.LocalName;
                if (localName != "EntityDescriptor" && localName != "EntitiesDescriptor")
                {
                    return new TestResult { Success = false, Message = $"Metadata document root is <{localName}>; expected <EntityDescriptor> or <EntitiesDescriptor>." };
                }
            }
            catch (System.Xml.XmlException ex)
            {
                return new TestResult { Success = false, Message = $"Metadata is not valid XML: {ex.Message}" };
            }
            return new TestResult { Success = true, Message = $"Fetched and parsed SAML metadata at {metadataUrl}." };
        }

        // Manual endpoints — verify the SSO URL is at least a well-formed URL.
        var ssoUrl = GetString(config, "singleSignOnServiceUrl");
        if (string.IsNullOrWhiteSpace(ssoUrl))
            return new TestResult { Success = false, Message = "Either Metadata URL or Single Sign-On Service URL is required." };
        if (!Uri.TryCreate(ssoUrl, UriKind.Absolute, out _))
            return new TestResult { Success = false, Message = "Single Sign-On Service URL is not a valid absolute URL." };

        return new TestResult { Success = true, Message = "Manual SAML endpoint configuration is well-formed." };
    }

    /// <summary>
    /// LDAP — open a raw TCP connection to <c>server:port</c> with a
    /// 5-second timeout. We don't bind here (binding requires the LDAP
    /// enterprise module's connection pool); reachability + required-field
    /// validation is enough for the "Test" button on this page.
    /// </summary>
    private async Task<TestResult> TestLdapAsync(Dictionary<string, object> config, CancellationToken cancellationToken)
    {
        var server = GetString(config, "server");
        if (string.IsNullOrWhiteSpace(server))
            return new TestResult { Success = false, Message = "LDAP server is required." };

        var baseDn = GetString(config, "baseDn");
        if (string.IsNullOrWhiteSpace(baseDn))
            return new TestResult { Success = false, Message = "Base DN is required." };

        var portStr = GetString(config, "port");
        var useSsl = GetBool(config, "useSsl");
        var defaultPort = useSsl ? 636 : 389;
        var port = int.TryParse(portStr, out var p) ? p : defaultPort;

        try
        {
            using var tcp = new System.Net.Sockets.TcpClient();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(TimeSpan.FromSeconds(5));
            await tcp.ConnectAsync(server, port, cts.Token);
            return new TestResult { Success = true, Message = $"TCP connection to {server}:{port} succeeded. Bind credentials are not validated by this test." };
        }
        catch (OperationCanceledException)
        {
            return new TestResult { Success = false, Message = $"Connection to {server}:{port} timed out after 5 seconds." };
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            return new TestResult { Success = false, Message = $"Could not reach {server}:{port}: {ex.SocketErrorCode}." };
        }
    }

    /// <summary>
    /// Reachability ping for fixed-endpoint OAuth providers. We only need to
    /// know the host responds — any HTTP status counts as a pass; we'd see
    /// real auth errors (invalid client_id etc.) only on a full sign-in flow.
    /// </summary>
    private async Task<TestResult> PingHostAsync(string url, string label, CancellationToken cancellationToken)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return new TestResult { Success = false, Message = $"{label} is not a valid URL." };

        var http = _httpClientFactory.CreateClient();
        http.Timeout = TimeSpan.FromSeconds(8);
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, uri);
            using var response = await http.SendAsync(request, cancellationToken);
            return new TestResult { Success = true, Message = $"{label} reachable at {uri.Host} ({(int)response.StatusCode})." };
        }
        catch (HttpRequestException ex)
        {
            return new TestResult { Success = false, Message = $"Could not reach {uri.Host}: {ex.Message}" };
        }
        catch (TaskCanceledException)
        {
            return new TestResult { Success = false, Message = $"Connection to {uri.Host} timed out." };
        }
    }

    private static string GetString(Dictionary<string, object> config, string key)
        => config.TryGetValue(key, out var v) ? v?.ToString() ?? "" : "";

    private static bool GetBool(Dictionary<string, object> config, string key)
    {
        if (!config.TryGetValue(key, out var v) || v == null) return false;
        return bool.TryParse(v.ToString(), out var b) && b;
    }

    // Fields that should be masked in API responses
    private static readonly HashSet<string> SensitiveFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "clientSecret", "client_secret", "secret", "privateKey", "private_key",
        "bindPassword", "bind_password", "password", "apiKey", "api_key",
        "signingCertificate", "signing_certificate"
    };

    private static IdentityProviderDto MapToDto(IdentityProvider provider) => new()
    {
        Id = provider.Id,
        Scheme = provider.Scheme,
        DisplayName = provider.DisplayName,
        Enabled = provider.Enabled,
        ProviderType = provider.ProviderType,
        IconUrl = provider.IconUrl,
        DisplayOrder = provider.DisplayOrder,
        AllowedClientIds = provider.AllowedClientIds,
        Configuration = MaskSensitiveFields(provider.Properties),
        Created = provider.Created,
        Updated = provider.Updated,
        LastAccessed = provider.LastAccessed
    };

    private static Dictionary<string, object> MergeConfigurationPreservingSecrets(
        string? existingProperties,
        Dictionary<string, object> newConfig)
    {
        if (string.IsNullOrEmpty(existingProperties))
            return newConfig;

        var existingConfig = JsonSerializer.Deserialize<Dictionary<string, object>>(existingProperties);
        if (existingConfig == null)
            return newConfig;

        var merged = new Dictionary<string, object>(newConfig);

        foreach (var field in SensitiveFields)
        {
            if (merged.TryGetValue(field, out var newValue))
            {
                var newValueStr = newValue?.ToString();
                if (newValueStr == "••••••••" || newValueStr == "********")
                {
                    if (existingConfig.TryGetValue(field, out var originalValue))
                    {
                        merged[field] = originalValue;
                    }
                }
            }
        }

        return merged;
    }

    private static Dictionary<string, object>? MaskSensitiveFields(string? properties)
    {
        if (string.IsNullOrEmpty(properties))
            return null;

        var config = JsonSerializer.Deserialize<Dictionary<string, object>>(properties);
        if (config == null)
            return null;

        var masked = new Dictionary<string, object>();
        foreach (var kvp in config)
        {
            if (SensitiveFields.Contains(kvp.Key) && kvp.Value != null)
            {
                var valueStr = kvp.Value.ToString();
                masked[kvp.Key] = string.IsNullOrEmpty(valueStr) ? "" : "••••••••";
            }
            else
            {
                masked[kvp.Key] = kvp.Value ?? "";
            }
        }

        return masked;
    }

    /// <summary>
    /// Validate the <c>defaultRoles</c> and <c>accessControl</c> sections of
    /// an IdP configuration. Returns null when valid, or a human-readable
    /// error message suitable for a 400 response. Catches the kinds of
    /// mistakes the admin UI can't fully prevent (claim-gate with no values,
    /// allowlist with no emails, role names that don't exist for this tenant).
    /// </summary>
    private async Task<string?> ValidateAccessControlAndDefaultRolesAsync(
        Dictionary<string, object>? configuration,
        CancellationToken cancellationToken)
    {
        if (configuration == null) return null;

        // defaultRoles — every entry must be an existing role for the
        // current tenant. Reject unknown roles up front so saves don't
        // silently succeed and then no-op at sign-in time.
        if (configuration.TryGetValue("defaultRoles", out var rolesObj) && rolesObj != null)
        {
            var roleIds = ExtractStringList(rolesObj);
            foreach (var roleId in roleIds)
            {
                var role = await _roleStore.GetByIdAsync(roleId, cancellationToken);
                if (role == null || (role.TenantId != null && role.TenantId != TenantId))
                {
                    return $"Default role '{roleId}' is not a valid role for this tenant.";
                }
            }
        }

        // accessControl — mode + the per-mode required fields.
        if (configuration.TryGetValue("accessControl", out var acObj) && acObj is JsonElement acElement
            && acElement.ValueKind == JsonValueKind.Object)
        {
            var mode = acElement.TryGetProperty("mode", out var modeProp) && modeProp.ValueKind == JsonValueKind.String
                ? modeProp.GetString() ?? "open"
                : "open";

            if (string.Equals(mode, "emailAllowlist", StringComparison.OrdinalIgnoreCase))
            {
                if (!acElement.TryGetProperty("allowedEmails", out var emailsProp)
                    || emailsProp.ValueKind != JsonValueKind.Array
                    || emailsProp.GetArrayLength() == 0)
                {
                    return "Access control mode 'emailAllowlist' requires at least one allowed email.";
                }
                foreach (var emailEl in emailsProp.EnumerateArray())
                {
                    if (emailEl.ValueKind != JsonValueKind.String) continue;
                    var email = emailEl.GetString();
                    if (string.IsNullOrWhiteSpace(email) || !LooksLikeEmail(email))
                    {
                        return $"'{email}' is not a valid email address.";
                    }
                }
            }
            else if (string.Equals(mode, "claimGate", StringComparison.OrdinalIgnoreCase))
            {
                if (!acElement.TryGetProperty("claimGate", out var gateProp) || gateProp.ValueKind != JsonValueKind.Object)
                {
                    return "Access control mode 'claimGate' requires a claimGate object.";
                }
                if (!gateProp.TryGetProperty("claimPath", out var pathProp)
                    || pathProp.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(pathProp.GetString()))
                {
                    return "Claim gate requires a non-empty claimPath.";
                }
                if (!gateProp.TryGetProperty("anyOf", out var anyProp)
                    || anyProp.ValueKind != JsonValueKind.Array
                    || anyProp.GetArrayLength() == 0)
                {
                    return "Claim gate requires at least one allowed value in anyOf.";
                }
            }
            else if (!string.Equals(mode, "open", StringComparison.OrdinalIgnoreCase))
            {
                return $"Unknown access control mode '{mode}'. Use 'open', 'emailAllowlist', or 'claimGate'.";
            }
        }

        return null;
    }

    private static List<string> ExtractStringList(object value)
    {
        if (value is JsonElement element && element.ValueKind == JsonValueKind.Array)
        {
            var list = new List<string>();
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var s = item.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
                }
            }
            return list;
        }
        if (value is IEnumerable<object> objs)
        {
            return objs.Select(o => o?.ToString() ?? "")
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
        }
        return new List<string>();
    }

    private static bool LooksLikeEmail(string value)
    {
        try
        {
            var addr = new System.Net.Mail.MailAddress(value);
            return addr.Address == value;
        }
        catch { return false; }
    }

    /// <summary>
    /// Guards against two hazards when disabling or deleting an identity
    /// provider:
    ///   1. The provider is set as the tenant's <c>DefaultIdpScheme</c> —
    ///      removing it leaves a stale reference that breaks "Login with SSO".
    ///   2. Disabling/deleting it would leave the tenant with no sign-in
    ///      method (local login off, passkey off, no other enabled IdPs).
    /// Returns a user-facing error message on failure, or null when the
    /// modification is safe to apply. <paramref name="actionVerb"/> is
    /// interpolated into the message ("disabling" / "deleting").
    /// </summary>
    private async Task<string?> CheckIdpDisableOrDeleteAsync(
        IdentityProvider existing,
        bool willStillBeEnabled,
        bool willStillExist,
        string actionVerb,
        CancellationToken cancellationToken)
    {
        // Global providers (TenantId == null) aren't owned by a tenant, so
        // there's no per-tenant lockout / default-scheme to protect.
        if (string.IsNullOrEmpty(existing.TenantId))
            return null;

        var tenant = await _tenantStore.GetByIdAsync(existing.TenantId, cancellationToken);
        if (tenant == null)
            return null; // defensive — IdP without a tenant shouldn't happen

        // Hazard 1: stale DefaultIdpScheme reference.
        if (string.Equals(tenant.DefaultIdpScheme, existing.Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return $"This identity provider is set as the tenant's default for SSO. " +
                   $"Clear or change the default before {actionVerb} it.";
        }

        // Hazard 2: lockout. Only matters if both local login and passkey
        // are already off — otherwise the user still has a way to sign in.
        if (!tenant.EnableLocalLogin && !tenant.EnablePasskey)
        {
            var providers = await _providerStore.GetAllForTenantAsync(existing.TenantId, cancellationToken);
            var remainingEnabled = providers.Count(p =>
            {
                if (p.Id == existing.Id)
                {
                    if (!willStillExist) return false;
                    return willStillBeEnabled;
                }
                return p.Enabled;
            });

            if (remainingEnabled == 0)
            {
                return $"Cannot {actionVerb} this identity provider: the tenant has local login and passkey " +
                       $"both disabled, and this is the last enabled identity provider. Users would have " +
                       $"no way to sign in. Enable local login or passkey first, or keep another IdP enabled.";
            }
        }

        return null;
    }
}

using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oluso.Core.Api;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;

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
    private readonly IConfiguration _configuration;
    private readonly ITenantContext _tenantContext;
    private readonly IOlusoEventService _eventService;
    private readonly ILogger<IdentityProvidersController> _logger;

    public IdentityProvidersController(
        IIdentityProviderStore providerStore,
        IConfiguration configuration,
        ITenantContext tenantContext,
        IOlusoEventService eventService,
        ILogger<IdentityProvidersController> logger) : base(tenantContext)
    {
        _providerStore = providerStore;
        _configuration = configuration;
        _tenantContext = tenantContext;
        _eventService = eventService;
        _logger = logger;
    }

    private string? TenantId => _tenantContext.TenantId ?? User.FindFirst("tenant_id")?.Value;
    private string AdminUserId => User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "unknown";
    private string? AdminUserName => User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value ?? User.FindFirst("name")?.Value;
    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    /// <summary>
    /// Get server info for callback URLs
    /// </summary>
    [HttpGet("server-info")]
    public ActionResult<ServerInfoResponse> GetServerInfo()
    {
        var issuerUri = _configuration["Oluso:IssuerUri"]
            ?? _configuration["Jwt:Issuer"]
            ?? $"{Request.Scheme}://{Request.Host}";

        return Ok(new ServerInfoResponse
        {
            IssuerUri = issuerUri.TrimEnd('/'),
            CallbackPathTemplate = "/signin-{scheme}"
        });
    }

    /// <summary>
    /// Check if a scheme name is available (globally unique across all tenants)
    /// </summary>
    [HttpGet("check-scheme/{scheme}")]
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
    public async Task<ActionResult<IEnumerable<IdentityProviderDto>>> GetAll(CancellationToken cancellationToken)
    {
        var providers = await _providerStore.GetAllAsync(cancellationToken);
        var dtos = providers.Select(MapToDto);
        return Ok(dtos);
    }

    /// <summary>
    /// Get identity provider by ID with configuration for editing.
    /// Secrets are masked - use GET /{id}/secret/{key} to reveal individual secrets.
    /// </summary>
    [HttpGet("{id:int}/edit")]
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
                Type = ExternalProviderType.GitHub,
                Name = "GitHub",
                Description = "GitHub OAuth (supports Enterprise)",
                RequiredFields = new[] { "clientId", "clientSecret" },
                OptionalFields = new[] { "enterpriseUrl", "allowedOrganizations", "requestEmail" }
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
    public async Task<ActionResult<IdentityProviderDto>> Create(
        [FromBody] CreateIdentityProviderRequest request,
        CancellationToken cancellationToken)
    {
        // Validate scheme doesn't exist globally (auth schemes must be unique across all tenants)
        if (await _providerStore.SchemeExistsGloballyAsync(request.Scheme, cancellationToken))
        {
            return Conflict(new { error = $"Provider with scheme '{request.Scheme}' already exists. Authentication schemes must be unique across all tenants." });
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
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var existing = await _providerStore.GetByIdAsync(id, cancellationToken);
        if (existing == null)
            return NotFound();

        if (existing.NonEditable)
            return BadRequest(new { error = "This provider cannot be deleted" });

        await _providerStore.DeleteAsync(id, cancellationToken);

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
    public async Task<ActionResult<IdentityProviderDto>> Toggle(int id, CancellationToken cancellationToken)
    {
        var existing = await _providerStore.GetByIdAsync(id, cancellationToken);
        if (existing == null)
            return NotFound();

        existing.Enabled = !existing.Enabled;
        var updated = await _providerStore.UpdateAsync(existing, cancellationToken);

        _logger.LogInformation(
            "{Action} identity provider: {Scheme}",
            existing.Enabled ? "Enabled" : "Disabled", existing.Scheme);

        return Ok(MapToDto(updated));
    }

    /// <summary>
    /// Test provider configuration by attempting to fetch discovery document
    /// </summary>
    [HttpPost("{id:int}/test")]
    public async Task<ActionResult<TestResult>> TestProvider(int id, CancellationToken cancellationToken)
    {
        var provider = await _providerStore.GetByIdAsync(id, cancellationToken);
        if (provider == null)
            return NotFound();

        try
        {
            // For OIDC providers, try to fetch discovery document
            if (provider.ProviderType == ExternalProviderType.Oidc ||
                provider.ProviderType == ExternalProviderType.Google ||
                provider.ProviderType == ExternalProviderType.Microsoft)
            {
                if (!string.IsNullOrEmpty(provider.Properties))
                {
                    var config = JsonSerializer.Deserialize<Dictionary<string, object>>(provider.Properties);
                    if (config != null && config.TryGetValue("authority", out var authorityObj))
                    {
                        var authority = authorityObj?.ToString();
                        if (!string.IsNullOrEmpty(authority))
                        {
                            using var httpClient = new HttpClient();
                            var discoveryUrl = authority.TrimEnd('/') + "/.well-known/openid-configuration";
                            var response = await httpClient.GetAsync(discoveryUrl, cancellationToken);

                            if (response.IsSuccessStatusCode)
                            {
                                return Ok(new TestResult
                                {
                                    Success = true,
                                    Message = "Successfully fetched OIDC discovery document"
                                });
                            }
                            else
                            {
                                return Ok(new TestResult
                                {
                                    Success = false,
                                    Message = $"Failed to fetch discovery document: {response.StatusCode}"
                                });
                            }
                        }
                    }
                }
            }

            return Ok(new TestResult
            {
                Success = true,
                Message = "Configuration appears valid (limited validation for this provider type)"
            });
        }
        catch (Exception ex)
        {
            return Ok(new TestResult
            {
                Success = false,
                Message = $"Test failed: {ex.Message}"
            });
        }
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
}

#region DTOs

public class IdentityProviderDto
{
    public int Id { get; set; }
    public string Scheme { get; set; } = null!;
    public string? DisplayName { get; set; }
    public bool Enabled { get; set; }
    public ExternalProviderType ProviderType { get; set; }
    public string? IconUrl { get; set; }
    public int DisplayOrder { get; set; }
    public List<string> AllowedClientIds { get; set; } = new();
    public Dictionary<string, object>? Configuration { get; set; }
    public DateTime Created { get; set; }
    public DateTime? Updated { get; set; }
    public DateTime? LastAccessed { get; set; }
}

public class IdentityProviderEditDto
{
    public int Id { get; set; }
    public string Scheme { get; set; } = null!;
    public string? DisplayName { get; set; }
    public bool Enabled { get; set; }
    public ExternalProviderType ProviderType { get; set; }
    public string? IconUrl { get; set; }
    public int DisplayOrder { get; set; }
    public List<string> AllowedClientIds { get; set; } = new();
    public Dictionary<string, object>? Configuration { get; set; }
    public bool NonEditable { get; set; }
    public DateTime Created { get; set; }
    public DateTime? Updated { get; set; }
    public DateTime? LastAccessed { get; set; }
}

public class CreateIdentityProviderRequest
{
    public string Scheme { get; set; } = null!;
    public string? DisplayName { get; set; }
    public bool? Enabled { get; set; }
    public ExternalProviderType ProviderType { get; set; }
    public string? IconUrl { get; set; }
    public int? DisplayOrder { get; set; }
    public List<string>? AllowedClientIds { get; set; }
    public Dictionary<string, object>? Configuration { get; set; }
}

public class UpdateIdentityProviderRequest
{
    public string? DisplayName { get; set; }
    public bool? Enabled { get; set; }
    public ExternalProviderType? ProviderType { get; set; }
    public string? IconUrl { get; set; }
    public int? DisplayOrder { get; set; }
    public List<string>? AllowedClientIds { get; set; }
    public Dictionary<string, object>? Configuration { get; set; }
}

public class ProviderTypeInfo
{
    public ExternalProviderType Type { get; set; }
    public string Name { get; set; } = null!;
    public string Description { get; set; } = null!;
    public string? IconUrl { get; set; }
    public string[] RequiredFields { get; set; } = Array.Empty<string>();
    public string[] OptionalFields { get; set; } = Array.Empty<string>();
}

public class TestResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = null!;
}

public class ServerInfoResponse
{
    public string IssuerUri { get; set; } = null!;
    public string CallbackPathTemplate { get; set; } = null!;
}

public class SchemeAvailabilityResponse
{
    public bool Available { get; set; }
    public string? Message { get; set; }
}

public class SecretRevealResponse
{
    public string Value { get; set; } = null!;
}

#endregion

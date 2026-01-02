using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oluso.Core.Api;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Services;
using Oluso.Enterprise.Saml.Configuration;
using Oluso.Enterprise.Saml.Entities;
using Oluso.Enterprise.Saml.Services;
using Oluso.Enterprise.Saml.Stores;

namespace Oluso.Enterprise.Saml.Controllers;

/// <summary>
/// Admin API for managing SAML Service Providers.
/// SAML SPs are applications (like Salesforce, ServiceNow) that use this system as their SAML IdP.
/// </summary>
[Route("api/admin/saml/service-providers")]
public class SamlServiceProvidersController : AdminBaseController
{
    private readonly ISamlServiceProviderStore _spStore;
    private readonly ISamlTenantSettingsService _samlTenantSettings;
    private readonly IOptions<SamlIdpOptions> _idpOptions;
    private readonly IConfiguration _configuration;
    private readonly IOlusoEventService _eventService;
    private readonly ILogger<SamlServiceProvidersController> _logger;

    public SamlServiceProvidersController(
        ISamlServiceProviderStore spStore,
        ISamlTenantSettingsService samlTenantSettings,
        IOptions<SamlIdpOptions> idpOptions,
        IConfiguration configuration,
        ITenantContext tenantContext,
        IOlusoEventService eventService,
        ILogger<SamlServiceProvidersController> logger) : base(tenantContext)
    {
        _spStore = spStore;
        _samlTenantSettings = samlTenantSettings;
        _idpOptions = idpOptions;
        _configuration = configuration;
        _eventService = eventService;
        _logger = logger;
    }

    /// <summary>
    /// Get IdP server info (Entity ID, SSO URL, Metadata URL) for configuring SPs.
    /// The Enabled status is pulled from tenant settings in the database.
    /// </summary>
    [HttpGet("idp-info")]
    public async Task<ActionResult<IdpInfoResponse>> GetIdpInfo(CancellationToken cancellationToken)
    {
        var options = _idpOptions.Value;
        var baseUrl = options.BaseUrl;

        if (string.IsNullOrEmpty(baseUrl))
        {
            baseUrl = _configuration["Oluso:IssuerUri"]
                ?? _configuration["Jwt:Issuer"]
                ?? $"{Request.Scheme}://{Request.Host}";
        }

        baseUrl = baseUrl.TrimEnd('/');

        // Get the enabled status from tenant settings in the database
        var tenantSettings = TenantId != null
            ? await _samlTenantSettings.GetSettingsAsync(TenantId, cancellationToken)
            : null;

        // Enabled is true if: tenant settings say enabled, or no tenant context but global config is enabled
        var isEnabled = tenantSettings?.Enabled ?? options.Enabled;

        return Ok(new IdpInfoResponse
        {
            Enabled = isEnabled,
            EntityId = tenantSettings?.EntityId ?? options.EntityId ?? baseUrl,
            SingleSignOnServiceUrl = $"{baseUrl}{options.SingleSignOnServicePath}",
            SingleLogoutServiceUrl = $"{baseUrl}{options.SingleLogoutServicePath}",
            MetadataUrl = $"{baseUrl}{options.MetadataPath}",
            SupportedNameIdFormats = options.NameIdFormats
        });
    }

    /// <summary>
    /// Check if an Entity ID is available (unique within tenant)
    /// </summary>
    [HttpGet("check-entity-id/{entityId}")]
    public async Task<ActionResult<EntityIdAvailabilityResponse>> CheckEntityIdAvailability(
        string entityId,
        [FromQuery] int? excludeId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(entityId))
        {
            return Ok(new EntityIdAvailabilityResponse { Available = false, Message = "Entity ID is required" });
        }

        var decoded = Uri.UnescapeDataString(entityId);
        var exists = await _spStore.ExistsAsync(decoded, excludeId, cancellationToken);

        return Ok(new EntityIdAvailabilityResponse
        {
            Available = !exists,
            Message = exists ? "This Entity ID is already registered" : null
        });
    }

    /// <summary>
    /// Get all SAML Service Providers
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<SamlServiceProviderDto>>> GetAll(
        [FromQuery] bool includeDisabled = false,
        CancellationToken cancellationToken = default)
    {
        var providers = await _spStore.GetAllAsync(includeDisabled, cancellationToken);
        var dtos = providers.Select(MapToDto);
        return Ok(dtos);
    }

    /// <summary>
    /// Get SAML Service Provider by ID
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<SamlServiceProviderDto>> GetById(int id, CancellationToken cancellationToken)
    {
        var provider = await _spStore.GetByIdAsync(id, cancellationToken);
        if (provider == null)
            return NotFound();

        return Ok(MapToDto(provider));
    }

    /// <summary>
    /// Get SAML Service Provider by ID for editing (returns unmasked certificates)
    /// </summary>
    [HttpGet("{id:int}/edit")]
    public async Task<ActionResult<SamlServiceProviderEditDto>> GetForEdit(int id, CancellationToken cancellationToken)
    {
        var provider = await _spStore.GetByIdAsync(id, cancellationToken);
        if (provider == null)
            return NotFound();

        _logger.LogInformation(
            "Full configuration accessed for SAML SP {EntityId} (edit mode)",
            provider.EntityId);

        return Ok(new SamlServiceProviderEditDto
        {
            Id = provider.Id,
            EntityId = provider.EntityId,
            DisplayName = provider.DisplayName,
            Description = provider.Description,
            Enabled = provider.Enabled,
            MetadataUrl = provider.MetadataUrl,
            AssertionConsumerServiceUrl = provider.AssertionConsumerServiceUrl,
            SingleLogoutServiceUrl = provider.SingleLogoutServiceUrl,
            SigningCertificate = provider.SigningCertificate,
            EncryptionCertificate = provider.EncryptionCertificate,
            EncryptAssertions = provider.EncryptAssertions,
            NameIdFormat = provider.NameIdFormat,
            AllowedClaims = ParseJsonList(provider.AllowedClaimsJson),
            ClaimMappings = ParseJsonDictionary(provider.ClaimMappingsJson),
            SsoBinding = provider.SsoBinding,
            SignResponses = provider.SignResponses,
            SignAssertions = provider.SignAssertions,
            RequireSignedAuthnRequests = provider.RequireSignedAuthnRequests,
            DefaultRelayState = provider.DefaultRelayState,
            NonEditable = provider.NonEditable
        });
    }

    /// <summary>
    /// Get SAML Service Provider by Entity ID
    /// </summary>
    [HttpGet("by-entity-id/{entityId}")]
    public async Task<ActionResult<SamlServiceProviderDto>> GetByEntityId(string entityId, CancellationToken cancellationToken)
    {
        var decoded = Uri.UnescapeDataString(entityId);
        var provider = await _spStore.GetByEntityIdAsync(decoded, cancellationToken);
        if (provider == null)
            return NotFound();

        return Ok(MapToDto(provider));
    }

    /// <summary>
    /// Create a new SAML Service Provider
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<SamlServiceProviderDto>> Create(
        [FromBody] CreateSamlServiceProviderRequest request,
        CancellationToken cancellationToken)
    {
        // Validate Entity ID doesn't exist
        if (await _spStore.ExistsAsync(request.EntityId, cancellationToken: cancellationToken))
        {
            return Conflict(new { error = $"Service Provider with Entity ID '{request.EntityId}' already exists." });
        }

        var provider = new SamlServiceProvider
        {
            EntityId = request.EntityId,
            DisplayName = request.DisplayName,
            Description = request.Description,
            Enabled = request.Enabled ?? true,
            MetadataUrl = request.MetadataUrl,
            AssertionConsumerServiceUrl = request.AssertionConsumerServiceUrl,
            SingleLogoutServiceUrl = request.SingleLogoutServiceUrl,
            SigningCertificate = request.SigningCertificate,
            EncryptionCertificate = request.EncryptionCertificate,
            EncryptAssertions = request.EncryptAssertions ?? false,
            NameIdFormat = request.NameIdFormat,
            AllowedClaimsJson = SerializeJsonList(request.AllowedClaims),
            ClaimMappingsJson = SerializeJsonDictionary(request.ClaimMappings),
            SsoBinding = request.SsoBinding ?? "POST",
            SignResponses = request.SignResponses ?? true,
            SignAssertions = request.SignAssertions ?? true,
            RequireSignedAuthnRequests = request.RequireSignedAuthnRequests ?? false,
            DefaultRelayState = request.DefaultRelayState
        };

        var created = await _spStore.AddAsync(provider, cancellationToken);

        _logger.LogInformation(
            "Created SAML Service Provider: {EntityId} ({DisplayName})",
            created.EntityId, created.DisplayName);

        // Raise audit event
        await _eventService.RaiseAsync(new AdminSamlSpCreatedEvent
        {
            TenantId = TenantId,
            AdminUserId = AdminUserId!,
            AdminUserName = AdminUserName,
            IpAddress = ClientIp,
            ResourceId = created.Id.ToString(),
            ResourceName = created.DisplayName ?? created.EntityId,
            EntityId = created.EntityId
        }, cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = created.Id }, MapToDto(created));
    }

    /// <summary>
    /// Update a SAML Service Provider
    /// </summary>
    [HttpPut("{id:int}")]
    public async Task<ActionResult<SamlServiceProviderDto>> Update(
        int id,
        [FromBody] UpdateSamlServiceProviderRequest request,
        CancellationToken cancellationToken)
    {
        var existing = await _spStore.GetByIdAsync(id, cancellationToken);
        if (existing == null)
            return NotFound();

        if (existing.NonEditable)
            return BadRequest(new { error = "This Service Provider cannot be modified" });

        // Update properties
        if (request.DisplayName != null) existing.DisplayName = request.DisplayName;
        if (request.Description != null) existing.Description = request.Description;
        if (request.Enabled.HasValue) existing.Enabled = request.Enabled.Value;
        if (request.MetadataUrl != null) existing.MetadataUrl = request.MetadataUrl;
        if (request.AssertionConsumerServiceUrl != null) existing.AssertionConsumerServiceUrl = request.AssertionConsumerServiceUrl;
        if (request.SingleLogoutServiceUrl != null) existing.SingleLogoutServiceUrl = request.SingleLogoutServiceUrl;
        if (request.EncryptAssertions.HasValue) existing.EncryptAssertions = request.EncryptAssertions.Value;
        if (request.NameIdFormat != null) existing.NameIdFormat = request.NameIdFormat;
        if (request.SsoBinding != null) existing.SsoBinding = request.SsoBinding;
        if (request.SignResponses.HasValue) existing.SignResponses = request.SignResponses.Value;
        if (request.SignAssertions.HasValue) existing.SignAssertions = request.SignAssertions.Value;
        if (request.RequireSignedAuthnRequests.HasValue) existing.RequireSignedAuthnRequests = request.RequireSignedAuthnRequests.Value;
        if (request.DefaultRelayState != null) existing.DefaultRelayState = request.DefaultRelayState;

        // Handle certificates - preserve masked values
        if (request.SigningCertificate != null && !IsMaskedValue(request.SigningCertificate))
        {
            existing.SigningCertificate = request.SigningCertificate;
        }
        if (request.EncryptionCertificate != null && !IsMaskedValue(request.EncryptionCertificate))
        {
            existing.EncryptionCertificate = request.EncryptionCertificate;
        }

        // Update JSON fields
        if (request.AllowedClaims != null)
        {
            existing.AllowedClaimsJson = SerializeJsonList(request.AllowedClaims);
        }
        if (request.ClaimMappings != null)
        {
            existing.ClaimMappingsJson = SerializeJsonDictionary(request.ClaimMappings);
        }

        var updated = await _spStore.UpdateAsync(existing, cancellationToken);

        _logger.LogInformation("Updated SAML Service Provider: {EntityId}", updated.EntityId);

        // Raise audit event
        await _eventService.RaiseAsync(new AdminSamlSpUpdatedEvent
        {
            TenantId = TenantId,
            AdminUserId = AdminUserId!,
            AdminUserName = AdminUserName,
            IpAddress = ClientIp,
            ResourceId = updated.Id.ToString(),
            ResourceName = updated.DisplayName ?? updated.EntityId,
            EntityId = updated.EntityId
        }, cancellationToken);

        return Ok(MapToDto(updated));
    }

    /// <summary>
    /// Delete a SAML Service Provider
    /// </summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var existing = await _spStore.GetByIdAsync(id, cancellationToken);
        if (existing == null)
            return NotFound();

        if (existing.NonEditable)
            return BadRequest(new { error = "This Service Provider cannot be deleted" });

        await _spStore.DeleteAsync(id, cancellationToken);

        _logger.LogInformation("Deleted SAML Service Provider: {EntityId}", existing.EntityId);

        // Raise audit event
        await _eventService.RaiseAsync(new AdminSamlSpDeletedEvent
        {
            TenantId = TenantId,
            AdminUserId = AdminUserId!,
            AdminUserName = AdminUserName,
            IpAddress = ClientIp,
            ResourceId = id.ToString(),
            ResourceName = existing.DisplayName ?? existing.EntityId,
            EntityId = existing.EntityId
        }, cancellationToken);

        return NoContent();
    }

    /// <summary>
    /// Toggle Service Provider enabled status
    /// </summary>
    [HttpPost("{id:int}/toggle")]
    public async Task<ActionResult<SamlServiceProviderDto>> Toggle(int id, CancellationToken cancellationToken)
    {
        var existing = await _spStore.GetByIdAsync(id, cancellationToken);
        if (existing == null)
            return NotFound();

        existing.Enabled = !existing.Enabled;
        var updated = await _spStore.UpdateAsync(existing, cancellationToken);

        _logger.LogInformation(
            "{Action} SAML Service Provider: {EntityId}",
            existing.Enabled ? "Enabled" : "Disabled", existing.EntityId);

        return Ok(MapToDto(updated));
    }

    /// <summary>
    /// Test SP configuration by fetching metadata (if URL provided)
    /// </summary>
    [HttpPost("{id:int}/test")]
    public async Task<ActionResult<TestSpResult>> TestServiceProvider(int id, CancellationToken cancellationToken)
    {
        var provider = await _spStore.GetByIdAsync(id, cancellationToken);
        if (provider == null)
            return NotFound();

        try
        {
            if (!string.IsNullOrEmpty(provider.MetadataUrl))
            {
                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(10);
                var response = await httpClient.GetAsync(provider.MetadataUrl, cancellationToken);

                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync(cancellationToken);
                    // Basic XML validation
                    if (content.Contains("<EntityDescriptor") || content.Contains("<md:EntityDescriptor"))
                    {
                        return Ok(new TestSpResult
                        {
                            Success = true,
                            Message = "Successfully fetched and validated SP metadata"
                        });
                    }
                    else
                    {
                        return Ok(new TestSpResult
                        {
                            Success = false,
                            Message = "Metadata URL returned content but it doesn't appear to be valid SAML metadata"
                        });
                    }
                }
                else
                {
                    return Ok(new TestSpResult
                    {
                        Success = false,
                        Message = $"Failed to fetch metadata: {response.StatusCode}"
                    });
                }
            }
            else if (!string.IsNullOrEmpty(provider.AssertionConsumerServiceUrl))
            {
                // Just validate the ACS URL format
                if (Uri.TryCreate(provider.AssertionConsumerServiceUrl, UriKind.Absolute, out var uri) &&
                    (uri.Scheme == "https" || uri.Scheme == "http"))
                {
                    return Ok(new TestSpResult
                    {
                        Success = true,
                        Message = "Assertion Consumer Service URL is valid (no metadata URL to test)"
                    });
                }
                else
                {
                    return Ok(new TestSpResult
                    {
                        Success = false,
                        Message = "Assertion Consumer Service URL is not a valid HTTP(S) URL"
                    });
                }
            }

            return Ok(new TestSpResult
            {
                Success = true,
                Message = "Configuration appears valid (limited validation without metadata URL)"
            });
        }
        catch (Exception ex)
        {
            return Ok(new TestSpResult
            {
                Success = false,
                Message = $"Test failed: {ex.Message}"
            });
        }
    }

    #region SAML IdP Settings

    /// <summary>
    /// Get SAML IdP configuration for the current tenant.
    /// Uses tenant context from X-Tenant-Id header.
    /// </summary>
    [HttpGet("idp-configuration")]
    public async Task<ActionResult<SamlIdpConfigurationDto>> GetIdpConfiguration(CancellationToken cancellationToken)
    {
        if (TenantId == null)
        {
            return BadRequest(new { error = "No tenant context available" });
        }

        try
        {
            var settings = await _samlTenantSettings.GetSettingsAsync(TenantId, cancellationToken);
            return Ok(new SamlIdpConfigurationDto
            {
                Enabled = settings.Enabled,
                LoginJourneyName = settings.LoginJourneyName
            });
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    /// <summary>
    /// Update SAML IdP configuration for the current tenant.
    /// Uses tenant context from X-Tenant-Id header.
    /// </summary>
    [HttpPut("idp-configuration")]
    public async Task<ActionResult<SamlIdpConfigurationDto>> UpdateIdpConfiguration(
        [FromBody] UpdateSamlIdpConfigurationRequest request,
        CancellationToken cancellationToken)
    {
        if (TenantId == null)
        {
            return BadRequest(new { error = "No tenant context available" });
        }

        try
        {
            var settings = await _samlTenantSettings.UpdateSettingsAsync(TenantId, s =>
            {
                if (request.Enabled.HasValue)
                    s.Enabled = request.Enabled.Value;
                if (request.LoginJourneyName != null)
                    s.LoginJourneyName = request.LoginJourneyName == "" ? null : request.LoginJourneyName;
            }, cancellationToken);

            _logger.LogInformation("Updated SAML IdP configuration for tenant: {TenantId}", TenantId);

            return Ok(new SamlIdpConfigurationDto
            {
                Enabled = settings.Enabled,
                LoginJourneyName = settings.LoginJourneyName
            });
        }
        catch (InvalidOperationException)
        {
            return NotFound();
        }
    }

    #endregion

    #region IdP Certificates

    /// <summary>
    /// Get the current signing certificate info for the tenant
    /// </summary>
    [HttpGet("idp-certificate/signing")]
    public async Task<ActionResult<SamlCertificateInfoDto>> GetSigningCertificate(CancellationToken cancellationToken)
    {
        if (TenantId == null)
            return BadRequest(new { error = "No tenant context available" });

        var certInfo = await _samlTenantSettings.GetSigningCertificateInfoAsync(TenantId, cancellationToken);
        if (certInfo == null)
            return Ok(new SamlCertificateInfoDto { Source = "Global", HasCertificate = false });

        return Ok(MapToCertificateDto(certInfo));
    }

    /// <summary>
    /// Get the current encryption certificate info for the tenant
    /// </summary>
    [HttpGet("idp-certificate/encryption")]
    public async Task<ActionResult<SamlCertificateInfoDto>> GetEncryptionCertificate(CancellationToken cancellationToken)
    {
        if (TenantId == null)
            return BadRequest(new { error = "No tenant context available" });

        var certInfo = await _samlTenantSettings.GetEncryptionCertificateInfoAsync(TenantId, cancellationToken);
        if (certInfo == null)
            return Ok(new SamlCertificateInfoDto { Source = "Global", HasCertificate = false });

        return Ok(MapToCertificateDto(certInfo));
    }

    /// <summary>
    /// Generate a new signing certificate for the tenant
    /// </summary>
    [HttpPost("idp-certificate/signing/generate")]
    public async Task<ActionResult<SamlCertificateInfoDto>> GenerateSigningCertificate(CancellationToken cancellationToken)
    {
        if (TenantId == null)
            return BadRequest(new { error = "No tenant context available" });

        var certInfo = await _samlTenantSettings.GenerateSigningCertificateAsync(TenantId, cancellationToken);
        _logger.LogInformation("Generated new signing certificate for tenant {TenantId}", TenantId);

        return Ok(MapToCertificateDto(certInfo));
    }

    /// <summary>
    /// Generate a new encryption certificate for the tenant
    /// </summary>
    [HttpPost("idp-certificate/encryption/generate")]
    public async Task<ActionResult<SamlCertificateInfoDto>> GenerateEncryptionCertificate(CancellationToken cancellationToken)
    {
        if (TenantId == null)
            return BadRequest(new { error = "No tenant context available" });

        var certInfo = await _samlTenantSettings.GenerateEncryptionCertificateAsync(TenantId, cancellationToken);
        _logger.LogInformation("Generated new encryption certificate for tenant {TenantId}", TenantId);

        return Ok(MapToCertificateDto(certInfo));
    }

    /// <summary>
    /// Upload a signing certificate for the tenant
    /// </summary>
    [HttpPost("idp-certificate/signing/upload")]
    public async Task<ActionResult<SamlCertificateInfoDto>> UploadSigningCertificate(
        [FromBody] UploadCertificateRequest request,
        CancellationToken cancellationToken)
    {
        if (TenantId == null)
            return BadRequest(new { error = "No tenant context available" });

        if (string.IsNullOrEmpty(request.Base64Pfx))
            return BadRequest(new { error = "PFX data is required" });

        try
        {
            var certInfo = await _samlTenantSettings.UploadSigningCertificateAsync(
                TenantId, request.Base64Pfx, request.Password, cancellationToken);
            _logger.LogInformation("Uploaded signing certificate for tenant {TenantId}", TenantId);

            return Ok(MapToCertificateDto(certInfo));
        }
        catch (Exception ex) when (ex.Message.Contains("password") || ex.Message.Contains("Password"))
        {
            return BadRequest(new { error = "Invalid PFX file or incorrect password" });
        }
    }

    /// <summary>
    /// Upload an encryption certificate for the tenant
    /// </summary>
    [HttpPost("idp-certificate/encryption/upload")]
    public async Task<ActionResult<SamlCertificateInfoDto>> UploadEncryptionCertificate(
        [FromBody] UploadCertificateRequest request,
        CancellationToken cancellationToken)
    {
        if (TenantId == null)
            return BadRequest(new { error = "No tenant context available" });

        if (string.IsNullOrEmpty(request.Base64Pfx))
            return BadRequest(new { error = "PFX data is required" });

        try
        {
            var certInfo = await _samlTenantSettings.UploadEncryptionCertificateAsync(
                TenantId, request.Base64Pfx, request.Password, cancellationToken);
            _logger.LogInformation("Uploaded encryption certificate for tenant {TenantId}", TenantId);

            return Ok(MapToCertificateDto(certInfo));
        }
        catch (Exception ex) when (ex.Message.Contains("password") || ex.Message.Contains("Password"))
        {
            return BadRequest(new { error = "Invalid PFX file or incorrect password" });
        }
    }

    /// <summary>
    /// Reset signing certificate to use global certificate
    /// </summary>
    [HttpPost("idp-certificate/signing/reset")]
    public async Task<IActionResult> ResetSigningCertificate(CancellationToken cancellationToken)
    {
        if (TenantId == null)
            return BadRequest(new { error = "No tenant context available" });

        await _samlTenantSettings.ResetSigningCertificateToGlobalAsync(TenantId, cancellationToken);
        _logger.LogInformation("Reset signing certificate to global for tenant {TenantId}", TenantId);

        return Ok(new { message = "Signing certificate reset to use global certificate" });
    }

    /// <summary>
    /// Reset encryption certificate to use global certificate
    /// </summary>
    [HttpPost("idp-certificate/encryption/reset")]
    public async Task<IActionResult> ResetEncryptionCertificate(CancellationToken cancellationToken)
    {
        if (TenantId == null)
            return BadRequest(new { error = "No tenant context available" });

        await _samlTenantSettings.ResetEncryptionCertificateToGlobalAsync(TenantId, cancellationToken);
        _logger.LogInformation("Reset encryption certificate to global for tenant {TenantId}", TenantId);

        return Ok(new { message = "Encryption certificate reset to use global certificate" });
    }

    private static SamlCertificateInfoDto MapToCertificateDto(SamlCertificateInfo certInfo) => new()
    {
        Source = certInfo.Source.ToString(),
        CertificateId = certInfo.CertificateId,
        Subject = certInfo.Subject,
        Issuer = certInfo.Issuer,
        NotBefore = certInfo.NotBefore,
        NotAfter = certInfo.NotAfter,
        Thumbprint = certInfo.Thumbprint,
        IsExpired = certInfo.IsExpired,
        IsExpiringSoon = certInfo.IsExpiringSoon,
        HasCertificate = true
    };

    #endregion

    #region Helpers

    private static SamlServiceProviderDto MapToDto(SamlServiceProvider provider) => new()
    {
        Id = provider.Id,
        EntityId = provider.EntityId,
        DisplayName = provider.DisplayName,
        Description = provider.Description,
        Enabled = provider.Enabled,
        MetadataUrl = provider.MetadataUrl,
        AssertionConsumerServiceUrl = provider.AssertionConsumerServiceUrl,
        SingleLogoutServiceUrl = provider.SingleLogoutServiceUrl,
        HasSigningCertificate = !string.IsNullOrEmpty(provider.SigningCertificate),
        HasEncryptionCertificate = !string.IsNullOrEmpty(provider.EncryptionCertificate),
        EncryptAssertions = provider.EncryptAssertions,
        NameIdFormat = provider.NameIdFormat,
        AllowedClaims = ParseJsonList(provider.AllowedClaimsJson),
        ClaimMappings = ParseJsonDictionary(provider.ClaimMappingsJson),
        SsoBinding = provider.SsoBinding,
        SignResponses = provider.SignResponses,
        SignAssertions = provider.SignAssertions,
        RequireSignedAuthnRequests = provider.RequireSignedAuthnRequests,
        DefaultRelayState = provider.DefaultRelayState,
        NonEditable = provider.NonEditable,
        Created = provider.Created,
        Updated = provider.Updated,
        LastAccessed = provider.LastAccessed
    };

    private static bool IsMaskedValue(string value) =>
        value == "••••••••" || value == "********" || value == "[CERTIFICATE]";

    private static List<string>? ParseJsonList(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonSerializer.Deserialize<List<string>>(json); }
        catch { return null; }
    }

    private static Dictionary<string, string>? ParseJsonDictionary(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(json); }
        catch { return null; }
    }

    private static string? SerializeJsonList(List<string>? list)
    {
        if (list == null || list.Count == 0) return null;
        return JsonSerializer.Serialize(list);
    }

    private static string? SerializeJsonDictionary(Dictionary<string, string>? dict)
    {
        if (dict == null || dict.Count == 0) return null;
        return JsonSerializer.Serialize(dict);
    }

    #endregion
}

#region DTOs

public class IdpInfoResponse
{
    public bool Enabled { get; set; }
    public string EntityId { get; set; } = null!;
    public string SingleSignOnServiceUrl { get; set; } = null!;
    public string SingleLogoutServiceUrl { get; set; } = null!;
    public string MetadataUrl { get; set; } = null!;
    public List<string> SupportedNameIdFormats { get; set; } = new();
}

public class EntityIdAvailabilityResponse
{
    public bool Available { get; set; }
    public string? Message { get; set; }
}

public class SamlServiceProviderDto
{
    public int Id { get; set; }
    public string EntityId { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public bool Enabled { get; set; }
    public string? MetadataUrl { get; set; }
    public string? AssertionConsumerServiceUrl { get; set; }
    public string? SingleLogoutServiceUrl { get; set; }
    public bool HasSigningCertificate { get; set; }
    public bool HasEncryptionCertificate { get; set; }
    public bool EncryptAssertions { get; set; }
    public string? NameIdFormat { get; set; }
    public List<string>? AllowedClaims { get; set; }
    public Dictionary<string, string>? ClaimMappings { get; set; }
    public string SsoBinding { get; set; } = "POST";
    public bool SignResponses { get; set; }
    public bool SignAssertions { get; set; }
    public bool RequireSignedAuthnRequests { get; set; }
    public string? DefaultRelayState { get; set; }
    public bool NonEditable { get; set; }
    public DateTime Created { get; set; }
    public DateTime? Updated { get; set; }
    public DateTime? LastAccessed { get; set; }
}

public class SamlServiceProviderEditDto
{
    public int Id { get; set; }
    public string EntityId { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public bool Enabled { get; set; }
    public string? MetadataUrl { get; set; }
    public string? AssertionConsumerServiceUrl { get; set; }
    public string? SingleLogoutServiceUrl { get; set; }
    public string? SigningCertificate { get; set; }
    public string? EncryptionCertificate { get; set; }
    public bool EncryptAssertions { get; set; }
    public string? NameIdFormat { get; set; }
    public List<string>? AllowedClaims { get; set; }
    public Dictionary<string, string>? ClaimMappings { get; set; }
    public string SsoBinding { get; set; } = "POST";
    public bool SignResponses { get; set; }
    public bool SignAssertions { get; set; }
    public bool RequireSignedAuthnRequests { get; set; }
    public string? DefaultRelayState { get; set; }
    public bool NonEditable { get; set; }
}

public class CreateSamlServiceProviderRequest
{
    public string EntityId { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public bool? Enabled { get; set; }
    public string? MetadataUrl { get; set; }
    public string? AssertionConsumerServiceUrl { get; set; }
    public string? SingleLogoutServiceUrl { get; set; }
    public string? SigningCertificate { get; set; }
    public string? EncryptionCertificate { get; set; }
    public bool? EncryptAssertions { get; set; }
    public string? NameIdFormat { get; set; }
    public List<string>? AllowedClaims { get; set; }
    public Dictionary<string, string>? ClaimMappings { get; set; }
    public string? SsoBinding { get; set; }
    public bool? SignResponses { get; set; }
    public bool? SignAssertions { get; set; }
    public bool? RequireSignedAuthnRequests { get; set; }
    public string? DefaultRelayState { get; set; }
}

public class UpdateSamlServiceProviderRequest
{
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public bool? Enabled { get; set; }
    public string? MetadataUrl { get; set; }
    public string? AssertionConsumerServiceUrl { get; set; }
    public string? SingleLogoutServiceUrl { get; set; }
    public string? SigningCertificate { get; set; }
    public string? EncryptionCertificate { get; set; }
    public bool? EncryptAssertions { get; set; }
    public string? NameIdFormat { get; set; }
    public List<string>? AllowedClaims { get; set; }
    public Dictionary<string, string>? ClaimMappings { get; set; }
    public string? SsoBinding { get; set; }
    public bool? SignResponses { get; set; }
    public bool? SignAssertions { get; set; }
    public bool? RequireSignedAuthnRequests { get; set; }
    public string? DefaultRelayState { get; set; }
}

public class TestSpResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = null!;
}

public class SamlIdpConfigurationDto
{
    public bool Enabled { get; set; }
    public string? LoginJourneyName { get; set; }
}

public class UpdateSamlIdpConfigurationRequest
{
    public bool? Enabled { get; set; }
    public string? LoginJourneyName { get; set; }
}

public class SamlCertificateInfoDto
{
    public string Source { get; set; } = "Global";
    public string? CertificateId { get; set; }
    public string? Subject { get; set; }
    public string? Issuer { get; set; }
    public DateTime? NotBefore { get; set; }
    public DateTime? NotAfter { get; set; }
    public string? Thumbprint { get; set; }
    public bool IsExpired { get; set; }
    public bool IsExpiringSoon { get; set; }
    public bool HasCertificate { get; set; }
}

public class UploadCertificateRequest
{
    public string Base64Pfx { get; set; } = null!;
    public string? Password { get; set; }
}

#endregion

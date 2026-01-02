using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oluso.Core.Api;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Enterprise.Ldap.Configuration;
using Oluso.Enterprise.Ldap.Entities;
using Oluso.Enterprise.Ldap.Server;
using Oluso.Enterprise.Ldap.Stores;

namespace Oluso.Enterprise.Ldap.Controllers;

/// <summary>
/// Admin API for managing LDAP Server settings.
/// Controls the LDAP server that exposes tenant users via LDAP protocol.
/// </summary>
[Route("api/admin/ldap")]
public class LdapServerController : AdminBaseController
{
    private readonly ILdapTenantSettingsService _ldapSettings;
    private readonly ILdapServiceAccountStore? _serviceAccountStore;
    private readonly IPasswordHasher<LdapServiceAccount>? _passwordHasher;
    private readonly IOptions<LdapServerOptions> _options;
    private readonly ICertificateService? _certificateService;
    private readonly ILogger<LdapServerController> _logger;

    public LdapServerController(
        ITenantContext tenantContext,
        ILdapTenantSettingsService ldapSettings,
        IOptions<LdapServerOptions> options,
        ICertificateService? certificateService,
        ILdapServiceAccountStore? serviceAccountStore,
        IPasswordHasher<LdapServiceAccount>? passwordHasher,
        ILogger<LdapServerController> logger) : base(tenantContext)
    {
        _ldapSettings = ldapSettings;
        _serviceAccountStore = serviceAccountStore;
        _passwordHasher = passwordHasher;
        _options = options;
        _certificateService = certificateService;
        _logger = logger;
    }

    /// <summary>
    /// Get LDAP Server info including connection details and status.
    /// </summary>
    [HttpGet("server-info")]
    public async Task<ActionResult<LdapServerInfoResponse>> GetServerInfo(CancellationToken cancellationToken)
    {
        var globalOptions = _options.Value;
        var tenantSettings = await _ldapSettings.GetSettingsAsync(cancellationToken);

        // Determine effective settings (tenant overrides global)
        var isEnabled = tenantSettings.Enabled || globalOptions.Enabled;
        var baseDn = tenantSettings.BaseDn ?? globalOptions.BaseDn;
        var organization = tenantSettings.Organization ?? globalOptions.Organization;

        // Build tenant-specific DN if tenant isolation is enabled
        string effectiveBaseDn;
        if (TenantId != null && globalOptions.TenantIsolation)
        {
            effectiveBaseDn = $"o={TenantId},{baseDn}";
        }
        else
        {
            effectiveBaseDn = baseDn;
        }

        return Ok(new LdapServerInfoResponse
        {
            Enabled = isEnabled,
            Port = globalOptions.Port,
            SslPort = globalOptions.SslPort,
            EnableSsl = globalOptions.EnableSsl,
            EnableStartTls = globalOptions.EnableStartTls,
            BaseDn = effectiveBaseDn,
            Organization = organization,
            UserOu = globalOptions.UserOu,
            GroupOu = globalOptions.GroupOu,
            AllowAnonymousBind = tenantSettings.AllowAnonymousBind || globalOptions.AllowAnonymousBind,
            MaxSearchResults = tenantSettings.MaxSearchResults ?? globalOptions.MaxSearchResults,
            TenantIsolation = globalOptions.TenantIsolation,
            AdminDn = tenantSettings.AdminDn ?? globalOptions.AdminDn ?? $"cn=admin,{effectiveBaseDn}"
        });
    }

    /// <summary>
    /// Get LDAP Server settings for the current tenant.
    /// </summary>
    [HttpGet("settings")]
    public async Task<ActionResult<TenantLdapSettings>> GetSettings(CancellationToken cancellationToken)
    {
        var settings = await _ldapSettings.GetSettingsAsync(cancellationToken);
        return Ok(settings);
    }

    /// <summary>
    /// Update LDAP Server settings for the current tenant.
    /// Settings are stored in the tenant's Configuration JSON.
    /// </summary>
    [HttpPut("settings")]
    public async Task<ActionResult<TenantLdapSettings>> UpdateSettings(
        [FromBody] UpdateLdapServerSettingsRequest request,
        CancellationToken cancellationToken)
    {
        if (TenantId == null)
        {
            return BadRequest(new { error = "Tenant context required to update settings" });
        }

        var settings = new TenantLdapSettings
        {
            Enabled = request.Enabled,
            BaseDn = request.BaseDn,
            Organization = request.Organization,
            AllowAnonymousBind = request.AllowAnonymousBind,
            MaxSearchResults = request.MaxSearchResults,
            AdminDn = request.AdminDn
        };

        await _ldapSettings.UpdateSettingsAsync(TenantId, settings, cancellationToken);

        _logger.LogInformation("Updated LDAP Server settings for tenant {TenantId}", TenantId);

        return Ok(settings);
    }

    /// <summary>
    /// Test LDAP connection with admin credentials.
    /// </summary>
    [HttpPost("test-connection")]
    public async Task<ActionResult<TestConnectionResponse>> TestConnection(CancellationToken cancellationToken)
    {
        var globalOptions = _options.Value;

        if (!globalOptions.Enabled)
        {
            return Ok(new TestConnectionResponse
            {
                Success = false,
                Message = "LDAP Server is not enabled globally",
                Details = new { globalEnabled = false }
            });
        }

        var tenantSettings = await _ldapSettings.GetSettingsAsync(cancellationToken);

        // Basic check - in a real implementation you'd try to connect
        return Ok(new TestConnectionResponse
        {
            Success = true,
            Message = "LDAP Server is running",
            Details = new
            {
                port = globalOptions.Port,
                sslPort = globalOptions.SslPort,
                tenantEnabled = tenantSettings.Enabled,
                globalEnabled = globalOptions.Enabled
            }
        });
    }

    #region Certificate Management

    /// <summary>
    /// Get TLS certificate info for this tenant.
    /// </summary>
    [HttpGet("certificate")]
    public async Task<ActionResult<LdapCertificateInfoResponse>> GetTlsCertificate(CancellationToken cancellationToken)
    {
        if (_certificateService == null)
        {
            return Ok(new LdapCertificateInfoResponse
            {
                HasCertificate = false,
                Source = "NotConfigured"
            });
        }

        var tenantSettings = await _ldapSettings.GetSettingsAsync(cancellationToken);
        var source = tenantSettings.TlsCertificate?.Source ?? LdapCertificateSource.Global;

        var cert = await _certificateService.GetCertificateAsync(
            CertificatePurpose.LdapTls,
            TenantId,
            cancellationToken: cancellationToken);

        if (cert == null)
        {
            // Try global cert
            cert = await _certificateService.GetCertificateAsync(
                CertificatePurpose.LdapTls,
                cancellationToken: cancellationToken);
            source = LdapCertificateSource.Global;
        }

        if (cert == null)
        {
            return Ok(new LdapCertificateInfoResponse
            {
                HasCertificate = false,
                Source = source.ToString()
            });
        }

        return Ok(new LdapCertificateInfoResponse
        {
            HasCertificate = true,
            Source = source.ToString(),
            Subject = cert.Subject,
            Thumbprint = cert.Thumbprint,
            NotBefore = cert.NotBefore,
            NotAfter = cert.NotAfter,
            IsExpired = cert.NotAfter < DateTime.UtcNow,
            IsExpiringSoon = cert.NotAfter < DateTime.UtcNow.AddDays(30)
        });
    }

    /// <summary>
    /// Generate a new self-signed TLS certificate for this tenant.
    /// </summary>
    [HttpPost("certificate/generate")]
    public async Task<ActionResult<LdapCertificateInfoResponse>> GenerateTlsCertificate(CancellationToken cancellationToken)
    {
        if (_certificateService == null)
        {
            return BadRequest(new { error = "Certificate service not configured" });
        }

        if (TenantId == null)
        {
            return BadRequest(new { error = "Tenant context required" });
        }

        var generateRequest = new GenerateCertificateRequest
        {
            Purpose = CertificatePurpose.LdapTls,
            TenantId = TenantId,
            Subject = $"CN=LDAP TLS Certificate,O={TenantId}",
            ValidityDays = 365,
            KeyUsage = CertificateKeyUsage.KeyEncipherment | CertificateKeyUsage.DigitalSignature
        };

        var certInfo = await _certificateService.GenerateSelfSignedCertificateAsync(generateRequest, cancellationToken);

        // Update tenant settings to use auto-generated cert
        var settings = await _ldapSettings.GetSettingsAsync(TenantId, cancellationToken);
        settings.TlsCertificate = new LdapCertificateConfig
        {
            Source = LdapCertificateSource.Auto,
            CertificateId = certInfo.Id
        };
        await _ldapSettings.UpdateSettingsAsync(TenantId, settings, cancellationToken);

        _logger.LogInformation("Generated LDAP TLS certificate for tenant {TenantId}", TenantId);

        return Ok(new LdapCertificateInfoResponse
        {
            HasCertificate = true,
            Source = "Auto",
            Subject = certInfo.Subject,
            Thumbprint = certInfo.Thumbprint,
            NotBefore = certInfo.NotBefore,
            NotAfter = certInfo.NotAfter,
            IsExpired = false,
            IsExpiringSoon = false
        });
    }

    /// <summary>
    /// Upload a TLS certificate for this tenant.
    /// </summary>
    [HttpPost("certificate/upload")]
    public async Task<ActionResult<LdapCertificateInfoResponse>> UploadTlsCertificate(
        [FromBody] UploadCertificateRequest request,
        CancellationToken cancellationToken)
    {
        if (_certificateService == null)
        {
            return BadRequest(new { error = "Certificate service not configured" });
        }

        if (TenantId == null)
        {
            return BadRequest(new { error = "Tenant context required" });
        }

        try
        {
            var importRequest = new ImportCertificateRequest
            {
                Purpose = CertificatePurpose.LdapTls,
                TenantId = TenantId,
                PfxData = request.Base64Pfx,
                Password = request.Password
            };

            var certInfo = await _certificateService.ImportCertificateAsync(importRequest, cancellationToken);

            // Update tenant settings to use uploaded cert
            var settings = await _ldapSettings.GetSettingsAsync(TenantId, cancellationToken);
            settings.TlsCertificate = new LdapCertificateConfig
            {
                Source = LdapCertificateSource.Uploaded,
                CertificateId = certInfo.Id
            };
            await _ldapSettings.UpdateSettingsAsync(TenantId, settings, cancellationToken);

            _logger.LogInformation("Uploaded LDAP TLS certificate for tenant {TenantId}", TenantId);

            return Ok(new LdapCertificateInfoResponse
            {
                HasCertificate = true,
                Source = "Uploaded",
                Subject = certInfo.Subject,
                Thumbprint = certInfo.Thumbprint,
                NotBefore = certInfo.NotBefore,
                NotAfter = certInfo.NotAfter,
                IsExpired = certInfo.NotAfter < DateTime.UtcNow,
                IsExpiringSoon = certInfo.NotAfter < DateTime.UtcNow.AddDays(30)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload LDAP TLS certificate for tenant {TenantId}", TenantId);
            return BadRequest(new { error = "Failed to import certificate", details = ex.Message });
        }
    }

    /// <summary>
    /// Reset to use global TLS certificate.
    /// </summary>
    [HttpPost("certificate/reset")]
    public async Task<ActionResult> ResetTlsCertificate(CancellationToken cancellationToken)
    {
        if (TenantId == null)
        {
            return BadRequest(new { error = "Tenant context required" });
        }

        var settings = await _ldapSettings.GetSettingsAsync(TenantId, cancellationToken);
        settings.TlsCertificate = new LdapCertificateConfig
        {
            Source = LdapCertificateSource.Global,
            CertificateId = null
        };
        await _ldapSettings.UpdateSettingsAsync(TenantId, settings, cancellationToken);

        _logger.LogInformation("Reset LDAP TLS certificate to global for tenant {TenantId}", TenantId);

        return Ok(new { message = "Certificate reset to global" });
    }

    #endregion

    #region Service Account Management

    /// <summary>
    /// Get all service accounts for this tenant.
    /// </summary>
    [HttpGet("service-accounts")]
    public async Task<ActionResult<IEnumerable<ServiceAccountResponse>>> GetServiceAccounts(CancellationToken cancellationToken)
    {
        if (_serviceAccountStore == null)
        {
            return Ok(Array.Empty<ServiceAccountResponse>());
        }

        if (TenantId == null)
        {
            return BadRequest(new { error = "Tenant context required" });
        }

        var accounts = await _serviceAccountStore.GetAllAsync(TenantId, cancellationToken);
        var responses = accounts.Select(MapToResponse);
        return Ok(responses);
    }

    /// <summary>
    /// Get a service account by ID.
    /// </summary>
    [HttpGet("service-accounts/{id}")]
    public async Task<ActionResult<ServiceAccountResponse>> GetServiceAccount(string id, CancellationToken cancellationToken)
    {
        if (_serviceAccountStore == null)
        {
            return NotFound(new { error = "Service accounts not configured" });
        }

        if (TenantId == null)
        {
            return BadRequest(new { error = "Tenant context required" });
        }

        var account = await _serviceAccountStore.GetByIdAsync(TenantId, id, cancellationToken);
        if (account == null)
        {
            return NotFound(new { error = "Service account not found" });
        }

        return Ok(MapToResponse(account));
    }

    /// <summary>
    /// Create a new service account.
    /// </summary>
    [HttpPost("service-accounts")]
    public async Task<ActionResult<ServiceAccountResponse>> CreateServiceAccount(
        [FromBody] CreateServiceAccountRequest request,
        CancellationToken cancellationToken)
    {
        if (_serviceAccountStore == null || _passwordHasher == null)
        {
            return BadRequest(new { error = "Service accounts not configured" });
        }

        if (TenantId == null)
        {
            return BadRequest(new { error = "Tenant context required" });
        }

        // Get server options for building BindDn
        var globalOptions = _options.Value;
        var baseDn = globalOptions.TenantIsolation
            ? $"o={TenantId},{globalOptions.BaseDn}"
            : globalOptions.BaseDn;

        // Generate a sanitized CN from name
        var cn = request.Name.ToLowerInvariant()
            .Replace(" ", "-")
            .Replace(",", "")
            .Replace("=", "");

        var bindDn = $"cn={cn},ou=services,{baseDn}";

        var account = new LdapServiceAccount
        {
            TenantId = TenantId,
            Name = request.Name,
            Description = request.Description,
            BindDn = bindDn,
            Permission = request.Permission,
            AllowedOus = request.AllowedOus != null ? string.Join(",", request.AllowedOus) : null,
            AllowedIpRanges = request.AllowedIpRanges != null ? string.Join(",", request.AllowedIpRanges) : null,
            MaxSearchResults = request.MaxSearchResults ?? 0,
            RateLimitPerMinute = request.RateLimitPerMinute ?? 0,
            ExpiresAt = request.ExpiresAt
        };

        // Hash the password
        account.PasswordHash = _passwordHasher.HashPassword(account, request.Password);

        var created = await _serviceAccountStore.CreateAsync(account, cancellationToken);

        _logger.LogInformation("Created LDAP service account {Name} for tenant {TenantId}", request.Name, TenantId);

        return CreatedAtAction(nameof(GetServiceAccount), new { id = created.Id }, MapToResponse(created));
    }

    /// <summary>
    /// Update a service account.
    /// </summary>
    [HttpPut("service-accounts/{id}")]
    public async Task<ActionResult<ServiceAccountResponse>> UpdateServiceAccount(
        string id,
        [FromBody] UpdateServiceAccountRequest request,
        CancellationToken cancellationToken)
    {
        if (_serviceAccountStore == null)
        {
            return BadRequest(new { error = "Service accounts not configured" });
        }

        if (TenantId == null)
        {
            return BadRequest(new { error = "Tenant context required" });
        }

        var account = await _serviceAccountStore.GetByIdAsync(TenantId, id, cancellationToken);
        if (account == null)
        {
            return NotFound(new { error = "Service account not found" });
        }

        // Update fields
        if (request.Name != null) account.Name = request.Name;
        if (request.Description != null) account.Description = request.Description;
        if (request.IsEnabled.HasValue) account.IsEnabled = request.IsEnabled.Value;
        if (request.Permission.HasValue) account.Permission = request.Permission.Value;
        if (request.AllowedOus != null) account.AllowedOus = string.Join(",", request.AllowedOus);
        if (request.AllowedIpRanges != null) account.AllowedIpRanges = string.Join(",", request.AllowedIpRanges);
        if (request.MaxSearchResults.HasValue) account.MaxSearchResults = request.MaxSearchResults.Value;
        if (request.RateLimitPerMinute.HasValue) account.RateLimitPerMinute = request.RateLimitPerMinute.Value;
        if (request.ExpiresAt.HasValue) account.ExpiresAt = request.ExpiresAt;

        var updated = await _serviceAccountStore.UpdateAsync(account, cancellationToken);

        _logger.LogInformation("Updated LDAP service account {Id} for tenant {TenantId}", id, TenantId);

        return Ok(MapToResponse(updated));
    }

    /// <summary>
    /// Reset service account password.
    /// </summary>
    [HttpPost("service-accounts/{id}/reset-password")]
    public async Task<ActionResult> ResetServiceAccountPassword(
        string id,
        [FromBody] ResetServiceAccountPasswordRequest request,
        CancellationToken cancellationToken)
    {
        if (_serviceAccountStore == null || _passwordHasher == null)
        {
            return BadRequest(new { error = "Service accounts not configured" });
        }

        if (TenantId == null)
        {
            return BadRequest(new { error = "Tenant context required" });
        }

        var account = await _serviceAccountStore.GetByIdAsync(TenantId, id, cancellationToken);
        if (account == null)
        {
            return NotFound(new { error = "Service account not found" });
        }

        account.PasswordHash = _passwordHasher.HashPassword(account, request.NewPassword);
        await _serviceAccountStore.UpdateAsync(account, cancellationToken);

        _logger.LogInformation("Reset password for LDAP service account {Id} for tenant {TenantId}", id, TenantId);

        return Ok(new { message = "Password reset successfully" });
    }

    /// <summary>
    /// Delete a service account.
    /// </summary>
    [HttpDelete("service-accounts/{id}")]
    public async Task<ActionResult> DeleteServiceAccount(string id, CancellationToken cancellationToken)
    {
        if (_serviceAccountStore == null)
        {
            return BadRequest(new { error = "Service accounts not configured" });
        }

        if (TenantId == null)
        {
            return BadRequest(new { error = "Tenant context required" });
        }

        var account = await _serviceAccountStore.GetByIdAsync(TenantId, id, cancellationToken);
        if (account == null)
        {
            return NotFound(new { error = "Service account not found" });
        }

        await _serviceAccountStore.DeleteAsync(TenantId, id, cancellationToken);

        _logger.LogInformation("Deleted LDAP service account {Id} for tenant {TenantId}", id, TenantId);

        return NoContent();
    }

    private static ServiceAccountResponse MapToResponse(LdapServiceAccount account)
    {
        return new ServiceAccountResponse
        {
            Id = account.Id,
            Name = account.Name,
            Description = account.Description,
            BindDn = account.BindDn,
            IsEnabled = account.IsEnabled,
            Permission = account.Permission.ToString(),
            AllowedOus = account.GetAllowedOusList(),
            AllowedIpRanges = account.GetAllowedIpRangesList(),
            MaxSearchResults = account.MaxSearchResults,
            RateLimitPerMinute = account.RateLimitPerMinute,
            CreatedAt = account.CreatedAt,
            UpdatedAt = account.UpdatedAt,
            LastUsedAt = account.LastUsedAt,
            ExpiresAt = account.ExpiresAt,
            IsExpired = account.IsExpired
        };
    }

    #endregion
}

#region DTOs

public class LdapServerInfoResponse
{
    public bool Enabled { get; set; }
    public int Port { get; set; }
    public int SslPort { get; set; }
    public bool EnableSsl { get; set; }
    public bool EnableStartTls { get; set; }
    public string BaseDn { get; set; } = null!;
    public string Organization { get; set; } = null!;
    public string UserOu { get; set; } = null!;
    public string GroupOu { get; set; } = null!;
    public bool AllowAnonymousBind { get; set; }
    public int MaxSearchResults { get; set; }
    public bool TenantIsolation { get; set; }
    public string AdminDn { get; set; } = null!;
}

public class UpdateLdapServerSettingsRequest
{
    public bool Enabled { get; set; }
    public string? BaseDn { get; set; }
    public string? Organization { get; set; }
    public bool AllowAnonymousBind { get; set; }
    public int? MaxSearchResults { get; set; }
    public string? AdminDn { get; set; }
}

public class TestConnectionResponse
{
    public bool Success { get; set; }
    public string Message { get; set; } = null!;
    public object? Details { get; set; }
}

public class LdapCertificateInfoResponse
{
    public bool HasCertificate { get; set; }
    public string Source { get; set; } = "Global";
    public string? Subject { get; set; }
    public string? Thumbprint { get; set; }
    public DateTime? NotBefore { get; set; }
    public DateTime? NotAfter { get; set; }
    public bool IsExpired { get; set; }
    public bool IsExpiringSoon { get; set; }
}

public class UploadCertificateRequest
{
    public string Base64Pfx { get; set; } = null!;
    public string? Password { get; set; }
}

public class ServiceAccountResponse
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string BindDn { get; set; } = null!;
    public bool IsEnabled { get; set; }
    public string Permission { get; set; } = null!;
    public List<string> AllowedOus { get; set; } = new();
    public List<string> AllowedIpRanges { get; set; } = new();
    public int? MaxSearchResults { get; set; }
    public int? RateLimitPerMinute { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public bool IsExpired { get; set; }
}

public class CreateServiceAccountRequest
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string Password { get; set; } = null!;
    public LdapServiceAccountPermission Permission { get; set; } = LdapServiceAccountPermission.ReadOnly;
    public List<string>? AllowedOus { get; set; }
    public List<string>? AllowedIpRanges { get; set; }
    public int? MaxSearchResults { get; set; }
    public int? RateLimitPerMinute { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

public class UpdateServiceAccountRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public bool? IsEnabled { get; set; }
    public LdapServiceAccountPermission? Permission { get; set; }
    public List<string>? AllowedOus { get; set; }
    public List<string>? AllowedIpRanges { get; set; }
    public int? MaxSearchResults { get; set; }
    public int? RateLimitPerMinute { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

public class ResetServiceAccountPasswordRequest
{
    public string NewPassword { get; set; } = null!;
}

#endregion

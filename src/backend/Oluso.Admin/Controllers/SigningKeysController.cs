using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oluso.Core.Api;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;

namespace Oluso.Admin.Controllers;

/// <summary>
/// Admin API for managing signing keys
/// </summary>
[Route("api/admin/signing-keys")]
public class SigningKeysController : AdminBaseController
{
    private readonly ISigningKeyStore _keyStore;
    private readonly ISigningKeyService _signingKeyService;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<SigningKeysController> _logger;

    public SigningKeysController(
        ISigningKeyStore keyStore,
        ISigningKeyService signingKeyService,
        ITenantContext tenantContext,
        ILogger<SigningKeysController> logger) : base(tenantContext)
    {
        _keyStore = keyStore;
        _signingKeyService = signingKeyService;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>
    /// Get all signing keys for the current tenant
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<SigningKeyDto>>> GetKeys(
        [FromQuery] string? clientId = null,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId;

        var keys = clientId != null
            ? await _keyStore.GetByClientAsync(tenantId, clientId, cancellationToken)
            : await _keyStore.GetByTenantAsync(tenantId, cancellationToken);

        var dtos = keys.Select(MapToDto).OrderByDescending(k => k.Priority);
        return Ok(dtos);
    }

    /// <summary>
    /// Get a specific key by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<SigningKeyDto>> GetKey(
        string id,
        CancellationToken cancellationToken = default)
    {
        var key = await _keyStore.GetByIdAsync(id, cancellationToken);
        if (key == null)
        {
            return NotFound();
        }

        // Ensure key belongs to current tenant
        if (key.TenantId != _tenantContext.TenantId)
        {
            return Forbid();
        }

        return Ok(MapToDto(key));
    }

    /// <summary>
    /// Generate a new signing key
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<SigningKeyDto>> GenerateKey(
        [FromBody] GenerateKeyRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId;

        var generateRequest = new GenerateKeyRequest
        {
            TenantId = tenantId,
            ClientId = request.ClientId,
            Name = request.Name,
            KeyType = Enum.TryParse<SigningKeyType>(request.KeyType, true, out var kt) ? kt : SigningKeyType.RSA,
            Algorithm = request.Algorithm,
            KeySize = request.KeySize,
            LifetimeDays = request.LifetimeDays,
            ActivateImmediately = request.ActivateImmediately ?? true,
            Priority = request.Priority
        };

        var key = await _signingKeyService.GenerateKeyAsync(generateRequest, cancellationToken);

        _logger.LogInformation(
            "Admin generated new signing key {KeyId} for tenant {TenantId}",
            key.KeyId, tenantId);

        return CreatedAtAction(nameof(GetKey), new { id = key.Id }, MapToDto(key));
    }

    /// <summary>
    /// Rotate keys - generates a new key and marks old ones for expiration
    /// </summary>
    [HttpPost("rotate")]
    public async Task<ActionResult<SigningKeyDto>> RotateKeys(
        [FromQuery] string? clientId = null,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId;

        await _signingKeyService.RotateKeysAsync(clientId, cancellationToken);

        // Get the new active key
        var activeKey = await _keyStore.GetActiveSigningKeyAsync(tenantId, clientId, cancellationToken);

        _logger.LogInformation(
            "Admin rotated signing keys for tenant {TenantId}, client {ClientId}",
            tenantId, clientId ?? "(tenant default)");

        if (activeKey == null)
        {
            return Ok(new { message = "Keys rotated successfully" });
        }

        return Ok(MapToDto(activeKey));
    }

    /// <summary>
    /// Revoke a key
    /// </summary>
    [HttpPost("{id}/revoke")]
    public async Task<ActionResult> RevokeKey(
        string id,
        [FromBody] RevokeKeyRequestDto? request = null,
        CancellationToken cancellationToken = default)
    {
        var key = await _keyStore.GetByIdAsync(id, cancellationToken);
        if (key == null)
        {
            return NotFound();
        }

        if (key.TenantId != _tenantContext.TenantId)
        {
            return Forbid();
        }

        await _signingKeyService.RevokeKeyAsync(key.KeyId, request?.Reason ?? "Revoked by admin", cancellationToken);

        _logger.LogWarning(
            "Admin revoked key {KeyId} for tenant {TenantId}. Reason: {Reason}",
            key.KeyId, key.TenantId, request?.Reason ?? "Not specified");

        return NoContent();
    }

    /// <summary>
    /// Update key status (activate, expire, archive)
    /// </summary>
    [HttpPatch("{id}/status")]
    public async Task<ActionResult<SigningKeyDto>> UpdateKeyStatus(
        string id,
        [FromBody] UpdateKeyStatusDto request,
        CancellationToken cancellationToken = default)
    {
        var key = await _keyStore.GetByIdAsync(id, cancellationToken);
        if (key == null)
        {
            return NotFound();
        }

        if (key.TenantId != _tenantContext.TenantId)
        {
            return Forbid();
        }

        if (!string.IsNullOrEmpty(request.Status))
        {
            key.Status = Enum.Parse<SigningKeyStatus>(request.Status, true);
        }

        if (request.Priority.HasValue)
        {
            key.Priority = request.Priority.Value;
        }

        if (request.IncludeInJwks.HasValue)
        {
            key.IncludeInJwks = request.IncludeInJwks.Value;
        }

        if (request.ExpiresAt.HasValue)
        {
            key.ExpiresAt = request.ExpiresAt.Value;
        }

        await _keyStore.UpdateAsync(key, cancellationToken);

        return Ok(MapToDto(key));
    }

    /// <summary>
    /// Delete a key permanently
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteKey(
        string id,
        CancellationToken cancellationToken = default)
    {
        var key = await _keyStore.GetByIdAsync(id, cancellationToken);
        if (key == null)
        {
            return NotFound();
        }

        if (key.TenantId != _tenantContext.TenantId)
        {
            return Forbid();
        }

        // Don't allow deleting active keys
        if (key.Status == SigningKeyStatus.Active)
        {
            return BadRequest(new { error = "Cannot delete active key. Revoke or rotate first." });
        }

        await _keyStore.DeleteAsync(id, cancellationToken);

        _logger.LogWarning("Admin deleted key {KeyId} for tenant {TenantId}", key.KeyId, key.TenantId);

        return NoContent();
    }

    /// <summary>
    /// Get rotation configuration
    /// </summary>
    [HttpGet("rotation-config")]
    public async Task<ActionResult<KeyRotationConfigDto>> GetRotationConfig(
        [FromQuery] string? clientId = null,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId;
        var config = await _keyStore.GetRotationConfigAsync(tenantId, clientId, cancellationToken);

        if (config == null)
        {
            // Return default config
            return Ok(new KeyRotationConfigDto
            {
                Enabled = true,
                KeyType = "RSA",
                Algorithm = "RS256",
                KeySize = 2048,
                KeyLifetimeDays = 90,
                RotationLeadDays = 14,
                GracePeriodDays = 30,
                MaxKeys = 5
            });
        }

        return Ok(MapToConfigDto(config));
    }

    /// <summary>
    /// Update rotation configuration
    /// </summary>
    [HttpPut("rotation-config")]
    public async Task<ActionResult<KeyRotationConfigDto>> UpdateRotationConfig(
        [FromBody] KeyRotationConfigDto request,
        CancellationToken cancellationToken = default)
    {
        var tenantId = _tenantContext.TenantId;
        var config = await _keyStore.GetRotationConfigAsync(tenantId, request.ClientId, cancellationToken)
            ?? new KeyRotationConfig
            {
                TenantId = tenantId,
                ClientId = request.ClientId
            };

        config.Enabled = request.Enabled ?? true;
        config.KeyType = Enum.Parse<SigningKeyType>(request.KeyType ?? "RSA", true);
        config.Algorithm = request.Algorithm ?? "RS256";
        config.KeySize = request.KeySize ?? 2048;
        config.KeyLifetimeDays = request.KeyLifetimeDays ?? 90;
        config.RotationLeadDays = request.RotationLeadDays ?? 14;
        config.GracePeriodDays = request.GracePeriodDays ?? 30;
        config.MaxKeys = request.MaxKeys ?? 5;

        await _keyStore.SaveRotationConfigAsync(config, cancellationToken);

        return Ok(MapToConfigDto(config));
    }

    /// <summary>
    /// Get keys that are expiring soon
    /// </summary>
    [HttpGet("expiring")]
    public async Task<ActionResult<IEnumerable<SigningKeyDto>>> GetExpiringKeys(
        [FromQuery] int daysUntilExpiration = 14,
        CancellationToken cancellationToken = default)
    {
        var keys = await _keyStore.GetExpiringKeysAsync(daysUntilExpiration, cancellationToken);

        // Filter to current tenant
        var tenantId = _tenantContext.TenantId;
        var filtered = keys.Where(k => k.TenantId == tenantId);

        return Ok(filtered.Select(MapToDto));
    }

    private static SigningKeyDto MapToDto(SigningKey key) => new()
    {
        Id = key.Id,
        Name = key.Name,
        KeyId = key.KeyId,
        KeyType = key.KeyType.ToString(),
        Algorithm = key.Algorithm,
        Use = key.Use.ToString(),
        KeySize = key.KeySize,
        ClientId = key.ClientId,
        Status = key.Status.ToString(),
        StorageProvider = key.StorageProvider.ToString(),
        CreatedAt = key.CreatedAt,
        ActivatedAt = key.ActivatedAt,
        ExpiresAt = key.ExpiresAt,
        RevokedAt = key.RevokedAt,
        RevocationReason = key.RevocationReason,
        LastUsedAt = key.LastUsedAt,
        SignatureCount = key.SignatureCount,
        Priority = key.Priority,
        IncludeInJwks = key.IncludeInJwks,
        CanSign = key.CanSign,
        CanVerify = key.CanVerify,
        IsExpiringSoon = key.IsExpiringSoon,
        IsExpired = key.IsExpired
    };

    private static KeyRotationConfigDto MapToConfigDto(KeyRotationConfig config) => new()
    {
        ClientId = config.ClientId,
        Enabled = config.Enabled,
        KeyType = config.KeyType.ToString(),
        Algorithm = config.Algorithm,
        KeySize = config.KeySize,
        KeyLifetimeDays = config.KeyLifetimeDays,
        RotationLeadDays = config.RotationLeadDays,
        GracePeriodDays = config.GracePeriodDays,
        MaxKeys = config.MaxKeys,
        LastRotationAt = config.LastRotationAt,
        NextRotationAt = config.NextRotationAt
    };
}

#region DTOs

/// <summary>
/// Signing key metadata - NEVER contains actual key material
/// </summary>
public class SigningKeyDto
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string KeyId { get; set; } = null!;
    public string KeyType { get; set; } = null!;
    public string Algorithm { get; set; } = null!;
    public string Use { get; set; } = null!;
    public int KeySize { get; set; }
    public string? ClientId { get; set; }
    public string Status { get; set; } = null!;
    public string StorageProvider { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime? ActivatedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevocationReason { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public long SignatureCount { get; set; }
    public int Priority { get; set; }
    public bool IncludeInJwks { get; set; }
    public bool CanSign { get; set; }
    public bool CanVerify { get; set; }
    public bool IsExpiringSoon { get; set; }
    public bool IsExpired { get; set; }
}

public class UpdateKeyStatusDto
{
    public string? Status { get; set; }
    public int? Priority { get; set; }
    public bool? IncludeInJwks { get; set; }
    public DateTime? ExpiresAt { get; set; }
}

public class KeyRotationConfigDto
{
    public string? ClientId { get; set; }
    public bool? Enabled { get; set; }
    public string? KeyType { get; set; }
    public string? Algorithm { get; set; }
    public int? KeySize { get; set; }
    public int? KeyLifetimeDays { get; set; }
    public int? RotationLeadDays { get; set; }
    public int? GracePeriodDays { get; set; }
    public int? MaxKeys { get; set; }
    public DateTime? LastRotationAt { get; set; }
    public DateTime? NextRotationAt { get; set; }
}

public class GenerateKeyRequestDto
{
    public string? ClientId { get; set; }
    public string? Name { get; set; }
    public string? KeyType { get; set; }
    public string? Algorithm { get; set; }
    public int? KeySize { get; set; }
    public int? LifetimeDays { get; set; }
    public bool? ActivateImmediately { get; set; }
    public int? Priority { get; set; }
}

public class RevokeKeyRequestDto
{
    public string? Reason { get; set; }
}

#endregion

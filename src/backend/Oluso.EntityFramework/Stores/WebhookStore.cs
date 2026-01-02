using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Oluso.EntityFramework.Stores;

/// <summary>
/// Entity Framework implementation of IWebhookStore
/// </summary>
public class WebhookStore : IWebhookStore
{
    private readonly IOlusoDbContext _context;
    private readonly ILogger<WebhookStore> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public WebhookStore(
        IOlusoDbContext context,
        ILogger<WebhookStore> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<IEnumerable<WebhookEndpoint>> GetEndpointsAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        var endpoints = await _context.WebhookEndpoints
            .Include(e => e.EventSubscriptions)
            .Where(e => e.TenantId == tenantId)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

        return endpoints.Select(MapToDto);
    }

    public async Task<WebhookEndpoint?> GetEndpointAsync(
        string endpointId,
        CancellationToken cancellationToken = default)
    {
        var endpoint = await _context.WebhookEndpoints
            .Include(e => e.EventSubscriptions)
            .FirstOrDefaultAsync(e => e.Id == endpointId, cancellationToken);

        return endpoint != null ? MapToDto(endpoint) : null;
    }

    public async Task<WebhookEndpoint> CreateEndpointAsync(
        string tenantId,
        CreateWebhookEndpoint dto,
        CancellationToken cancellationToken = default)
    {
        // Generate a secure secret
        var secret = GenerateSecret();
        var secretHash = HashSecret(secret);

        var endpoint = new WebhookEndpointEntity
        {
            TenantId = tenantId,
            Name = dto.Name,
            Description = dto.Description,
            Url = dto.Url,
            SecretHash = secretHash,
            Enabled = dto.Enabled,
            ApiVersion = dto.ApiVersion,
            HeadersJson = dto.Headers != null
                ? JsonSerializer.Serialize(dto.Headers, JsonOptions)
                : null
        };

        // Add event subscriptions
        foreach (var eventType in dto.EventTypes)
        {
            endpoint.EventSubscriptions.Add(new WebhookEventSubscriptionEntity
            {
                EventType = eventType,
                Enabled = true
            });
        }

        _context.WebhookEndpoints.Add(endpoint);
        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Created webhook endpoint {EndpointId} for tenant {TenantId}",
            endpoint.Id, tenantId);

        // Return with the plain secret (only time it's visible)
        var result = MapToDto(endpoint);
        result.Secret = secret;
        return result;
    }

    public async Task<WebhookEndpoint> UpdateEndpointAsync(
        string endpointId,
        UpdateWebhookEndpoint dto,
        CancellationToken cancellationToken = default)
    {
        var endpoint = await _context.WebhookEndpoints
            .Include(e => e.EventSubscriptions)
            .FirstOrDefaultAsync(e => e.Id == endpointId, cancellationToken)
            ?? throw new InvalidOperationException($"Endpoint {endpointId} not found");

        if (dto.Name != null) endpoint.Name = dto.Name;
        if (dto.Description != null) endpoint.Description = dto.Description;
        if (dto.Url != null) endpoint.Url = dto.Url;
        if (dto.Enabled.HasValue) endpoint.Enabled = dto.Enabled.Value;
        if (dto.ApiVersion != null) endpoint.ApiVersion = dto.ApiVersion;
        if (dto.Headers != null) endpoint.HeadersJson = JsonSerializer.Serialize(dto.Headers, JsonOptions);

        if (dto.EventTypes != null)
        {
            // Update event subscriptions
            endpoint.EventSubscriptions.Clear();
            foreach (var eventType in dto.EventTypes)
            {
                endpoint.EventSubscriptions.Add(new WebhookEventSubscriptionEntity
                {
                    EventType = eventType,
                    Enabled = true
                });
            }
        }

        endpoint.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);

        return MapToDto(endpoint);
    }

    public async Task DeleteEndpointAsync(
        string endpointId,
        CancellationToken cancellationToken = default)
    {
        var endpoint = await _context.WebhookEndpoints
            .Include(e => e.EventSubscriptions)
            .Include(e => e.Deliveries)
            .FirstOrDefaultAsync(e => e.Id == endpointId, cancellationToken);

        if (endpoint != null)
        {
            // Mark pending deliveries as cancelled
            foreach (var delivery in endpoint.Deliveries.Where(d =>
                d.Status == WebhookDeliveryStatusEnum.Pending ||
                d.Status == WebhookDeliveryStatusEnum.Failed))
            {
                delivery.Status = WebhookDeliveryStatusEnum.Cancelled;
                delivery.UpdatedAt = DateTime.UtcNow;
            }

            _context.WebhookEndpoints.Remove(endpoint);
            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Deleted webhook endpoint {EndpointId}", endpointId);
        }
    }

    public async Task<string> RotateSecretAsync(
        string endpointId,
        CancellationToken cancellationToken = default)
    {
        var endpoint = await _context.WebhookEndpoints
            .FirstOrDefaultAsync(e => e.Id == endpointId, cancellationToken)
            ?? throw new InvalidOperationException($"Endpoint {endpointId} not found");

        var secret = GenerateSecret();
        endpoint.SecretHash = HashSecret(secret);
        endpoint.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Rotated secret for webhook endpoint {EndpointId}", endpointId);

        return secret;
    }

    public async Task<IEnumerable<WebhookDelivery>> GetDeliveryHistoryAsync(
        string endpointId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        var deliveries = await _context.WebhookDeliveries
            .Where(d => d.EndpointId == endpointId)
            .OrderByDescending(d => d.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return deliveries.Select(MapDeliveryToDto);
    }

    public async Task<WebhookDelivery?> GetDeliveryAsync(
        string deliveryId,
        CancellationToken cancellationToken = default)
    {
        var delivery = await _context.WebhookDeliveries
            .FirstOrDefaultAsync(d => d.Id == deliveryId, cancellationToken);

        return delivery != null ? MapDeliveryToDto(delivery) : null;
    }

    #region Helpers

    private static string GenerateSecret()
    {
        var bytes = new byte[32];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(bytes);
        return $"whsec_{Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=')}";
    }

    private static string HashSecret(string secret)
    {
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(secret));
        return Convert.ToBase64String(hash);
    }

    private WebhookEndpoint MapToDto(WebhookEndpointEntity endpoint)
    {
        var headers = !string.IsNullOrEmpty(endpoint.HeadersJson)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(endpoint.HeadersJson, JsonOptions)
            : null;

        return new WebhookEndpoint
        {
            Id = endpoint.Id,
            TenantId = endpoint.TenantId ?? "",
            Name = endpoint.Name,
            Description = endpoint.Description,
            Url = endpoint.Url,
            Secret = null, // Never return the secret hash
            Enabled = endpoint.Enabled,
            EventTypes = endpoint.EventSubscriptions
                .Where(s => s.Enabled)
                .Select(s => s.EventType)
                .ToList(),
            Headers = headers,
            ApiVersion = endpoint.ApiVersion,
            CreatedAt = endpoint.CreatedAt,
            UpdatedAt = endpoint.UpdatedAt
        };
    }

    private static WebhookDelivery MapDeliveryToDto(WebhookDeliveryEntity delivery) => new()
    {
        Id = delivery.Id,
        EndpointId = delivery.EndpointId,
        EventType = delivery.EventType,
        Status = MapDeliveryStatus(delivery.Status),
        HttpStatusCode = delivery.HttpStatus ?? 0,
        Error = delivery.ErrorMessage,
        AttemptCount = delivery.RetryCount,
        CreatedAt = delivery.CreatedAt,
        DeliveredAt = delivery.Status == WebhookDeliveryStatusEnum.Success ? delivery.UpdatedAt : null
    };

    private static WebhookDeliveryStatus MapDeliveryStatus(WebhookDeliveryStatusEnum status)
    {
        return status switch
        {
            WebhookDeliveryStatusEnum.Pending => WebhookDeliveryStatus.Pending,
            WebhookDeliveryStatusEnum.Success => WebhookDeliveryStatus.Success,
            WebhookDeliveryStatusEnum.Failed => WebhookDeliveryStatus.Failed,
            WebhookDeliveryStatusEnum.Cancelled => WebhookDeliveryStatus.Failed,
            _ => WebhookDeliveryStatus.Pending
        };
    }

    #endregion
}

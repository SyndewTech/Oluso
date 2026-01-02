using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Events;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Oluso.EntityFramework.Stores;

/// <summary>
/// Entity Framework implementation of IWebhookSubscriptionStore
/// </summary>
public class WebhookSubscriptionStore : IWebhookSubscriptionStore
{
    private readonly IOlusoDbContext _context;
    private readonly ILogger<WebhookSubscriptionStore> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public WebhookSubscriptionStore(
        IOlusoDbContext context,
        ILogger<WebhookSubscriptionStore> logger)
    {
        _context = context;
        _logger = logger;
    }

    #region Webhook Endpoints

    public async Task<IReadOnlyList<WebhookEndpoint>> GetEndpointsAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        var endpoints = await _context.WebhookEndpoints
            .Include(e => e.EventSubscriptions)
            .Where(e => e.TenantId == tenantId)
            .OrderByDescending(e => e.CreatedAt)
            .ToListAsync(cancellationToken);

        return endpoints.Select(MapToDto).ToList();
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
        return result with { Secret = secret };
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

    #endregion

    #region Event Subscriptions

    public async Task<IReadOnlyList<WebhookEndpoint>> GetSubscribedEndpointsAsync(
        string tenantId,
        string eventType,
        CancellationToken cancellationToken = default)
    {
        var endpoints = await _context.WebhookEndpoints
            .Include(e => e.EventSubscriptions)
            .Where(e => e.TenantId == tenantId &&
                       e.Enabled &&
                       e.EventSubscriptions.Any(s => s.EventType == eventType && s.Enabled))
            .ToListAsync(cancellationToken);

        return endpoints.Select(MapToDto).ToList();
    }

    public async Task UpdateEventSubscriptionsAsync(
        string endpointId,
        IEnumerable<string> eventTypes,
        CancellationToken cancellationToken = default)
    {
        var endpoint = await _context.WebhookEndpoints
            .Include(e => e.EventSubscriptions)
            .FirstOrDefaultAsync(e => e.Id == endpointId, cancellationToken)
            ?? throw new InvalidOperationException($"Endpoint {endpointId} not found");

        endpoint.EventSubscriptions.Clear();
        foreach (var eventType in eventTypes)
        {
            endpoint.EventSubscriptions.Add(new WebhookEventSubscriptionEntity
            {
                EventType = eventType,
                Enabled = true
            });
        }

        endpoint.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
    }

    #endregion

    #region Delivery Logs

    public async Task<WebhookDelivery> RecordDeliveryAsync(
        string endpointId,
        WebhookDelivery dto,
        CancellationToken cancellationToken = default)
    {
        var endpoint = await _context.WebhookEndpoints
            .FirstOrDefaultAsync(e => e.Id == endpointId, cancellationToken);

        if (endpoint == null)
        {
            throw new InvalidOperationException($"Endpoint {endpointId} not found");
        }

        var delivery = new WebhookDeliveryEntity
        {
            Id = dto.Id,
            EndpointId = endpointId,
            TenantId = endpoint.TenantId,
            EventType = dto.EventType,
            PayloadId = dto.PayloadId,
            Payload = dto.Payload ?? "",
            Status = (WebhookDeliveryStatusEnum)dto.Status,
            HttpStatus = dto.HttpStatus,
            ResponseBody = dto.ResponseBody,
            ErrorMessage = dto.ErrorMessage,
            RetryCount = dto.RetryCount,
            NextRetryAt = dto.NextRetryAt,
            ResponseTimeMs = dto.ResponseTimeMs
        };

        _context.WebhookDeliveries.Add(delivery);

        // Update endpoint stats
        endpoint.TotalDeliveries++;
        endpoint.LastDeliveryAt = DateTime.UtcNow;

        if (dto.Status == WebhookDeliveryStatus.Success)
        {
            endpoint.SuccessfulDeliveries++;
            endpoint.LastSuccessAt = DateTime.UtcNow;
        }
        else
        {
            endpoint.FailedDeliveries++;
            endpoint.LastFailureAt = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);

        return MapDeliveryToDto(delivery);
    }

    public async Task<IReadOnlyList<WebhookDelivery>> GetDeliveryHistoryAsync(
        string endpointId,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var deliveries = await _context.WebhookDeliveries
            .Where(d => d.EndpointId == endpointId)
            .OrderByDescending(d => d.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return deliveries.Select(MapDeliveryToDto).ToList();
    }

    public async Task<WebhookDelivery?> GetDeliveryAsync(
        string deliveryId,
        CancellationToken cancellationToken = default)
    {
        var delivery = await _context.WebhookDeliveries
            .FirstOrDefaultAsync(d => d.Id == deliveryId, cancellationToken);

        return delivery != null ? MapDeliveryToDto(delivery) : null;
    }

    public async Task<IReadOnlyList<WebhookDelivery>> GetPendingRetriesAsync(
        int maxRetries = 5,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;

        var deliveries = await _context.WebhookDeliveries
            .Include(d => d.Endpoint)
            .Where(d => d.Status == WebhookDeliveryStatusEnum.Failed &&
                       d.RetryCount < maxRetries &&
                       d.NextRetryAt <= now &&
                       d.Endpoint.Enabled)
            .OrderBy(d => d.NextRetryAt)
            .Take(limit)
            .ToListAsync(cancellationToken);

        return deliveries.Select(MapDeliveryToDto).ToList();
    }

    public async Task UpdateDeliveryAsync(
        string deliveryId,
        WebhookDeliveryStatus status,
        int? httpStatus = null,
        string? responseBody = null,
        CancellationToken cancellationToken = default)
    {
        var delivery = await _context.WebhookDeliveries
            .Include(d => d.Endpoint)
            .FirstOrDefaultAsync(d => d.Id == deliveryId, cancellationToken);

        if (delivery == null) return;

        delivery.Status = (WebhookDeliveryStatusEnum)status;
        delivery.RetryCount++;
        delivery.UpdatedAt = DateTime.UtcNow;

        if (httpStatus.HasValue) delivery.HttpStatus = httpStatus;
        if (responseBody != null) delivery.ResponseBody = responseBody;

        if (status == WebhookDeliveryStatus.Success)
        {
            delivery.NextRetryAt = null;
            delivery.Endpoint.SuccessfulDeliveries++;
            delivery.Endpoint.LastSuccessAt = DateTime.UtcNow;
        }
        else if (status == WebhookDeliveryStatus.Failed)
        {
            // Calculate next retry with exponential backoff
            var delayMinutes = delivery.RetryCount switch
            {
                1 => 1,
                2 => 5,
                3 => 30,
                4 => 120,
                _ => 480
            };
            delivery.NextRetryAt = DateTime.UtcNow.AddMinutes(delayMinutes);
        }
        else
        {
            delivery.NextRetryAt = null;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    #endregion

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
            UpdatedAt = endpoint.UpdatedAt,
            Stats = new WebhookEndpointStats
            {
                TotalDeliveries = endpoint.TotalDeliveries,
                SuccessfulDeliveries = endpoint.SuccessfulDeliveries,
                FailedDeliveries = endpoint.FailedDeliveries,
                LastDeliveryAt = endpoint.LastDeliveryAt,
                LastSuccessAt = endpoint.LastSuccessAt,
                LastFailureAt = endpoint.LastFailureAt
            }
        };
    }

    private static WebhookDelivery MapDeliveryToDto(WebhookDeliveryEntity delivery) => new()
    {
        Id = delivery.Id,
        EndpointId = delivery.EndpointId,
        EventType = delivery.EventType,
        PayloadId = delivery.PayloadId,
        Payload = delivery.Payload,
        Status = (WebhookDeliveryStatus)delivery.Status,
        HttpStatus = delivery.HttpStatus,
        ResponseBody = delivery.ResponseBody,
        ErrorMessage = delivery.ErrorMessage,
        RetryCount = delivery.RetryCount,
        NextRetryAt = delivery.NextRetryAt,
        ResponseTimeMs = delivery.ResponseTimeMs,
        CreatedAt = delivery.CreatedAt,
        UpdatedAt = delivery.UpdatedAt
    };

    #endregion
}

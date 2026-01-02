using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Oluso.Core.Domain.Entities;
using Oluso.Core.UserJourneys;

namespace Oluso.EntityFramework.Stores;

/// <summary>
/// Entity Framework implementation of IJourneyStateStore
/// </summary>
public class JourneyStateStore : IJourneyStateStore
{
    private readonly IOlusoDbContext _context;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public JourneyStateStore(IOlusoDbContext context)
    {
        _context = context;
    }

    public async Task<JourneyState?> GetAsync(string journeyId, CancellationToken cancellationToken = default)
    {
        var entity = await _context.JourneyStates
            .FirstOrDefaultAsync(s => s.Id == journeyId, cancellationToken);

        return entity != null ? MapToState(entity) : null;
    }

    public async Task SaveAsync(JourneyState state, CancellationToken cancellationToken = default)
    {
        var entity = await _context.JourneyStates
            .FirstOrDefaultAsync(s => s.Id == state.Id, cancellationToken);

        if (entity == null)
        {
            entity = new JourneyStateEntity { Id = state.Id };
            _context.JourneyStates.Add(entity);
        }

        MapToEntity(state, entity);

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteAsync(string journeyId, CancellationToken cancellationToken = default)
    {
        var entity = await _context.JourneyStates
            .FirstOrDefaultAsync(s => s.Id == journeyId, cancellationToken);

        if (entity != null)
        {
            _context.JourneyStates.Remove(entity);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IEnumerable<JourneyState>> GetByUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        var entities = await _context.JourneyStates
            .Where(s => s.UserId == userId)
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken);

        return entities.Select(MapToState);
    }

    public async Task CleanupExpiredAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var cutoff = now.AddDays(-7); // Keep expired states for 7 days for debugging

        var expired = await _context.JourneyStates
            .Where(s => s.ExpiresAt != null && s.ExpiresAt < cutoff)
            .ToListAsync(cancellationToken);

        if (expired.Count > 0)
        {
            _context.JourneyStates.RemoveRange(expired);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    private static JourneyState MapToState(JourneyStateEntity entity)
    {
        return new JourneyState
        {
            Id = entity.Id,
            TenantId = entity.TenantId ?? "",
            ClientId = entity.ClientId,
            UserId = entity.UserId,
            PolicyId = entity.PolicyId,
            CurrentStepId = entity.CurrentStepId,
            Status = Enum.TryParse<JourneyStatus>(entity.Status, out var status) ? status : JourneyStatus.InProgress,
            CreatedAt = entity.CreatedAt,
            ExpiresAt = entity.ExpiresAt,
            Data = DeserializeDictionary(entity.Data),
            ClaimsBag = DeserializeClaimsBag(entity.ClaimsBag),
            SessionId = entity.SessionId,
            AuthenticatedUserId = entity.AuthenticatedUserId,
            CorrelationId = entity.CorrelationId,
            CallbackUrl = entity.CallbackUrl
        };
    }

    private static void MapToEntity(JourneyState state, JourneyStateEntity entity)
    {
        entity.TenantId = state.TenantId;
        entity.ClientId = state.ClientId;
        entity.UserId = state.UserId;
        entity.PolicyId = state.PolicyId;
        entity.CurrentStepId = state.CurrentStepId;
        entity.Status = state.Status.ToString();
        entity.CreatedAt = state.CreatedAt;
        entity.ExpiresAt = state.ExpiresAt;
        entity.Data = SerializeDictionary(state.Data);
        entity.ClaimsBag = SerializeClaimsBag(state.ClaimsBag);
        entity.SessionId = state.SessionId;
        entity.AuthenticatedUserId = state.AuthenticatedUserId;
        entity.CorrelationId = state.CorrelationId;
        entity.CallbackUrl = state.CallbackUrl;
    }

    private static IDictionary<string, object>? DeserializeDictionary(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json, JsonOptions);
            if (dict == null) return null;

            var result = new Dictionary<string, object>();
            foreach (var kvp in dict)
            {
                result[kvp.Key] = ConvertJsonElement(kvp.Value);
            }
            return result;
        }
        catch
        {
            return null;
        }
    }

    private static object ConvertJsonElement(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElement).ToList(),
            JsonValueKind.Object => element.EnumerateObject().ToDictionary(p => p.Name, p => ConvertJsonElement(p.Value)),
            _ => element.ToString()
        };
    }

    private static string? SerializeDictionary(IDictionary<string, object>? data)
    {
        if (data == null || data.Count == 0) return null;
        return JsonSerializer.Serialize(data, JsonOptions);
    }

    private static JourneyClaimsBag DeserializeClaimsBag(string? json)
    {
        var bag = new JourneyClaimsBag();
        if (string.IsNullOrEmpty(json)) return bag;

        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions);
            if (dict != null)
            {
                foreach (var kvp in dict)
                {
                    bag.Set(kvp.Key, kvp.Value);
                }
            }
        }
        catch
        {
            // Ignore deserialization errors
        }

        return bag;
    }

    private static string? SerializeClaimsBag(JourneyClaimsBag? bag)
    {
        if (bag == null) return null;
        var claims = bag.GetAll();
        if (claims.Count == 0) return null;
        return JsonSerializer.Serialize(claims, JsonOptions);
    }
}

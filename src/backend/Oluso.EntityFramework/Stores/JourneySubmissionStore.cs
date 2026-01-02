using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Oluso.Core.Domain.Entities;
using Oluso.Core.UserJourneys;

namespace Oluso.EntityFramework.Stores;

/// <summary>
/// Entity Framework implementation of IJourneySubmissionStore
/// </summary>
public class JourneySubmissionStore : IJourneySubmissionStore
{
    private readonly IOlusoDbContext _context;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public JourneySubmissionStore(IOlusoDbContext context)
    {
        _context = context;
    }

    public async Task<JourneySubmission?> GetAsync(string submissionId, CancellationToken cancellationToken = default)
    {
        var entity = await _context.JourneySubmissions
            .FirstOrDefaultAsync(s => s.Id == submissionId, cancellationToken);

        return entity != null ? MapToSubmission(entity) : null;
    }

    public async Task<IEnumerable<JourneySubmission>> GetByPolicyAsync(
        string policyId,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        var entities = await _context.JourneySubmissions
            .Where(s => s.PolicyId == policyId)
            .OrderByDescending(s => s.CreatedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync(cancellationToken);

        return entities.Select(MapToSubmission);
    }

    public async Task<int> CountByPolicyAsync(string policyId, CancellationToken cancellationToken = default)
    {
        return await _context.JourneySubmissions
            .CountAsync(s => s.PolicyId == policyId, cancellationToken);
    }

    public async Task<bool> HasDuplicateAsync(
        string policyId,
        string field,
        string value,
        CancellationToken cancellationToken = default)
    {
        // We need to search within the JSON data
        // This is a simple implementation; for production, consider a more efficient approach
        var submissions = await _context.JourneySubmissions
            .Where(s => s.PolicyId == policyId)
            .Select(s => s.Data)
            .ToListAsync(cancellationToken);

        foreach (var dataJson in submissions)
        {
            try
            {
                var data = JsonSerializer.Deserialize<Dictionary<string, object>>(dataJson, JsonOptions);
                if (data != null && data.TryGetValue(field, out var fieldValue))
                {
                    if (string.Equals(fieldValue?.ToString(), value, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // Skip malformed data
            }
        }

        return false;
    }

    public async Task<string> SaveAsync(JourneySubmission submission, CancellationToken cancellationToken = default)
    {
        var entity = await _context.JourneySubmissions
            .FirstOrDefaultAsync(s => s.Id == submission.Id, cancellationToken);

        if (entity == null)
        {
            entity = new JourneySubmissionEntity { Id = submission.Id };
            _context.JourneySubmissions.Add(entity);
        }

        MapToEntity(submission, entity);

        await _context.SaveChangesAsync(cancellationToken);

        return entity.Id;
    }

    public async Task DeleteAsync(string submissionId, CancellationToken cancellationToken = default)
    {
        var entity = await _context.JourneySubmissions
            .FirstOrDefaultAsync(s => s.Id == submissionId, cancellationToken);

        if (entity != null)
        {
            _context.JourneySubmissions.Remove(entity);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IEnumerable<JourneySubmission>> ExportAsync(
        string policyId,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.JourneySubmissions
            .Where(s => s.PolicyId == policyId);

        if (from.HasValue)
        {
            query = query.Where(s => s.CreatedAt >= from.Value);
        }

        if (to.HasValue)
        {
            query = query.Where(s => s.CreatedAt <= to.Value);
        }

        var entities = await query
            .OrderByDescending(s => s.CreatedAt)
            .ToListAsync(cancellationToken);

        return entities.Select(MapToSubmission);
    }

    private static JourneySubmission MapToSubmission(JourneySubmissionEntity entity)
    {
        return new JourneySubmission
        {
            Id = entity.Id,
            PolicyId = entity.PolicyId,
            PolicyName = entity.PolicyName,
            TenantId = entity.TenantId,
            JourneyId = entity.JourneyId,
            Data = DeserializeData(entity.Data),
            Metadata = new SubmissionMetadata
            {
                IpAddress = entity.IpAddress,
                UserAgent = entity.UserAgent,
                Referrer = entity.Referrer,
                UtmParameters = DeserializeUtmParameters(entity.UtmParameters),
                Country = entity.Country,
                Locale = entity.Locale
            },
            Status = Enum.TryParse<SubmissionStatus>(entity.Status, out var status) ? status : SubmissionStatus.New,
            Notes = entity.Notes,
            Tags = entity.Tags?.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList(),
            CreatedAt = entity.CreatedAt,
            ReviewedAt = entity.ReviewedAt,
            ReviewedBy = entity.ReviewedBy
        };
    }

    private static void MapToEntity(JourneySubmission submission, JourneySubmissionEntity entity)
    {
        entity.PolicyId = submission.PolicyId;
        entity.PolicyName = submission.PolicyName;
        entity.TenantId = submission.TenantId;
        entity.JourneyId = submission.JourneyId;
        entity.Data = SerializeData(submission.Data);
        entity.IpAddress = submission.Metadata.IpAddress;
        entity.UserAgent = submission.Metadata.UserAgent;
        entity.Referrer = submission.Metadata.Referrer;
        entity.UtmParameters = SerializeUtmParameters(submission.Metadata.UtmParameters);
        entity.Country = submission.Metadata.Country;
        entity.Locale = submission.Metadata.Locale;
        entity.Status = submission.Status.ToString();
        entity.Notes = submission.Notes;
        entity.Tags = submission.Tags != null ? string.Join(',', submission.Tags) : null;
        entity.CreatedAt = submission.CreatedAt;
        entity.ReviewedAt = submission.ReviewedAt;
        entity.ReviewedBy = submission.ReviewedBy;
    }

    private static IDictionary<string, object> DeserializeData(string json)
    {
        if (string.IsNullOrEmpty(json)) return new Dictionary<string, object>();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object>>(json, JsonOptions)
                ?? new Dictionary<string, object>();
        }
        catch
        {
            return new Dictionary<string, object>();
        }
    }

    private static string SerializeData(IDictionary<string, object> data)
    {
        return JsonSerializer.Serialize(data, JsonOptions);
    }

    private static IDictionary<string, string>? DeserializeUtmParameters(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static string? SerializeUtmParameters(IDictionary<string, string>? utmParameters)
    {
        if (utmParameters == null || utmParameters.Count == 0) return null;
        return JsonSerializer.Serialize(utmParameters, JsonOptions);
    }
}

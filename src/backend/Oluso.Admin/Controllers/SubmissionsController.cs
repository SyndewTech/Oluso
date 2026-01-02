using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oluso.Core.Api;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.UserJourneys;
using System.Text;
using System.Text.Json;

namespace Oluso.Admin.Controllers;

/// <summary>
/// Admin API for managing data collection submissions (waitlists, surveys, etc.)
/// </summary>
[Route("api/admin/submissions")]
public class SubmissionsController : AdminBaseController
{
    private readonly IJourneySubmissionStore? _submissionStore;
    private readonly IJourneyPolicyStore _policyStore;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<SubmissionsController> _logger;

    public SubmissionsController(
        IJourneyPolicyStore policyStore,
        ITenantContext tenantContext,
        ILogger<SubmissionsController> logger,
        IJourneySubmissionStore? submissionStore = null) : base(tenantContext)
    {
        _policyStore = policyStore;
        _submissionStore = submissionStore;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>
    /// Get all data collection policies with submission counts
    /// </summary>
    [HttpGet("policies")]
    public async Task<ActionResult<IEnumerable<DataCollectionPolicyDto>>> GetDataCollectionPolicies(
        CancellationToken cancellationToken = default)
    {
        var policies = await _policyStore.GetByTenantAsync(_tenantContext.TenantId, cancellationToken);

        // Filter to only data collection policies
        // Include policies that are data collection types OR have submissions enabled OR don't require auth
        var dataCollectionTypes = new[] {
            JourneyType.Waitlist, JourneyType.ContactForm, JourneyType.Survey,
            JourneyType.Feedback, JourneyType.DataCollection
        };

        var dataCollectionPolicies = policies
            .Where(p => dataCollectionTypes.Contains(p.Type) || p.PersistSubmissions || !p.RequiresAuthentication)
            .ToList();

        var result = new List<DataCollectionPolicyDto>();

        foreach (var policy in dataCollectionPolicies)
        {
            // Get count only if submission store is available
            var count = _submissionStore != null
                ? await _submissionStore.CountByPolicyAsync(policy.Id, cancellationToken)
                : 0;

            result.Add(new DataCollectionPolicyDto
            {
                Id = policy.Id,
                Name = policy.Name,
                Description = policy.Description,
                Type = policy.Type.ToString(),
                Enabled = policy.Enabled,
                SubmissionCount = count,
                MaxSubmissions = policy.MaxSubmissions,
                AllowDuplicates = policy.AllowDuplicates,
                CreatedAt = policy.CreatedAt
            });
        }

        return Ok(result.OrderBy(p => p.Name));
    }

    /// <summary>
    /// Get submissions for a policy
    /// </summary>
    [HttpGet("policy/{policyId}")]
    public async Task<ActionResult<SubmissionListResponse>> GetSubmissions(
        string policyId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken cancellationToken = default)
    {
        if (_submissionStore == null)
        {
            return Ok(new SubmissionListResponse
            {
                Submissions = new List<SubmissionDto>(),
                Total = 0,
                Skip = skip,
                Take = take
            });
        }

        // Verify policy belongs to tenant
        var policy = await _policyStore.GetAsync(policyId, cancellationToken);
        if (policy == null)
        {
            return NotFound();
        }
        if (policy.TenantId != null && policy.TenantId != _tenantContext.TenantId)
        {
            return Forbid();
        }

        var submissions = await _submissionStore.GetByPolicyAsync(policyId, skip, take, cancellationToken);
        var total = await _submissionStore.CountByPolicyAsync(policyId, cancellationToken);

        return Ok(new SubmissionListResponse
        {
            Submissions = submissions.Select(MapToDto).ToList(),
            Total = total,
            Skip = skip,
            Take = take
        });
    }

    /// <summary>
    /// Get a single submission
    /// </summary>
    [HttpGet("{submissionId}")]
    public async Task<ActionResult<SubmissionDto>> GetSubmission(
        string submissionId,
        CancellationToken cancellationToken = default)
    {
        if (_submissionStore == null)
        {
            return NotFound();
        }

        var submission = await _submissionStore.GetAsync(submissionId, cancellationToken);
        if (submission == null)
        {
            return NotFound();
        }

        // Verify tenant access
        if (submission.TenantId != null && submission.TenantId != _tenantContext.TenantId)
        {
            return Forbid();
        }

        return Ok(MapToDto(submission));
    }

    /// <summary>
    /// Update a submission (status, notes, tags)
    /// </summary>
    [HttpPatch("{submissionId}")]
    public async Task<ActionResult<SubmissionDto>> UpdateSubmission(
        string submissionId,
        [FromBody] UpdateSubmissionRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_submissionStore == null)
        {
            return NotFound();
        }

        var submission = await _submissionStore.GetAsync(submissionId, cancellationToken);
        if (submission == null)
        {
            return NotFound();
        }

        // Verify tenant access
        if (submission.TenantId != null && submission.TenantId != _tenantContext.TenantId)
        {
            return Forbid();
        }

        // Update fields
        if (request.Status.HasValue)
        {
            submission.Status = request.Status.Value;
            submission.ReviewedAt = DateTime.UtcNow;
            submission.ReviewedBy = User.Identity?.Name;
        }
        if (request.Notes != null)
        {
            submission.Notes = request.Notes;
        }
        if (request.Tags != null)
        {
            submission.Tags = request.Tags.ToList();
        }

        await _submissionStore.SaveAsync(submission, cancellationToken);

        _logger.LogInformation("Updated submission {SubmissionId}", submissionId);

        return Ok(MapToDto(submission));
    }

    /// <summary>
    /// Delete a submission
    /// </summary>
    [HttpDelete("{submissionId}")]
    public async Task<IActionResult> DeleteSubmission(
        string submissionId,
        CancellationToken cancellationToken = default)
    {
        if (_submissionStore == null)
        {
            return NotFound();
        }

        var submission = await _submissionStore.GetAsync(submissionId, cancellationToken);
        if (submission == null)
        {
            return NotFound();
        }

        // Verify tenant access
        if (submission.TenantId != null && submission.TenantId != _tenantContext.TenantId)
        {
            return Forbid();
        }

        await _submissionStore.DeleteAsync(submissionId, cancellationToken);

        _logger.LogInformation("Deleted submission {SubmissionId}", submissionId);

        return NoContent();
    }

    /// <summary>
    /// Export submissions for a policy
    /// </summary>
    [HttpGet("policy/{policyId}/export")]
    public async Task<IActionResult> ExportSubmissions(
        string policyId,
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] string format = "csv",
        CancellationToken cancellationToken = default)
    {
        if (_submissionStore == null)
        {
            return NotFound();
        }

        // Verify policy belongs to tenant
        var policy = await _policyStore.GetAsync(policyId, cancellationToken);
        if (policy == null)
        {
            return NotFound();
        }
        if (policy.TenantId != null && policy.TenantId != _tenantContext.TenantId)
        {
            return Forbid();
        }

        var submissions = await _submissionStore.ExportAsync(policyId, from, to, cancellationToken);
        var submissionList = submissions.ToList();

        if (format.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(submissionList.Select(MapToDto), new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            return File(jsonBytes, "application/json", $"{policy.Name}-submissions.json");
        }

        // Default to CSV
        var csv = GenerateCsv(submissionList);
        return File(Encoding.UTF8.GetBytes(csv), "text/csv", $"{policy.Name}-submissions.csv");
    }

    /// <summary>
    /// Get submission count for a policy
    /// </summary>
    [HttpGet("policy/{policyId}/count")]
    public async Task<ActionResult<object>> GetSubmissionCount(
        string policyId,
        CancellationToken cancellationToken = default)
    {
        if (_submissionStore == null)
        {
            return Ok(new { count = 0 });
        }

        // Verify policy belongs to tenant
        var policy = await _policyStore.GetAsync(policyId, cancellationToken);
        if (policy == null)
        {
            return NotFound();
        }
        if (policy.TenantId != null && policy.TenantId != _tenantContext.TenantId)
        {
            return Forbid();
        }

        var count = await _submissionStore.CountByPolicyAsync(policyId, cancellationToken);
        return Ok(new { count });
    }

    private static SubmissionDto MapToDto(JourneySubmission submission)
    {
        return new SubmissionDto
        {
            Id = submission.Id,
            PolicyId = submission.PolicyId,
            PolicyName = submission.PolicyName,
            TenantId = submission.TenantId,
            JourneyId = submission.JourneyId,
            Data = submission.Data.ToDictionary(k => k.Key, v => v.Value),
            Metadata = new SubmissionMetadataDto
            {
                IpAddress = submission.Metadata.IpAddress,
                UserAgent = submission.Metadata.UserAgent,
                Referrer = submission.Metadata.Referrer,
                UtmParameters = submission.Metadata.UtmParameters?.ToDictionary(k => k.Key, v => v.Value),
                Country = submission.Metadata.Country,
                Locale = submission.Metadata.Locale
            },
            Status = submission.Status.ToString(),
            Notes = submission.Notes,
            Tags = submission.Tags?.ToList(),
            CreatedAt = submission.CreatedAt,
            ReviewedAt = submission.ReviewedAt,
            ReviewedBy = submission.ReviewedBy
        };
    }

    private static string GenerateCsv(List<JourneySubmission> submissions)
    {
        if (submissions.Count == 0)
        {
            return "No submissions found";
        }

        var sb = new StringBuilder();

        // Collect all unique data keys across all submissions
        var allKeys = submissions
            .SelectMany(s => s.Data.Keys)
            .Distinct()
            .OrderBy(k => k)
            .ToList();

        // Headers
        var headers = new List<string> { "Id", "CreatedAt", "Status", "IP Address", "Country", "Referrer" };
        headers.AddRange(allKeys);
        sb.AppendLine(string.Join(",", headers.Select(EscapeCsvField)));

        // Data rows
        foreach (var submission in submissions)
        {
            var values = new List<string>
            {
                submission.Id,
                submission.CreatedAt.ToString("O"),
                submission.Status.ToString(),
                submission.Metadata.IpAddress ?? "",
                submission.Metadata.Country ?? "",
                submission.Metadata.Referrer ?? ""
            };

            foreach (var key in allKeys)
            {
                var value = submission.Data.TryGetValue(key, out var v) ? v?.ToString() ?? "" : "";
                values.Add(value);
            }

            sb.AppendLine(string.Join(",", values.Select(EscapeCsvField)));
        }

        return sb.ToString();
    }

    private static string EscapeCsvField(string field)
    {
        if (string.IsNullOrEmpty(field)) return "";

        // If field contains comma, quotes, or newlines, wrap in quotes and escape existing quotes
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }

        return field;
    }
}

#region DTOs

public class DataCollectionPolicyDto
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string Type { get; set; } = null!;
    public bool Enabled { get; set; }
    public int SubmissionCount { get; set; }
    public int MaxSubmissions { get; set; }
    public bool AllowDuplicates { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class SubmissionListResponse
{
    public List<SubmissionDto> Submissions { get; set; } = new();
    public int Total { get; set; }
    public int Skip { get; set; }
    public int Take { get; set; }
}

public class SubmissionDto
{
    public string Id { get; set; } = null!;
    public string PolicyId { get; set; } = null!;
    public string? PolicyName { get; set; }
    public string? TenantId { get; set; }
    public string? JourneyId { get; set; }
    public Dictionary<string, object> Data { get; set; } = new();
    public SubmissionMetadataDto Metadata { get; set; } = new();
    public string Status { get; set; } = null!;
    public string? Notes { get; set; }
    public List<string>? Tags { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public string? ReviewedBy { get; set; }
}

public class SubmissionMetadataDto
{
    public string? IpAddress { get; set; }
    public string? UserAgent { get; set; }
    public string? Referrer { get; set; }
    public Dictionary<string, string>? UtmParameters { get; set; }
    public string? Country { get; set; }
    public string? Locale { get; set; }
}

public class UpdateSubmissionRequest
{
    public SubmissionStatus? Status { get; set; }
    public string? Notes { get; set; }
    public List<string>? Tags { get; set; }
}

#endregion

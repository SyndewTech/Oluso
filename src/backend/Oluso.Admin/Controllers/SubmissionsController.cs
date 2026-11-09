using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oluso.Admin.Authorization;
using Oluso.Core.Api;
using Oluso.Core.Authorization;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Licensing;
using Oluso.Core.UserJourneys;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Oluso.Admin.Dto.Submissions;

namespace Oluso.Admin.Controllers;

/// <summary>
/// Admin API for managing data collection submissions (waitlists, surveys, etc.)
/// </summary>
[Route("api/admin/submissions")]
[RequirePermission(AdminPermissions.SubmissionsRead)]
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
                CreatedAt = policy.CreatedAt,
                OwnerModule = policy.OwnerModule,
                OwnerModuleName = policy.OwnerModule == null ? null
                    : ModuleRegistry.Instance.GetModule(policy.OwnerModule)?.DisplayName ?? policy.OwnerModule,
                AnswersHidden = policy.IsModuleOwned,
            });
        }

        return Ok(result.OrderBy(p => p.Name));
    }

    /// <summary>
    /// Get submissions for a policy with optional filters
    /// </summary>
    [HttpGet("policy/{policyId}")]
    public async Task<ActionResult<PagedResult<SubmissionDto>>> GetSubmissions(
        string policyId,
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        [FromQuery] string? status = null,
        [FromQuery] string? search = null,
        [FromQuery] DateTime? dateFrom = null,
        [FromQuery] DateTime? dateTo = null,
        [FromQuery] string? tags = null,
        CancellationToken cancellationToken = default)
    {
        if (_submissionStore == null)
        {
            return Ok(new PagedResult<SubmissionDto>
            {
                Items = new List<SubmissionDto>(),
                TotalCount = 0,
                Page = skip / take + 1,
                PageSize = take,
                TotalPages = 0,
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

        // Parse status filter
        SubmissionStatus? statusFilter = null;
        if (!string.IsNullOrEmpty(status) && Enum.TryParse<SubmissionStatus>(status, true, out var parsedStatus))
        {
            statusFilter = parsedStatus;
        }

        // Parse tags filter
        var tagsList = string.IsNullOrEmpty(tags) ? null : tags.Split(',').Select(t => t.Trim()).Where(t => !string.IsNullOrEmpty(t)).ToList();

        var hideAnswers = policy.IsModuleOwned;
        var filtering = statusFilter.HasValue || !string.IsNullOrEmpty(search) || dateFrom.HasValue || dateTo.HasValue
            || tagsList is { Count: > 0 };
        if (!filtering)
        {
            var page = await _submissionStore.GetByPolicyAsync(policyId, skip, take, cancellationToken);
            var count = await _submissionStore.CountByPolicyAsync(policyId, cancellationToken);
            return Ok(new PagedResult<SubmissionDto>
            {
                Items = page.Select(s => MapToDto(s, hideAnswers)).ToList(),
                TotalCount = count,
                Page = skip / take + 1,
                PageSize = take,
                TotalPages = (int)Math.Ceiling((double)count / take),
            });
        }

        // Filters apply to every submission of the policy, then the page is taken from the matches.
        var submissions = await _submissionStore.GetByPolicyAsync(policyId, 0, int.MaxValue, cancellationToken);
        var filtered = submissions.AsEnumerable();

        if (statusFilter.HasValue)
        {
            filtered = filtered.Where(s => s.Status == statusFilter.Value);
        }

        if (!string.IsNullOrEmpty(search))
        {
            var searchLower = search.ToLowerInvariant();
            filtered = filtered.Where(s =>
                (!hideAnswers && s.Data.Any(d => d.Value?.ToString()?.ToLowerInvariant().Contains(searchLower) == true)) ||
                s.Metadata.IpAddress?.ToLowerInvariant().Contains(searchLower) == true ||
                s.Metadata.Country?.ToLowerInvariant().Contains(searchLower) == true);
        }

        if (dateFrom.HasValue)
        {
            filtered = filtered.Where(s => s.CreatedAt >= dateFrom.Value);
        }

        if (dateTo.HasValue)
        {
            filtered = filtered.Where(s => s.CreatedAt <= dateTo.Value.AddDays(1)); // Include entire end day
        }

        if (tagsList != null && tagsList.Any())
        {
            filtered = filtered.Where(s => s.Tags != null && s.Tags.Any(t => tagsList.Contains(t)));
        }

        var filteredList = filtered.ToList();
        var total = filteredList.Count;

        // Apply pagination after filtering
        var paginatedList = filteredList.Skip(skip).Take(take).ToList();

        return Ok(new PagedResult<SubmissionDto>
        {
            Items = paginatedList.Select(s => MapToDto(s, hideAnswers)).ToList(),
            TotalCount = total,
            Page = skip / take + 1,
            PageSize = take,
            TotalPages = (int)Math.Ceiling((double)total / take),
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

        return Ok(MapToDto(submission, await AnswersHiddenAsync(submission.PolicyId, cancellationToken)));
    }

    /// <summary>
    /// Update a submission (status, notes, tags)
    /// </summary>
    [HttpPatch("{submissionId}")]
    [RequirePermission(AdminPermissions.SubmissionsWrite)]
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

        return Ok(MapToDto(submission, await AnswersHiddenAsync(submission.PolicyId, cancellationToken)));
    }

    /// <summary>
    /// Delete a submission
    /// </summary>
    [HttpDelete("{submissionId}")]
    [RequirePermission(AdminPermissions.SubmissionsDelete)]
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
    /// Bulk update submissions (status, tags)
    /// </summary>
    [HttpPost("bulk-update")]
    [RequirePermission(AdminPermissions.SubmissionsWrite)]
    public async Task<ActionResult<object>> BulkUpdateSubmissions(
        [FromBody] BulkUpdateSubmissionsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_submissionStore == null)
        {
            return Ok(new { updated = 0 });
        }

        if (request.SubmissionIds == null || !request.SubmissionIds.Any())
        {
            return BadRequest("No submission IDs provided");
        }

        var updated = 0;
        foreach (var submissionId in request.SubmissionIds)
        {
            var submission = await _submissionStore.GetAsync(submissionId, cancellationToken);
            if (submission == null) continue;

            // Verify tenant access
            if (submission.TenantId != null && submission.TenantId != _tenantContext.TenantId) continue;

            // Update status
            if (request.Status.HasValue)
            {
                submission.Status = request.Status.Value;
                submission.ReviewedAt = DateTime.UtcNow;
                submission.ReviewedBy = User.Identity?.Name;
            }

            // Replace tags
            if (request.Tags != null)
            {
                submission.Tags = request.Tags.ToList();
            }

            // Add tags
            if (request.AddTags != null && request.AddTags.Any())
            {
                submission.Tags ??= new List<string>();
                foreach (var tag in request.AddTags)
                {
                    if (!submission.Tags.Contains(tag))
                    {
                        submission.Tags.Add(tag);
                    }
                }
            }

            // Remove tags
            if (request.RemoveTags != null && request.RemoveTags.Any() && submission.Tags != null)
            {
                submission.Tags = submission.Tags.Where(t => !request.RemoveTags.Contains(t)).ToList();
            }

            await _submissionStore.SaveAsync(submission, cancellationToken);
            updated++;
        }

        _logger.LogInformation("Bulk updated {Count} submissions", updated);

        return Ok(new { updated });
    }

    /// <summary>
    /// Bulk delete submissions
    /// </summary>
    [HttpPost("bulk-delete")]
    [RequirePermission(AdminPermissions.SubmissionsDelete)]
    public async Task<ActionResult<object>> BulkDeleteSubmissions(
        [FromBody] BulkDeleteSubmissionsRequest request,
        CancellationToken cancellationToken = default)
    {
        if (_submissionStore == null)
        {
            return Ok(new { deleted = 0 });
        }

        if (request.SubmissionIds == null || !request.SubmissionIds.Any())
        {
            return BadRequest("No submission IDs provided");
        }

        var deleted = 0;
        foreach (var submissionId in request.SubmissionIds)
        {
            var submission = await _submissionStore.GetAsync(submissionId, cancellationToken);
            if (submission == null) continue;

            // Verify tenant access
            if (submission.TenantId != null && submission.TenantId != _tenantContext.TenantId) continue;

            await _submissionStore.DeleteAsync(submissionId, cancellationToken);
            deleted++;
        }

        _logger.LogInformation("Bulk deleted {Count} submissions", deleted);

        return Ok(new { deleted });
    }

    /// <summary>
    /// Get submission statistics for a policy
    /// </summary>
    [HttpGet("policy/{policyId}/statistics")]
    public async Task<ActionResult<SubmissionStatisticsDto>> GetStatistics(
        string policyId,
        CancellationToken cancellationToken = default)
    {
        if (_submissionStore == null)
        {
            return Ok(new SubmissionStatisticsDto());
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

        // Get all submissions for statistics calculation
        var submissions = await _submissionStore.GetByPolicyAsync(policyId, 0, int.MaxValue, cancellationToken);
        var submissionList = submissions.ToList();

        var now = DateTime.UtcNow;
        var today = now.Date;
        var weekAgo = today.AddDays(-7);
        var monthAgo = today.AddDays(-30);
        var twoWeeksAgo = today.AddDays(-14);

        var stats = new SubmissionStatisticsDto
        {
            Total = submissionList.Count,
            Today = submissionList.Count(s => s.CreatedAt >= today),
            ThisWeek = submissionList.Count(s => s.CreatedAt >= weekAgo),
            ThisMonth = submissionList.Count(s => s.CreatedAt >= monthAgo),
            ByStatus = new Dictionary<string, int>()
        };

        // Count by status
        foreach (var status in Enum.GetValues<SubmissionStatus>())
        {
            stats.ByStatus[status.ToString()] = submissionList.Count(s => s.Status == status);
        }

        // Calculate average per day (last 30 days)
        if (submissionList.Any())
        {
            var daysActive = Math.Max(1, (now - submissionList.Min(s => s.CreatedAt)).Days);
            daysActive = Math.Min(daysActive, 30); // Cap at 30 days
            stats.AveragePerDay = Math.Round((double)stats.ThisMonth / daysActive, 1);
        }

        // Calculate trend (this week vs last week)
        var lastWeekCount = submissionList.Count(s => s.CreatedAt >= twoWeeksAgo && s.CreatedAt < weekAgo);
        if (lastWeekCount > 0)
        {
            var percentChange = ((double)stats.ThisWeek - lastWeekCount) / lastWeekCount * 100;
            stats.TrendPercentage = Math.Round(percentChange, 1);
            stats.RecentTrend = percentChange > 5 ? "up" : percentChange < -5 ? "down" : "stable";
        }
        else
        {
            stats.RecentTrend = stats.ThisWeek > 0 ? "up" : "stable";
        }

        return Ok(stats);
    }

    /// <summary>
    /// Get all unique tags used in submissions for a policy
    /// </summary>
    [HttpGet("policy/{policyId}/tags")]
    public async Task<ActionResult<IEnumerable<string>>> GetAvailableTags(
        string policyId,
        CancellationToken cancellationToken = default)
    {
        if (_submissionStore == null)
        {
            return Ok(Array.Empty<string>());
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

        var submissions = await _submissionStore.GetByPolicyAsync(policyId, 0, int.MaxValue, cancellationToken);
        var allTags = submissions
            .Where(s => s.Tags != null)
            .SelectMany(s => s.Tags!)
            .Distinct()
            .OrderBy(t => t)
            .ToList();

        return Ok(allTags);
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
        var hideAnswers = policy.IsModuleOwned;

        if (format.Equals("json", StringComparison.OrdinalIgnoreCase))
        {
            var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(submissionList.Select(s => MapToDto(s, hideAnswers)), new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
            return File(jsonBytes, "application/json", $"{policy.Name}-submissions.json");
        }

        // Default to CSV
        var csv = GenerateCsv(submissionList, hideAnswers);
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

    /// <summary>True when the journey belongs to a module, whose answers are never shown here.</summary>
    private async Task<bool> AnswersHiddenAsync(string policyId, CancellationToken cancellationToken) =>
        (await _policyStore.GetAsync(policyId, cancellationToken))?.IsModuleOwned == true;

    /// <param name="hideAnswers">A module journey's run: its answers (including any stored before the module
    /// owned the journey) are left out.</param>
    private static SubmissionDto MapToDto(JourneySubmission submission, bool hideAnswers)
    {
        return new SubmissionDto
        {
            Id = submission.Id,
            PolicyId = submission.PolicyId,
            PolicyName = submission.PolicyName,
            TenantId = submission.TenantId,
            JourneyId = submission.JourneyId,
            Data = hideAnswers ? new Dictionary<string, object>() : submission.Data.ToDictionary(k => k.Key, v => v.Value),
            Metadata = new SubmissionMetadataDto
            {
                IpAddress = submission.Metadata.IpAddress,
                UserAgent = submission.Metadata.UserAgent,
                Referrer = submission.Metadata.Referrer,
                UtmParameters = submission.Metadata.UtmParameters?.ToDictionary(k => k.Key, v => v.Value),
                Country = submission.Metadata.Country,
                Locale = submission.Metadata.Locale
            },
            Status = submission.Status,
            Notes = submission.Notes,
            Tags = submission.Tags?.ToList(),
            CreatedAt = submission.CreatedAt,
            ReviewedAt = submission.ReviewedAt,
            ReviewedBy = submission.ReviewedBy
        };
    }

    private static string GenerateCsv(List<JourneySubmission> submissions, bool hideAnswers)
    {
        if (submissions.Count == 0)
        {
            return "No submissions found";
        }

        var sb = new StringBuilder();

        // Collect all unique data keys across all submissions
        var allKeys = hideAnswers ? new List<string>() : submissions
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

        // A value a spreadsheet would run as a formula (=, +, -, @, or a leading tab or carriage return) is written
        // as text, so a submitted answer can't execute when an administrator opens the export.
        if (field[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            field = "'" + field;
        }

        // If field contains comma, quotes, or newlines, wrap in quotes and escape existing quotes
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }

        return field;
    }
}

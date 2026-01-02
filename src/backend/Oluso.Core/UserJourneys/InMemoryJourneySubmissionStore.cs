using System.Collections.Concurrent;

namespace Oluso.Core.UserJourneys;

/// <summary>
/// In-memory implementation of IJourneySubmissionStore for development/testing
/// </summary>
public class InMemoryJourneySubmissionStore : IJourneySubmissionStore
{
    private readonly ConcurrentDictionary<string, JourneySubmission> _submissions = new();

    public Task<JourneySubmission?> GetAsync(string submissionId, CancellationToken cancellationToken = default)
    {
        _submissions.TryGetValue(submissionId, out var submission);
        return Task.FromResult(submission);
    }

    public Task<IEnumerable<JourneySubmission>> GetByPolicyAsync(
        string policyId,
        int skip = 0,
        int take = 50,
        CancellationToken cancellationToken = default)
    {
        var submissions = _submissions.Values
            .Where(s => s.PolicyId == policyId)
            .OrderByDescending(s => s.CreatedAt)
            .Skip(skip)
            .Take(take);

        return Task.FromResult(submissions);
    }

    public Task<int> CountByPolicyAsync(string policyId, CancellationToken cancellationToken = default)
    {
        var count = _submissions.Values.Count(s => s.PolicyId == policyId);
        return Task.FromResult(count);
    }

    public Task<bool> HasDuplicateAsync(
        string policyId,
        string field,
        string value,
        CancellationToken cancellationToken = default)
    {
        var hasDuplicate = _submissions.Values.Any(s =>
            s.PolicyId == policyId &&
            s.Data.TryGetValue(field, out var existingValue) &&
            string.Equals(existingValue?.ToString(), value, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult(hasDuplicate);
    }

    public Task<string> SaveAsync(JourneySubmission submission, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(submission.Id))
        {
            submission.Id = Guid.NewGuid().ToString();
        }

        _submissions[submission.Id] = submission;
        return Task.FromResult(submission.Id);
    }

    public Task DeleteAsync(string submissionId, CancellationToken cancellationToken = default)
    {
        _submissions.TryRemove(submissionId, out _);
        return Task.CompletedTask;
    }

    public Task<IEnumerable<JourneySubmission>> ExportAsync(
        string policyId,
        DateTime? from = null,
        DateTime? to = null,
        CancellationToken cancellationToken = default)
    {
        var query = _submissions.Values.Where(s => s.PolicyId == policyId);

        if (from.HasValue)
        {
            query = query.Where(s => s.CreatedAt >= from.Value);
        }

        if (to.HasValue)
        {
            query = query.Where(s => s.CreatedAt <= to.Value);
        }

        return Task.FromResult(query.OrderByDescending(s => s.CreatedAt).AsEnumerable());
    }
}

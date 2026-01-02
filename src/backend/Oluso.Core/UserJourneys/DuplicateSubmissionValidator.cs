namespace Oluso.Core.UserJourneys;

/// <summary>
/// Validates that submitted data doesn't create duplicate submissions.
/// </summary>
public class DuplicateSubmissionValidator : IPreCompletionValidator
{
    private readonly IJourneySubmissionStore _submissionStore;
    private readonly string _policyId;
    private readonly IList<string> _duplicateCheckFields;

    public DuplicateSubmissionValidator(
        IJourneySubmissionStore submissionStore,
        string policyId,
        IList<string> duplicateCheckFields)
    {
        _submissionStore = submissionStore;
        _policyId = policyId;
        _duplicateCheckFields = duplicateCheckFields;
    }

    public async Task<string?> ValidateAsync(
        StepExecutionContext context,
        IDictionary<string, object>? outputData,
        CancellationToken cancellationToken = default)
    {
        if (outputData == null || _duplicateCheckFields.Count == 0)
        {
            return null;
        }

        // Only check fields that were just collected AND are in the duplicate check list
        foreach (var field in _duplicateCheckFields)
        {
            if (outputData.TryGetValue(field, out var value) && value != null)
            {
                var hasDuplicate = await _submissionStore.HasDuplicateAsync(
                    _policyId, field, value.ToString() ?? "", cancellationToken);

                if (hasDuplicate)
                {
                    return $"A submission with this {field} already exists.";
                }
            }
        }

        return null;
    }
}

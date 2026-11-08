using Microsoft.Extensions.Logging;
using Oluso.Core.Protocols;
using Oluso.Core.UserJourneys;

namespace Oluso.Protocols;

/// <summary>
/// A user's answer on a consent screen, carried back to <c>/authorize</c> so the same request
/// isn't asked again. Remembered consent is also stored for later requests; this covers the
/// request at hand, including one-time consent and <c>prompt=consent</c>. The proof is a
/// server-side record bound to the user and client and used once: a decision the standalone
/// consent page recorded (<see cref="DecisionParameter"/>), or the completed consent journey
/// itself (<see cref="JourneyParameter"/>).
/// </summary>
public interface IConsentDecisions
{
    /// <summary>Records scopes the user granted; returns the id to send back as <see cref="DecisionParameter"/>.</summary>
    Task<string> RecordAsync(string subjectId, string clientId, IEnumerable<string> scopes, CancellationToken cancellationToken = default);

    /// <summary>The scopes granted for this request, or null when it carries no valid decision.</summary>
    Task<IReadOnlyList<string>?> TakeAsync(IDictionary<string, string>? parameters, string subjectId, string clientId, CancellationToken cancellationToken = default);
}

public sealed class ConsentDecisions : IConsentDecisions
{
    public const string DecisionParameter = "consent_decision";
    public const string JourneyParameter = "consent_journey";

    /// <summary>Journey data written by the consent step: the scopes the user granted.</summary>
    public const string ConsentedScopesDataKey = "consented_scopes";

    private const string DecisionProtocolName = "consent_decision";
    private const string AppliedDataKey = "consent_applied";
    private static readonly TimeSpan DecisionLifetime = TimeSpan.FromMinutes(10);

    private readonly IProtocolStateStore _decisions;
    private readonly IJourneyStateStore? _journeys;
    private readonly ILogger<ConsentDecisions> _logger;

    public ConsentDecisions(IProtocolStateStore decisions, ILogger<ConsentDecisions> logger, IJourneyStateStore? journeys = null)
    {
        _decisions = decisions;
        _journeys = journeys;
        _logger = logger;
    }

    public Task<string> RecordAsync(string subjectId, string clientId, IEnumerable<string> scopes, CancellationToken cancellationToken = default) =>
        _decisions.StoreAsync(new ProtocolState
        {
            ProtocolName = DecisionProtocolName,
            SerializedRequest = string.Empty,
            ClientId = clientId,
            Properties = new Dictionary<string, string>
            {
                ["SubjectId"] = subjectId,
                ["Scopes"] = string.Join(" ", scopes),
            },
        }, DecisionLifetime, cancellationToken);

    public async Task<IReadOnlyList<string>?> TakeAsync(
        IDictionary<string, string>? parameters, string subjectId, string clientId, CancellationToken cancellationToken = default)
    {
        if (parameters == null) return null;

        if (parameters.TryGetValue(DecisionParameter, out var decisionId) && !string.IsNullOrEmpty(decisionId))
        {
            var decision = await _decisions.GetAsync(decisionId, cancellationToken);
            if (decision is { ProtocolName: DecisionProtocolName }
                && decision.ClientId == clientId
                && decision.Properties.TryGetValue("SubjectId", out var subject) && subject == subjectId)
            {
                await _decisions.RemoveAsync(decisionId, cancellationToken);
                return Split(decision.Properties.TryGetValue("Scopes", out var scopes) ? scopes : null);
            }
            _logger.LogWarning("Ignoring consent decision {DecisionId}: unknown, used, or for another user or client", decisionId);
            return null;
        }

        if (_journeys != null && parameters.TryGetValue(JourneyParameter, out var journeyId) && !string.IsNullOrEmpty(journeyId))
        {
            var journey = await _journeys.GetAsync(journeyId, cancellationToken);
            var granted = journey?.Data != null && journey.Data.TryGetValue(ConsentedScopesDataKey, out var value) ? value?.ToString() : null;
            if (journey is { Status: JourneyStatus.Completed } && journey.ClientId == clientId
                && (journey.UserId ?? journey.AuthenticatedUserId) == subjectId
                && !string.IsNullOrEmpty(granted) && journey.Data?.ContainsKey(AppliedDataKey) != true)
            {
                var data = new Dictionary<string, object>(journey.Data!) { [AppliedDataKey] = true };
                await _journeys.SaveAsync(journey with { Data = data }, cancellationToken);
                return Split(granted);
            }
            _logger.LogWarning("Ignoring consent journey {JourneyId}: not completed, already used, or for another user or client", journeyId);
        }

        return null;
    }

    private static IReadOnlyList<string> Split(string? scopes) =>
        (scopes ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries);
}

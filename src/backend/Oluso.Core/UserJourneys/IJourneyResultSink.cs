namespace Oluso.Core.UserJourneys;

/// <summary>
/// Receives the answers of a module's journeys. A journey owned by a module (<see cref="JourneyPolicy.OwnerModule"/>)
/// hands its answers only to the sinks of the module the run belongs to; the platform keeps a record of the run
/// (when, which journey, its status) but never the answers. Register one per module with
/// <c>services.AddScoped&lt;IJourneyResultSink, ...&gt;()</c>.
/// </summary>
public interface IJourneyResultSink
{
    /// <summary>Feature key of the module whose journey runs this sink receives.</summary>
    string Module { get; }

    /// <summary>
    /// Takes the answers of a completed run. Throwing fails the journey instead of completing it, so the answers
    /// are never dropped; the person can submit again.
    /// </summary>
    Task DeliverAsync(JourneyAnswers answers, CancellationToken cancellationToken = default);
}

/// <summary>The answers of one completed run of a module's journey.</summary>
/// <param name="Module">The module the run belongs to: the owner, or for a shared journey the module that started it.</param>
/// <param name="SubmissionId">Id of the platform's answer-free record of the run.</param>
/// <param name="UserId">The signed-in person who submitted, when there was one.</param>
/// <param name="Answers">What the person entered, keyed by field.</param>
/// <param name="LaunchContext">The context the module put on the launch link, when it started the run.</param>
public sealed record JourneyAnswers(
    string Module,
    string? TenantId,
    string PolicyId,
    string PolicyName,
    string JourneyId,
    string SubmissionId,
    string? UserId,
    IReadOnlyDictionary<string, object> Answers,
    IReadOnlyDictionary<string, string>? LaunchContext,
    SubmissionMetadata Metadata,
    DateTime CompletedAt);

/// <summary>Which module a run of a module-owned journey belongs to.</summary>
public static class JourneyRunOwnership
{
    /// <summary>
    /// Journey data key naming the module that started the run, set by whatever starts a journey on a module's
    /// behalf. Only honoured for a <see cref="JourneySharing.Shared"/> journey; a private journey's runs always
    /// belong to its owner.
    /// </summary>
    public const string LaunchingModuleDataKey = "_launching_module";

    /// <summary>The module a run of <paramref name="policy"/> belongs to, or null for a platform journey.</summary>
    public static string? ModuleFor(JourneyPolicy policy, IReadOnlyDictionary<string, object>? journeyData)
    {
        if (!policy.IsModuleOwned) return null;
        if (policy.Sharing == JourneySharing.Shared
            && journeyData != null
            && journeyData.TryGetValue(LaunchingModuleDataKey, out var launcher)
            && launcher?.ToString() is { Length: > 0 } module)
        {
            return module;
        }
        return policy.OwnerModule;
    }
}

/// <summary>Which journeys a module may offer and start.</summary>
public static class JourneyPolicyUse
{
    /// <summary>
    /// True when <paramref name="module"/> may use the journey: a platform journey, one of its own, or another
    /// module's journey that is shared. Another module's private journey never is.
    /// </summary>
    public static bool UsableByModule(this JourneyPolicy policy, string module) =>
        !policy.IsModuleOwned
        || string.Equals(policy.OwnerModule, module, StringComparison.Ordinal)
        || policy.Sharing == JourneySharing.Shared;
}

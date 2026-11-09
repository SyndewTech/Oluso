namespace Oluso.Core.UserJourneys;

/// <summary>
/// The passkey assertion a journey step is waiting on. Steps that offer passkey sign-in (fido2_login, local_login,
/// composite_login) remember the assertion id they created; the response is verified only against that id, never
/// one posted by the page, and the id is taken as it is read, so each assertion completes one step of one journey
/// once. (The FIDO2 service's ceremony store is single-use as well.)
/// </summary>
public static class JourneyPasskeyAssertion
{
    public const string DataKey = "fido2_assertion_id";

    public static void Remember(StepExecutionContext context, string assertionId) => context.SetData(DataKey, assertionId);

    /// <summary>
    /// The assertion id this journey is waiting on, cleared from the journey. Null when none is pending or the page
    /// posted a different one.
    /// </summary>
    public static string? Take(StepExecutionContext context)
    {
        var pending = context.GetDataString(DataKey);
        context.JourneyData.Remove(DataKey);
        context.JourneyData.Remove("fido2_challenge");

        var posted = context.GetInput("assertionId");
        if (string.IsNullOrEmpty(pending)) return null;
        if (!string.IsNullOrEmpty(posted) && !string.Equals(posted, pending, StringComparison.Ordinal)) return null;
        return pending;
    }
}

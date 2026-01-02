using System.Diagnostics;
using OpenTelemetry.Trace;

namespace Oluso.Telemetry.OpenTelemetry;

/// <summary>
/// OpenTelemetry implementation of Oluso tracing
/// </summary>
public class OpenTelemetryTracing : IOlusoTracing
{
    public static readonly ActivitySource ActivitySource = new(
        OlusoActivityNames.SourceName,
        "1.0.0");

    public IDisposable? StartActivity(string name, ActivityKind kind = ActivityKind.Internal)
    {
        return ActivitySource.StartActivity(name, kind);
    }

    public IDisposable? StartTokenActivity(string grantType, string clientId)
    {
        var activity = ActivitySource.StartActivity(
            OlusoActivityNames.TokenEndpoint,
            ActivityKind.Server);

        activity?.SetTag(OlusoTracingTags.GrantType, grantType);
        activity?.SetTag(OlusoTracingTags.ClientId, clientId);

        return activity;
    }

    public IDisposable? StartAuthorizeActivity(string clientId, string responseType)
    {
        var activity = ActivitySource.StartActivity(
            OlusoActivityNames.AuthorizeEndpoint,
            ActivityKind.Server);

        activity?.SetTag(OlusoTracingTags.ClientId, clientId);
        activity?.SetTag(OlusoTracingTags.ResponseType, responseType);

        return activity;
    }

    public IDisposable? StartAuthenticationActivity(string method, string? clientId = null)
    {
        var activity = ActivitySource.StartActivity(
            OlusoActivityNames.LocalLogin,
            ActivityKind.Internal);

        activity?.SetTag(OlusoTracingTags.AuthMethod, method);
        if (clientId != null)
            activity?.SetTag(OlusoTracingTags.ClientId, clientId);

        return activity;
    }

    public IDisposable? StartExternalIdpActivity(string provider)
    {
        var activity = ActivitySource.StartActivity(
            OlusoActivityNames.ExternalLogin,
            ActivityKind.Client);

        activity?.SetTag(OlusoTracingTags.IdpName, provider);

        return activity;
    }

    public IDisposable? StartJourneyActivity(string journeyId, string policyType)
    {
        var activity = ActivitySource.StartActivity(
            OlusoActivityNames.JourneyExecution,
            ActivityKind.Internal);

        activity?.SetTag(OlusoTracingTags.JourneyId, journeyId);
        activity?.SetTag(OlusoTracingTags.JourneyPolicy, policyType);

        return activity;
    }

    public IDisposable? StartJourneyStepActivity(string journeyId, string stepType, string stepId)
    {
        var activity = ActivitySource.StartActivity(
            OlusoActivityNames.JourneyStep,
            ActivityKind.Internal);

        activity?.SetTag(OlusoTracingTags.JourneyId, journeyId);
        activity?.SetTag(OlusoTracingTags.StepType, stepType);
        activity?.SetTag(OlusoTracingTags.StepId, stepId);

        return activity;
    }

    public void SetTag(string key, object? value)
    {
        Activity.Current?.SetTag(key, value);
    }

    public void AddEvent(string name, IDictionary<string, object?>? attributes = null)
    {
        if (Activity.Current == null) return;

        var tags = attributes != null
            ? new ActivityTagsCollection(attributes.Select(kvp =>
                new KeyValuePair<string, object?>(kvp.Key, kvp.Value)))
            : null;

        Activity.Current.AddEvent(new ActivityEvent(name, tags: tags));
    }

    public void RecordException(Exception exception)
    {
        Activity.Current?.AddException(exception);
        Activity.Current?.SetStatus(ActivityStatusCode.Error, exception.Message);
    }

    public void SetStatus(ActivityStatusCode status, string? description = null)
    {
        Activity.Current?.SetStatus(status, description);
    }
}

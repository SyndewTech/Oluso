using System.Text.Json;
using Microsoft.Extensions.Localization;
using Oluso.Core.UserJourneys;
using Oluso.Dto.Journeys;

namespace Oluso.UserJourneys.Headless;

/// <summary>
/// Describes one hosted view as a <see cref="JourneyStepForm"/>. A description posts exactly what its view posts: the
/// same field names, the same hidden values (carried by the actions) and the same button names and values.
/// </summary>
public abstract class StepFormDescriber<TModel> : IJourneyStepFormDescriber where TModel : class
{
    private static readonly JsonSerializerOptions WidgetJson = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    protected StepFormDescriber(IStringLocalizerFactory localizers)
    {
        L = localizers.Create(typeof(StepFormDescriber<>));
    }

    /// <summary>The hosted views' strings, with the tenant's overrides.</summary>
    protected IStringLocalizer L { get; }

    public abstract IReadOnlyCollection<string> Views { get; }

    public JourneyStepForm Describe(JourneyStepDescription step) =>
        Describe(step, step.Model as TModel);

    /// <summary>The view's model is null when the step shows the view without one.</summary>
    protected abstract JourneyStepForm Describe(JourneyStepDescription step, TModel? model);

    protected string T(string key) => L[key].Value;
    protected string T(string key, params object[] args) => L[key, args].Value;

    protected static JourneyFormAction Submit(string label, string name = "__submitted", string value = "true") =>
        new() { Name = name, Value = value, Label = label, Kind = "primary" };

    /// <summary>A submit button with no name of its own: it posts only the fields.</summary>
    protected static JourneyFormAction SubmitFields(string label) =>
        new() { Label = label, Kind = "primary" };

    protected static JourneyFormAction Secondary(string label, string name, string value, bool skipValidation = true) =>
        new() { Name = name, Value = value, Label = label, Kind = "secondary", SkipValidation = skipValidation };

    protected static JourneyFormAction Link(string label, string name, string value) =>
        new() { Name = name, Value = value, Label = label, Kind = "link", SkipValidation = true };

    /// <summary>
    /// A button in a form of its own on the hosted page (resend, use a recovery code, cancel): posts only its own name,
    /// value and <see cref="JourneyFormAction.Values"/>, none of the step's fields.
    /// </summary>
    protected static JourneyFormAction Alone(JourneyFormAction action, string? description = null)
    {
        action.PostsFields = false;
        action.SkipValidation = true;
        action.Description = description ?? action.Description;
        return action;
    }

    protected static JourneyFormAction Cancel(string label, string name, string value) =>
        new() { Name = name, Value = value, Label = label, Kind = "cancel", SkipValidation = true };

    protected static JourneyFormField Hidden(string name, string? value) =>
        new() { Name = name, Type = "hidden", Value = value };

    protected static JourneyFormField Content(string name, string text) =>
        new() { Name = name, Type = "content", Value = text };

    protected static JourneyFormWidget Widget(string type, object? data, string? resultField = null, JourneyFormAction? submit = null) => new()
    {
        Type = type,
        Data = data == null ? null : JsonSerializer.SerializeToElement(data, data.GetType(), WidgetJson),
        ResultField = resultField,
        Submit = submit
    };

    protected static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}

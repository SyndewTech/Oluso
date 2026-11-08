using Oluso.Dto.Journeys;

namespace Oluso.Core.UserJourneys;

/// <summary>A step result a describer turns into a <see cref="JourneyStepForm"/>.</summary>
public sealed record JourneyStepDescription(
    string StepId,
    string StepType,
    string ViewName,
    object? Model,
    IDictionary<string, object>? ViewData);

/// <summary>
/// Describes the steps of one or more hosted views as forms any client can draw. Posting a form's fields with the
/// chosen action's name and value must be exactly the input the hosted view posts, so the step handler can't tell
/// the two apart.
/// </summary>
public interface IJourneyStepFormDescriber
{
    /// <summary>The view names this describer handles, as step handlers return them (e.g. "Journey/_LocalLogin").</summary>
    IReadOnlyCollection<string> Views { get; }

    JourneyStepForm Describe(JourneyStepDescription step);
}

/// <summary>
/// Renders a hosted step view to HTML for clients to show when no describer knows the view (custom pages, plugin
/// views). The HTML is the view's own markup; its form posts to the headless step endpoint.
/// </summary>
public interface IJourneyStepHtmlRenderer
{
    Task<string?> RenderAsync(JourneyStepDescription step, CancellationToken cancellationToken = default);
}

/// <summary>Finds the describer for a step's view.</summary>
public interface IJourneyStepForms
{
    /// <summary>The step as a form, or null when no describer knows its view.</summary>
    JourneyStepForm? Describe(JourneyStepDescription step);
}

public sealed class JourneyStepForms : IJourneyStepForms
{
    private readonly Dictionary<string, IJourneyStepFormDescriber> _byView;

    public JourneyStepForms(IEnumerable<IJourneyStepFormDescriber> describers)
    {
        _byView = new Dictionary<string, IJourneyStepFormDescriber>(StringComparer.Ordinal);
        foreach (var describer in describers)
        {
            foreach (var view in describer.Views)
            {
                _byView[Normalize(view)] = describer;
            }
        }
    }

    public JourneyStepForm? Describe(JourneyStepDescription step) =>
        _byView.TryGetValue(Normalize(step.ViewName), out var describer) ? describer.Describe(step) : null;

    /// <summary>"Journey/_X" and "~/Views/Shared/Journey/_X.cshtml" name the same view.</summary>
    public static string Normalize(string viewName)
    {
        var name = viewName.Replace('\\', '/');
        if (name.StartsWith("~/Views/Shared/", StringComparison.Ordinal)) name = name["~/Views/Shared/".Length..];
        if (name.EndsWith(".cshtml", StringComparison.OrdinalIgnoreCase)) name = name[..^".cshtml".Length];
        return name;
    }
}

public static class JourneyStepFormServiceCollectionExtensions
{
    /// <summary>Registers every <see cref="IJourneyStepFormDescriber"/> in the assembly, next to the views it describes.</summary>
    public static Microsoft.Extensions.DependencyInjection.IServiceCollection AddJourneyStepFormDescribers(
        this Microsoft.Extensions.DependencyInjection.IServiceCollection services, System.Reflection.Assembly assembly)
    {
        foreach (var type in assembly.GetTypes().Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IJourneyStepFormDescriber).IsAssignableFrom(t)))
        {
            Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddEnumerable(
                services, Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Scoped(typeof(IJourneyStepFormDescriber), type));
        }
        return services;
    }
}

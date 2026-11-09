namespace Oluso.Core.UserJourneys;

/// <summary>
/// Model bound by a <c>custom_page</c> step's Razor partial.
/// </summary>
/// <remarks>
/// The partial is authored by the application that hosts the package, not by
/// this library: drop a <c>_{name}.cshtml</c> under
/// <c>Views/Shared/Journey/Templates/</c> in the host app (or a referenced
/// Razor Class Library) and reference it by <c>{name}</c> from a step's
/// <c>templatePath</c> setting. Templates are compiled into the host at build
/// time — there is no runtime template engine — so a partial can use the full
/// Razor/C# surface and bind to this model with
/// <c>@model Oluso.Core.UserJourneys.CustomPageViewModel</c>.
/// </remarks>
public sealed class CustomPageViewModel
{
    /// <summary>
    /// Logical template name from the step's <c>templatePath</c> setting
    /// (without the folder prefix or <c>_</c>/<c>.cshtml</c> decoration).
    /// </summary>
    public required string TemplateName { get; init; }

    /// <summary>
    /// Journey data bag — claims emitted by earlier steps, OIDC state, and any
    /// values mapped in by a prior <c>api_call</c> step. Credential-bearing
    /// keys are removed before the model reaches the view. Values may be nested
    /// dictionaries or lists, so a template can loop over a collection placed
    /// here by an upstream step.
    /// </summary>
    public required IReadOnlyDictionary<string, object> Data { get; init; }

    /// <summary>
    /// Input submitted to this step on a prior render of the same page
    /// (credential keys removed). Empty on first render.
    /// </summary>
    public required IReadOnlyDictionary<string, object> Input { get; init; }

    /// <summary>
    /// Static object configured on the step via its <c>model</c> setting in the
    /// journey policy — labels, copy, feature flags the page author wants to
    /// parameterise the template with. Exposed as <c>Config</c> (not
    /// <c>Model</c>) so a template can read it without colliding with Razor's
    /// own <c>Model</c> keyword.
    /// </summary>
    public required IReadOnlyDictionary<string, object> Config { get; init; }

    public string? UserId { get; init; }

    public string? TenantId { get; init; }

    /// <summary>
    /// Convenience accessor for the string form of a journey-data value, or
    /// <c>null</c> when the key is absent.
    /// </summary>
    public string? Get(string key) =>
        Data.TryGetValue(key, out var value) ? value?.ToString() : null;
}

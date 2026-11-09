using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.UserJourneys;

namespace Oluso.UserJourneys.Steps;

/// <summary>
/// Renders a host-authored Razor partial as a journey screen.
/// </summary>
/// <remarks>
/// The package ships the step, not the page. A consumer drops a
/// <c>_{name}.cshtml</c> partial under <c>Views/Shared/Journey/Templates/</c>
/// in their host app (or a referenced Razor Class Library) before they deploy,
/// then points a step at it:
/// <code>
/// { "type": "custom_page",
///   "settings": { "templatePath": "MyPage", "model": { "title": "Hello" } } }
/// </code>
/// The partial binds to <see cref="CustomPageViewModel"/> and is compiled into
/// the host at build time — no runtime template engine is involved, so there is
/// no per-template compilation cost and no untrusted-template execution surface.
/// </remarks>
public class CustomPageStepHandler : IStepHandler
{
    public string StepType => "custom_page";

    /// <summary>
    /// View-name prefix the <c>templatePath</c> resolves under. A name of
    /// <c>"MyPage"</c> renders the partial <c>Journey/Templates/_MyPage</c>,
    /// found at <c>Views/Shared/Journey/Templates/_MyPage.cshtml</c> via the
    /// standard Razor view engine (host views take precedence over the
    /// library's, so a consumer can also override a bundled sample).
    /// </summary>
    private const string TemplateViewPrefix = "Journey/Templates/_";

    // A journey policy may be edited by a less-trusted admin, so constrain the
    // configured name to a bare token. This keeps templatePath inside the
    // Templates folder — no slashes, dots or traversal that could resolve an
    // arbitrary partial elsewhere in the app.
    private static readonly Regex SafeName = new("^[A-Za-z0-9][A-Za-z0-9_-]*$", RegexOptions.Compiled);

    public Task<StepConfigurationValidationResult> ValidateConfigurationAsync(IDictionary<string, object>? configuration)
    {
        var name = StepConfigurationHelpers.GetString(configuration, "templatePath");
        if (string.IsNullOrWhiteSpace(name))
        {
            return Task.FromResult(StepConfigurationValidationResult.Invalid("templatePath is required"));
        }
        if (!SafeName.IsMatch(name))
        {
            return Task.FromResult(StepConfigurationValidationResult.Invalid(
                "templatePath must be a simple name (letters, digits, '-' or '_'); it maps to Views/Shared/Journey/Templates/_{name}.cshtml"));
        }
        return Task.FromResult(StepConfigurationValidationResult.Valid());
    }

    public Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default)
    {
        var logger = context.ServiceProvider.GetRequiredService<ILogger<CustomPageStepHandler>>();

        var name = context.GetConfig<string?>("templatePath", null);
        if (string.IsNullOrWhiteSpace(name) || !SafeName.IsMatch(name))
        {
            logger.LogError("custom_page step has missing or invalid templatePath '{TemplatePath}'", name);
            return Task.FromResult(StepHandlerResult.Fail("custom_page_config", "Invalid or missing templatePath"));
        }

        var config = context.GetConfig<Dictionary<string, object>?>("model", null);

        // The page renders into the user's browser, so strip credential-bearing
        // keys before they can land in HTML — we don't trust a flow to have
        // kept tokens or OTPs out of journey state.
        var model = new CustomPageViewModel
        {
            TemplateName = name,
            Data = ToReadOnly(SensitiveInputs.RedactCopy(context.JourneyData)),
            Input = ToReadOnly(SensitiveInputs.RedactCopy(context.UserInput)),
            Config = ToReadOnly(config),
            UserId = context.UserId,
            TenantId = context.TenantId
        };

        var viewName = TemplateViewPrefix + name;
        logger.LogDebug("custom_page rendering partial {ViewName}", viewName);
        return Task.FromResult(StepHandlerResult.ShowUi(viewName, model));
    }

    private static IReadOnlyDictionary<string, object> ToReadOnly(IDictionary<string, object>? source) =>
        source is null ? new Dictionary<string, object>() : new Dictionary<string, object>(source);
}

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Razor;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewEngines;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Oluso.Core.UserJourneys;

namespace Oluso.UI.Headless;

/// <summary>
/// Renders a hosted step partial to HTML for headless clients, finding it where the hosted journey page does: under
/// /Views/Shared and under /Pages (class-library views).
/// </summary>
public sealed class RazorStepHtmlRenderer : IJourneyStepHtmlRenderer
{
    private readonly IRazorViewEngine _views;
    private readonly ITempDataProvider _tempData;
    private readonly IHttpContextAccessor _http;
    private readonly ILogger<RazorStepHtmlRenderer> _logger;

    public RazorStepHtmlRenderer(IRazorViewEngine views, ITempDataProvider tempData, IHttpContextAccessor http, ILogger<RazorStepHtmlRenderer> logger)
    {
        _views = views;
        _tempData = tempData;
        _http = http;
        _logger = logger;
    }

    public async Task<string?> RenderAsync(JourneyStepDescription step, CancellationToken cancellationToken = default)
    {
        var httpContext = _http.HttpContext;
        if (httpContext == null) return null;

        var actionContext = new ActionContext(httpContext, httpContext.GetRouteData() ?? new RouteData(), new ActionDescriptor());
        var view = Find(actionContext, step.ViewName);
        if (view == null)
        {
            _logger.LogWarning("Step {StepId} view {View} not found for a headless client", step.StepId, step.ViewName);
            return null;
        }

        var viewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary()) { Model = step.Model };
        if (step.ViewData != null)
        {
            foreach (var (key, value) in step.ViewData) viewData[key] = value;
        }

        await using var writer = new StringWriter();
        var context = new ViewContext(actionContext, view, viewData, new TempDataDictionary(httpContext, _tempData), writer, new HtmlHelperOptions());
        await view.RenderAsync(context);
        return writer.ToString();
    }

    private IView? Find(ActionContext context, string viewName)
    {
        var found = _views.FindView(context, viewName, isMainPage: false);
        if (found.Success) return found.View;

        var name = JourneyStepForms.Normalize(viewName);
        foreach (var path in new[] { $"/Views/Shared/{name}.cshtml", $"/Pages/{name}.cshtml" })
        {
            var byPath = _views.GetView(executingFilePath: null, path, isMainPage: false);
            if (byPath.Success) return byPath.View;
        }
        return null;
    }
}

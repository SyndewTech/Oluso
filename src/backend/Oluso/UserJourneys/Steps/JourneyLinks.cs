using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Oluso.Core.UserJourneys;

namespace Oluso.UserJourneys.Steps;

/// <summary>
/// Links that leave the browser (emailed magic links) must be absolute; they are
/// anchored on the host the journey is running on so they land on the same tenant.
/// </summary>
internal static class JourneyLinks
{
    public static string Absolute(StepExecutionContext context, string path)
    {
        var request = context.ServiceProvider.GetService<IHttpContextAccessor>()?.HttpContext?.Request;
        return request == null ? path : $"{request.Scheme}://{request.Host}{request.PathBase}{path}";
    }
}

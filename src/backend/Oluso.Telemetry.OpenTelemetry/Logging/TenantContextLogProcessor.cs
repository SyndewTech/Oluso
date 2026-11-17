using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using OpenTelemetry;
using OpenTelemetry.Logs;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Telemetry.OpenTelemetry.Logging;

/// <summary>
/// Stamps each log record with tenant + user identifiers from the current
/// request scope. Lets log backends (Loki, OpenObserve, ClickHouse) filter
/// by tenant without callers having to scope every ILogger invocation.
/// A record whose exception wraps another (a DbUpdateException around the
/// database's error) also carries the innermost one's type and message.
/// </summary>
internal sealed class TenantContextLogProcessor : BaseProcessor<LogRecord>
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IServiceProvider _services;

    public TenantContextLogProcessor(
        IHttpContextAccessor httpContextAccessor,
        IServiceProvider services)
    {
        _httpContextAccessor = httpContextAccessor;
        _services = services;
    }

    public const string RootExceptionTypeAttribute = "exception.root_type";
    public const string RootExceptionMessageAttribute = "exception.root_message";

    public override void OnEnd(LogRecord data)
    {
        if (data.Exception?.InnerException is not null)
        {
            var root = data.Exception.GetBaseException();
            data.Attributes = new List<KeyValuePair<string, object?>>(data.Attributes ?? [])
            {
                new(RootExceptionTypeAttribute, root.GetType().FullName),
                new(RootExceptionMessageAttribute, root.Message),
            };
        }

        var http = _httpContextAccessor.HttpContext;
        if (http is null)
        {
            // Background work (timer-driven jobs, hosted services) has no
            // request scope and so no tenant — leave the record untouched.
            return;
        }

        // ITenantContext is resolved from the request-scoped DI container so
        // we get the tenant the request middleware actually parsed, not a
        // root-scope instance.
        var tenant = http.RequestServices.GetService(typeof(ITenantContext)) as ITenantContext;
        var attrs = data.Attributes is null
            ? new List<KeyValuePair<string, object?>>(4)
            : new List<KeyValuePair<string, object?>>(data.Attributes);

        if (!string.IsNullOrEmpty(tenant?.TenantId))
        {
            attrs.Add(new KeyValuePair<string, object?>("tenant.id", tenant.TenantId));
        }

        var user = http.User;
        if (user?.Identity?.IsAuthenticated == true)
        {
            var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? user.FindFirst("sub")?.Value;
            if (!string.IsNullOrEmpty(userId))
            {
                attrs.Add(new KeyValuePair<string, object?>("user.id", userId));
            }
        }

        if (!string.IsNullOrEmpty(http.Request.Path.Value))
        {
            attrs.Add(new KeyValuePair<string, object?>("http.target", http.Request.Path.Value));
        }

        data.Attributes = attrs;
    }
}

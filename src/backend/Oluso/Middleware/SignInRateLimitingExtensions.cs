using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Security;

namespace Oluso.Middleware;

/// <summary>
/// Registers <see cref="SignInRateLimitPolicies"/>. Requests are partitioned by the client IP
/// as resolved by the forwarded-headers middleware (never the raw X-Forwarded-For header, which
/// a caller could vary to dodge the limit). Only POSTs count: rendering a page or reading a
/// session is free; submitting credentials or codes is what is limited.
/// </summary>
public static class SignInRateLimitingExtensions
{
    public static IServiceCollection AddSignInRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var options = configuration.GetSection(SignInRateLimitOptions.SectionName).Get<SignInRateLimitOptions>()
            ?? new SignInRateLimitOptions();

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            limiter.OnRejected = OnRejectedAsync;

            limiter.AddPolicy(SignInRateLimitPolicies.SignIn, http =>
                Partition(http, options.Enabled && HttpMethods.IsPost(http.Request.Method),
                    "sign-in", options.SignInPermitLimit, options.SignInWindow));

            limiter.AddPolicy(SignInRateLimitPolicies.Token, http =>
                Partition(http, options.Enabled, "token", options.TokenPermitLimit, options.TokenWindow));
        });

        return services;
    }

    private static RateLimitPartition<string> Partition(
        HttpContext http, bool limited, string policy, int permitLimit, TimeSpan window)
    {
        if (!limited)
        {
            return RateLimitPartition.GetNoLimiter($"{policy}:unlimited");
        }

        var client = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        return RateLimitPartition.GetFixedWindowLimiter($"{policy}:{client}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit,
            Window = window,
            QueueLimit = 0,
            AutoReplenishment = true
        });
    }

    private static async ValueTask OnRejectedAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var http = context.HttpContext;
        var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var wait)
            ? (int)Math.Ceiling(wait.TotalSeconds)
            : 60;
        http.Response.Headers.RetryAfter = retryAfter.ToString(CultureInfo.InvariantCulture);

        http.RequestServices.GetService<ILoggerFactory>()?.CreateLogger("Oluso.RateLimiting").LogWarning(
            "Rate limit hit for {Path} from {Client}", http.Request.Path, http.Connection.RemoteIpAddress);

        var message = $"Too many attempts. Please wait {retryAfter} seconds and try again.";
        var wantsJson = http.Request.Path.StartsWithSegments("/api")
            || http.Request.Path.StartsWithSegments("/connect")
            || http.Request.Headers.Accept.Any(a => a?.Contains("application/json", StringComparison.OrdinalIgnoreCase) == true);

        if (wantsJson)
        {
            await http.Response.WriteAsJsonAsync(new { error = "too_many_requests", error_description = message }, cancellationToken);
        }
        else
        {
            http.Response.ContentType = "text/plain; charset=utf-8";
            await http.Response.WriteAsync(message, cancellationToken);
        }
    }
}

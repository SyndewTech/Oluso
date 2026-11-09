using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Oluso.Core.Security;
using Oluso.Middleware;
using Xunit;

namespace Oluso.Integration.Tests;

/// <summary>
/// Sign-in surfaces limit credential submissions per client: POSTs over the limit get a 429 with
/// Retry-After, page loads are never limited, and each client has its own budget. A host of its
/// own, so these limits can't spill into other tests.
/// </summary>
public class SignInRateLimitingTests
{
    private static async Task<WebApplication> StartAsync(Dictionary<string, string?> settings)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddRouting();
        builder.Services.AddSignInRateLimiting(builder.Configuration);

        var app = builder.Build();
        app.Use((http, next) =>
        {
            // Stand-in for the forwarded-headers middleware: the client's address.
            if (http.Request.Headers.TryGetValue("X-Test-Client", out var client))
            {
                http.Connection.RemoteIpAddress = IPAddress.Parse(client.ToString());
            }
            return next(http);
        });
        app.UseRouting();
        app.UseRateLimiter();
        app.MapMethods("/sign-in", new[] { "GET", "POST" }, () => Results.Ok()).RequireRateLimiting(SignInRateLimitPolicies.SignIn);
        app.MapPost("/connect/token", () => Results.Ok()).RequireRateLimiting(SignInRateLimitPolicies.Token);

        await app.StartAsync();
        return app;
    }

    private static Dictionary<string, string?> Limits(int signIn, bool enabled = true) => new()
    {
        ["Oluso:RateLimiting:Enabled"] = enabled.ToString(),
        ["Oluso:RateLimiting:SignInPermitLimit"] = signIn.ToString(),
        ["Oluso:RateLimiting:SignInWindow"] = "00:01:00",
        ["Oluso:RateLimiting:TokenPermitLimit"] = "1",
        ["Oluso:RateLimiting:TokenWindow"] = "00:01:00",
    };

    private static Task<HttpResponseMessage> Send(HttpClient http, HttpMethod method, string path, string client)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Test-Client", client);
        return http.SendAsync(request);
    }

    [Fact]
    public async Task Submissions_over_the_limit_are_rejected_with_retry_after()
    {
        await using var app = await StartAsync(Limits(signIn: 2));
        var http = app.GetTestClient();

        (await Send(http, HttpMethod.Post, "/sign-in", "203.0.113.7")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Send(http, HttpMethod.Post, "/sign-in", "203.0.113.7")).StatusCode.Should().Be(HttpStatusCode.OK);
        var rejected = await Send(http, HttpMethod.Post, "/sign-in", "203.0.113.7");

        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        rejected.Headers.RetryAfter.Should().NotBeNull();
    }

    [Fact]
    public async Task Page_loads_are_never_limited()
    {
        await using var app = await StartAsync(Limits(signIn: 1));
        var http = app.GetTestClient();

        for (var i = 0; i < 5; i++)
        {
            (await Send(http, HttpMethod.Get, "/sign-in", "203.0.113.7")).StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }

    [Fact]
    public async Task Each_client_has_its_own_budget()
    {
        await using var app = await StartAsync(Limits(signIn: 1));
        var http = app.GetTestClient();

        (await Send(http, HttpMethod.Post, "/sign-in", "203.0.113.7")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await Send(http, HttpMethod.Post, "/sign-in", "203.0.113.7")).StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await Send(http, HttpMethod.Post, "/sign-in", "198.51.100.4")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Token_requests_have_their_own_limit_and_a_json_rejection()
    {
        await using var app = await StartAsync(Limits(signIn: 10));
        var http = app.GetTestClient();

        (await Send(http, HttpMethod.Post, "/connect/token", "203.0.113.7")).StatusCode.Should().Be(HttpStatusCode.OK);
        var rejected = await Send(http, HttpMethod.Post, "/connect/token", "203.0.113.7");

        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        (await rejected.Content.ReadAsStringAsync()).Should().Contain("too_many_requests");
    }

    [Fact]
    public async Task Limits_can_be_switched_off()
    {
        await using var app = await StartAsync(Limits(signIn: 1, enabled: false));
        var http = app.GetTestClient();

        for (var i = 0; i < 5; i++)
        {
            (await Send(http, HttpMethod.Post, "/sign-in", "203.0.113.7")).StatusCode.Should().Be(HttpStatusCode.OK);
        }
    }
}

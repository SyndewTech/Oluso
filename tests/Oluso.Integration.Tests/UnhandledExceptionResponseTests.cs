using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Oluso.Integration.Tests.Fixtures;
using Xunit;

namespace Oluso.Integration.Tests;

/// <summary>
/// Outside development, an unhandled exception answers 500 with a problem response that carries a trace id and never
/// the exception, and keeps its CORS headers, so a cross-origin admin UI can read it instead of reporting a request
/// blocked by the browser.
/// </summary>
public class UnhandledExceptionResponseTests : IntegrationTestBase
{
    private const string AdminOrigin = "https://admin.example.test";
    private const string ThrowingPath = "/__test/throws";
    private const string Secret = "duplicate key value violates unique constraint \"IX_Secret\"";

    public UnhandledExceptionResponseTests(OlusoWebApplicationFactory factory) : base(factory)
    {
    }

    private sealed class ThrowingPathFilter : IStartupFilter
    {
        /// <summary>After the host's pipeline (so inside its exception handler), where an unmatched path ends up.</summary>
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            next(app);
            app.Use(async (context, nextMiddleware) =>
            {
                if (context.Request.Path == ThrowingPath)
                {
                    throw new InvalidOperationException("Saving failed",
                        new InvalidOperationException(Secret));
                }
                await nextMiddleware();
            });
        };
    }

    [Fact]
    public async Task A_failure_answers_a_problem_without_the_exception_and_keeps_cors()
    {
        var http = Factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("Oluso:Cors:Origins:0", AdminOrigin);
            builder.ConfigureServices(services => services.AddSingleton<IStartupFilter, ThrowingPathFilter>());
        }).CreateClient();
        var request = new HttpRequestMessage(HttpMethod.Get, ThrowingPath);
        request.Headers.Add("Origin", AdminOrigin);

        var response = await http.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Headers.GetValues("Access-Control-Allow-Origin").Should().ContainSingle(AdminOrigin);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().NotContain("unique constraint").And.NotContain("Saving failed").And.NotContain("IX_Secret");
        JsonDocument.Parse(body).RootElement.TryGetProperty("traceId", out _).Should().BeTrue();
    }
}

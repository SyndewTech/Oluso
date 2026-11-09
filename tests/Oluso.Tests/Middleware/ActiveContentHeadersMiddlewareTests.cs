using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Oluso.Middleware;
using Xunit;

namespace Oluso.Tests.Middleware;

/// <summary>
/// The platform origin is also the sign-in origin, so any uploaded document that
/// can carry script (SVG/XML) must be inert when opened directly, and no response
/// may be content-sniffed into something executable.
/// </summary>
public class ActiveContentHeadersMiddlewareTests
{
    private sealed class StartingResponseFeature : HttpResponseFeature
    {
        private readonly List<(Func<object, Task> Callback, object State)> _starting = new();

        public override void OnStarting(Func<object, Task> callback, object state) => _starting.Add((callback, state));

        public async Task StartAsync()
        {
            for (var i = _starting.Count - 1; i >= 0; i--)
                await _starting[i].Callback(_starting[i].State);
        }
    }

    private static async Task<IHeaderDictionary> Run(string? contentType, string? existingPolicy = null, Action<HttpContext>? page = null)
    {
        var feature = new StartingResponseFeature();
        var features = new FeatureCollection();
        features.Set<IHttpResponseFeature>(feature);
        features.Set<IHttpRequestFeature>(new HttpRequestFeature());
        var context = new DefaultHttpContext(features);

        var middleware = new ActiveContentHeadersMiddleware(ctx =>
        {
            ctx.Response.ContentType = contentType;
            if (existingPolicy is not null)
                ctx.Response.Headers.ContentSecurityPolicy = existingPolicy;
            page?.Invoke(ctx);
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);
        await feature.StartAsync();
        return context.Response.Headers;
    }

    [Theory]
    [InlineData("image/svg+xml")]
    [InlineData("IMAGE/SVG+XML; charset=utf-8")]
    [InlineData("application/xhtml+xml")]
    [InlineData("application/xml")]
    [InlineData("text/xml")]
    public async Task Active_xml_documents_are_sandboxed(string contentType)
    {
        var headers = await Run(contentType);

        headers.ContentSecurityPolicy.ToString().Should().Contain("sandbox").And.Contain("default-src 'none'");
        headers.XContentTypeOptions.ToString().Should().Be("nosniff");
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("application/json")]
    [InlineData(null)]
    public async Task Other_responses_get_nosniff_but_no_document_policy(string? contentType)
    {
        var headers = await Run(contentType);

        headers.XContentTypeOptions.ToString().Should().Be("nosniff");
        headers.ContentSecurityPolicy.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task A_response_that_sets_its_own_policy_keeps_it()
    {
        var headers = await Run("image/svg+xml", existingPolicy: "default-src 'self'");

        headers.ContentSecurityPolicy.ToString().Should().Be("default-src 'self'");
    }

    [Theory]
    [InlineData("text/html")]
    [InlineData("TEXT/HTML; charset=utf-8")]
    public async Task A_page_is_framed_only_by_its_own_site(string contentType)
    {
        var headers = await Run(contentType);

        headers.ContentSecurityPolicy.ToString().Should().Be("frame-ancestors 'self'");
        headers.XFrameOptions.ToString().Should().Be("SAMEORIGIN");
    }

    [Fact]
    public async Task A_page_meant_to_be_embedded_is_framed_by_the_origins_it_names_and_nothing_else()
    {
        var headers = await Run("text/html", page: ctx =>
        {
            FramePolicy.Allow(ctx, "https://shop.test, https://evil.test/path, javascript:alert(1), https://shop.test/");
            FramePolicy.AllowOriginOf(ctx, "https://app.test:8443/callback?x=1");
        });

        headers.ContentSecurityPolicy.ToString().Should().Be("frame-ancestors 'self' https://shop.test https://app.test:8443");
        headers.XFrameOptions.ToString().Should().BeEmpty();
    }

    [Fact]
    public async Task A_page_that_sets_its_own_frame_policy_keeps_it()
    {
        var headers = await Run("text/html", existingPolicy: "frame-ancestors https://parent.test");

        headers.ContentSecurityPolicy.ToString().Should().Be("frame-ancestors https://parent.test");
        headers.XFrameOptions.ToString().Should().BeEmpty();
    }
}

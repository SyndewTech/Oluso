using FluentAssertions;
using Oluso.Protocols.Validation;
using Xunit;

namespace Oluso.Tests.Protocols.Validation;

/// <summary>The Matrix client registration rules (Matrix spec, OAuth 2.0 API; MSC2966).</summary>
public class MatrixClientMetadataTests
{
    private static (string Error, string Description)? Check(
        string? type, string? clientUri, params string[] redirects) =>
        MatrixClientMetadata.Validate(type, clientUri, null, null, null, redirects);

    [Theory]
    [InlineData("native", "app.example:/oauth-callback")]
    [InlineData("native", "app.example.mobile:/callback")]
    [InlineData("native", "app.example:callback")]
    [InlineData("native", "http://localhost/callback")]
    [InlineData("native", "http://127.0.0.1/callback")]
    [InlineData("native", "http://[::1]/callback")]
    [InlineData("native", "https://example.app/oauth-callback")]
    [InlineData("web", "https://example.app/auth/oauth-callback")]
    [InlineData("web", "https://client.example.app/auth/oauth-callback")]
    [InlineData(null, "https://example.app/auth/oauth-callback")]
    public void Allowed_redirect_uris_pass(string? type, string redirect)
    {
        Check(type, "https://example.app", redirect).Should().BeNull();
    }

    [Theory]
    [InlineData("native", "app.example://oauth-callback")] // authority
    [InlineData("native", "example:/oauth-callback")] // not the reversed host
    [InlineData("native", "app.exampleX:/oauth-callback")]
    [InlineData("native", "http://localhost:8080/callback")] // port at registration
    [InlineData("native", "http://[::1]:8080/callback")]
    [InlineData("native", "http://example.com/callback")]
    [InlineData("native", "https://evil.example/callback")]
    [InlineData("web", "app.example:/oauth-callback")] // private-use is native only
    [InlineData("web", "http://localhost/callback")] // loopback is native only
    [InlineData("web", "https://notexample.app/callback")]
    [InlineData("web", "https://user:pass@example.app/callback")]
    public void Disallowed_redirect_uris_fail(string type, string redirect)
    {
        var result = Check(type, "https://example.app", redirect);
        result.Should().NotBeNull();
        result!.Value.Error.Should().Be("invalid_redirect_uri");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://example.app")]
    [InlineData("example.app")]
    public void Client_uri_is_required_and_https(string? clientUri)
    {
        Check("web", clientUri, "https://example.app/cb")!.Value.Error.Should().Be("invalid_client_metadata");
    }

    [Fact]
    public void Application_type_must_be_web_or_native()
    {
        Check("desktop", "https://example.app", "https://example.app/cb")!.Value.Error.Should().Be("invalid_client_metadata");
    }

    [Theory]
    [InlineData("https://example.app/logo.png", null, null, true)]
    [InlineData("https://cdn.example.app/logo.png", "https://example.app/privacy", "https://legal.example.app/terms", true)]
    [InlineData("https://elsewhere.example/logo.png", null, null, false)]
    [InlineData(null, "http://example.app/privacy", null, false)]
    [InlineData(null, null, "https://example.app.evil.example/terms", false)]
    public void Logo_policy_and_terms_must_be_https_on_the_client_host(string? logo, string? policy, string? tos, bool ok)
    {
        var result = MatrixClientMetadata.Validate("web", "https://example.app", logo, policy, tos, new[] { "https://example.app/cb" });

        if (ok) result.Should().BeNull();
        else result!.Value.Error.Should().Be("invalid_client_metadata");
    }

    [Fact]
    public void The_reversed_host_is_the_labels_in_reverse()
    {
        MatrixClientMetadata.ReversedHost("example.app").Should().Be("app.example");
        MatrixClientMetadata.ReversedHost("client.example.org").Should().Be("org.example.client");
    }
}

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using Oluso.Integration.Tests.Fixtures;
using Xunit;

namespace Oluso.Integration.Tests;

/// <summary>
/// Request objects end to end (OpenID Connect Core 1.0 section 6): the authorize endpoint uses the parameters in the
/// request object over those outside it, reports a bad request object to the client's redirect URI, and discovery
/// describes what is supported. Registration of jwks and request_object_signing_alg is in DynamicClientRegistrationTests.
/// </summary>
public class RequestObjectTests : IntegrationTestBase
{
    private const string ClientId = "test-client";
    private const string RedirectUri = "http://localhost:5100/signin-oidc";

    public RequestObjectTests(OlusoWebApplicationFactory factory) : base(factory)
    {
    }

    [Fact]
    public async Task Authorize_UsesTheParametersInsideTheRequestObject()
    {
        // prompt=none with no session must come back as login_required on the redirect URI, with the request
        // object's state: both come only from inside the request object.
        var requestObject = Unsigned(new Dictionary<string, object>
        {
            ["client_id"] = ClientId,
            ["response_type"] = "code",
            ["prompt"] = "none",
            ["state"] = "state-from-request-object"
        });

        var location = await AuthorizeAsync(requestObject, outerState: "outer-state");

        location.GetLeftPart(UriPartial.Path).Should().Be(RedirectUri);
        var query = HttpUtility.ParseQueryString(location.Query);
        query["error"].Should().Be("login_required");
        query["state"].Should().Be("state-from-request-object");
    }

    [Fact]
    public async Task Authorize_WithAnUnusableRequestObject_ReportsInvalidRequestObjectToTheClient()
    {
        var requestObject = Unsigned(new Dictionary<string, object> { ["client_id"] = "another-client" });

        var location = await AuthorizeAsync(requestObject, outerState: "outer-state");

        location.GetLeftPart(UriPartial.Path).Should().Be(RedirectUri);
        var query = HttpUtility.ParseQueryString(location.Query);
        query["error"].Should().Be("invalid_request_object");
        query["state"].Should().Be("outer-state");
    }

    [Fact]
    public async Task Discovery_AdvertisesTheRequestObjectSigningAlgorithms()
    {
        using var discovery = JsonDocument.Parse(await Client.GetStringAsync("/.well-known/openid-configuration"));

        var algorithms = discovery.RootElement.GetProperty("request_object_signing_alg_values_supported")
            .EnumerateArray().Select(a => a.GetString()).ToList();
        algorithms.Should().Contain(new[] { "none", "RS256", "PS256", "ES256", "HS256" });
        discovery.RootElement.TryGetProperty("request_object_encryption_alg_values_supported", out _).Should().BeFalse();
    }

    private async Task<Uri> AuthorizeAsync(string requestObject, string outerState)
    {
        var verifier = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var url = "/connect/authorize?" +
            "response_type=code&" +
            $"client_id={ClientId}&" +
            $"redirect_uri={Uri.EscapeDataString(RedirectUri)}&" +
            "scope=openid&" +
            $"state={outerState}&" +
            $"code_challenge={challenge}&code_challenge_method=S256&" +
            $"request={Uri.EscapeDataString(requestObject)}";

        using var client = Factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false
        });
        var response = await client.GetAsync(url);

        response.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = response.Headers.Location!;
        return location.IsAbsoluteUri ? location : new Uri(new Uri("http://localhost"), location);
    }

    private static string Unsigned(Dictionary<string, object> claims)
    {
        static string Segment(object value) => Base64UrlEncoder.Encode(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)));
        return $"{Segment(new { alg = "none" })}.{Segment(claims)}.";
    }
}

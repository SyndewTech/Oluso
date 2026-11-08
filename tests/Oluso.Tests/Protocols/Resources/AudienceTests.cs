using FluentAssertions;
using Microsoft.Extensions.Primitives;
using Moq;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Protocols.Oidc;
using Oluso.Protocols.Services;
using Xunit;

namespace Oluso.Tests.Protocols.Resources;

/// <summary>
/// The access token audience: the resources asked for (RFC 8707), else those owning a requested
/// scope (RFC 9068 §3), else the tenant default, else the client.
/// </summary>
public class AudienceTests
{
    private readonly Mock<IResourceStore> _resources = new();
    private readonly Mock<ITenantSettingsProvider> _settings = new();
    private readonly TenantProtocolSettings _protocol = new();

    public AudienceTests()
    {
        _settings.Setup(s => s.GetProtocolSettingsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_protocol);
        _resources.Setup(r => r.FindResourcesByScopeNamesAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> scopes, CancellationToken _) => Registered()
                .Where(r => r.AllowedScopes.Any(s => scopes.Contains(s.Scope))).ToList());
    }

    private static List<Resource> Registered() => new()
    {
        Api("https://pay.example", "pay_api"),
        Api("https://hr.example", "hr_api", "shared_scope"),
        Api("https://ops.example", "shared_scope"),
    };

    private static Resource Api(string uri, params string[] scopes) => new()
    {
        Uri = uri,
        Enabled = true,
        AllowedScopes = scopes.Select(s => new ResourceScope { Scope = s }).ToList(),
    };

    private Task<ICollection<string>> Resolve(IEnumerable<string>? resources, string[] scopes, params string[] allowed) =>
        new AudienceResolver(_resources.Object, _settings.Object).ResolveAsync(resources, scopes, "app", allowed);

    [Fact]
    public async Task Scopes_spanning_a_resource_and_olusos_own_apis_list_both_audiences()
    {
        _resources.Setup(r => r.FindApiScopesByNameAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> names, CancellationToken _) => names.Where(n => n is "oluso.approvals.read" or "pay_api")
                .Select(n => new ApiScope { Name = n, Enabled = true }).ToList());

        (await Resolve(null, new[] { "openid", "pay_api", "oluso.approvals.read" })).Should().Equal("https://pay.example", "app");
        // Identity scopes and the resource's own scopes add nothing.
        (await Resolve(null, new[] { "openid", "profile", "pay_api" })).Should().Equal("https://pay.example");
        // Stand-alone scopes alone: the client, as before.
        (await Resolve(null, new[] { "oluso.approvals.read" })).Should().Equal("app");
        // Resources asked for are the audience, whatever the scopes.
        (await Resolve(new[] { "https://pay.example" }, new[] { "pay_api", "oluso.approvals.read" })).Should().Equal("https://pay.example");
    }

    [Fact]
    public async Task Requested_resources_are_the_audience()
    {
        (await Resolve(new[] { "https://pay.example" }, new[] { "hr_api" })).Should().Equal("https://pay.example");
    }

    [Fact]
    public async Task Without_a_resource_the_apis_owning_the_requested_scopes_are_the_audience()
    {
        (await Resolve(null, new[] { "openid", "profile", "pay_api" })).Should().Equal("https://pay.example");
        (await Resolve(null, new[] { "shared_scope" })).Should().Equal("https://hr.example", "https://ops.example");
    }

    [Fact]
    public async Task Inference_respects_the_clients_allowed_resources()
    {
        (await Resolve(null, new[] { "shared_scope" }, "https://ops.example")).Should().Equal("https://ops.example");
    }

    [Fact]
    public async Task With_no_api_scope_the_tenant_default_then_the_client_is_the_audience()
    {
        (await Resolve(null, new[] { "openid" })).Should().Equal("app");

        _protocol.DefaultResourceUri = "https://default.example";
        (await Resolve(null, new[] { "openid" })).Should().Equal("https://default.example");
    }

    [Fact]
    public void A_client_may_be_limited_to_some_resources()
    {
        ResourceIndicators.FirstNotAllowed(Array.Empty<string>(), new[] { "https://any.example" }).Should().BeNull();
        ResourceIndicators.FirstNotAllowed(new[] { "https://a.example" }, new[] { "https://a.example", "https://b.example" })
            .Should().Be("https://b.example");
    }

    [Fact]
    public void Repeated_resource_parameters_are_kept_separate_not_comma_joined()
    {
        var values = new StringValues(new[] { "https://a.example", "https://b.example https://a.example" });

        OidcResourceParameters.Read(values).Should().Equal("https://a.example", "https://b.example");
        OidcResourceParameters.Flatten("resource", values).Should().Be("https://a.example https://b.example");
        OidcResourceParameters.Flatten("scope", new StringValues("openid profile")).Should().Be("openid profile");
    }
}

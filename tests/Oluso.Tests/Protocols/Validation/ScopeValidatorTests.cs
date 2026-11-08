using FluentAssertions;
using Moq;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Protocols.Validation;
using Oluso.Protocols.Validation;
using Xunit;

namespace Oluso.Tests.Protocols.Validation;

/// <summary>
/// RFC 6749 §3.3: the server may grant less than the requested scope. Scopes the client isn't allowed or that
/// don't exist are left out (and audited) rather than failing the request, unless nothing is left, openid is left
/// out, a scope token is malformed, or the client is strict.
/// </summary>
public class ScopeValidatorTests
{
    private static readonly string[] Allowed = { "openid", "profile", "email", "offline_access", "api.read", "api.write" };

    private readonly Mock<IResourceStore> _resources = new();
    private readonly List<OlusoEvent> _events = new();
    private readonly Mock<IOlusoEventService> _eventService = new();

    public ScopeValidatorTests()
    {
        _resources.Setup(r => r.GetAllIdentityResourcesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IdentityResource> { new() { Name = "custom.identity" } });
        _resources.Setup(r => r.GetAllApiScopesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ApiScope> { new() { Name = "api.read" }, new() { Name = "api.write" }, new() { Name = "api.admin" } });
        _eventService.Setup(e => e.RaiseAsync(It.IsAny<OlusoEvent>(), It.IsAny<CancellationToken>()))
            .Callback<OlusoEvent, CancellationToken>((evt, _) => _events.Add(evt))
            .Returns(Task.CompletedTask);
    }

    private ScopeValidator Validator() => new(_resources.Object, events: _eventService.Object);

    private Task<ScopeValidationResult> Validate(string scopes, bool strict = false) =>
        Validator().ValidateAsync(new ScopeValidationContext
        {
            RequestedScopes = Validator().ParseScopes(scopes),
            ClientAllowedScopes = Allowed,
            ClientId = "app",
            Endpoint = "authorize",
            Strict = strict
        });

    [Fact]
    public async Task Everything_allowed_and_known_is_granted_in_the_requested_order()
    {
        var result = await Validate("api.write openid profile");

        result.IsValid.Should().BeTrue();
        result.ValidScopes.Should().Equal("api.write", "openid", "profile");
        result.IdentityScopes.Should().BeEquivalentTo("openid", "profile");
        result.ApiScopes.Should().Equal("api.write");
        result.DroppedScopes.Should().BeEmpty();
        _events.Should().BeEmpty();
    }

    [Fact]
    public async Task A_scope_the_client_is_not_allowed_is_left_out()
    {
        var result = await Validate("openid api.read api.admin");

        result.IsValid.Should().BeTrue();
        result.ValidScopes.Should().Equal("openid", "api.read");
        result.DroppedScopes.Should().Equal(new DroppedScope("api.admin", DroppedScope.NotAllowed));
    }

    [Fact]
    public async Task An_unknown_scope_is_left_out()
    {
        // Allowed for the client but defined nowhere (e.g. a deleted API scope).
        var validator = Validator();
        var result = await validator.ValidateAsync(new ScopeValidationContext
        {
            RequestedScopes = new[] { "openid", "gone.scope" },
            ClientAllowedScopes = Allowed.Append("gone.scope"),
        });

        result.IsValid.Should().BeTrue();
        result.ValidScopes.Should().Equal("openid");
        result.DroppedScopes.Should().Equal(new DroppedScope("gone.scope", DroppedScope.Unknown));
    }

    [Fact]
    public async Task Leaving_scopes_out_is_audited_with_each_reason()
    {
        await Validate("openid api.admin no.such.scope");

        var evt = _events.Should().ContainSingle().Which.Should().BeOfType<ScopesDroppedEvent>().Subject;
        evt.ClientId.Should().Be("app");
        evt.Endpoint.Should().Be("authorize");
        evt.RequestedScopes.Should().Equal("openid", "api.admin", "no.such.scope");
        evt.GrantedScopes.Should().Equal("openid");
        evt.DroppedScopes.Should().Equal(new Dictionary<string, string>
        {
            ["api.admin"] = DroppedScope.NotAllowed,
            ["no.such.scope"] = DroppedScope.Unknown,
        });
    }

    [Fact]
    public async Task Nothing_left_is_invalid_scope()
    {
        var result = await Validate("api.admin no.such.scope");

        result.Error.Should().Be("invalid_scope");
        result.ErrorDescription.Should().Contain("api.admin").And.Contain("no.such.scope");
        _events.Should().BeEmpty();
    }

    [Fact]
    public async Task Openid_left_out_fails_the_request()
    {
        var result = await Validator().ValidateAsync(new ScopeValidationContext
        {
            RequestedScopes = new[] { "openid", "api.read" },
            ClientAllowedScopes = new[] { "api.read" },
        });

        result.Error.Should().Be("invalid_scope");
        result.ErrorDescription.Should().Contain("openid");
    }

    [Theory]
    [InlineData("openid \"quoted\"")]
    [InlineData("openid back\\slash")]
    [InlineData("openid tab\tinside")]
    [InlineData("openid é")]
    public async Task A_malformed_scope_token_fails_the_request(string scopes)
    {
        var result = await Validate(scopes);

        result.Error.Should().Be("invalid_scope");
        result.ErrorDescription.Should().StartWith("Malformed");
    }

    [Fact]
    public async Task A_strict_client_is_refused_instead_of_narrowed()
    {
        var result = await Validate("openid api.admin", strict: true);

        result.Error.Should().Be("invalid_scope");
        result.ErrorDescription.Should().Contain("api.admin");
        (await Validate("openid api.read", strict: true)).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Strictness_is_the_strict_scopes_client_property()
    {
        ScopeValidationContext.IsStrict(new[] { new ClientProperty { Key = "strict_scopes", Value = "true" } }).Should().BeTrue();
        ScopeValidationContext.IsStrict(new[] { new ClientProperty { Key = "strict_scopes", Value = "false" } }).Should().BeFalse();
        ScopeValidationContext.IsStrict(new Dictionary<string, string> { ["strict_scopes"] = "True" }).Should().BeTrue();
        ScopeValidationContext.IsStrict(new Dictionary<string, string>()).Should().BeFalse();
    }

    [Fact]
    public async Task No_scope_requested_is_valid_and_empty()
    {
        var result = await Validate("");

        result.IsValid.Should().BeTrue();
        result.ValidScopes.Should().BeEmpty();
    }
}

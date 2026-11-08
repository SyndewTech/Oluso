using FluentAssertions;
using Moq;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols.Validation;
using Oluso.Core.Services;
using Oluso.Protocols.Validation;
using Xunit;

namespace Oluso.Tests.Protocols.Validation;

/// <summary>
/// A scope that requires a tenant feature (ApiScope property <c>required_feature</c>) is issued only while the
/// routed tenant has the feature on (otherwise it is left out of the grant); modules stamp the requirement on the
/// scopes they own.
/// </summary>
public class ScopeFeatureGateTests
{
    private const string Tenant = "tenant-a";

    private readonly List<ApiScope> _scopes = new();
    private readonly HashSet<string> _enabled = new(StringComparer.OrdinalIgnoreCase);
    private readonly Mock<IResourceStore> _resources = new();
    private readonly Mock<ITenantFeatureStore> _features = new();
    private readonly Mock<ITenantContext> _tenant = new();

    public ScopeFeatureGateTests()
    {
        _resources.Setup(r => r.FindApiScopesByNameAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> names, CancellationToken _) => _scopes.Where(s => names.Contains(s.Name)).ToList());
        _resources.Setup(r => r.GetAllApiScopesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => _scopes.ToList());
        _resources.Setup(r => r.GetAllIdentityResourcesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<IdentityResource>());
        _resources.Setup(r => r.AddApiScopeAsync(It.IsAny<ApiScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApiScope s, CancellationToken _) => { _scopes.Add(s); return s; });
        _resources.Setup(r => r.UpdateApiScopeAsync(It.IsAny<ApiScope>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApiScope s, CancellationToken _) => s);
        _features.Setup(f => f.IsFeatureEnabledAsync(Tenant, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string key, CancellationToken _) => _enabled.Contains(key));
        _tenant.SetupGet(t => t.HasTenant).Returns(true);
        _tenant.SetupGet(t => t.TenantId).Returns(Tenant);
    }

    private ScopeFeatureGate Gate() => new(_resources.Object, _tenant.Object, _features.Object);

    private void AddScope(string name, string? feature = null)
    {
        var scope = new ApiScope { Name = name };
        if (feature != null)
            scope.Properties.Add(new ApiScopeProperty { Key = ApiScopeFeatures.RequiredFeatureProperty, Value = feature });
        _scopes.Add(scope);
    }

    [Fact]
    public async Task A_gated_scope_is_unavailable_until_its_feature_is_on()
    {
        AddScope("oluso.approvals.read", "approval");
        AddScope("plain");

        (await Gate().FindUnavailableAsync(new[] { "oluso.approvals.read", "plain", "openid" })).Should().Equal("oluso.approvals.read");

        _enabled.Add("approval");
        (await Gate().FindUnavailableAsync(new[] { "oluso.approvals.read", "plain" })).Should().BeEmpty();
    }

    [Fact]
    public async Task Any_one_of_several_features_will_do()
    {
        AddScope("suite.vault.invoices", "accounting accounting-sme");
        (await Gate().FindUnavailableAsync(new[] { "suite.vault.invoices" })).Should().NotBeEmpty();

        _enabled.Add("accounting-sme");
        (await Gate().FindUnavailableAsync(new[] { "suite.vault.invoices" })).Should().BeEmpty();
    }

    [Fact]
    public async Task Without_a_feature_store_a_gated_scope_is_not_issued()
    {
        AddScope("oluso.calendar.read", "calendar");
        var gate = new ScopeFeatureGate(_resources.Object, _tenant.Object, features: null);
        (await gate.FindUnavailableAsync(new[] { "oluso.calendar.read" })).Should().Equal("oluso.calendar.read");
    }

    [Fact]
    public async Task The_scope_validator_leaves_a_gated_scope_out_of_the_grant()
    {
        AddScope("oluso.approvals.read", "approval");
        var validator = new ScopeValidator(_resources.Object, Gate());

        var narrowed = await validator.ValidateAsync(new[] { "openid", "oluso.approvals.read" }, new[] { "openid", "oluso.approvals.read" });
        narrowed.IsValid.Should().BeTrue();
        narrowed.ValidScopes.Should().Equal("openid");
        narrowed.ApiScopes.Should().BeEmpty();
        narrowed.DroppedScopes.Should().Equal(new DroppedScope("oluso.approvals.read", DroppedScope.FeatureUnavailable));

        _enabled.Add("approval");
        var granted = await validator.ValidateAsync(new[] { "openid", "oluso.approvals.read" }, new[] { "openid", "oluso.approvals.read" });
        granted.ValidScopes.Should().Equal("openid", "oluso.approvals.read");
        granted.DroppedScopes.Should().BeEmpty();
    }

    [Fact]
    public async Task Only_a_gated_scope_requested_is_invalid_scope_and_a_strict_client_is_refused()
    {
        AddScope("oluso.approvals.read", "approval");
        var validator = new ScopeValidator(_resources.Object, Gate());

        var alone = await validator.ValidateAsync(new[] { "oluso.approvals.read" }, new[] { "openid", "oluso.approvals.read" });
        alone.Error.Should().Be("invalid_scope");
        alone.ErrorDescription.Should().Contain("oluso.approvals.read");

        var strict = await validator.ValidateAsync(new ScopeValidationContext
        {
            RequestedScopes = new[] { "openid", "oluso.approvals.read" },
            ClientAllowedScopes = new[] { "openid", "oluso.approvals.read" },
            Strict = true
        });
        strict.Error.Should().Be("invalid_scope");
        strict.ErrorDescription.Should().Contain("not available for this tenant");
    }

    [Fact]
    public async Task Declaring_scopes_stamps_the_feature_on_new_and_existing_ones_idempotently()
    {
        AddScope("oluso.approvals.act"); // declared before scopes carried their feature

        var created = await _resources.Object.EnsureStandaloneApiScopesAsync(new[]
        {
            new StandaloneApiScopeDefinition("oluso.approvals.act", RequiredFeature: "approval"),
            new StandaloneApiScopeDefinition("oluso.approvals.read", RequiredFeature: "approval"),
        });

        created.Should().Equal("oluso.approvals.read");
        _scopes.Should().OnlyContain(s => ApiScopeFeatures.RequiredFeatures(s).SequenceEqual(new[] { "approval" }));
        _scopes.Should().OnlyContain(s => s.Properties.Count == 1);

        (await _resources.Object.EnsureRequiredFeaturesAsync(new[]
        {
            new StandaloneApiScopeDefinition("oluso.approvals.act", RequiredFeature: "approval"),
        })).Should().BeEmpty("the requirement is already in place");
    }

    [Fact]
    public void Declarations_of_one_scope_by_two_features_merge()
    {
        ApiScopeFeatures.Format(new[] { "accounting", "accounting-sme", "ACCOUNTING" }).Should().Be("accounting accounting-sme");
        ApiScopeFeatures.Parse("a, b  c").Should().Equal("a", "b", "c");
        ApiScopeFeatures.Format(Array.Empty<string>()).Should().BeNull();
    }
}

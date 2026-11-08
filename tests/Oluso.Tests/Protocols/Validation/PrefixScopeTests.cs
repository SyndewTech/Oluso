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
/// Prefix scopes (<see cref="ApiScopePrefixes"/>): an ApiScope with property <c>match=prefix</c> stands for every
/// value made of its name and a client-chosen suffix (the fixture uses the Matrix device scope
/// <c>urn:matrix:client:device:&lt;device_id&gt;</c> as one example). The full value is granted when the client is
/// allowed the prefix scope and the suffix is valid: by default any RFC 6749 scope-token characters, at most
/// <see cref="ApiScopePrefixes.DefaultSuffixMaxLength"/> of them, narrowed per scope by <c>suffix_pattern</c> and
/// <c>suffix_max_length</c>.
/// </summary>
public class PrefixScopeTests
{
    private const string DevicePrefix = "urn:matrix:client:device:";
    private const string ApiScope = "urn:matrix:client:api:*";

    private readonly List<ApiScope> _scopes = new();
    private readonly HashSet<string> _enabledFeatures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Mock<IResourceStore> _resources = new();
    private readonly TenantProtocolSettings _protocol = new();
    private readonly Mock<ITenantSettingsProvider> _settings = new();

    public PrefixScopeTests()
    {
        _resources.Setup(r => r.GetAllIdentityResourcesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<IdentityResource>());
        _resources.Setup(r => r.GetAllApiScopesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => _scopes.ToList());
        _resources.Setup(r => r.FindApiScopesByNameAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> names, CancellationToken _) => _scopes.Where(s => names.Contains(s.Name)).ToList());
        _resources.Setup(r => r.FindPrefixApiScopesAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _scopes.Where(ApiScopePrefixes.IsPrefix).ToList());
        _settings.Setup(s => s.GetProtocolSettingsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(() => _protocol);

        AddScope(ApiScope);
        AddScope(DevicePrefix, prefix: true);
    }

    private ApiScope AddScope(string name, bool prefix = false, string? feature = null, string? pattern = null, string? maxLength = null)
    {
        var scope = new ApiScope { Name = name, DisplayName = name + " (display)" };
        if (prefix)
            scope.Properties.Add(new ApiScopeProperty { Key = ApiScopePrefixes.MatchProperty, Value = ApiScopePrefixes.PrefixMatch });
        if (feature != null)
            scope.Properties.Add(new ApiScopeProperty { Key = ApiScopeFeatures.RequiredFeatureProperty, Value = feature });
        if (pattern != null)
            scope.Properties.Add(new ApiScopeProperty { Key = ApiScopePrefixes.SuffixPatternProperty, Value = pattern });
        if (maxLength != null)
            scope.Properties.Add(new ApiScopeProperty { Key = ApiScopePrefixes.SuffixMaxLengthProperty, Value = maxLength });
        _scopes.Add(scope);
        return scope;
    }

    private ScopeValidator Validator()
    {
        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.HasTenant).Returns(true);
        tenant.SetupGet(t => t.TenantId).Returns("t1");
        var features = new Mock<ITenantFeatureStore>();
        features.Setup(f => f.IsFeatureEnabledAsync("t1", It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string key, CancellationToken _) => _enabledFeatures.Contains(key));
        return new ScopeValidator(
            _resources.Object,
            new ScopeFeatureGate(_resources.Object, tenant.Object, features.Object, _settings.Object),
            tenantSettings: _settings.Object);
    }

    private Task<ScopeValidationResult> Validate(string scopes, IEnumerable<string>? allowed = null, bool strict = false) =>
        Validator().ValidateAsync(new ScopeValidationContext
        {
            RequestedScopes = scopes.Split(' '),
            ClientAllowedScopes = allowed ?? new[] { "openid", ApiScope, DevicePrefix },
            Strict = strict,
        });

    [Fact]
    public async Task A_value_under_an_allowed_prefix_scope_is_granted_in_full()
    {
        var result = await Validate($"openid {ApiScope} {DevicePrefix}ABCDEFGHIJ");

        result.IsValid.Should().BeTrue();
        result.ValidScopes.Should().Equal("openid", ApiScope, DevicePrefix + "ABCDEFGHIJ");
        result.ApiScopes.Should().Contain(DevicePrefix + "ABCDEFGHIJ");
        result.DroppedScopes.Should().BeEmpty();
    }

    [Theory]
    [InlineData("a")]
    [InlineData("AbC-123._~xyz")]
    [InlineData("has/slash")]
    [InlineData("has:colon")]
    [InlineData("https://files.example/folder/42?x=1&y=2")]
    [InlineData("has+plus")]
    [InlineData("star*")]
    [InlineData("!#$%&'()*+,-./:;<=>?@[]^_`{|}~")]
    public async Task By_default_any_scope_token_character_is_accepted(string suffix)
    {
        (await Validate($"openid {DevicePrefix}{suffix}")).ValidScopes.Should().Contain(DevicePrefix + suffix);
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("has\"quote")]
    [InlineData("back\\slash")]
    [InlineData("tab\there")]
    [InlineData("caf\u00e9")]
    [InlineData("del\u007f")]
    public void A_suffix_outside_the_scope_token_charset_is_never_valid(string suffix)
    {
        var scope = _scopes.Single(s => s.Name == DevicePrefix);
        ApiScopePrefixes.IsValidSuffix(scope, suffix).Should().BeFalse();

        // The charset is the outer bound: even a pattern that matches anything can't widen it.
        var anything = AddScope("anything:", prefix: true, pattern: ".*");
        ApiScopePrefixes.IsValidSuffix(anything, suffix).Should().BeFalse();
        ApiScopePrefixes.IsValidSuffix(anything, "fine").Should().BeTrue();
    }

    [Theory]
    [InlineData("a\"b")]
    [InlineData("a\\b")]
    public async Task A_requested_value_with_a_quote_or_backslash_is_malformed(string suffix)
    {
        var result = await Validate($"openid {DevicePrefix}{suffix}");

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be("invalid_scope");
    }

    [Fact]
    public async Task A_suffix_longer_than_the_limit_is_left_out()
    {
        (await Validate($"openid {DevicePrefix}{new string('a', ApiScopePrefixes.DefaultSuffixMaxLength)}"))
            .ValidScopes.Should().HaveCount(2);
        (await Validate($"openid {DevicePrefix}{new string('a', ApiScopePrefixes.DefaultSuffixMaxLength + 1)}"))
            .ValidScopes.Should().Equal("openid");
    }

    [Fact]
    public async Task The_suffix_rule_is_configurable_per_scope()
    {
        AddScope("custom:", prefix: true, pattern: "[0-9]+", maxLength: "4");
        var allowed = new[] { "openid", "custom:" };

        (await Validate("openid custom:1234", allowed)).ValidScopes.Should().Contain("custom:1234");
        (await Validate("openid custom:12345", allowed)).ValidScopes.Should().Equal("openid");
        // The pattern is anchored: a partial match isn't enough.
        var partial = await Validate("openid custom:12ab", allowed);
        partial.ValidScopes.Should().Equal("openid");
        partial.DroppedScopes.Should().Equal(new DroppedScope("custom:12ab", DroppedScope.InvalidValue));
    }

    [Fact]
    public async Task A_pattern_like_the_matrix_device_id_advice_narrows_the_default()
    {
        AddScope("dev:", prefix: true, pattern: "[A-Za-z0-9._~-]+");
        var allowed = new[] { "openid", "dev:" };

        (await Validate("openid dev:AbC-123._~xyz", allowed)).ValidScopes.Should().Contain("dev:AbC-123._~xyz");
        (await Validate("openid dev:has/slash", allowed)).ValidScopes.Should().Equal("openid");
        (await Validate("openid dev:has:colon", allowed)).ValidScopes.Should().Equal("openid");
    }

    [Fact]
    public async Task The_matrix_device_pattern_takes_element_x_base64_device_ids()
    {
        AddScope("dev:", prefix: true, pattern: Oluso.Core.Protocols.MatrixScopes.DeviceIdPattern);
        var allowed = new[] { "openid", "dev:" };

        // matrix-rust-sdk signs in with QR as the device named by its unpadded base64 Curve25519 key.
        (await Validate("openid dev:2IZoarIZe3gOMAqdSiFHSAcA15KfOasxueUUNwJI7Ws", allowed)).ValidScopes.Should().HaveCount(2);
        (await Validate("openid dev:a+b/c=", allowed)).ValidScopes.Should().Contain("dev:a+b/c=");
        (await Validate("openid dev:ABCDEFGHIJ", allowed)).ValidScopes.Should().Contain("dev:ABCDEFGHIJ");
        // Not scope-token characters: '"' and '\' (a malformed scope string fails as a whole).
        (await Validate("openid dev:has\"quote", allowed)).ValidScopes.Should().NotContain(s => s.StartsWith("dev:"));
        (await Validate("openid dev:back\\slash", allowed)).ValidScopes.Should().NotContain(s => s.StartsWith("dev:"));
    }

    [Fact]
    public async Task A_longer_maximum_length_than_the_default_is_honoured()
    {
        var longer = ApiScopePrefixes.DefaultSuffixMaxLength + 100;
        AddScope("long:", prefix: true, maxLength: longer.ToString());
        var allowed = new[] { "openid", "long:" };

        (await Validate($"openid long:{new string('a', longer)}", allowed)).ValidScopes.Should().HaveCount(2);
        (await Validate($"openid long:{new string('a', longer + 1)}", allowed)).ValidScopes.Should().Equal("openid");
    }

    [Fact]
    public void A_pattern_that_does_not_compile_accepts_nothing()
    {
        ApiScopePrefixes.TryCompilePattern("[a-", out _).Should().BeFalse();
        ApiScopePrefixes.TryCompilePattern("[a-z]+", out var regex).Should().BeTrue();
        regex!.MatchTimeout.Should().Be(ApiScopePrefixes.PatternMatchTimeout);

        var broken = AddScope("broken:", prefix: true, pattern: "[a-");
        ApiScopePrefixes.IsValidSuffix(broken, "abc").Should().BeFalse();
    }

    [Fact]
    public async Task The_bare_prefix_name_is_not_a_scope_value()
    {
        var result = await Validate($"openid {DevicePrefix}");

        result.ValidScopes.Should().Equal("openid");
        result.DroppedScopes.Should().Equal(new DroppedScope(DevicePrefix, DroppedScope.Unknown));
    }

    [Fact]
    public async Task A_client_not_allowed_the_prefix_scope_does_not_get_its_values()
    {
        var result = await Validate($"openid {DevicePrefix}ABCDEFGHIJ", allowed: new[] { "openid", ApiScope });

        result.ValidScopes.Should().Equal("openid");
        result.DroppedScopes.Should().Equal(new DroppedScope(DevicePrefix + "ABCDEFGHIJ", DroppedScope.NotAllowed));
    }

    [Fact]
    public async Task Allowing_one_value_does_not_allow_the_others()
    {
        var allowed = new[] { "openid", DevicePrefix + "ONLYTHIS" };

        (await Validate($"openid {DevicePrefix}ONLYTHIS", allowed)).ValidScopes.Should().Contain(DevicePrefix + "ONLYTHIS");
        (await Validate($"openid {DevicePrefix}SOMEOTHER", allowed)).ValidScopes.Should().Equal("openid");
    }

    [Fact]
    public async Task Strict_clients_are_refused_an_invalid_suffix()
    {
        var result = await Validate($"openid {DevicePrefix}{new string('a', ApiScopePrefixes.DefaultSuffixMaxLength + 1)}", strict: true);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be("invalid_scope");
        result.ErrorDescription.Should().Contain("Invalid scope value");
    }

    [Fact]
    public async Task A_disabled_prefix_scope_matches_nothing()
    {
        _scopes.Single(s => s.Name == DevicePrefix).Enabled = false;

        (await Validate($"openid {DevicePrefix}ABCDEFGHIJ")).ValidScopes.Should().Equal("openid");
    }

    [Fact]
    public async Task The_feature_gate_applies_to_values_of_a_gated_prefix_scope()
    {
        AddScope("gated:", prefix: true, feature: "matrix");
        var allowed = new[] { "openid", "gated:" };

        var off = await Validate("openid gated:DEVICE", allowed);
        off.ValidScopes.Should().Equal("openid");
        off.DroppedScopes.Should().Equal(new DroppedScope("gated:DEVICE", DroppedScope.FeatureUnavailable));

        _enabledFeatures.Add("matrix");
        (await Validate("openid gated:DEVICE", allowed)).ValidScopes.Should().Contain("gated:DEVICE");
    }

    [Fact]
    public async Task An_exact_scope_that_shares_the_prefix_keeps_its_own_rules()
    {
        // An ordinary scope whose name happens to start with the prefix scope's name: the exact row wins, so it needs
        // its own allowance and its own rules (here a feature gate).
        AddScope(DevicePrefix + "special/one", feature: "special");
        _enabledFeatures.Add("special");

        // Allowed only the prefix scope: the exact scope is not an instance of it.
        var notAllowed = await Validate($"openid {DevicePrefix}special/one");
        notAllowed.DroppedScopes.Should().Equal(new DroppedScope(DevicePrefix + "special/one", DroppedScope.NotAllowed));

        // Allowed by its own name: granted as an ordinary scope.
        var allowed = await Validate($"openid {DevicePrefix}special/one", allowed: new[] { "openid", DevicePrefix + "special/one" });
        allowed.ValidScopes.Should().Equal("openid", DevicePrefix + "special/one");

        // And prefix values still resolve next to it.
        (await Validate($"openid {DevicePrefix}ABCDEFGHIJ")).ValidScopes.Should().Contain(DevicePrefix + "ABCDEFGHIJ");
    }

    [Fact]
    public async Task The_longest_matching_prefix_scope_wins()
    {
        AddScope(DevicePrefix + "sub:", prefix: true, pattern: "[0-9]+");

        var resolved = await ApiScopePrefixes.ResolveAsync(_resources.Object, new[] { DevicePrefix + "sub:42", DevicePrefix + "ABC" });

        resolved[DevicePrefix + "sub:42"].Name.Should().Be(DevicePrefix + "sub:");
        resolved[DevicePrefix + "ABC"].Name.Should().Be(DevicePrefix);
    }

    [Fact]
    public async Task Resolution_maps_each_value_to_its_scope_row_for_claims_and_consent()
    {
        var resolved = await ApiScopePrefixes.ResolveAsync(_resources.Object,
            new[] { ApiScope, DevicePrefix + "ABCDEFGHIJ", DevicePrefix + new string('a', ApiScopePrefixes.DefaultSuffixMaxLength + 1), DevicePrefix, "unknown" });

        resolved.Keys.Should().BeEquivalentTo(ApiScope, DevicePrefix + "ABCDEFGHIJ");
        resolved[DevicePrefix + "ABCDEFGHIJ"].DisplayName.Should().Be(DevicePrefix + " (display)");
    }

    [Fact]
    public async Task Default_scopes_from_AllowedScopes_leave_out_prefix_scope_names()
    {
        (await ApiScopePrefixes.WithoutPrefixScopesAsync(_resources.Object, new[] { ApiScope, DevicePrefix }))
            .Should().Equal(ApiScope);
    }

    [Fact]
    public async Task The_tenant_default_applies_to_a_scope_without_its_own_maximum()
    {
        _protocol.PrefixScopeSuffixMaxLength = 20;

        (await Validate($"openid {DevicePrefix}{new string('a', 20)}")).ValidScopes.Should().HaveCount(2);
        var tooLong = await Validate($"openid {DevicePrefix}{new string('a', 21)}");
        tooLong.ValidScopes.Should().Equal("openid");
        tooLong.DroppedScopes.Should().Equal(new DroppedScope(DevicePrefix + new string('a', 21), DroppedScope.InvalidValue));
    }

    [Fact]
    public async Task A_tenant_default_above_256_lets_longer_suffixes_through()
    {
        _protocol.PrefixScopeSuffixMaxLength = 1000;

        (await Validate($"openid {DevicePrefix}{new string('a', 1000)}")).ValidScopes.Should().HaveCount(2);
        (await Validate($"openid {DevicePrefix}{new string('a', 1001)}")).ValidScopes.Should().Equal("openid");
    }

    [Fact]
    public async Task A_scope_s_own_maximum_wins_over_the_tenant_default()
    {
        _protocol.PrefixScopeSuffixMaxLength = 10;
        AddScope("long:", prefix: true, maxLength: "30");
        AddScope("short:", prefix: true, maxLength: "3");
        var allowed = new[] { "openid", "long:", "short:" };

        (await Validate($"openid long:{new string('a', 30)}", allowed)).ValidScopes.Should().HaveCount(2);
        (await Validate($"openid long:{new string('a', 31)}", allowed)).ValidScopes.Should().Equal("openid");

        _protocol.PrefixScopeSuffixMaxLength = 2048;
        (await Validate("openid short:abc", allowed)).ValidScopes.Should().HaveCount(2);
        (await Validate("openid short:abcd", allowed)).ValidScopes.Should().Equal("openid");
    }

    [Fact]
    public async Task Without_a_tenant_setting_the_default_is_256()
    {
        _protocol.PrefixScopeSuffixMaxLength = null;
        (await ApiScopePrefixes.GetDefaultSuffixMaxLengthAsync(_settings.Object)).Should().Be(256);
        (await ApiScopePrefixes.GetDefaultSuffixMaxLengthAsync(null)).Should().Be(256);

        (await Validate($"openid {DevicePrefix}{new string('a', 256)}")).ValidScopes.Should().HaveCount(2);
        (await Validate($"openid {DevicePrefix}{new string('a', 257)}")).ValidScopes.Should().Equal("openid");
    }

    [Theory]
    [InlineData(null, 256)]
    [InlineData(0, 256)]
    [InlineData(-5, 256)]
    [InlineData(2049, 256)]
    [InlineData(1, 1)]
    [InlineData(2048, 2048)]
    [InlineData(512, 512)]
    public void A_stored_default_outside_the_allowed_range_falls_back_to_256(int? stored, int expected)
    {
        ApiScopePrefixes.EffectiveDefaultSuffixMaxLength(stored).Should().Be(expected);
    }

    [Fact]
    public async Task Resolution_uses_the_default_it_is_given()
    {
        var value = DevicePrefix + new string('a', 300);

        (await ApiScopePrefixes.ResolveAsync(_resources.Object, new[] { value })).Should().BeEmpty();
        (await ApiScopePrefixes.ResolveAsync(_resources.Object, new[] { value }, default, 300)).Keys.Should().Equal(value);
    }
}

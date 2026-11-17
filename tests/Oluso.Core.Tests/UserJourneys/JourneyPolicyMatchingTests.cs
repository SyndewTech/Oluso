using FluentAssertions;
using Oluso.Core.UserJourneys;
using Xunit;

namespace Oluso.Core.Tests.UserJourneys;

/// <summary>
/// Choosing a journey policy by its conditions: one whose conditions the request meets is more specific than one
/// without conditions, so the built-in "Sign In with MFA" (priority 85, acr_values contains mfa) is chosen over the
/// default sign-in (priority 100, no conditions) when the request asks for MFA, and never otherwise.
/// </summary>
public class JourneyPolicyMatchingTests
{
    private readonly InMemoryJourneyPolicyStore _store = new();

    [Fact]
    public async Task A_request_asking_for_mfa_gets_the_mfa_policy_despite_its_lower_priority()
    {
        var policy = await _store.FindMatchingAsync(new JourneyPolicyMatchContext
        {
            Type = JourneyType.SignIn,
            AcrValues = "https://oluso.io/acr/mfa",
        });

        policy!.Id.Should().Be("signin-mfa");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://oluso.io/acr/sfa")]
    public async Task Otherwise_the_policy_without_conditions_runs(string? acrValues)
    {
        var policy = await _store.FindMatchingAsync(new JourneyPolicyMatchContext { Type = JourneyType.SignIn, AcrValues = acrValues });

        policy!.Id.Should().Be("signin");
    }

    [Fact]
    public async Task A_policy_whose_conditions_fail_is_never_chosen()
    {
        await _store.DeleteAsync("signin");

        var policy = await _store.FindMatchingAsync(new JourneyPolicyMatchContext { Type = JourneyType.SignIn });

        policy.Should().BeNull("the only sign-in policy left requires acr_values to ask for mfa");
    }

    [Fact]
    public void A_custom_policy_runs_only_when_asked_for_never_in_place_of_a_typed_one()
    {
        var custom = Policy("custom", 1000);
        custom.Type = JourneyType.Custom;

        JourneyPolicyMatching.Choose([custom], new JourneyPolicyMatchContext { Type = JourneyType.SignIn })
            .Should().BeNull("a custom journey starts by its id or slug, as the database store has always chosen");
    }

    private static JourneyPolicy Policy(string id, int priority, string? tenantId = null, params JourneyPolicyCondition[] conditions) => new()
    {
        Id = id, Slug = id, Name = id, Type = JourneyType.SignIn, Priority = priority, TenantId = tenantId,
        Steps = new List<JourneyPolicyStep>(), Conditions = conditions.Length > 0 ? conditions.ToList() : null,
    };

    private static JourneyPolicyCondition Condition(string type, string op, string value) => new() { Type = type, Operator = op, Value = value };

    [Theory]
    [InlineData("acr_values", "contains", "MFA")]
    [InlineData("AcrValue", "contains", "mfa")]
    [InlineData("acrValues", "ends_with", "/mfa")]
    [InlineData("client_id", "equals", "partner-app")]
    [InlineData("ClientId", "in", "other-app, partner-app")]
    [InlineData("scope", "contains", "payments")]
    [InlineData("tenant_id", "eq", "acme")]
    [InlineData("acr_values", "regex", "acr/(mfa|hwk)$")]
    [InlineData("login_hint", "exists", "")]
    public void Condition_names_and_operators_are_read_the_same_whatever_their_spelling(string type, string op, string value)
    {
        var context = new JourneyPolicyMatchContext
        {
            Type = JourneyType.SignIn, TenantId = "acme", ClientId = "partner-app", AcrValues = "https://oluso.io/acr/mfa",
            Scopes = new[] { "openid", "payments" },
            AdditionalParameters = new Dictionary<string, string> { ["login_hint"] = "jo@acme.test" },
        };

        JourneyPolicyMatching.Matches(new[] { Condition(type, op, value) }, context).Should().BeTrue();
    }

    [Fact]
    public void An_unknown_operator_or_a_bad_pattern_never_matches()
    {
        var context = new JourneyPolicyMatchContext { Type = JourneyType.SignIn, ClientId = "partner-app" };

        JourneyPolicyMatching.Matches(new[] { Condition("client_id", "sounds_like", "partner-app") }, context).Should().BeFalse();
        JourneyPolicyMatching.Matches(new[] { Condition("client_id", "regex", "([") }, context).Should().BeFalse();
    }

    [Fact]
    public void Priority_comes_before_tenant_and_tenant_before_global_at_equal_priority()
    {
        var mfa = Condition("acr_values", "contains", "mfa");
        var context = new JourneyPolicyMatchContext { Type = JourneyType.SignIn, TenantId = "acme", AcrValues = "https://oluso.io/acr/mfa" };

        JourneyPolicyMatching.Choose(new[] { Policy("tenant", 50, "acme", mfa), Policy("global", 90, null, mfa) }, context)!
            .Id.Should().Be("global");
        JourneyPolicyMatching.Choose(new[] { Policy("global", 90, null, mfa), Policy("tenant", 90, "acme", mfa) }, context)!
            .Id.Should().Be("tenant");
        JourneyPolicyMatching.Choose(new[] { Policy("other-tenant", 99, "other", mfa), Policy("default", 1) }, context)!
            .Id.Should().Be("default", "another tenant's policy is never a candidate");
    }

    [Fact]
    public async Task The_in_memory_store_reads_condition_aliases_as_the_database_store_does()
    {
        await _store.SaveAsync(Policy("by-alias", 10, null, Condition("acr_values", "contains", "partner")));

        var policy = await _store.FindMatchingAsync(new JourneyPolicyMatchContext
        {
            Type = JourneyType.SignIn,
            AcrValues = "urn:partner",
        });

        policy!.Id.Should().Be("by-alias");
    }
}

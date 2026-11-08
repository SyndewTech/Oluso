using FluentAssertions;
using Oluso.Core.Conditions;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Xunit;

namespace Oluso.Core.Tests.UserJourneys;

/// <summary>
/// Every source and operator the admin UI offers for a journey condition resolves and
/// evaluates, through the one operator table shared with approval rules.
/// </summary>
public class JourneyConditionsTests
{
    private static JourneyConditionValues Values(
        Dictionary<string, object>? data = null,
        Dictionary<string, IReadOnlyList<string>>? claims = null,
        OlusoUserInfo? user = null) => new()
    {
        JourneyData = data ?? new Dictionary<string, object>(),
        UserId = "u1",
        TenantId = "acme",
        ClientId = "portal",
        Claims = claims == null ? null : new Dictionary<string, IReadOnlyList<string>>(claims, StringComparer.OrdinalIgnoreCase),
        User = user
    };

    [Theory]
    [InlineData("eq")]
    [InlineData("equals")]
    [InlineData("==")]
    [InlineData("in")]
    public void A_multi_valued_claim_matches_when_any_value_does(string op)
    {
        var values = Values(claims: new() { ["role"] = new[] { "Member", "Approver" } });

        ConditionOperators.Evaluate(op, values.Get("claim", "role"), "approver").Should().BeTrue();
        ConditionOperators.Evaluate("ne", values.Get("claim", "roles"), "Approver").Should().BeFalse();
        ConditionOperators.Evaluate("not_in", values.Get("claim", "role"), "Admin, Owner").Should().BeTrue();
    }

    [Fact]
    public void A_claim_not_on_the_user_falls_back_to_claims_earlier_steps_produced()
    {
        var values = Values(
            data: new() { ["idp"] = "AcmeEntra" },
            claims: new() { ["email"] = new[] { "jo@acme.test" } });

        values.Get("claim", "email").Should().Equal("jo@acme.test");
        values.Get("claim", "idp").Should().Equal("AcmeEntra");
    }

    [Fact]
    public void Context_resolves_the_journey_identifiers()
    {
        var values = Values();

        values.Get("context", "userId").Should().Equal("u1");
        values.Get("context", "tenant_id").Should().Equal("acme");
        values.Get("context", "clientId").Should().Equal("portal");
        values.Get("context", "isAuthenticated").Should().Equal("true");
    }

    [Fact]
    public void User_reads_the_profile_and_path_reads_nested_data()
    {
        var values = Values(
            data: new() { ["company"] = new Dictionary<string, object> { ["size"] = "40" } },
            user: new OlusoUserInfo { Id = "u1", Username = "jo", TwoFactorEnabled = true });

        values.Get("user", "two_factor_enabled").Should().Equal("true");
        ConditionOperators.Evaluate("greaterThan", values.Get("path", "company.size"), "10").Should().BeTrue();
    }

    [Theory]
    [InlineData("gt", "2026-03-01", "2026-02-01", true)]
    [InlineData("lessThan", "9", "10", true)]
    [InlineData("gte", "abc", "abd", false)]
    [InlineData("starts_with", "Acme Ltd", "acme", true)]
    [InlineData("notContains", "Acme Ltd", "corp", true)]
    [InlineData("matches", "AB-123", "^ab-\\d+$", true)]
    [InlineData("bogus", "x", "x", false)]
    public void Single_value_operators(string op, string actual, string expected, bool result)
    {
        ConditionOperators.Evaluate(op, new[] { actual }, expected).Should().Be(result);
    }

    [Fact]
    public void Presence_operators_treat_blank_as_missing()
    {
        var blank = Values(data: new() { ["loginHint"] = "" });

        ConditionOperators.Evaluate("exists", blank.Get("data", "loginHint"), null).Should().BeFalse();
        ConditionOperators.Evaluate("not_exists", blank.Get("journeyData", "loginHint"), null).Should().BeTrue();
        ConditionOperators.Evaluate("false", blank.Get("data", "remember_me"), null).Should().BeTrue();
    }

    [Fact]
    public void Approval_expressions_order_dates_through_the_shared_table()
    {
        var evaluator = new DefaultExpressionEvaluator();

        evaluator.Evaluate("SubmittedAt > '2026-01-01'", new Dictionary<string, object?> { ["SubmittedAt"] = "2026-02-01" })
            .Should().BeTrue();
        evaluator.Evaluate("Amount >= 100", new Dictionary<string, object?> { ["Amount"] = 100m }).Should().BeTrue();
        evaluator.Evaluate("RequesterRoles in ('HRAdmin')", new Dictionary<string, object?> { ["RequesterRoles"] = "Staff, HRAdmin" })
            .Should().BeTrue();
    }
}

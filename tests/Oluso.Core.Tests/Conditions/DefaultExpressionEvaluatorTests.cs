using FluentAssertions;
using Oluso.Core.Conditions;
using Xunit;

namespace Oluso.Core.Tests.Conditions;

/// <summary>
/// Tests the platform-shared boolean expression engine that powers both
/// approval-workflow selection (<c>Workflow.Condition</c>) and step skip
/// logic (<c>WorkflowStep.Condition</c>). A bug here regresses every
/// condition-driven feature at once, so the grid below covers each grammar
/// branch — operators, types, AND/OR/NOT, parens, missing properties,
/// malformed input.
///
/// <para>Use these as the canonical reference for what's actually supported.
/// Add a case before adding a feature.</para>
/// </summary>
public class DefaultExpressionEvaluatorTests
{
    private readonly DefaultExpressionEvaluator _evaluator = new();

    // ── Equality + inequality ────────────────────────────────────────────

    [Theory]
    [InlineData("Status == 'Active'", "Active", true)]
    [InlineData("Status == 'Active'", "Inactive", false)]
    [InlineData("Status = 'Active'", "Active", true)] // single-= legacy
    [InlineData("Status != 'Active'", "Inactive", true)]
    [InlineData("Status != 'Active'", "Active", false)]
    public void StringEquality_HitsAndMisses(string expr, string value, bool expected)
    {
        var data = AsData(("Status", value));
        _evaluator.Evaluate(expr, data).Should().Be(expected);
    }

    [Fact]
    public void Equality_IsCaseInsensitive()
    {
        // Matches the prior approval-condition parser's behaviour — tenant
        // config rarely matches exact casing of enum-derived snapshot values.
        _evaluator.Evaluate("Status == 'active'", AsData(("Status", "Active")))
            .Should().BeTrue();
    }

    [Fact]
    public void DoubleAndSingleQuotedValues_BothParse()
    {
        var data = AsData(("Status", "Active"));
        _evaluator.Evaluate("Status == \"Active\"", data).Should().BeTrue();
        _evaluator.Evaluate("Status == 'Active'", data).Should().BeTrue();
    }

    // ── Numeric comparisons ──────────────────────────────────────────────

    [Theory]
    [InlineData("Amount > 100", 150, true)]
    [InlineData("Amount > 100", 100, false)]
    [InlineData("Amount >= 100", 100, true)]
    [InlineData("Amount < 100", 50, true)]
    [InlineData("Amount <= 100", 100, true)]
    [InlineData("Amount <= 100", 101, false)]
    public void NumericComparison_OperatorMatrix(string expr, decimal value, bool expected)
    {
        _evaluator.Evaluate(expr, AsData(("Amount", value))).Should().Be(expected);
    }

    [Fact]
    public void NumericComparison_HandlesFractionalValues()
    {
        // Compare evaluator uses decimal parsing — should round-trip
        // currency-like values exactly. Caller stores cents-as-int elsewhere
        // but plenty of conditions compare percent fields (e.g. tax rates).
        _evaluator.Evaluate("Rate >= 0.07", AsData(("Rate", 0.075m))).Should().BeTrue();
        _evaluator.Evaluate("Rate >= 0.08", AsData(("Rate", 0.075m))).Should().BeFalse();
    }

    [Fact]
    public void NumericComparison_FailsClosedWhenValueIsNotNumeric()
    {
        // "Status > 5" against a string field — decimal.TryParse fails, the
        // comparison returns false. Matches existing semantics (no exception
        // bubbles up to the runtime).
        _evaluator.Evaluate("Status > 5", AsData(("Status", "Active"))).Should().BeFalse();
    }

    // ── Boolean fields ──────────────────────────────────────────────────

    [Theory]
    [InlineData("IsBillable == true", true, true)]
    [InlineData("IsBillable == false", true, false)]
    [InlineData("IsBillable != false", true, true)]
    [InlineData("IsBillable == true", false, false)]
    public void BooleanField_HandledViaFastPath(string expr, bool value, bool expected)
    {
        _evaluator.Evaluate(expr, AsData(("IsBillable", value))).Should().Be(expected);
    }

    [Fact]
    public void BooleanField_StringifiedBoolean_AlsoParses()
    {
        // Some context enrichers stamp values as strings ("true"/"false").
        // The fallback bool.TryParse on actual.ToString() handles that.
        _evaluator.Evaluate("HasManager == true", AsData(("HasManager", "true")))
            .Should().BeTrue();
    }

    // ── contains / !contains ────────────────────────────────────────────

    [Theory]
    [InlineData("Notes contains 'urgent'", "This is URGENT please review", true)]
    [InlineData("Notes contains 'urgent'", "Regular note", false)]
    [InlineData("Notes !contains 'urgent'", "Regular note", true)]
    [InlineData("Notes !contains 'urgent'", "URGENT case", false)]
    public void ContainsOperator_IsCaseInsensitive(string expr, string value, bool expected)
    {
        _evaluator.Evaluate(expr, AsData(("Notes", value))).Should().Be(expected);
    }

    // ── AND / OR / NOT / parens ─────────────────────────────────────────

    [Fact]
    public void And_BothSidesMustBeTrue()
    {
        var data = AsData(("Status", "Active"), ("Amount", 200m));
        _evaluator.Evaluate("Status == 'Active' AND Amount > 100", data).Should().BeTrue();
        _evaluator.Evaluate("Status == 'Active' AND Amount > 500", data).Should().BeFalse();
        _evaluator.Evaluate("Status == 'Closed' AND Amount > 100", data).Should().BeFalse();
    }

    [Fact]
    public void Or_EitherSideTrueWins()
    {
        var data = AsData(("Status", "Closed"), ("Amount", 200m));
        _evaluator.Evaluate("Status == 'Active' OR Amount > 100", data).Should().BeTrue();
        _evaluator.Evaluate("Status == 'Closed' OR Amount > 500", data).Should().BeTrue();
        _evaluator.Evaluate("Status == 'Active' OR Amount > 500", data).Should().BeFalse();
    }

    [Fact]
    public void Not_FlipsResult()
    {
        var data = AsData(("Status", "Active"));
        _evaluator.Evaluate("NOT Status == 'Active'", data).Should().BeFalse();
        _evaluator.Evaluate("NOT Status == 'Closed'", data).Should().BeTrue();
    }

    [Fact]
    public void And_HasHigherPrecedenceThanOr()
    {
        // A OR B AND C  ≡  A OR (B AND C). Without precedence wiring this
        // would be misread as (A OR B) AND C and the assertion would flip.
        var data = AsData(("A", "no"), ("B", "yes"), ("C", "no"));
        _evaluator.Evaluate("A == 'yes' OR B == 'yes' AND C == 'yes'", data)
            .Should().BeFalse();
    }

    [Fact]
    public void Parens_OverridePrecedence()
    {
        var data = AsData(("A", "no"), ("B", "yes"), ("C", "no"));
        // Forcing OR-then-AND with parens.
        _evaluator.Evaluate("(A == 'yes' OR B == 'yes') AND C == 'no'", data)
            .Should().BeTrue();
    }

    [Fact]
    public void NestedParens_AndNots_CompoundCorrectly()
    {
        // Realistic shape — "branch memo from PH branch above threshold, but
        // not the chairman bypass" — that the hospital demo workflow uses.
        var data = AsData(
            ("Origin", "Branch"),
            ("BranchCode", "PH"),
            ("Amount", 50000m),
            ("ChairmanBypass", false));

        const string expr =
            "Origin == 'Branch' AND (BranchCode == 'PH' OR BranchCode == 'LAGOS') " +
            "AND Amount >= 10000 AND NOT ChairmanBypass == true";

        _evaluator.Evaluate(expr, data).Should().BeTrue();
    }

    // ── Case-insensitive keywords ────────────────────────────────────────

    [Theory]
    [InlineData("Status == 'Active' and Amount > 100")]
    [InlineData("Status == 'Active' And Amount > 100")]
    [InlineData("Status == 'Active' AND Amount > 100")]
    public void AndKeyword_IsCaseInsensitive(string expr)
    {
        var data = AsData(("Status", "Active"), ("Amount", 200m));
        _evaluator.Evaluate(expr, data).Should().BeTrue();
    }

    // ── Missing properties ──────────────────────────────────────────────

    [Fact]
    public void MissingProperty_EvaluatesToFalseAndFiresOnMissingHook()
    {
        var missing = new List<string>();
        _evaluator.Evaluate(
            "Status == 'Active'",
            AsData(("Other", "x")),
            onMissing: name => missing.Add(name))
            .Should().BeFalse();

        // The hook lets approval-service log warnings on typos / missing
        // enrichers. If this stops firing, condition typos go silent.
        missing.Should().ContainSingle().And.Contain("Status");
    }

    [Fact]
    public void MissingProperty_InOrBranch_TakesTheOtherBranch()
    {
        // "(MissingProp == 'x') OR (Status == 'Active')" — the missing prop
        // arm is false, but OR with a true arm still returns true. Verifies
        // a missing prop doesn't poison the whole expression.
        var data = AsData(("Status", "Active"));
        _evaluator.Evaluate("MissingProp == 'x' OR Status == 'Active'", data)
            .Should().BeTrue();
    }

    // ── Malformed expressions ───────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("garbage")]
    [InlineData("Status ==")]                 // missing value
    [InlineData("Status == 'unterminated")]   // unterminated string
    [InlineData("Status == 'x' AND")]         // dangling AND
    [InlineData("(Status == 'x'")]            // unbalanced parens
    public void Malformed_ReturnsFalseInsteadOfThrowing(string expr)
    {
        // Failing closed is the contract — approval runtime must never crash
        // on a tenant-authored condition string. Surface defects via logs,
        // not 500s.
        _evaluator.Evaluate(expr, AsData(("Status", "Active"))).Should().BeFalse();
    }

    // ── ExtractReferencedProperties ─────────────────────────────────────

    [Fact]
    public void ExtractReferencedProperties_ReturnsEveryIdentifierExactlyOnce()
    {
        var props = _evaluator.ExtractReferencedProperties(
            "Status == 'Active' AND (Amount > 100 OR Notes contains 'urgent') AND Status != 'Archived'");

        props.Should().BeEquivalentTo(new[] { "Status", "Amount", "Notes" });
    }

    [Fact]
    public void ExtractReferencedProperties_OnMalformed_ReturnsEmpty()
    {
        // The save-time validator on Workflows uses this to confirm referenced
        // properties exist for the entity type. On malformed input we return
        // empty rather than throw so the validator can flag the parse error
        // separately without two failure modes for the same bad input.
        _evaluator.ExtractReferencedProperties("garbage ==").Should().BeEmpty();
    }

    [Fact]
    public void ExtractReferencedProperties_HandlesDottedIdentifiers()
    {
        // Approval enrichers stamp dotted keys (e.g. Requester.WorkLocation.Code)
        // — extraction must keep them intact so validation can match the
        // entity-type's declared property list.
        _evaluator.ExtractReferencedProperties("Requester.WorkLocationId == 'lagos-uuid'")
            .Should().ContainSingle()
            .Which.Should().Be("Requester.WorkLocationId");
    }

    // ── Realistic approval-workflow conditions ──────────────────────────

    [Fact]
    public void Workflow_HighValueExpense_Condition()
    {
        // Picks a workflow when total > 5000 AND the requester isn't from
        // Finance themselves (so self-approval doesn't kick in).
        const string expr = "TotalAmount > 5000 AND RequesterDepartmentName != 'Finance'";

        _evaluator.Evaluate(expr, AsData(
            ("TotalAmount", 7500m),
            ("RequesterDepartmentName", "Engineering"))).Should().BeTrue();

        _evaluator.Evaluate(expr, AsData(
            ("TotalAmount", 4999m),
            ("RequesterDepartmentName", "Engineering"))).Should().BeFalse();

        _evaluator.Evaluate(expr, AsData(
            ("TotalAmount", 9999m),
            ("RequesterDepartmentName", "Finance"))).Should().BeFalse();
    }

    [Fact]
    public void Step_SkipWhen_HRRequestFromManager()
    {
        // Skip the direct-manager step when the requester is already a manager —
        // common pattern to avoid asking someone to approve their own subordinate
        // when the approver chain would loop back.
        const string expr = "RequesterJobTitle contains 'Manager' OR RequesterJobTitle contains 'Director'";

        _evaluator.Evaluate(expr, AsData(("RequesterJobTitle", "Senior Manager")))
            .Should().BeTrue();
        _evaluator.Evaluate(expr, AsData(("RequesterJobTitle", "Engineer")))
            .Should().BeFalse();
    }

    [Fact]
    public void Workflow_BranchOrigin_RoutesToBranchVariant()
    {
        // Hospital memo demo: same entity (Memo), three workflow definitions
        // each conditioned on Origin so the right chain runs.
        const string expr = "Origin == 'Branch'";

        _evaluator.Evaluate(expr, AsData(("Origin", "Branch"))).Should().BeTrue();
        _evaluator.Evaluate(expr, AsData(("Origin", "HeadOffice"))).Should().BeFalse();
        _evaluator.Evaluate(expr, AsData(("Origin", "SpecialisedGroup"))).Should().BeFalse();
    }

    // ── in / !in ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Status in ('Active', 'Pending')", "Active", true)]
    [InlineData("Status in ('Active', 'Pending')", "pending", true)]
    [InlineData("Status in ('Active', 'Pending')", "Closed", false)]
    [InlineData("Status in [\"Active\", \"Pending\"]", "Pending", true)]
    [InlineData("Status in (Active, Pending)", "Active", true)]
    [InlineData("Status !in ('Active', 'Pending')", "Closed", true)]
    [InlineData("Status !in ('Active', 'Pending')", "Active", false)]
    public void In_MatchesAnyListedValue(string expr, string value, bool expected)
    {
        _evaluator.Evaluate(expr, AsData(("Status", value))).Should().Be(expected);
    }

    [Fact]
    public void In_AgainstCsvProperty_MatchesWhenAnyHeldValueIsListed()
    {
        const string expr = "RequesterRoles in ('HRAdmin', 'ITAdmin')";

        _evaluator.Evaluate(expr, AsData(("RequesterRoles", "Staff,ITAdmin"))).Should().BeTrue();
        _evaluator.Evaluate(expr, AsData(("RequesterRoles", "Staff, Finance"))).Should().BeFalse();
        _evaluator.Evaluate(expr, AsData(("RequesterRoles", new[] { "HRAdmin" }))).Should().BeTrue();
        _evaluator.Evaluate(expr, AsData(("RequesterRoles", ""))).Should().BeFalse();
    }

    [Fact]
    public void In_ComposesWithAndOr()
    {
        const string expr = "RequesterRoles in ('HRAdmin', 'ITAdmin') AND (Amount > 100 OR Priority == 'High')";

        _evaluator.Evaluate(expr, AsData(("RequesterRoles", "ITAdmin"), ("Amount", 50), ("Priority", "High"))).Should().BeTrue();
        _evaluator.Evaluate(expr, AsData(("RequesterRoles", "ITAdmin"), ("Amount", 50), ("Priority", "Low"))).Should().BeFalse();
        _evaluator.Evaluate(expr, AsData(("RequesterRoles", "Staff"), ("Amount", 500), ("Priority", "High"))).Should().BeFalse();
    }

    [Theory]
    [InlineData("Status in ()")]
    [InlineData("Status in ('A'")]
    [InlineData("Status in 'A'")]
    [InlineData("Status in ('A' 'B')")]
    public void In_MalformedLists_FailClosed(string expr)
    {
        _evaluator.Evaluate(expr, AsData(("Status", "A"))).Should().BeFalse();
    }

    [Fact]
    public void In_ReportsReferencedProperty()
    {
        _evaluator.ExtractReferencedProperties("RequesterTeamCodes in ('LAGOS', 'ABUJA') AND Amount > 1")
            .Should().BeEquivalentTo("RequesterTeamCodes", "Amount");
    }

    // ── Helpers ─────────────────────────────────────────────────────────

    private static IReadOnlyDictionary<string, object?> AsData(
        params (string Key, object? Value)[] pairs)
    {
        var dict = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (k, v) in pairs) dict[k] = v;
        return dict;
    }
}

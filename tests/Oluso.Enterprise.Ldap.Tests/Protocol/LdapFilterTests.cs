using FluentAssertions;
using Oluso.Enterprise.Ldap.Server.Protocol;
using Xunit;

namespace Oluso.Enterprise.Ldap.Tests.Protocol;

/// <summary>
/// Unit tests for LDAP filter matching (RFC 4515).
/// </summary>
public class LdapFilterTests
{
    #region EqualityFilter Tests

    [Fact]
    public void EqualityFilter_WithExactMatch_ReturnsTrue()
    {
        var filter = new EqualityFilter
        {
            AttributeDescription = "cn",
            AssertionValue = "John Doe"
        };

        var attributes = CreateAttributes(("cn", "John Doe"));

        filter.Matches(attributes).Should().BeTrue();
    }

    [Fact]
    public void EqualityFilter_CaseInsensitive_ReturnsTrue()
    {
        var filter = new EqualityFilter
        {
            AttributeDescription = "cn",
            AssertionValue = "JOHN DOE"
        };

        var attributes = CreateAttributes(("cn", "john doe"));

        filter.Matches(attributes).Should().BeTrue();
    }

    [Fact]
    public void EqualityFilter_AttributeNameCaseInsensitive_ReturnsTrue()
    {
        var filter = new EqualityFilter
        {
            AttributeDescription = "CN",
            AssertionValue = "John Doe"
        };

        var attributes = CreateAttributes(("cn", "John Doe"));

        filter.Matches(attributes).Should().BeTrue();
    }

    [Fact]
    public void EqualityFilter_WithNoMatch_ReturnsFalse()
    {
        var filter = new EqualityFilter
        {
            AttributeDescription = "cn",
            AssertionValue = "John Doe"
        };

        var attributes = CreateAttributes(("cn", "Jane Doe"));

        filter.Matches(attributes).Should().BeFalse();
    }

    [Fact]
    public void EqualityFilter_WithMissingAttribute_ReturnsFalse()
    {
        var filter = new EqualityFilter
        {
            AttributeDescription = "cn",
            AssertionValue = "John Doe"
        };

        var attributes = CreateAttributes(("sn", "Doe"));

        filter.Matches(attributes).Should().BeFalse();
    }

    [Fact]
    public void EqualityFilter_WithMultipleValues_MatchesAny()
    {
        var filter = new EqualityFilter
        {
            AttributeDescription = "mail",
            AssertionValue = "john@work.com"
        };

        var attributes = new Dictionary<string, List<string>>
        {
            ["mail"] = new() { "john@home.com", "john@work.com" }
        };

        filter.Matches(attributes).Should().BeTrue();
    }

    #endregion

    #region SubstringFilter Tests

    [Fact]
    public void SubstringFilter_Initial_MatchesPrefix()
    {
        var filter = new SubstringFilter
        {
            AttributeDescription = "cn",
            Initial = "John"
        };

        var attributes = CreateAttributes(("cn", "John Doe"));

        filter.Matches(attributes).Should().BeTrue();
    }

    [Fact]
    public void SubstringFilter_Initial_NoMatch_ReturnsFalse()
    {
        var filter = new SubstringFilter
        {
            AttributeDescription = "cn",
            Initial = "Jane"
        };

        var attributes = CreateAttributes(("cn", "John Doe"));

        filter.Matches(attributes).Should().BeFalse();
    }

    [Fact]
    public void SubstringFilter_Final_MatchesSuffix()
    {
        var filter = new SubstringFilter
        {
            AttributeDescription = "cn",
            Final = "Doe"
        };

        var attributes = CreateAttributes(("cn", "John Doe"));

        filter.Matches(attributes).Should().BeTrue();
    }

    [Fact]
    public void SubstringFilter_Final_NoMatch_ReturnsFalse()
    {
        var filter = new SubstringFilter
        {
            AttributeDescription = "cn",
            Final = "Smith"
        };

        var attributes = CreateAttributes(("cn", "John Doe"));

        filter.Matches(attributes).Should().BeFalse();
    }

    [Fact]
    public void SubstringFilter_Any_MatchesMiddle()
    {
        var filter = new SubstringFilter
        {
            AttributeDescription = "cn",
            Any = new List<string> { "ohn" }
        };

        var attributes = CreateAttributes(("cn", "John Doe"));

        filter.Matches(attributes).Should().BeTrue();
    }

    [Fact]
    public void SubstringFilter_CombinedPattern_Matches()
    {
        // Pattern: J*o*e (starts with J, contains o, ends with e)
        var filter = new SubstringFilter
        {
            AttributeDescription = "cn",
            Initial = "J",
            Any = new List<string> { "o" },
            Final = "e"
        };

        var attributes = CreateAttributes(("cn", "John Doe"));

        filter.Matches(attributes).Should().BeTrue();
    }

    [Fact]
    public void SubstringFilter_CombinedPattern_NoMatch()
    {
        var filter = new SubstringFilter
        {
            AttributeDescription = "cn",
            Initial = "J",
            Any = new List<string> { "x" },
            Final = "e"
        };

        var attributes = CreateAttributes(("cn", "John Doe"));

        filter.Matches(attributes).Should().BeFalse();
    }

    [Fact]
    public void SubstringFilter_MultipleAny_AllMustMatch()
    {
        var filter = new SubstringFilter
        {
            AttributeDescription = "mail",
            Any = new List<string> { "john", "@", ".com" }
        };

        var attributes = CreateAttributes(("mail", "john.doe@example.com"));

        filter.Matches(attributes).Should().BeTrue();
    }

    [Fact]
    public void SubstringFilter_CaseInsensitive()
    {
        var filter = new SubstringFilter
        {
            AttributeDescription = "cn",
            Initial = "JOHN"
        };

        var attributes = CreateAttributes(("cn", "john doe"));

        filter.Matches(attributes).Should().BeTrue();
    }

    #endregion

    #region AndFilter Tests

    [Fact]
    public void AndFilter_AllMatch_ReturnsTrue()
    {
        var filter = new AndFilter
        {
            Filters = new List<LdapFilter>
            {
                new EqualityFilter { AttributeDescription = "cn", AssertionValue = "John Doe" },
                new EqualityFilter { AttributeDescription = "mail", AssertionValue = "john@example.com" }
            }
        };

        var attributes = CreateAttributes(
            ("cn", "John Doe"),
            ("mail", "john@example.com")
        );

        filter.Matches(attributes).Should().BeTrue();
    }

    [Fact]
    public void AndFilter_OneFails_ReturnsFalse()
    {
        var filter = new AndFilter
        {
            Filters = new List<LdapFilter>
            {
                new EqualityFilter { AttributeDescription = "cn", AssertionValue = "John Doe" },
                new EqualityFilter { AttributeDescription = "mail", AssertionValue = "wrong@example.com" }
            }
        };

        var attributes = CreateAttributes(
            ("cn", "John Doe"),
            ("mail", "john@example.com")
        );

        filter.Matches(attributes).Should().BeFalse();
    }

    [Fact]
    public void AndFilter_EmptyFilters_ReturnsTrue()
    {
        var filter = new AndFilter { Filters = new List<LdapFilter>() };

        var attributes = CreateAttributes(("cn", "Anyone"));

        filter.Matches(attributes).Should().BeTrue();
    }

    #endregion

    #region OrFilter Tests

    [Fact]
    public void OrFilter_AnyMatch_ReturnsTrue()
    {
        var filter = new OrFilter
        {
            Filters = new List<LdapFilter>
            {
                new EqualityFilter { AttributeDescription = "cn", AssertionValue = "John Doe" },
                new EqualityFilter { AttributeDescription = "cn", AssertionValue = "Jane Doe" }
            }
        };

        var attributes = CreateAttributes(("cn", "John Doe"));

        filter.Matches(attributes).Should().BeTrue();
    }

    [Fact]
    public void OrFilter_AllFail_ReturnsFalse()
    {
        var filter = new OrFilter
        {
            Filters = new List<LdapFilter>
            {
                new EqualityFilter { AttributeDescription = "cn", AssertionValue = "John Doe" },
                new EqualityFilter { AttributeDescription = "cn", AssertionValue = "Jane Doe" }
            }
        };

        var attributes = CreateAttributes(("cn", "Bob Smith"));

        filter.Matches(attributes).Should().BeFalse();
    }

    [Fact]
    public void OrFilter_EmptyFilters_ReturnsFalse()
    {
        var filter = new OrFilter { Filters = new List<LdapFilter>() };

        var attributes = CreateAttributes(("cn", "Anyone"));

        filter.Matches(attributes).Should().BeFalse();
    }

    #endregion

    #region NotFilter Tests

    [Fact]
    public void NotFilter_InvertsTrue()
    {
        var filter = new NotFilter
        {
            Filter = new EqualityFilter { AttributeDescription = "cn", AssertionValue = "John Doe" }
        };

        var attributes = CreateAttributes(("cn", "Jane Doe"));

        filter.Matches(attributes).Should().BeTrue();
    }

    [Fact]
    public void NotFilter_InvertsFalse()
    {
        var filter = new NotFilter
        {
            Filter = new EqualityFilter { AttributeDescription = "cn", AssertionValue = "John Doe" }
        };

        var attributes = CreateAttributes(("cn", "John Doe"));

        filter.Matches(attributes).Should().BeFalse();
    }

    #endregion

    #region PresentFilter Tests

    [Fact]
    public void PresentFilter_WithExistingAttribute_ReturnsTrue()
    {
        var filter = new PresentFilter { AttributeDescription = "mail" };

        var attributes = CreateAttributes(("mail", "john@example.com"));

        filter.Matches(attributes).Should().BeTrue();
    }

    [Fact]
    public void PresentFilter_WithMissingAttribute_ReturnsFalse()
    {
        var filter = new PresentFilter { AttributeDescription = "mail" };

        var attributes = CreateAttributes(("cn", "John Doe"));

        filter.Matches(attributes).Should().BeFalse();
    }

    [Fact]
    public void PresentFilter_ObjectClass_AlwaysTrue()
    {
        var filter = new PresentFilter { AttributeDescription = "objectClass" };

        var attributes = CreateAttributes(("cn", "John Doe")); // No objectClass attr

        filter.Matches(attributes).Should().BeTrue();
    }

    [Fact]
    public void PresentFilter_AttributeNameCaseInsensitive()
    {
        var filter = new PresentFilter { AttributeDescription = "OBJECTCLASS" };

        var attributes = CreateAttributes(("cn", "John Doe"));

        filter.Matches(attributes).Should().BeTrue();
    }

    [Fact]
    public void PresentFilter_WithEmptyValue_ReturnsFalse()
    {
        var filter = new PresentFilter { AttributeDescription = "mail" };

        var attributes = new Dictionary<string, List<string>>
        {
            ["mail"] = new() // Empty list
        };

        filter.Matches(attributes).Should().BeFalse();
    }

    #endregion

    #region GreaterOrEqualFilter Tests

    [Fact]
    public void GreaterOrEqualFilter_WithGreaterValue_ReturnsTrue()
    {
        var filter = new GreaterOrEqualFilter
        {
            AttributeDescription = "uidNumber",
            AssertionValue = "100"
        };

        var attributes = CreateAttributes(("uidnumber", "200"));

        filter.Matches(attributes).Should().BeTrue();
    }

    [Fact]
    public void GreaterOrEqualFilter_WithEqualValue_ReturnsTrue()
    {
        var filter = new GreaterOrEqualFilter
        {
            AttributeDescription = "uidNumber",
            AssertionValue = "100"
        };

        var attributes = CreateAttributes(("uidnumber", "100"));

        filter.Matches(attributes).Should().BeTrue();
    }

    [Fact]
    public void GreaterOrEqualFilter_WithLesserValue_ReturnsFalse()
    {
        var filter = new GreaterOrEqualFilter
        {
            AttributeDescription = "uidNumber",
            AssertionValue = "100"
        };

        var attributes = CreateAttributes(("uidnumber", "050")); // String comparison: "050" < "100"

        filter.Matches(attributes).Should().BeFalse();
    }

    #endregion

    #region LessOrEqualFilter Tests

    [Fact]
    public void LessOrEqualFilter_WithLesserValue_ReturnsTrue()
    {
        var filter = new LessOrEqualFilter
        {
            AttributeDescription = "uidNumber",
            AssertionValue = "200"
        };

        var attributes = CreateAttributes(("uidnumber", "100"));

        filter.Matches(attributes).Should().BeTrue();
    }

    [Fact]
    public void LessOrEqualFilter_WithEqualValue_ReturnsTrue()
    {
        var filter = new LessOrEqualFilter
        {
            AttributeDescription = "uidNumber",
            AssertionValue = "100"
        };

        var attributes = CreateAttributes(("uidnumber", "100"));

        filter.Matches(attributes).Should().BeTrue();
    }

    [Fact]
    public void LessOrEqualFilter_WithGreaterValue_ReturnsFalse()
    {
        var filter = new LessOrEqualFilter
        {
            AttributeDescription = "uidNumber",
            AssertionValue = "100"
        };

        var attributes = CreateAttributes(("uidnumber", "200"));

        filter.Matches(attributes).Should().BeFalse();
    }

    #endregion

    #region ApproxMatchFilter Tests

    [Fact]
    public void ApproxMatchFilter_ContainsValue_ReturnsTrue()
    {
        var filter = new ApproxMatchFilter
        {
            AttributeDescription = "cn",
            AssertionValue = "John"
        };

        var attributes = CreateAttributes(("cn", "John Doe"));

        filter.Matches(attributes).Should().BeTrue();
    }

    [Fact]
    public void ApproxMatchFilter_NoContain_ReturnsFalse()
    {
        var filter = new ApproxMatchFilter
        {
            AttributeDescription = "cn",
            AssertionValue = "Jane"
        };

        var attributes = CreateAttributes(("cn", "John Doe"));

        filter.Matches(attributes).Should().BeFalse();
    }

    #endregion

    #region Complex Filter Tests

    [Fact]
    public void ComplexFilter_AndWithOr_Works()
    {
        // (&(objectClass=person)(|(cn=John*)(cn=Jane*)))
        var filter = new AndFilter
        {
            Filters = new List<LdapFilter>
            {
                new EqualityFilter { AttributeDescription = "objectClass", AssertionValue = "person" },
                new OrFilter
                {
                    Filters = new List<LdapFilter>
                    {
                        new SubstringFilter { AttributeDescription = "cn", Initial = "John" },
                        new SubstringFilter { AttributeDescription = "cn", Initial = "Jane" }
                    }
                }
            }
        };

        var johnAttributes = CreateAttributes(
            ("objectclass", "person"),
            ("cn", "John Doe")
        );

        var janeAttributes = CreateAttributes(
            ("objectclass", "person"),
            ("cn", "Jane Smith")
        );

        var bobAttributes = CreateAttributes(
            ("objectclass", "person"),
            ("cn", "Bob Jones")
        );

        filter.Matches(johnAttributes).Should().BeTrue();
        filter.Matches(janeAttributes).Should().BeTrue();
        filter.Matches(bobAttributes).Should().BeFalse();
    }

    [Fact]
    public void ComplexFilter_NotWithAnd_Works()
    {
        // (&(objectClass=person)(!(cn=admin)))
        var filter = new AndFilter
        {
            Filters = new List<LdapFilter>
            {
                new EqualityFilter { AttributeDescription = "objectClass", AssertionValue = "person" },
                new NotFilter
                {
                    Filter = new EqualityFilter { AttributeDescription = "cn", AssertionValue = "admin" }
                }
            }
        };

        var userAttributes = CreateAttributes(
            ("objectclass", "person"),
            ("cn", "John Doe")
        );

        var adminAttributes = CreateAttributes(
            ("objectclass", "person"),
            ("cn", "admin")
        );

        filter.Matches(userAttributes).Should().BeTrue();
        filter.Matches(adminAttributes).Should().BeFalse();
    }

    #endregion

    #region Helper Methods

    private static Dictionary<string, List<string>> CreateAttributes(params (string key, string value)[] attrs)
    {
        return attrs.ToDictionary(
            a => a.key.ToLowerInvariant(),
            a => new List<string> { a.value }
        );
    }

    #endregion
}

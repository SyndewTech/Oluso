using FluentAssertions;
using Oluso.Enterprise.Scim.Filtering;
using Xunit;

namespace Oluso.Enterprise.Scim.Tests;

public class ScimFilterTests
{
    private sealed class Person
    {
        public string? Name { get; init; }
        public string? Email { get; init; }
        public string? Title { get; init; }
        public bool Active { get; init; }
    }

    private static readonly List<Person> People = new()
    {
        new Person { Name = "alice", Email = "alice@example.com", Title = "Engineer", Active = true },
        new Person { Name = "Bob", Email = "bob@example.com", Title = null, Active = false },
        new Person { Name = "carol", Email = "carol@contoso.com", Title = "Manager", Active = true },
    };

    private static List<Person> Apply(string filter)
    {
        var node = ScimFilterParser.Parse(filter);
        var predicate = new ScimFilterTranslator<Person>()
            .MapString(p => p.Name, "username")
            .MapString(p => p.Email, "emails.value", "emails")
            .MapString(_ => "work", "emails.type")
            .MapString(p => p.Title, "title")
            .MapBool(p => p.Active, "active")
            .Translate(node)
            .Compile();

        return People.Where(predicate).ToList();
    }

    [Theory]
    [InlineData("userName eq \"alice\"", "alice")]
    [InlineData("userName eq \"ALICE\"", "alice")] // case-insensitive
    public void Eq_IsCaseInsensitive(string filter, string expected)
    {
        Apply(filter).Should().ContainSingle().Which.Name.Should().Be(expected);
    }

    [Fact]
    public void And_CombinesClauses()
    {
        Apply("userName eq \"alice\" and active eq true").Should().ContainSingle().Which.Name.Should().Be("alice");
        Apply("userName eq \"bob\" and active eq true").Should().BeEmpty();
    }

    [Fact]
    public void Or_WithGrouping()
    {
        Apply("(userName eq \"alice\") or (userName eq \"bob\")")
            .Select(p => p.Name).Should().BeEquivalentTo(new[] { "alice", "Bob" });
    }

    [Fact]
    public void Not_NegatesExpression()
    {
        Apply("not (active eq true)").Should().ContainSingle().Which.Name.Should().Be("Bob");
    }

    [Theory]
    [InlineData("userName sw \"al\"", "alice")]
    [InlineData("userName ew \"rol\"", "carol")]
    [InlineData("emails co \"contoso\"", "carol")]
    public void StringOperators(string filter, string expected)
    {
        Apply(filter).Should().ContainSingle().Which.Name.Should().Be(expected);
    }

    [Fact]
    public void Pr_MatchesPresentAttributesOnly()
    {
        Apply("title pr").Select(p => p.Name).Should().BeEquivalentTo(new[] { "alice", "carol" });
    }

    [Fact]
    public void Ne_ExcludesMatchAndIncludesNull()
    {
        // bob has a null title; ne should include it as "not equal".
        Apply("title ne \"Engineer\"").Select(p => p.Name).Should().BeEquivalentTo(new[] { "Bob", "carol" });
    }

    [Fact]
    public void ValuePath_RescopesInnerAttribute()
    {
        // emails[type eq "work" and value co "example"] -> work email containing "example"
        Apply("emails[type eq \"work\" and value co \"example\"]")
            .Select(p => p.Name).Should().BeEquivalentTo(new[] { "alice", "Bob" });
    }

    [Theory]
    [InlineData("userName eq")]          // missing value
    [InlineData("userName zz \"x\"")]    // unknown operator
    [InlineData("userName eq \"x\" and")] // dangling logical
    [InlineData("(userName eq \"x\"")]   // unbalanced paren
    public void MalformedFilters_Throw(string filter)
    {
        Action act = () => ScimFilterParser.Parse(filter);
        act.Should().Throw<ScimFilterException>();
    }

    [Fact]
    public void UnknownAttribute_ThrowsOnTranslate()
    {
        var node = ScimFilterParser.Parse("unknownAttr eq \"x\"");
        Action act = () => new ScimFilterTranslator<Person>()
            .MapString(p => p.Name, "username")
            .Translate(node);
        act.Should().Throw<ScimFilterException>();
    }
}

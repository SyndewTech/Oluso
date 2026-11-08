using System.Text.Json.Nodes;
using FluentAssertions;
using Oluso.Core.Domain.Entities;
using Oluso.Enterprise.Scim.Entities;
using Oluso.Enterprise.Scim.Outbound;
using Xunit;

namespace Oluso.Enterprise.Scim.Tests.Outbound;

public class ScimOutboundMapperTests
{
    private readonly ScimOutboundMapper _mapper = new(new ScimExpressionEngine());

    private static OlusoUser User() => new()
    {
        Id = "u1",
        TenantId = "t1",
        UserName = "jdoe",
        Email = "Jane.Doe@Example.org",
        FirstName = "Jane",
        LastName = "Doe",
        PhoneNumber = "+44 20 1234 5678",
        Locale = "en-GB",
        TimeZone = "Europe/London",
        IsActive = true,
        CustomPropertiesJson = "{\"department\":\"Finance\",\"badge\":42}",
    };

    private static ScimProvider Provider(string flavor = ScimProviderFlavors.Generic, string source = ScimProviderUserNameSources.UserName) => new()
    {
        Id = "p1",
        TenantId = "t1",
        Name = "Test",
        Flavor = flavor,
        UserNameSource = source,
    };

    private static ScimOutboundUserContext Context(OlusoUser user, params string[] roles) => new()
    {
        User = user,
        RoleNames = roles,
    };

    [Fact]
    public void Generic_defaults_produce_standard_user_payload()
    {
        var payload = _mapper.BuildUser(Provider(), ScimProviderFlavorProfiles.Generic, Context(User()), Array.Empty<ScimProviderAttributeMapping>());

        payload["schemas"]!.AsArray().Select(s => s!.GetValue<string>()).Should().ContainSingle(ScimConstants.Schemas.User);
        payload["userName"]!.GetValue<string>().Should().Be("jdoe");
        payload["externalId"]!.GetValue<string>().Should().Be("u1");
        payload["active"]!.GetValue<bool>().Should().BeTrue();
        payload["name"]!["givenName"]!.GetValue<string>().Should().Be("Jane");
        payload["name"]!["familyName"]!.GetValue<string>().Should().Be("Doe");
        payload["displayName"]!.GetValue<string>().Should().Be("Jane Doe");

        var email = payload["emails"]!.AsArray().Single()!.AsObject();
        email["value"]!.GetValue<string>().Should().Be("Jane.Doe@Example.org");
        email["type"]!.GetValue<string>().Should().Be("work");
        email["primary"]!.GetValue<bool>().Should().BeTrue();

        payload["phoneNumbers"]!.AsArray().Single()!["value"]!.GetValue<string>().Should().Be("+44 20 1234 5678");
        payload["locale"]!.GetValue<string>().Should().Be("en-GB");
        payload["timezone"]!.GetValue<string>().Should().Be("Europe/London");
    }

    [Fact]
    public void Stalwart_uses_email_as_userName_and_drops_phone_numbers()
    {
        var provider = Provider(ScimProviderFlavors.Stalwart, ScimProviderUserNameSources.Email);

        var payload = _mapper.BuildUser(provider, ScimProviderFlavorProfiles.Stalwart, Context(User()), Array.Empty<ScimProviderAttributeMapping>());

        payload["userName"]!.GetValue<string>().Should().Be("Jane.Doe@Example.org");
        payload["phoneNumbers"].Should().BeNull();
        payload["emails"].Should().BeNull("Stalwart derives the primary address from userName and treats it as read-only");
        payload["displayName"]!.GetValue<string>().Should().Be("Jane Doe");
    }

    [Fact]
    public void Stalwart_rejects_a_user_without_an_email_userName()
    {
        var provider = Provider(ScimProviderFlavors.Stalwart, ScimProviderUserNameSources.UserName);

        var act = () => _mapper.BuildUser(provider, ScimProviderFlavorProfiles.Stalwart, Context(User()), Array.Empty<ScimProviderAttributeMapping>());

        act.Should().Throw<ScimOutboundMappingException>().WithMessage("*email address*");
    }

    [Fact]
    public void Custom_mappings_replace_defaults_and_apply_transformation_and_default_value()
    {
        var mappings = new List<ScimProviderAttributeMapping>
        {
            new() { ScimAttribute = "emails[type eq \"work\"].value", InternalProperty = "Email", Transformation = "normalize_email" },
            new() { ScimAttribute = "title", InternalProperty = "custom:jobTitle", DefaultValue = "Staff" },
            new() { ScimAttribute = "urn:ietf:params:scim:schemas:extension:enterprise:2.0:User:department", InternalProperty = "custom:department" },
            new() { ScimAttribute = "nickName", InternalProperty = "FirstName", IsEnabled = false },
        };

        var payload = _mapper.BuildUser(Provider(), ScimProviderFlavorProfiles.Generic, Context(User()), mappings);

        payload["emails"]!.AsArray().Single()!["value"]!.GetValue<string>().Should().Be("jane.doe@example.org");
        payload["title"]!.GetValue<string>().Should().Be("Staff");
        payload["nickName"].Should().BeNull("disabled mappings are skipped");
        payload["name"].Should().BeNull("custom mappings replace the defaults rather than extend them");

        payload["schemas"]!.AsArray().Select(s => s!.GetValue<string>()).Should().Contain(ScimConstants.Schemas.EnterpriseUser);
        payload[ScimConstants.Schemas.EnterpriseUser]!["department"]!.GetValue<string>().Should().Be("Finance");
    }

    [Fact]
    public void Expression_computes_value_and_condition_gates_attribute()
    {
        var mappings = new List<ScimProviderAttributeMapping>
        {
            new() { ScimAttribute = "displayName", Expression = "{{ user.last_name | string.upcase }}, {{ user.first_name }}" },
            new() { ScimAttribute = "title", InternalProperty = "custom:department", Condition = "roles | array.contains \"Manager\"" },
            new() { ScimAttribute = "nickName", Expression = "{{ if user.locale == \"en-GB\" }}brit{{ end }}" },
        };

        var manager = _mapper.BuildUser(Provider(), ScimProviderFlavorProfiles.Generic, Context(User(), "Manager"), mappings);
        manager["displayName"]!.GetValue<string>().Should().Be("DOE, Jane");
        manager["title"]!.GetValue<string>().Should().Be("Finance");
        manager["nickName"]!.GetValue<string>().Should().Be("brit");

        var staff = _mapper.BuildUser(Provider(), ScimProviderFlavorProfiles.Generic, Context(User(), "Staff"), mappings);
        staff["title"].Should().BeNull("the condition is false for non-managers");
    }

    [Fact]
    public void UserName_expression_derives_the_identifier()
    {
        var provider = Provider(ScimProviderFlavors.Stalwart, ScimProviderUserNameSources.Expression);
        provider.UserNameExpression = "{{ user.first_name | string.downcase }}.{{ user.last_name | string.downcase }}@mail.example.org";

        var payload = _mapper.BuildUser(provider, ScimProviderFlavorProfiles.Stalwart, Context(User()), Array.Empty<ScimProviderAttributeMapping>());

        payload["userName"]!.GetValue<string>().Should().Be("jane.doe@mail.example.org");
    }

    [Fact]
    public void Unknown_property_and_bad_expression_are_configuration_errors()
    {
        var unknown = new List<ScimProviderAttributeMapping> { new() { ScimAttribute = "title", InternalProperty = "Department" } };
        var badExpression = new List<ScimProviderAttributeMapping> { new() { ScimAttribute = "title", Expression = "{{ user.first_name ) }}" } };

        FluentActions.Invoking(() => _mapper.BuildUser(Provider(), ScimProviderFlavorProfiles.Generic, Context(User()), unknown))
            .Should().Throw<ScimOutboundMappingException>().WithMessage("*Unknown internal property*");
        FluentActions.Invoking(() => _mapper.BuildUser(Provider(), ScimProviderFlavorProfiles.Generic, Context(User()), badExpression))
            .Should().Throw<ScimOutboundMappingException>().WithMessage("*does not parse*");
    }

    [Fact]
    public void Group_payload_is_order_independent_and_hash_stable()
    {
        var a = _mapper.BuildGroup("role:r1", "Finance", new[] { "x", "y", "z" });
        var b = _mapper.BuildGroup("role:r1", "Finance", new[] { "z", "y", "x", "x" });

        a["externalId"]!.GetValue<string>().Should().Be("role:r1");
        a["members"]!.AsArray().Should().HaveCount(3);
        _mapper.ComputeHash(a).Should().Be(_mapper.ComputeHash(b));
    }

    [Fact]
    public void Replace_patch_addresses_extension_attributes_individually()
    {
        var payload = new JsonObject
        {
            ["schemas"] = new JsonArray(ScimConstants.Schemas.User),
            ["userName"] = "jdoe",
            ["name"] = new JsonObject { ["givenName"] = "Jane" },
            [ScimConstants.Schemas.EnterpriseUser] = new JsonObject { ["department"] = "Finance" },
        };

        var patch = ScimOutboundProvisioningService.BuildReplacePatch(payload);
        var ops = patch["Operations"]!.AsArray().Select(o => o!["path"]!.GetValue<string>()).ToList();

        ops.Should().BeEquivalentTo("userName", "name", $"{ScimConstants.Schemas.EnterpriseUser}:department");
        patch["Operations"]!.AsArray().Should().OnlyContain(o => o!["op"]!.GetValue<string>() == "replace");
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData("", false)]
    [InlineData("x", true)]
    [InlineData(0, false)]
    [InlineData(3, true)]
    public void Truthiness_follows_template_semantics(object? value, bool expected)
    {
        ScimExpressionEngine.IsTruthy(value).Should().Be(expected);
    }

    [Fact]
    public void Validation_reports_parse_errors()
    {
        ScimExpressionEngine.ValidateTemplate("{{ user.email }}").Should().BeNull();
        ScimExpressionEngine.ValidateTemplate("{{ user.email ) }}").Should().NotBeNull();
        ScimExpressionEngine.ValidateExpression("roles | array.contains \"A\"").Should().BeNull();
        ScimExpressionEngine.ValidateExpression("roles | )").Should().NotBeNull();
    }
}

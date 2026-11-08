using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Oluso.Core.Domain.Entities;
using Oluso.Enterprise.Scim.Entities;
using Oluso.Enterprise.Scim.Models;
using Oluso.Enterprise.Scim.Outbound;
using Oluso.Enterprise.Scim.Services;
using Xunit;

namespace Oluso.Enterprise.Scim.Tests.Services;

public class ScimInboundMapperTests
{
    private static readonly JsonSerializerOptions Json = new() { PropertyNameCaseInsensitive = true };
    private readonly DefaultScimUserMapper _mapper = new(new ScimInboundMapper(new ScimExpressionEngine()));
    private readonly ScimClient _client = new() { Id = "c1", TenantId = "t1", Name = "Entra" };
    private static readonly IReadOnlyList<ScimAttributeMapping> NoMappings = Array.Empty<ScimAttributeMapping>();

    private const string Enterprise = ScimConstants.Schemas.EnterpriseUser;

    private static ScimUser Parse(string json) => JsonSerializer.Deserialize<ScimUser>(json, Json)!;

    private const string EntraCreate = """
        {
          "schemas": ["urn:ietf:params:scim:schemas:core:2.0:User", "urn:ietf:params:scim:schemas:extension:enterprise:2.0:User"],
          "externalId": "ext-1",
          "userName": "jane.doe@example.org",
          "name": { "givenName": "Jane", "familyName": "Doe" },
          "displayName": "Jane Doe",
          "title": "Finance Manager",
          "userType": "Employee",
          "active": true,
          "emails": [{ "value": "Jane.Doe@Example.org", "primary": true }],
          "phoneNumbers": [{ "value": "+44 20 1234 5678", "type": "work" }],
          "locale": "en-GB",
          "timezone": "Europe/London",
          "urn:ietf:params:scim:schemas:extension:enterprise:2.0:User": { "department": "Finance", "employeeNumber": "E-1042", "manager": { "value": "mgr-9" } }
        }
        """;

    [Fact]
    public void Defaults_on_create_reproduce_the_previous_fixed_mapping()
    {
        var user = _mapper.CreateFromScimUser(Parse(EntraCreate), "t1", _client, NoMappings);

        user.TenantId.Should().Be("t1");
        user.UserName.Should().Be("jane.doe@example.org");
        user.NormalizedUserName.Should().Be("JANE.DOE@EXAMPLE.ORG");
        user.FirstName.Should().Be("Jane");
        user.LastName.Should().Be("Doe");
        user.DisplayName.Should().Be("Jane Doe");
        user.Email.Should().Be("Jane.Doe@Example.org", "an email without a type still maps through the primary fallback");
        user.EmailConfirmed.Should().BeFalse("a provisioned address is trusted only at a domain the tenant verified, which the provisioning service decides");
        user.PhoneNumber.Should().Be("+44 20 1234 5678");
        user.Locale.Should().Be("en-GB");
        user.TimeZone.Should().Be("Europe/London");
        user.IsActive.Should().BeTrue();
        user.CustomPropertiesJson.Should().BeNull("no default mapping writes custom properties");
    }

    [Fact]
    public void Create_without_userName_is_rejected_and_active_defaults_to_true()
    {
        FluentActions.Invoking(() => _mapper.CreateFromScimUser(Parse("""{ "emails": [{ "value": "a@b.c" }] }"""), "t1", _client, NoMappings))
            .Should().Throw<ScimInboundMappingException>().WithMessage("userName is required");

        var user = _mapper.CreateFromScimUser(Parse("""{ "userName": "bob" }"""), "t1", _client, NoMappings);
        user.IsActive.Should().BeTrue();
        user.DisplayName.Should().BeNull();
    }

    [Fact]
    public void Display_name_falls_back_to_name_parts_by_priority()
    {
        var user = _mapper.CreateFromScimUser(Parse("""{ "userName": "bob", "name": { "givenName": "Bob", "familyName": "Stone" } }"""), "t1", _client, NoMappings);

        user.DisplayName.Should().Be("Bob Stone");
    }

    [Fact]
    public void Replace_clears_optional_attributes_but_keeps_identity_and_active()
    {
        var user = _mapper.CreateFromScimUser(Parse(EntraCreate), "t1", _client, NoMappings);
        user.IsActive = false;

        _mapper.ApplyReplace(user, Parse("""{ "userName": "jane.doe@example.org", "name": { "givenName": "Janet" } }"""), _client, NoMappings);

        user.FirstName.Should().Be("Janet");
        user.LastName.Should().BeNull("PUT replaces the whole resource");
        user.PhoneNumber.Should().BeNull();
        user.Locale.Should().BeNull();
        user.DisplayName.Should().Be("Janet", "the name-parts fallback still produces a display name");
        user.Email.Should().Be("Jane.Doe@Example.org", "email is an identity anchor and is never cleared by omission");
        user.UserName.Should().Be("jane.doe@example.org");
        user.IsActive.Should().BeFalse("an omitted active must not reactivate");
    }

    [Fact]
    public void Patch_pathless_active_false_from_entra_only_touches_active()
    {
        var user = _mapper.CreateFromScimUser(Parse(EntraCreate), "t1", _client, NoMappings);
        var patch = new ScimPatchRequest
        {
            Operations = { new ScimPatchOperation { Op = "Replace", Value = JsonSerializer.Deserialize<JsonElement>("""{ "active": false }""") } },
        };

        _mapper.ApplyPatch(user, patch, _client, NoMappings);

        user.IsActive.Should().BeFalse();
        user.FirstName.Should().Be("Jane");
        user.PhoneNumber.Should().Be("+44 20 1234 5678");
        user.Email.Should().Be("Jane.Doe@Example.org");
    }

    [Fact]
    public void Patch_paths_replace_and_remove_attributes()
    {
        var user = _mapper.CreateFromScimUser(Parse(EntraCreate), "t1", _client, NoMappings);
        var patch = new ScimPatchRequest
        {
            Operations =
            {
                new ScimPatchOperation { Op = "replace", Path = "name.givenName", Value = JsonSerializer.Deserialize<JsonElement>("\"Janet\"") },
                new ScimPatchOperation { Op = "replace", Path = "emails[type eq \"work\"].value", Value = JsonSerializer.Deserialize<JsonElement>("\"janet@example.org\"") },
                new ScimPatchOperation { Op = "remove", Path = "phoneNumbers" },
                new ScimPatchOperation { Op = "add", Path = "locale", Value = JsonSerializer.Deserialize<JsonElement>("\"fr-FR\"") },
            },
        };

        _mapper.ApplyPatch(user, patch, _client, NoMappings);

        user.FirstName.Should().Be("Janet");
        user.LastName.Should().Be("Doe");
        user.Email.Should().Be("janet@example.org");
        user.PhoneNumber.Should().BeNull("an explicit remove clears the target");
        user.Locale.Should().Be("fr-FR");
        user.DisplayName.Should().Be("Jane Doe", "untouched attributes keep their value");
    }

    [Fact]
    public void Custom_mappings_use_expressions_conditions_extensions_and_custom_properties()
    {
        var mappings = new List<ScimAttributeMapping>
        {
            new() { ScimAttribute = "userName", InternalProperty = "UserName", IsRequired = true },
            new() { ScimAttribute = "emails[type eq \"work\"].value", InternalProperty = "Email", Transformation = "normalize_email" },
            new() { ScimAttribute = "displayName", InternalProperty = "DisplayName", Expression = "{{ scim.name.familyName | string.upcase }}, {{ scim.name.givenName }}" },
            new() { ScimAttribute = $"{Enterprise}:department", InternalProperty = "custom:department" },
            new() { ScimAttribute = $"{Enterprise}:manager.value", InternalProperty = "custom:managerId" },
            new() { ScimAttribute = "title", InternalProperty = "custom:jobTitle", Condition = "scim.userType == \"Employee\"" },
            new() { ScimAttribute = "nickName", InternalProperty = "custom:source", Expression = "{{ client.name }}:{{ enterprise.employeeNumber }}" },
            new() { ScimAttribute = "preferredLanguage", InternalProperty = "Locale", DefaultValue = "en-US" },
        };

        var user = _mapper.CreateFromScimUser(Parse(EntraCreate), "t1", _client, mappings);

        user.Email.Should().Be("jane.doe@example.org");
        user.DisplayName.Should().Be("DOE, Jane");
        user.FirstName.Should().BeNull("custom mappings replace the defaults");
        user.Locale.Should().Be("en-US", "the default fills an absent attribute");

        var custom = JsonSerializer.Deserialize<Dictionary<string, string>>(user.CustomPropertiesJson!)!;
        custom.Should().Contain("department", "Finance");
        custom.Should().Contain("managerId", "mgr-9");
        custom.Should().Contain("jobTitle", "Finance Manager");
        custom.Should().Contain("source", "Entra:E-1042");

        var contractor = Parse(EntraCreate.Replace("\"Employee\"", "\"Contractor\""));
        var other = _mapper.CreateFromScimUser(contractor, "t1", _client, mappings);
        JsonSerializer.Deserialize<Dictionary<string, string>>(other.CustomPropertiesJson!)!.Should().NotContainKey("jobTitle", "the condition is false");
    }

    [Fact]
    public void Required_custom_mapping_and_bad_expression_surface_as_mapping_errors()
    {
        var required = new List<ScimAttributeMapping>
        {
            new() { ScimAttribute = "userName", InternalProperty = "UserName" },
            new() { ScimAttribute = $"{Enterprise}:employeeNumber", InternalProperty = "custom:employeeNumber", IsRequired = true },
        };
        FluentActions.Invoking(() => _mapper.CreateFromScimUser(Parse("""{ "userName": "bob" }"""), "t1", _client, required))
            .Should().Throw<ScimInboundMappingException>().WithMessage("*employeeNumber is required");

        var broken = new List<ScimAttributeMapping>
        {
            new() { ScimAttribute = "userName", InternalProperty = "UserName" },
            new() { ScimAttribute = "displayName", InternalProperty = "DisplayName", Expression = "{{ scim.name.givenName ) }}" },
        };
        FluentActions.Invoking(() => _mapper.CreateFromScimUser(Parse("""{ "userName": "bob" }"""), "t1", _client, broken))
            .Should().Throw<ScimInboundMappingException>().WithMessage("*does not parse*");
    }

    [Fact]
    public void Preview_reports_fields_without_touching_a_user()
    {
        var resource = JsonNode.Parse(EntraCreate)!.AsObject();

        var preview = _mapper.Preview(resource, _client, NoMappings, ScimInboundMode.Create);

        preview.Success.Should().BeTrue(preview.Error);
        preview.Fields.Should().Contain("UserName", "jane.doe@example.org");
        preview.Fields.Should().Contain("DisplayName", "Jane Doe");
        preview.Fields.Should().Contain("IsActive", "true");
    }

    [Fact]
    public void Attribute_path_reader_handles_filters_dots_and_extensions()
    {
        var resource = JsonNode.Parse(EntraCreate)!.AsObject();

        ScimAttributePath.Read(resource, "userName").Should().Be("jane.doe@example.org");
        ScimAttributePath.Read(resource, "NAME.GIVENNAME").Should().Be("Jane", "attribute names are case-insensitive");
        ScimAttributePath.Read(resource, "emails[type eq \"work\"].value").Should().Be("Jane.Doe@Example.org", "falls back to the primary element");
        ScimAttributePath.Read(resource, "emails[primary eq true].value").Should().Be("Jane.Doe@Example.org");
        ScimAttributePath.Read(resource, "emails.value").Should().Be("Jane.Doe@Example.org");
        ScimAttributePath.Read(resource, "phoneNumbers[type eq \"work\"].value").Should().Be("+44 20 1234 5678");
        ScimAttributePath.Read(resource, $"{Enterprise}:manager.value").Should().Be("mgr-9");
        ScimAttributePath.Read(resource, "active").Should().Be("true");
        ScimAttributePath.Read(resource, "missing.attribute").Should().BeNull();
    }
}

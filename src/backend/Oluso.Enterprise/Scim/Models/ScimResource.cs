using System.Text.Json.Serialization;

namespace Oluso.Enterprise.Scim.Models;

/// <summary>
/// Base class for all SCIM resources
/// </summary>
public abstract class ScimResource
{
    [JsonPropertyName("schemas")]
    public List<string> Schemas { get; set; } = new();

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("externalId")]
    public string? ExternalId { get; set; }

    [JsonPropertyName("meta")]
    public ScimMeta? Meta { get; set; }
}

/// <summary>
/// SCIM resource metadata
/// </summary>
public class ScimMeta
{
    [JsonPropertyName("resourceType")]
    public string? ResourceType { get; set; }

    [JsonPropertyName("created")]
    public DateTime? Created { get; set; }

    [JsonPropertyName("lastModified")]
    public DateTime? LastModified { get; set; }

    [JsonPropertyName("location")]
    public string? Location { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }
}

/// <summary>
/// SCIM User resource (RFC 7643)
/// </summary>
public class ScimUser : ScimResource
{
    public ScimUser()
    {
        Schemas.Add(ScimConstants.Schemas.User);
    }

    [JsonPropertyName("userName")]
    public string UserName { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public ScimName? Name { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    [JsonPropertyName("nickName")]
    public string? NickName { get; set; }

    [JsonPropertyName("profileUrl")]
    public string? ProfileUrl { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("userType")]
    public string? UserType { get; set; }

    [JsonPropertyName("preferredLanguage")]
    public string? PreferredLanguage { get; set; }

    [JsonPropertyName("locale")]
    public string? Locale { get; set; }

    [JsonPropertyName("timezone")]
    public string? Timezone { get; set; }

    [JsonPropertyName("active")]
    public bool Active { get; set; } = true;

    [JsonPropertyName("password")]
    public string? Password { get; set; }

    [JsonPropertyName("emails")]
    public List<ScimMultiValuedAttribute>? Emails { get; set; }

    [JsonPropertyName("phoneNumbers")]
    public List<ScimMultiValuedAttribute>? PhoneNumbers { get; set; }

    [JsonPropertyName("addresses")]
    public List<ScimAddress>? Addresses { get; set; }

    [JsonPropertyName("groups")]
    public List<ScimGroupMembership>? Groups { get; set; }
}

/// <summary>
/// SCIM Name complex attribute
/// </summary>
public class ScimName
{
    [JsonPropertyName("formatted")]
    public string? Formatted { get; set; }

    [JsonPropertyName("familyName")]
    public string? FamilyName { get; set; }

    [JsonPropertyName("givenName")]
    public string? GivenName { get; set; }

    [JsonPropertyName("middleName")]
    public string? MiddleName { get; set; }

    [JsonPropertyName("honorificPrefix")]
    public string? HonorificPrefix { get; set; }

    [JsonPropertyName("honorificSuffix")]
    public string? HonorificSuffix { get; set; }
}

/// <summary>
/// SCIM multi-valued attribute (emails, phoneNumbers, etc.)
/// </summary>
public class ScimMultiValuedAttribute
{
    [JsonPropertyName("value")]
    public string? Value { get; set; }

    [JsonPropertyName("display")]
    public string? Display { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("primary")]
    public bool Primary { get; set; }
}

/// <summary>
/// SCIM Address complex attribute
/// </summary>
public class ScimAddress
{
    [JsonPropertyName("formatted")]
    public string? Formatted { get; set; }

    [JsonPropertyName("streetAddress")]
    public string? StreetAddress { get; set; }

    [JsonPropertyName("locality")]
    public string? Locality { get; set; }

    [JsonPropertyName("region")]
    public string? Region { get; set; }

    [JsonPropertyName("postalCode")]
    public string? PostalCode { get; set; }

    [JsonPropertyName("country")]
    public string? Country { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("primary")]
    public bool Primary { get; set; }
}

/// <summary>
/// SCIM Group membership reference
/// </summary>
public class ScimGroupMembership
{
    [JsonPropertyName("value")]
    public string? Value { get; set; }

    [JsonPropertyName("$ref")]
    public string? Ref { get; set; }

    [JsonPropertyName("display")]
    public string? Display { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }
}

/// <summary>
/// SCIM Group resource
/// </summary>
public class ScimGroup : ScimResource
{
    public ScimGroup()
    {
        Schemas.Add(ScimConstants.Schemas.Group);
    }

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("members")]
    public List<ScimMember>? Members { get; set; }
}

/// <summary>
/// SCIM Group member
/// </summary>
public class ScimMember
{
    [JsonPropertyName("value")]
    public string? Value { get; set; }

    [JsonPropertyName("$ref")]
    public string? Ref { get; set; }

    [JsonPropertyName("display")]
    public string? Display { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }
}

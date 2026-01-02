using System.Text.Json.Serialization;

namespace Oluso.Enterprise.Scim.Models;

/// <summary>
/// SCIM Service Provider Configuration (RFC 7643)
/// </summary>
public class ScimServiceProviderConfig
{
    [JsonPropertyName("schemas")]
    public List<string> Schemas { get; set; } = new() { ScimConstants.Schemas.ServiceProviderConfig };

    [JsonPropertyName("documentationUri")]
    public string? DocumentationUri { get; set; }

    [JsonPropertyName("patch")]
    public ScimSupported Patch { get; set; } = new() { Supported = true };

    [JsonPropertyName("bulk")]
    public ScimBulkConfig Bulk { get; set; } = new();

    [JsonPropertyName("filter")]
    public ScimFilterConfig Filter { get; set; } = new();

    [JsonPropertyName("changePassword")]
    public ScimSupported ChangePassword { get; set; } = new() { Supported = true };

    [JsonPropertyName("sort")]
    public ScimSupported Sort { get; set; } = new() { Supported = true };

    [JsonPropertyName("etag")]
    public ScimSupported Etag { get; set; } = new() { Supported = false };

    [JsonPropertyName("authenticationSchemes")]
    public List<ScimAuthenticationScheme> AuthenticationSchemes { get; set; } = new();

    [JsonPropertyName("meta")]
    public ScimMeta? Meta { get; set; }
}

public class ScimSupported
{
    [JsonPropertyName("supported")]
    public bool Supported { get; set; }
}

public class ScimBulkConfig : ScimSupported
{
    [JsonPropertyName("maxOperations")]
    public int MaxOperations { get; set; } = 1000;

    [JsonPropertyName("maxPayloadSize")]
    public int MaxPayloadSize { get; set; } = 1048576; // 1MB
}

public class ScimFilterConfig : ScimSupported
{
    public ScimFilterConfig()
    {
        Supported = true;
    }

    [JsonPropertyName("maxResults")]
    public int MaxResults { get; set; } = 200;
}

public class ScimAuthenticationScheme
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("specUri")]
    public string? SpecUri { get; set; }

    [JsonPropertyName("documentationUri")]
    public string? DocumentationUri { get; set; }

    [JsonPropertyName("primary")]
    public bool Primary { get; set; }
}

/// <summary>
/// SCIM Resource Type (RFC 7643)
/// </summary>
public class ScimResourceType
{
    [JsonPropertyName("schemas")]
    public List<string> Schemas { get; set; } = new() { ScimConstants.Schemas.ResourceType };

    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("endpoint")]
    public string Endpoint { get; set; } = string.Empty;

    [JsonPropertyName("schema")]
    public string Schema { get; set; } = string.Empty;

    [JsonPropertyName("schemaExtensions")]
    public List<ScimSchemaExtension>? SchemaExtensions { get; set; }

    [JsonPropertyName("meta")]
    public ScimMeta? Meta { get; set; }
}

public class ScimSchemaExtension
{
    [JsonPropertyName("schema")]
    public string Schema { get; set; } = string.Empty;

    [JsonPropertyName("required")]
    public bool Required { get; set; }
}

/// <summary>
/// SCIM Schema Definition (RFC 7643)
/// </summary>
public class ScimSchema
{
    [JsonPropertyName("schemas")]
    public List<string> Schemas { get; set; } = new() { ScimConstants.Schemas.Schema };

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("attributes")]
    public List<ScimSchemaAttribute> Attributes { get; set; } = new();

    [JsonPropertyName("meta")]
    public ScimMeta? Meta { get; set; }
}

public class ScimSchemaAttribute
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = "string";

    [JsonPropertyName("multiValued")]
    public bool MultiValued { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("required")]
    public bool Required { get; set; }

    [JsonPropertyName("caseExact")]
    public bool CaseExact { get; set; }

    [JsonPropertyName("mutability")]
    public string Mutability { get; set; } = "readWrite";

    [JsonPropertyName("returned")]
    public string Returned { get; set; } = "default";

    [JsonPropertyName("uniqueness")]
    public string Uniqueness { get; set; } = "none";

    [JsonPropertyName("subAttributes")]
    public List<ScimSchemaAttribute>? SubAttributes { get; set; }

    [JsonPropertyName("canonicalValues")]
    public List<string>? CanonicalValues { get; set; }

    [JsonPropertyName("referenceTypes")]
    public List<string>? ReferenceTypes { get; set; }
}

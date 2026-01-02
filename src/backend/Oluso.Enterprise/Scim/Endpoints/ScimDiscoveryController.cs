using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oluso.Enterprise.Scim.Models;

namespace Oluso.Enterprise.Scim.Endpoints;

/// <summary>
/// SCIM 2.0 Discovery endpoints (RFC 7644)
/// </summary>
[ApiController]
[Route("scim/v2")]
[Produces(ScimConstants.ContentTypes.Scim, ScimConstants.ContentTypes.Json)]
[AllowAnonymous] // Discovery endpoints are typically public
public class ScimDiscoveryController : ControllerBase
{
    private string BaseUrl => $"{Request.Scheme}://{Request.Host}{Request.PathBase}/scim/v2";

    /// <summary>
    /// Service Provider Configuration
    /// </summary>
    [HttpGet("ServiceProviderConfig")]
    public IActionResult GetServiceProviderConfig()
    {
        var config = new ScimServiceProviderConfig
        {
            DocumentationUri = "https://tools.ietf.org/html/rfc7644",
            Patch = new ScimSupported { Supported = true },
            Bulk = new ScimBulkConfig
            {
                Supported = true,
                MaxOperations = 1000,
                MaxPayloadSize = 1048576
            },
            Filter = new ScimFilterConfig
            {
                Supported = true,
                MaxResults = 200
            },
            ChangePassword = new ScimSupported { Supported = true },
            Sort = new ScimSupported { Supported = true },
            Etag = new ScimSupported { Supported = false },
            AuthenticationSchemes = new List<ScimAuthenticationScheme>
            {
                new()
                {
                    Type = "oauthbearertoken",
                    Name = "OAuth Bearer Token",
                    Description = "Authentication using an OAuth 2.0 bearer token or API key",
                    SpecUri = "https://tools.ietf.org/html/rfc6750",
                    Primary = true
                }
            },
            Meta = new ScimMeta
            {
                ResourceType = "ServiceProviderConfig",
                Location = $"{BaseUrl}/ServiceProviderConfig"
            }
        };

        return Ok(config);
    }

    /// <summary>
    /// Resource Types
    /// </summary>
    [HttpGet("ResourceTypes")]
    public IActionResult GetResourceTypes()
    {
        var resourceTypes = new List<ScimResourceType>
        {
            new()
            {
                Id = "User",
                Name = "User",
                Description = "User Account",
                Endpoint = "/Users",
                Schema = ScimConstants.Schemas.User,
                Meta = new ScimMeta
                {
                    ResourceType = "ResourceType",
                    Location = $"{BaseUrl}/ResourceTypes/User"
                }
            },
            new()
            {
                Id = "Group",
                Name = "Group",
                Description = "Group",
                Endpoint = "/Groups",
                Schema = ScimConstants.Schemas.Group,
                Meta = new ScimMeta
                {
                    ResourceType = "ResourceType",
                    Location = $"{BaseUrl}/ResourceTypes/Group"
                }
            }
        };

        var response = new ScimListResponse<ScimResourceType>
        {
            TotalResults = resourceTypes.Count,
            ItemsPerPage = resourceTypes.Count,
            Resources = resourceTypes,
            // Override schemas for this response type
            Schemas = new List<string> { ScimConstants.Schemas.ListResponse }
        };

        return Ok(response);
    }

    /// <summary>
    /// Get a specific resource type
    /// </summary>
    [HttpGet("ResourceTypes/{name}")]
    public IActionResult GetResourceType(string name)
    {
        return name.ToLowerInvariant() switch
        {
            "user" => Ok(new ScimResourceType
            {
                Id = "User",
                Name = "User",
                Description = "User Account",
                Endpoint = "/Users",
                Schema = ScimConstants.Schemas.User,
                Meta = new ScimMeta
                {
                    ResourceType = "ResourceType",
                    Location = $"{BaseUrl}/ResourceTypes/User"
                }
            }),
            "group" => Ok(new ScimResourceType
            {
                Id = "Group",
                Name = "Group",
                Description = "Group",
                Endpoint = "/Groups",
                Schema = ScimConstants.Schemas.Group,
                Meta = new ScimMeta
                {
                    ResourceType = "ResourceType",
                    Location = $"{BaseUrl}/ResourceTypes/Group"
                }
            }),
            _ => NotFound(ScimError.NotFound($"ResourceType '{name}' not found"))
        };
    }

    /// <summary>
    /// Schemas
    /// </summary>
    [HttpGet("Schemas")]
    public IActionResult GetSchemas()
    {
        var schemas = new List<ScimSchema>
        {
            GetUserSchema(),
            GetGroupSchema()
        };

        var response = new ScimListResponse<ScimSchema>
        {
            TotalResults = schemas.Count,
            ItemsPerPage = schemas.Count,
            Resources = schemas
        };

        response.Schemas = new List<string> { ScimConstants.Schemas.ListResponse };

        return Ok(response);
    }

    /// <summary>
    /// Get a specific schema
    /// </summary>
    [HttpGet("Schemas/{id}")]
    public IActionResult GetSchema(string id)
    {
        return id switch
        {
            ScimConstants.Schemas.User => Ok(GetUserSchema()),
            ScimConstants.Schemas.Group => Ok(GetGroupSchema()),
            _ => NotFound(ScimError.NotFound($"Schema '{id}' not found"))
        };
    }

    private ScimSchema GetUserSchema()
    {
        return new ScimSchema
        {
            Id = ScimConstants.Schemas.User,
            Name = "User",
            Description = "User Account",
            Attributes = new List<ScimSchemaAttribute>
            {
                new() { Name = "userName", Type = "string", MultiValued = false, Required = true, CaseExact = false, Mutability = "readWrite", Returned = "default", Uniqueness = "server" },
                new() { Name = "name", Type = "complex", MultiValued = false, Required = false, SubAttributes = new List<ScimSchemaAttribute>
                {
                    new() { Name = "formatted", Type = "string" },
                    new() { Name = "familyName", Type = "string" },
                    new() { Name = "givenName", Type = "string" },
                    new() { Name = "middleName", Type = "string" },
                    new() { Name = "honorificPrefix", Type = "string" },
                    new() { Name = "honorificSuffix", Type = "string" }
                }},
                new() { Name = "displayName", Type = "string" },
                new() { Name = "nickName", Type = "string" },
                new() { Name = "profileUrl", Type = "reference", ReferenceTypes = new List<string> { "external" } },
                new() { Name = "title", Type = "string" },
                new() { Name = "userType", Type = "string" },
                new() { Name = "preferredLanguage", Type = "string" },
                new() { Name = "locale", Type = "string" },
                new() { Name = "timezone", Type = "string" },
                new() { Name = "active", Type = "boolean" },
                new() { Name = "password", Type = "string", Mutability = "writeOnly", Returned = "never" },
                new() { Name = "emails", Type = "complex", MultiValued = true, SubAttributes = new List<ScimSchemaAttribute>
                {
                    new() { Name = "value", Type = "string" },
                    new() { Name = "display", Type = "string" },
                    new() { Name = "type", Type = "string", CanonicalValues = new List<string> { "work", "home", "other" } },
                    new() { Name = "primary", Type = "boolean" }
                }},
                new() { Name = "phoneNumbers", Type = "complex", MultiValued = true, SubAttributes = new List<ScimSchemaAttribute>
                {
                    new() { Name = "value", Type = "string" },
                    new() { Name = "display", Type = "string" },
                    new() { Name = "type", Type = "string", CanonicalValues = new List<string> { "work", "home", "mobile", "fax", "pager", "other" } },
                    new() { Name = "primary", Type = "boolean" }
                }},
                new() { Name = "groups", Type = "complex", MultiValued = true, Mutability = "readOnly", SubAttributes = new List<ScimSchemaAttribute>
                {
                    new() { Name = "value", Type = "string", Mutability = "readOnly" },
                    new() { Name = "$ref", Type = "reference", Mutability = "readOnly" },
                    new() { Name = "display", Type = "string", Mutability = "readOnly" },
                    new() { Name = "type", Type = "string", Mutability = "readOnly", CanonicalValues = new List<string> { "direct", "indirect" } }
                }}
            },
            Meta = new ScimMeta
            {
                ResourceType = "Schema",
                Location = $"{BaseUrl}/Schemas/{ScimConstants.Schemas.User}"
            }
        };
    }

    private ScimSchema GetGroupSchema()
    {
        return new ScimSchema
        {
            Id = ScimConstants.Schemas.Group,
            Name = "Group",
            Description = "Group",
            Attributes = new List<ScimSchemaAttribute>
            {
                new() { Name = "displayName", Type = "string", Required = true },
                new() { Name = "members", Type = "complex", MultiValued = true, SubAttributes = new List<ScimSchemaAttribute>
                {
                    new() { Name = "value", Type = "string" },
                    new() { Name = "$ref", Type = "reference" },
                    new() { Name = "display", Type = "string" },
                    new() { Name = "type", Type = "string", CanonicalValues = new List<string> { "User", "Group" } }
                }}
            },
            Meta = new ScimMeta
            {
                ResourceType = "Schema",
                Location = $"{BaseUrl}/Schemas/{ScimConstants.Schemas.Group}"
            }
        };
    }
}

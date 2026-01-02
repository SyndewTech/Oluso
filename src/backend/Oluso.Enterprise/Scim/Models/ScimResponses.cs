using System.Text.Json.Serialization;

namespace Oluso.Enterprise.Scim.Models;

/// <summary>
/// SCIM List Response (RFC 7644)
/// </summary>
public class ScimListResponse<T>
{
    [JsonPropertyName("schemas")]
    public List<string> Schemas { get; set; } = new() { ScimConstants.Schemas.ListResponse };

    [JsonPropertyName("totalResults")]
    public int TotalResults { get; set; }

    [JsonPropertyName("startIndex")]
    public int StartIndex { get; set; } = 1;

    [JsonPropertyName("itemsPerPage")]
    public int ItemsPerPage { get; set; }

    [JsonPropertyName("Resources")]
    public List<T> Resources { get; set; } = new();
}

/// <summary>
/// SCIM Error Response (RFC 7644)
/// </summary>
public class ScimError
{
    [JsonPropertyName("schemas")]
    public List<string> Schemas { get; set; } = new() { ScimConstants.Schemas.Error };

    [JsonPropertyName("scimType")]
    public string? ScimType { get; set; }

    [JsonPropertyName("detail")]
    public string? Detail { get; set; }

    [JsonPropertyName("status")]
    public int Status { get; set; }

    public static ScimError NotFound(string? detail = null) => new()
    {
        Status = 404,
        Detail = detail ?? "Resource not found"
    };

    public static ScimError BadRequest(string? detail = null, string? scimType = null) => new()
    {
        Status = 400,
        ScimType = scimType,
        Detail = detail ?? "Invalid request"
    };

    public static ScimError Conflict(string? detail = null) => new()
    {
        Status = 409,
        ScimType = "uniqueness",
        Detail = detail ?? "Resource already exists"
    };

    public static ScimError InvalidFilter(string? detail = null) => new()
    {
        Status = 400,
        ScimType = "invalidFilter",
        Detail = detail ?? "Invalid filter"
    };

    public static ScimError InvalidSyntax(string? detail = null) => new()
    {
        Status = 400,
        ScimType = "invalidSyntax",
        Detail = detail ?? "Invalid syntax"
    };

    public static ScimError InvalidValue(string? detail = null) => new()
    {
        Status = 400,
        ScimType = "invalidValue",
        Detail = detail ?? "Invalid value"
    };

    public static ScimError Mutability(string? detail = null) => new()
    {
        Status = 400,
        ScimType = "mutability",
        Detail = detail ?? "Attribute is read-only"
    };

    public static ScimError TooMany(string? detail = null) => new()
    {
        Status = 400,
        ScimType = "tooMany",
        Detail = detail ?? "Too many results"
    };

    public static ScimError InternalError(string? detail = null) => new()
    {
        Status = 500,
        Detail = detail ?? "Internal server error"
    };
}

/// <summary>
/// SCIM Patch Operation (RFC 7644)
/// </summary>
public class ScimPatchRequest
{
    [JsonPropertyName("schemas")]
    public List<string> Schemas { get; set; } = new() { ScimConstants.Schemas.PatchOp };

    [JsonPropertyName("Operations")]
    public List<ScimPatchOperation> Operations { get; set; } = new();
}

/// <summary>
/// Individual SCIM Patch Operation
/// </summary>
public class ScimPatchOperation
{
    [JsonPropertyName("op")]
    public string Op { get; set; } = string.Empty;

    [JsonPropertyName("path")]
    public string? Path { get; set; }

    [JsonPropertyName("value")]
    public object? Value { get; set; }
}

/// <summary>
/// SCIM Bulk Request (RFC 7644)
/// </summary>
public class ScimBulkRequest
{
    [JsonPropertyName("schemas")]
    public List<string> Schemas { get; set; } = new() { ScimConstants.Schemas.BulkRequest };

    [JsonPropertyName("failOnErrors")]
    public int? FailOnErrors { get; set; }

    [JsonPropertyName("Operations")]
    public List<ScimBulkOperation> Operations { get; set; } = new();
}

/// <summary>
/// SCIM Bulk Operation
/// </summary>
public class ScimBulkOperation
{
    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    [JsonPropertyName("bulkId")]
    public string? BulkId { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("path")]
    public string Path { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public object? Data { get; set; }
}

/// <summary>
/// SCIM Bulk Response
/// </summary>
public class ScimBulkResponse
{
    [JsonPropertyName("schemas")]
    public List<string> Schemas { get; set; } = new() { ScimConstants.Schemas.BulkResponse };

    [JsonPropertyName("Operations")]
    public List<ScimBulkOperationResponse> Operations { get; set; } = new();
}

/// <summary>
/// Individual SCIM Bulk Operation Response
/// </summary>
public class ScimBulkOperationResponse
{
    [JsonPropertyName("method")]
    public string Method { get; set; } = string.Empty;

    [JsonPropertyName("bulkId")]
    public string? BulkId { get; set; }

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("location")]
    public string? Location { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = string.Empty;

    [JsonPropertyName("response")]
    public object? Response { get; set; }
}

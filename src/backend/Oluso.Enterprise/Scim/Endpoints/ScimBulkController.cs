using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Oluso.Enterprise.Scim.Models;
using Oluso.Enterprise.Scim.Services;

namespace Oluso.Enterprise.Scim.Endpoints;

/// <summary>
/// SCIM 2.0 Bulk endpoint (RFC 7644 §3.7). Processes a batch of operations
/// sequentially, resolves <c>bulkId</c> cross-references between operations,
/// and honours <c>failOnErrors</c>.
/// </summary>
[ApiController]
[Route("scim/v2/[controller]")]
[Produces(ScimConstants.ContentTypes.Scim, ScimConstants.ContentTypes.Json)]
public class BulkController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly IScimProvisioningService _service;
    private readonly IScimContextAccessor _scimContext;
    private readonly ScimOptions _options;

    public BulkController(IScimProvisioningService service, IScimContextAccessor scimContext, ScimOptions options)
    {
        _service = service;
        _scimContext = scimContext;
        _options = options;
    }

    private string BaseUrl => $"{Request.Scheme}://{Request.Host}{Request.PathBase}/scim/v2";

    [HttpPost]
    public async Task<IActionResult> Bulk([FromBody] ScimBulkRequest request)
    {
        var client = _scimContext.Client;
        if (client == null) return Unauthorized();

        if (request.Operations.Count > _options.MaxBulkOperations)
        {
            return StatusCode(413, ScimError.BadRequest(
                $"Number of operations ({request.Operations.Count}) exceeds maximum ({_options.MaxBulkOperations})",
                "tooMany"));
        }

        var response = new ScimBulkResponse();
        var bulkIdMap = new Dictionary<string, string>(StringComparer.Ordinal);
        var errorCount = 0;

        foreach (var op in request.Operations)
        {
            var opResponse = await ProcessOperationAsync(client, op, bulkIdMap);
            response.Operations.Add(opResponse);

            if (!IsSuccessStatus(opResponse.Status))
            {
                errorCount++;
                if (request.FailOnErrors.HasValue && errorCount >= request.FailOnErrors.Value)
                    break;
            }
        }

        return StatusCode(200, response);
    }

    private async Task<ScimBulkOperationResponse> ProcessOperationAsync(
        Entities.ScimClient client, ScimBulkOperation op, Dictionary<string, string> bulkIdMap)
    {
        var method = op.Method?.ToUpperInvariant() ?? "";
        var path = ResolveBulkReferences(op.Path, bulkIdMap);

        var (resourceType, resourceId) = ParsePath(path);

        if (resourceType is not ("Users" or "Groups"))
            return ErrorResponse(op, ScimError.BadRequest($"Unsupported bulk path '{op.Path}'", "invalidPath"));

        ScimOperationResult result;
        try
        {
            result = await DispatchAsync(client, method, resourceType, resourceId, op, bulkIdMap);
        }
        catch (Exception ex)
        {
            result = ScimOperationResult.FromError(ScimError.InternalError(ex.Message));
        }

        // A successful create maps its bulkId to the new internal resource id so
        // later operations in this batch can reference it via "bulkId:<id>".
        if (method == "POST" && result.IsSuccess && !string.IsNullOrEmpty(op.BulkId) && result.Location != null)
        {
            var createdId = result.Location.Split('/').LastOrDefault();
            if (!string.IsNullOrEmpty(createdId))
                bulkIdMap[op.BulkId] = createdId;
        }

        return new ScimBulkOperationResponse
        {
            Method = op.Method ?? "",
            BulkId = op.BulkId,
            Location = result.Location,
            Status = result.Status.ToString(),
            Response = result.IsSuccess ? null : result.Body
        };
    }

    private async Task<ScimOperationResult> DispatchAsync(
        Entities.ScimClient client, string method, string resourceType, string? id,
        ScimBulkOperation op, Dictionary<string, string> bulkIdMap)
    {
        var isUser = resourceType == "Users";

        switch (method)
        {
            case "POST":
                return isUser
                    ? await _service.CreateUserAsync(client, DeserializeUser(op, bulkIdMap), BaseUrl)
                    : await _service.CreateGroupAsync(client, DeserializeGroup(op, bulkIdMap), BaseUrl);

            case "PUT":
                if (string.IsNullOrEmpty(id)) return MissingId();
                return isUser
                    ? await _service.ReplaceUserAsync(client, id, DeserializeUser(op, bulkIdMap), BaseUrl)
                    : await _service.ReplaceGroupAsync(client, id, DeserializeGroup(op, bulkIdMap), BaseUrl);

            case "PATCH":
                if (string.IsNullOrEmpty(id)) return MissingId();
                return isUser
                    ? await _service.PatchUserAsync(client, id, DeserializePatch(op, bulkIdMap), BaseUrl)
                    : await _service.PatchGroupAsync(client, id, DeserializePatch(op, bulkIdMap), BaseUrl);

            case "DELETE":
                if (string.IsNullOrEmpty(id)) return MissingId();
                return isUser
                    ? await _service.DeleteUserAsync(client, id)
                    : await _service.DeleteGroupAsync(client, id);

            default:
                return ScimOperationResult.FromError(ScimError.BadRequest($"Unsupported bulk method '{op.Method}'", "invalidValue"));
        }
    }

    private static ScimOperationResult MissingId() =>
        ScimOperationResult.FromError(ScimError.BadRequest("Operation requires a resource id in the path", "invalidPath"));

    private static ScimBulkOperationResponse ErrorResponse(ScimBulkOperation op, ScimError error) => new()
    {
        Method = op.Method ?? "",
        BulkId = op.BulkId,
        Status = error.Status.ToString(),
        Response = error
    };

    private ScimUser DeserializeUser(ScimBulkOperation op, Dictionary<string, string> bulkIdMap) =>
        DeserializeData<ScimUser>(op, bulkIdMap) ?? new ScimUser();

    private ScimGroup DeserializeGroup(ScimBulkOperation op, Dictionary<string, string> bulkIdMap) =>
        DeserializeData<ScimGroup>(op, bulkIdMap) ?? new ScimGroup();

    private ScimPatchRequest DeserializePatch(ScimBulkOperation op, Dictionary<string, string> bulkIdMap) =>
        DeserializeData<ScimPatchRequest>(op, bulkIdMap) ?? new ScimPatchRequest();

    private static T? DeserializeData<T>(ScimBulkOperation op, Dictionary<string, string> bulkIdMap)
    {
        if (op.Data is null) return default;

        var raw = op.Data is JsonElement je ? je.GetRawText() : JsonSerializer.Serialize(op.Data);
        raw = ResolveBulkReferences(raw, bulkIdMap);
        return JsonSerializer.Deserialize<T>(raw, JsonOptions);
    }

    private static (string ResourceType, string? Id) ParsePath(string? path)
    {
        var segments = (path ?? "").Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0) return ("", null);
        return (segments[0], segments.Length > 1 ? segments[1] : null);
    }

    // Replaces "bulkId:<key>" tokens with the resolved internal id. Longer keys
    // first so one key cannot be partially matched inside another.
    private static string ResolveBulkReferences(string? text, Dictionary<string, string> bulkIdMap)
    {
        if (string.IsNullOrEmpty(text) || bulkIdMap.Count == 0) return text ?? "";

        var result = text;
        foreach (var (key, value) in bulkIdMap.OrderByDescending(kv => kv.Key.Length))
            result = result.Replace($"bulkId:{key}", value, StringComparison.Ordinal);

        return result;
    }

    private static bool IsSuccessStatus(string status) =>
        int.TryParse(status, out var code) && code is >= 200 and < 300;
}

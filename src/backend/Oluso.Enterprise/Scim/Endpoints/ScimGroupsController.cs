using Microsoft.AspNetCore.Mvc;
using Oluso.Enterprise.Scim.Models;
using Oluso.Enterprise.Scim.Services;

namespace Oluso.Enterprise.Scim.Endpoints;

/// <summary>
/// SCIM 2.0 Groups endpoint (RFC 7644)
/// </summary>
[ApiController]
[Route("scim/v2/[controller]")]
[Produces(ScimConstants.ContentTypes.Scim, ScimConstants.ContentTypes.Json)]
public class GroupsController : ControllerBase
{
    private readonly IScimProvisioningService _service;
    private readonly IScimContextAccessor _scimContext;

    public GroupsController(IScimProvisioningService service, IScimContextAccessor scimContext)
    {
        _service = service;
        _scimContext = scimContext;
    }

    private string BaseUrl => $"{Request.Scheme}://{Request.Host}{Request.PathBase}/scim/v2";

    /// <summary>
    /// Get a group by ID (internal ID or mapped external ID)
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetGroup(string id)
    {
        var client = _scimContext.Client;
        if (client == null) return Unauthorized();

        return this.ScimResult(await _service.GetGroupAsync(client, id, BaseUrl));
    }

    /// <summary>
    /// Search/list groups
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> SearchGroups(
        [FromQuery] string? filter = null,
        [FromQuery] int startIndex = 1,
        [FromQuery] int count = 100,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortOrder = null)
    {
        var client = _scimContext.Client;
        if (client == null) return Unauthorized();

        return this.ScimResult(await _service.SearchGroupsAsync(client, filter, startIndex, count, sortBy, sortOrder, BaseUrl));
    }

    /// <summary>
    /// Create a new group
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateGroup([FromBody] ScimGroup scimGroup)
    {
        var client = _scimContext.Client;
        if (client == null) return Unauthorized();

        return this.ScimResult(await _service.CreateGroupAsync(client, scimGroup, BaseUrl));
    }

    /// <summary>
    /// Replace a group (full update)
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> ReplaceGroup(string id, [FromBody] ScimGroup scimGroup)
    {
        var client = _scimContext.Client;
        if (client == null) return Unauthorized();

        return this.ScimResult(await _service.ReplaceGroupAsync(client, id, scimGroup, BaseUrl));
    }

    /// <summary>
    /// Partial update a group (PATCH)
    /// </summary>
    [HttpPatch("{id}")]
    public async Task<IActionResult> PatchGroup(string id, [FromBody] ScimPatchRequest patchRequest)
    {
        var client = _scimContext.Client;
        if (client == null) return Unauthorized();

        return this.ScimResult(await _service.PatchGroupAsync(client, id, patchRequest, BaseUrl));
    }

    /// <summary>
    /// Delete a group
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteGroup(string id)
    {
        var client = _scimContext.Client;
        if (client == null) return Unauthorized();

        return this.ScimResult(await _service.DeleteGroupAsync(client, id));
    }
}

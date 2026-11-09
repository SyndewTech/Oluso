using Microsoft.AspNetCore.Mvc;
using Oluso.Enterprise.Scim.Models;
using Oluso.Enterprise.Scim.Services;

namespace Oluso.Enterprise.Scim.Endpoints;

/// <summary>
/// SCIM 2.0 Users endpoint (RFC 7644)
/// </summary>
[ApiController]
[Route("scim/v2/[controller]")]
[Produces(ScimConstants.ContentTypes.Scim, ScimConstants.ContentTypes.Json)]
public class UsersController : ControllerBase
{
    private readonly IScimProvisioningService _service;
    private readonly IScimContextAccessor _scimContext;

    public UsersController(IScimProvisioningService service, IScimContextAccessor scimContext)
    {
        _service = service;
        _scimContext = scimContext;
    }

    private string BaseUrl => $"{Request.Scheme}://{Request.Host}{Request.PathBase}/scim/v2";

    /// <summary>
    /// Get a user by ID (internal ID or mapped external ID)
    /// </summary>
    [HttpGet("{id}")]
    public async Task<IActionResult> GetUser(string id)
    {
        var client = _scimContext.Client;
        if (client == null) return Unauthorized();

        return this.ScimResult(await _service.GetUserAsync(client, id, BaseUrl));
    }

    /// <summary>
    /// Search/list users
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> SearchUsers(
        [FromQuery] string? filter = null,
        [FromQuery] int startIndex = 1,
        [FromQuery] int count = 100,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortOrder = null)
    {
        var client = _scimContext.Client;
        if (client == null) return Unauthorized();

        return this.ScimResult(await _service.SearchUsersAsync(client, filter, startIndex, count, sortBy, sortOrder, BaseUrl));
    }

    /// <summary>
    /// Create a new user
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> CreateUser([FromBody] ScimUser scimUser)
    {
        var client = _scimContext.Client;
        if (client == null) return Unauthorized();

        return this.ScimResult(await _service.CreateUserAsync(client, scimUser, BaseUrl));
    }

    /// <summary>
    /// Replace a user (full update)
    /// </summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> ReplaceUser(string id, [FromBody] ScimUser scimUser)
    {
        var client = _scimContext.Client;
        if (client == null) return Unauthorized();

        return this.ScimResult(await _service.ReplaceUserAsync(client, id, scimUser, BaseUrl));
    }

    /// <summary>
    /// Partial update a user (PATCH)
    /// </summary>
    [HttpPatch("{id}")]
    public async Task<IActionResult> PatchUser(string id, [FromBody] ScimPatchRequest patchRequest)
    {
        var client = _scimContext.Client;
        if (client == null) return Unauthorized();

        return this.ScimResult(await _service.PatchUserAsync(client, id, patchRequest, BaseUrl));
    }

    /// <summary>
    /// Delete a user
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteUser(string id)
    {
        var client = _scimContext.Client;
        if (client == null) return Unauthorized();

        return this.ScimResult(await _service.DeleteUserAsync(client, id));
    }
}

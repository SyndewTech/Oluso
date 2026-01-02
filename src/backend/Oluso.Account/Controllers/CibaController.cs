using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Oluso.Core.Api;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Services;

namespace Oluso.Account.Controllers;

/// <summary>
/// Account API for managing CIBA (Client Initiated Backchannel Authentication) requests.
/// Users access this to view and approve/deny pending authentication requests.
/// </summary>
[Route("api/account/ciba")]
public class CibaController : AccountBaseController
{
    private readonly ICibaService? _cibaService;
    private readonly ICibaStore? _cibaStore;
    private readonly IClientStore _clientStore;
    private readonly IOlusoEventService _eventService;
    private readonly ILogger<CibaController> _logger;

    public CibaController(
        ITenantContext tenantContext,
        IClientStore clientStore,
        IOlusoEventService eventService,
        ILogger<CibaController> logger,
        ICibaService? cibaService = null,
        ICibaStore? cibaStore = null) : base(tenantContext)
    {
        _cibaService = cibaService;
        _cibaStore = cibaStore;
        _clientStore = clientStore;
        _eventService = eventService;
        _logger = logger;
    }

    /// <summary>
    /// Get all pending CIBA requests for the current user
    /// </summary>
    [HttpGet("pending")]
    public async Task<ActionResult<CibaRequestListDto>> GetPendingRequests(CancellationToken cancellationToken)
    {
        if (_cibaStore == null)
        {
            return Ok(new CibaRequestListDto { Requests = new List<CibaRequestDto>(), TotalCount = 0 });
        }

        var requests = await _cibaStore.GetPendingBySubjectAsync(UserId, cancellationToken);

        var dtos = new List<CibaRequestDto>();
        foreach (var request in requests)
        {
            var client = await _clientStore.FindClientByIdAsync(request.ClientId, cancellationToken);
            dtos.Add(MapToDto(request, client));
        }

        return Ok(new CibaRequestListDto
        {
            Requests = dtos,
            TotalCount = dtos.Count
        });
    }

    /// <summary>
    /// Get a specific CIBA request by auth_req_id
    /// </summary>
    [HttpGet("{authReqId}")]
    public async Task<ActionResult<CibaRequestDto>> GetRequest(string authReqId, CancellationToken cancellationToken)
    {
        if (_cibaStore == null)
        {
            return NotFound(new { error = "CIBA not enabled" });
        }

        var request = await _cibaStore.GetByAuthReqIdAsync(authReqId, cancellationToken);

        if (request == null)
        {
            return NotFound(new { error = "Request not found or expired" });
        }

        // Verify the request belongs to this user
        if (request.SubjectId != UserId)
        {
            return NotFound(new { error = "Request not found" });
        }

        var client = await _clientStore.FindClientByIdAsync(request.ClientId, cancellationToken);
        return Ok(MapToDto(request, client));
    }

    /// <summary>
    /// Approve a CIBA request
    /// </summary>
    [HttpPost("{authReqId}/approve")]
    public async Task<IActionResult> ApproveRequest(string authReqId, CancellationToken cancellationToken)
    {
        if (_cibaService == null)
        {
            return NotFound(new { error = "CIBA not enabled" });
        }

        var sessionId = User.FindFirst("sid")?.Value;

        var success = await _cibaService.ApproveRequestAsync(authReqId, UserId, sessionId, cancellationToken);

        if (!success)
        {
            return BadRequest(new { error = "Unable to approve request. It may have expired or already been processed." });
        }

        _logger.LogInformation("User {UserId} approved CIBA request {AuthReqId}", UserId, authReqId);

        await _eventService.RaiseAsync(new CibaRequestApprovedEvent
        {
            SubjectId = UserId,
            TenantId = TenantId,
            AuthReqId = authReqId,
            SessionId = sessionId,
            IpAddress = ClientIp
        }, cancellationToken);

        return Ok(new { message = "Request approved" });
    }

    /// <summary>
    /// Deny a CIBA request
    /// </summary>
    [HttpPost("{authReqId}/deny")]
    public async Task<IActionResult> DenyRequest(string authReqId, CancellationToken cancellationToken)
    {
        if (_cibaService == null || _cibaStore == null)
        {
            return NotFound(new { error = "CIBA not enabled" });
        }

        // First verify the request belongs to this user
        var request = await _cibaStore.GetByAuthReqIdAsync(authReqId, cancellationToken);
        if (request == null || request.SubjectId != UserId)
        {
            return NotFound(new { error = "Request not found" });
        }

        var success = await _cibaService.DenyRequestAsync(authReqId, cancellationToken);

        if (!success)
        {
            return BadRequest(new { error = "Unable to deny request. It may have expired or already been processed." });
        }

        _logger.LogInformation("User {UserId} denied CIBA request {AuthReqId}", UserId, authReqId);

        await _eventService.RaiseAsync(new CibaRequestDeniedEvent
        {
            SubjectId = UserId,
            TenantId = TenantId,
            AuthReqId = authReqId,
            IpAddress = ClientIp
        }, cancellationToken);

        return Ok(new { message = "Request denied" });
    }

    private static CibaRequestDto MapToDto(CibaRequest request, Core.Domain.Entities.Client? client) => new()
    {
        AuthReqId = request.AuthReqId,
        ClientId = request.ClientId,
        ClientName = client?.ClientName ?? request.ClientId,
        ClientLogoUri = client?.LogoUri,
        BindingMessage = request.BindingMessage,
        RequestedScopes = request.GetScopes().ToList(),
        Status = request.Status.ToString(),
        CreatedAt = request.CreatedAt,
        ExpiresAt = request.ExpiresAt
    };
}

#region DTOs

public class CibaRequestListDto
{
    public List<CibaRequestDto> Requests { get; set; } = new();
    public int TotalCount { get; set; }
}

public class CibaRequestDto
{
    public string AuthReqId { get; set; } = null!;
    public string ClientId { get; set; } = null!;
    public string? ClientName { get; set; }
    public string? ClientLogoUri { get; set; }
    public string? BindingMessage { get; set; }
    public List<string> RequestedScopes { get; set; } = new();
    public string Status { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}

#endregion

#region Events

public class CibaRequestApprovedEvent : OlusoEvent
{
    public override string Category => "Authentication";
    public override string EventType => "CibaRequestApproved";
    public string? SubjectId { get; set; }
    public string? AuthReqId { get; set; }
    public string? SessionId { get; set; }
    public string? IpAddress { get; set; }
}

public class CibaRequestDeniedEvent : OlusoEvent
{
    public override string Category => "Authentication";
    public override string EventType => "CibaRequestDenied";
    public string? SubjectId { get; set; }
    public string? AuthReqId { get; set; }
    public string? IpAddress { get; set; }
}

#endregion

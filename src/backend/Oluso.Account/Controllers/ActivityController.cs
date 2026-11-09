using Microsoft.AspNetCore.Mvc;
using Oluso.Account.Dto.Activity;
using Oluso.Core.Api;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Account.Controllers;

/// <summary>
/// Account API: the signed-in user's own recent account activity, sourced from the
/// audit log rows where they are the subject.
/// </summary>
[Route("api/account/activity")]
public class ActivityController : AccountBaseController
{
    private const int DefaultLimit = 50;
    private const int MaxLimit = 200;

    // Protocol plumbing that fires on every token refresh or authorize round-trip.
    // It is audit-worthy but meaningless as "my activity"; the security-relevant
    // outcomes (Login, LoginFailed, Lockout, ChangePassword, consent…) stay.
    private static readonly HashSet<string> ProtocolNoise = new(StringComparer.Ordinal)
    {
        "TokenRequest", "TokenIssued", "TokenIntrospected", "RefreshTokenUsed",
        "AuthorizationRequest", "AuthorizationCodeIssued",
    };

    private readonly IAuditLogService _auditLogService;

    public ActivityController(ITenantContext tenantContext, IAuditLogService auditLogService)
        : base(tenantContext)
    {
        _auditLogService = auditLogService;
    }

    /// <summary>
    /// Most recent activity for the current user, newest first.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<AccountActivityListDto>> Get(
        [FromQuery] int limit = DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        if (!_auditLogService.IsEnabled)
        {
            return Ok(new AccountActivityListDto { Enabled = false });
        }

        limit = Math.Clamp(limit, 1, MaxLimit);

        // The user's identity is platform-scoped and the audit sink attributes rows to
        // their home tenant, so query by subject alone rather than the request's tenant:
        // a user looking from a secondary tenant still sees their own trail.
        var logs = await _auditLogService.GetBySubjectAsync(null, UserId, MaxLimit, cancellationToken);

        var items = logs
            .Where(l => l.Action is null || !ProtocolNoise.Contains(l.Action))
            .Take(limit)
            .Select(l => new AccountActivityItemDto
            {
                Id = l.Id,
                Timestamp = l.Timestamp,
                Action = l.Action,
                Category = l.Category,
                EventType = l.EventType,
                ResourceType = l.ResourceType,
                ResourceName = l.ResourceName,
                ClientId = l.ClientId,
                IpAddress = l.IpAddress,
                UserAgent = l.UserAgent,
                Success = l.Success,
            })
            .ToList();

        return Ok(new AccountActivityListDto { Enabled = true, Items = items });
    }
}

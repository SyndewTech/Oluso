using Microsoft.AspNetCore.Mvc;
using Oluso.Core.Api;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Dto.Notifications;

namespace Oluso.Controllers;

/// <summary>
/// Per-user notification preferences for the authenticated workspace user.
/// One row per (TenantId, UserId); the endpoint upserts so callers never
/// have to know whether a row exists yet. Consumed by the workspace
/// "Snooze notifications", "Quiet hours", and "Muted events" UI surfaces.
///
/// <para>Consulted by <c>IUserNotificationGate</c> from any notification sink
/// (email, future SignalR/push) before delivery — so the data the user
/// edits here actually controls what they receive.</para>
/// </summary>
[Route("api/workspace/me/notification-profile")]
public class WorkspaceNotificationProfileController : WorkspaceBaseController
{
    private readonly IUserNotificationProfileStore _store;

    public WorkspaceNotificationProfileController(
        ITenantContext tenantContext,
        IUserNotificationProfileStore store)
        : base(tenantContext)
    {
        _store = store;
    }

    /// <summary>
    /// Returns the current user's profile. <c>null</c>-coalesced shape so the
    /// frontend never has to handle the 404-on-first-read case.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<UserNotificationProfileDto>> Get(CancellationToken cancellationToken)
    {
        var profile = await _store.GetAsync(TenantId, UserId, cancellationToken);
        return Ok(MapToDto(profile));
    }

    /// <summary>
    /// Replace the full preference set. PATCH semantics aren't needed — the
    /// profile is small and clients always send the complete state, which
    /// avoids ordering pitfalls (e.g. clearing snooze + setting quiet hours
    /// in one round trip).
    /// </summary>
    [HttpPut]
    public async Task<ActionResult<UserNotificationProfileDto>> Put(
        [FromBody] UserNotificationProfileDto request,
        CancellationToken cancellationToken)
    {
        // Validate quiet-hours range bounds. Wrap-around (start > end) is
        // valid by design — covers overnight windows like 22:00 → 07:00.
        if ((request.QuietHoursStartMinute.HasValue) !=
            (request.QuietHoursEndMinute.HasValue))
        {
            return BadRequest(new { error = "quietHoursStartMinute and quietHoursEndMinute must be set together" });
        }
        if (request.QuietHoursStartMinute is < 0 or >= 1440 ||
            request.QuietHoursEndMinute is < 0 or >= 1440)
        {
            return BadRequest(new { error = "quietHoursStartMinute / quietHoursEndMinute must be in [0, 1440)" });
        }

        var entity = new UserNotificationProfile
        {
            TenantId = TenantId,
            UserId = UserId,
            SnoozeUntilUtc = request.SnoozeUntilUtc,
            SnoozeReason = string.IsNullOrWhiteSpace(request.SnoozeReason) ? null : request.SnoozeReason.Trim(),
            QuietHoursStartMinute = request.QuietHoursStartMinute,
            QuietHoursEndMinute = request.QuietHoursEndMinute,
            TimeZoneId = string.IsNullOrWhiteSpace(request.TimeZoneId) ? null : request.TimeZoneId.Trim(),
            MutedEventTypes = string.IsNullOrWhiteSpace(request.MutedEventTypes) ? null : request.MutedEventTypes.Trim(),
        };

        var saved = await _store.UpsertAsync(entity, cancellationToken);
        return Ok(MapToDto(saved));
    }

    /// <summary>
    /// Convenience for the most common UI affordance: snooze everything for
    /// the next N minutes. Frontend sends the duration; server computes the
    /// absolute UTC instant so client-clock drift can't push the snooze
    /// past where the user expected.
    /// </summary>
    [HttpPost("snooze")]
    public async Task<ActionResult<UserNotificationProfileDto>> Snooze(
        [FromBody] SnoozeRequest request,
        CancellationToken cancellationToken)
    {
        if (request.DurationMinutes is < 1 or > 7 * 24 * 60)
        {
            return BadRequest(new { error = "durationMinutes must be between 1 and one week (10080)" });
        }

        var existing = await _store.GetAsync(TenantId, UserId, cancellationToken);
        var entity = existing ?? new UserNotificationProfile { TenantId = TenantId, UserId = UserId };
        entity.SnoozeUntilUtc = DateTime.UtcNow.AddMinutes(request.DurationMinutes);
        entity.SnoozeReason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim();

        var saved = await _store.UpsertAsync(entity, cancellationToken);
        return Ok(MapToDto(saved));
    }

    /// <summary>Clear the active snooze immediately.</summary>
    [HttpDelete("snooze")]
    public async Task<ActionResult<UserNotificationProfileDto>> ClearSnooze(CancellationToken cancellationToken)
    {
        var existing = await _store.GetAsync(TenantId, UserId, cancellationToken);
        if (existing == null) return Ok(MapToDto(null));

        existing.SnoozeUntilUtc = null;
        existing.SnoozeReason = null;
        var saved = await _store.UpsertAsync(existing, cancellationToken);
        return Ok(MapToDto(saved));
    }

    private static UserNotificationProfileDto MapToDto(UserNotificationProfile? profile)
    {
        if (profile == null)
        {
            return new UserNotificationProfileDto();
        }
        return new UserNotificationProfileDto
        {
            SnoozeUntilUtc = profile.SnoozeUntilUtc,
            SnoozeReason = profile.SnoozeReason,
            QuietHoursStartMinute = profile.QuietHoursStartMinute,
            QuietHoursEndMinute = profile.QuietHoursEndMinute,
            TimeZoneId = profile.TimeZoneId,
            MutedEventTypes = profile.MutedEventTypes,
        };
    }
}


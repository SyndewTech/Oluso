using Microsoft.EntityFrameworkCore;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.EntityFramework.Stores;

public class UserNotificationProfileStore : IUserNotificationProfileStore
{
    private readonly IOlusoDbContext _context;

    public UserNotificationProfileStore(IOlusoDbContext context)
    {
        _context = context;
    }

    public async Task<UserNotificationProfile?> GetAsync(
        string tenantId, string userId, CancellationToken cancellationToken = default)
    {
        return await _context.UserNotificationProfiles
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.UserId == userId, cancellationToken);
    }

    public async Task<UserNotificationProfile> UpsertAsync(
        UserNotificationProfile profile, CancellationToken cancellationToken = default)
    {
        var existing = await _context.UserNotificationProfiles
            .FirstOrDefaultAsync(p => p.TenantId == profile.TenantId && p.UserId == profile.UserId, cancellationToken);

        if (existing == null)
        {
            // First time the user touches their preferences — insert.
            _context.UserNotificationProfiles.Add(profile);
            await _context.SaveChangesAsync(cancellationToken);
            return profile;
        }

        // Preserve Id + CreatedAt; let caller overwrite preference fields.
        existing.SnoozeUntilUtc = profile.SnoozeUntilUtc;
        existing.SnoozeReason = profile.SnoozeReason;
        existing.QuietHoursStartMinute = profile.QuietHoursStartMinute;
        existing.QuietHoursEndMinute = profile.QuietHoursEndMinute;
        existing.TimeZoneId = profile.TimeZoneId;
        existing.MutedEventTypes = profile.MutedEventTypes;
        existing.UpdatedAt = DateTime.UtcNow;

        await _context.SaveChangesAsync(cancellationToken);
        return existing;
    }
}

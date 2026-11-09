using Oluso.Core.Domain.Entities;

namespace Oluso.Core.Domain.Interfaces;

/// <summary>
/// Read / write access to <see cref="UserNotificationProfile"/> rows.
/// Implemented in <c>Oluso.EntityFramework</c>; consumers depend on this
/// interface so a non-EF backend (e.g. an external preferences service)
/// can swap in without rippling.
///
/// <para>One row per <c>(TenantId, UserId)</c>. <see cref="UpsertAsync"/>
/// inserts on first save and updates thereafter — callers don't track the
/// row id. <see cref="GetAsync"/> returns <c>null</c> when no row exists,
/// which downstream gate logic treats as "deliver normally."</para>
/// </summary>
public interface IUserNotificationProfileStore
{
    Task<UserNotificationProfile?> GetAsync(
        string tenantId,
        string userId,
        CancellationToken cancellationToken = default);

    Task<UserNotificationProfile> UpsertAsync(
        UserNotificationProfile profile,
        CancellationToken cancellationToken = default);
}

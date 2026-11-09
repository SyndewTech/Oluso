namespace Oluso.Core.Services;

/// <summary>
/// Answers "where is this person rostered to work right now?" from a workforce roster. Implemented by a module that
/// owns rotas (HR); other modules consume it to scope what a user sees to the units they are on shift in. A module
/// that consumes it must work without it: no registration means no roster is available.
/// </summary>
public interface IStaffShiftProvider
{
    /// <summary>
    /// The units (Facilities space ids) the user is rostered on at <paramref name="atUtc"/>. Returns
    /// <see cref="StaffShiftLookup.NotRostered"/> when the user has no roster record at all (not an employee, or
    /// not linked to one), and an empty unit list when they are rostered but not on shift.
    /// </summary>
    Task<StaffShiftLookup> GetShiftUnitsAsync(string tenantId, string userId, DateTime atUtc, CancellationToken cancellationToken = default);
}

/// <summary>The outcome of a roster lookup.</summary>
public sealed record StaffShiftLookup(bool IsRostered, IReadOnlyCollection<string> UnitIds)
{
    public static readonly StaffShiftLookup NotRostered = new(false, Array.Empty<string>());
}

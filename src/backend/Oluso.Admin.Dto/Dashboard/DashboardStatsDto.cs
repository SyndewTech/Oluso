namespace Oluso.Admin.Dto.Dashboard;

public class DashboardStatsDto
{
    public int ClientsCount { get; set; }
    public int UsersCount { get; set; }
    public int ResourcesCount { get; set; }
    public int IdentityResourcesCount { get; set; }
    public int ActiveSessionsCount { get; set; }
    public int RecentLoginsCount { get; set; }
    public int JourneyPoliciesCount { get; set; }
    public int ActiveJourneysCount { get; set; }
    public int SubmissionsCount { get; set; }
    public int PendingSubmissionsCount { get; set; }
    public int FailedLoginsCount { get; set; }
    public int NewUsersCount { get; set; }
    public int PreviousLoginsCount { get; set; }
    public int PreviousFailedLoginsCount { get; set; }
    public int PreviousNewUsersCount { get; set; }
    public int SubmissionsToday { get; set; }
    public int SubmissionsYesterday { get; set; }
}

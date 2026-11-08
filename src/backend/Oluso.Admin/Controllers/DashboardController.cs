using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oluso.Core;
using Oluso.Core.Api;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Oluso.Admin.Dto.Dashboard;

namespace Oluso.Admin.Controllers;

/// <summary>
/// Dashboard API for admin UI statistics
/// </summary>
[Route("api/admin/dashboard")]
public class DashboardController : AdminBaseController
{
    private readonly IClientStore _clientStore;
    private readonly IResourceStore _resourceStore;
    private readonly IOlusoUserService _userService;
    private readonly IServerSideSessionStore _sessionStore;
    private readonly IAuditLogService _auditLogService;
    private readonly IJourneyPolicyStore _journeyPolicyStore;
    private readonly IJourneySubmissionStore _submissionStore;
    private readonly ILogger<DashboardController> _logger;

    public DashboardController(
        IClientStore clientStore,
        IResourceStore resourceStore,
        IOlusoUserService userService,
        ILogger<DashboardController> logger,
        ITenantContext tenantContext,
        IServerSideSessionStore sessionStore,
        IAuditLogService auditLogService,
        IJourneyPolicyStore journeyPolicyStore,
        IJourneySubmissionStore submissionStore) : base(tenantContext)
    {
        _clientStore = clientStore;
        _resourceStore = resourceStore;
        _userService = userService;
        _sessionStore = sessionStore;
        _auditLogService = auditLogService;
        _journeyPolicyStore = journeyPolicyStore;
        _submissionStore = submissionStore;
        _logger = logger;
    }

    /// <summary>
    /// Get dashboard statistics
    /// </summary>
    [HttpGet("stats")]
    public async Task<ActionResult<DashboardStatsDto>> GetStats(CancellationToken cancellationToken)
    {
        var clients = await _clientStore.GetAllClientsAsync(cancellationToken);
        var resources = await _resourceStore.GetAllResourcesAsync(cancellationToken);
        var identityResources = await _resourceStore.GetAllIdentityResourcesAsync(cancellationToken);

        // Get user count
        var usersResult = await _userService.GetUsersAsync(new UsersQuery { Page = 1, PageSize = 1 }, cancellationToken);

        // Get active sessions count (optional - may not be configured)
        var activeSessionsCount = 0;
        if (_sessionStore != null)
        {
            var (_, totalSessions) = await _sessionStore.GetAllSessionsAsync(0, 1, cancellationToken);
            activeSessionsCount = totalSessions;
        }

        // Get recent logins count (last 24 hours) and comparison to previous period
        var recentLoginsCount = 0;
        var failedLoginsCount = 0;
        var newUsersCount = 0;
        var previousLoginsCount = 0;
        var previousFailedLoginsCount = 0;
        var previousNewUsersCount = 0;

        if (_auditLogService?.IsEnabled == true)
        {
            var now = DateTime.UtcNow;
            var yesterday = now.AddHours(-24);
            var twoDaysAgo = now.AddHours(-48);

            // Tenant admins see their own tenant's activity; the platform tenant
            // dashboard keeps the cross-tenant view.
            var auditTenantId = string.Equals(TenantId, PlatformConstants.TenantId, StringComparison.Ordinal)
                ? null
                : TenantId;

            // Current period (last 24h) - Successful logins
            var loginQuery = new AuditLogQuery
            {
                TenantId = auditTenantId,
                Action = "Login",
                StartDate = yesterday,
                Success = true,
                Page = 1,
                PageSize = 1
            };
            var loginResult = await _auditLogService.QueryAsync(loginQuery, cancellationToken);
            recentLoginsCount = loginResult.TotalCount;

            // Previous period (24-48h ago) - Successful logins
            var prevLoginQuery = new AuditLogQuery
            {
                TenantId = auditTenantId,
                Action = "Login",
                StartDate = twoDaysAgo,
                EndDate = yesterday,
                Success = true,
                Page = 1,
                PageSize = 1
            };
            var prevLoginResult = await _auditLogService.QueryAsync(prevLoginQuery, cancellationToken);
            previousLoginsCount = prevLoginResult.TotalCount;

            // Current period - Failed logins (recorded as LoginFailed by the audit sink)
            var failedLoginQuery = new AuditLogQuery
            {
                TenantId = auditTenantId,
                Action = "LoginFailed",
                StartDate = yesterday,
                Success = false,
                Page = 1,
                PageSize = 1
            };
            var failedLoginResult = await _auditLogService.QueryAsync(failedLoginQuery, cancellationToken);
            failedLoginsCount = failedLoginResult.TotalCount;

            // Previous period - Failed logins
            var prevFailedLoginQuery = new AuditLogQuery
            {
                TenantId = auditTenantId,
                Action = "LoginFailed",
                StartDate = twoDaysAgo,
                EndDate = yesterday,
                Success = false,
                Page = 1,
                PageSize = 1
            };
            var prevFailedLoginResult = await _auditLogService.QueryAsync(prevFailedLoginQuery, cancellationToken);
            previousFailedLoginsCount = prevFailedLoginResult.TotalCount;

            // Current period - New user registrations
            var newUserQuery = new AuditLogQuery
            {
                TenantId = auditTenantId,
                Action = "UserCreated",
                StartDate = yesterday,
                Success = true,
                Page = 1,
                PageSize = 1
            };
            var newUserResult = await _auditLogService.QueryAsync(newUserQuery, cancellationToken);
            newUsersCount = newUserResult.TotalCount;

            // Previous period - New user registrations
            var prevNewUserQuery = new AuditLogQuery
            {
                TenantId = auditTenantId,
                Action = "UserCreated",
                StartDate = twoDaysAgo,
                EndDate = yesterday,
                Success = true,
                Page = 1,
                PageSize = 1
            };
            var prevNewUserResult = await _auditLogService.QueryAsync(prevNewUserQuery, cancellationToken);
            previousNewUsersCount = prevNewUserResult.TotalCount;
        }

        // Get journey policies count
        var journeyPoliciesCount = 0;
        var activeJourneysCount = 0;
        if (_journeyPolicyStore != null)
        {
            var policies = await _journeyPolicyStore.GetByTenantAsync(TenantId, cancellationToken);
            var policyList = policies.ToList();
            journeyPoliciesCount = policyList.Count;
            activeJourneysCount = policyList.Count(p => p.Enabled);
        }

        // Get submissions count
        var submissionsCount = 0;
        var pendingSubmissionsCount = 0;
        var submissionsToday = 0;
        var submissionsYesterday = 0;
        var todayStart = DateTime.UtcNow.Date;
        var yesterdayStart = todayStart.AddDays(-1);

        if (_journeyPolicyStore != null && _submissionStore != null)
        {
            // Get data collection policies and count their submissions
            var policies = await _journeyPolicyStore.GetByTenantAsync(TenantId, cancellationToken);
            var dataCollectionPolicies = policies.Where(p => p.PersistSubmissions).ToList();

            foreach (var policy in dataCollectionPolicies)
            {
                var policySubmissionCount = await _submissionStore.CountByPolicyAsync(policy.Id, cancellationToken);
                submissionsCount += policySubmissionCount;

                // Get all submissions for time-based counts
                var submissions = await _submissionStore.GetByPolicyAsync(policy.Id, 0, 10000, cancellationToken);
                var submissionList = submissions.ToList();

                pendingSubmissionsCount += submissionList.Count(s => s.Status == SubmissionStatus.New);
                submissionsToday += submissionList.Count(s => s.CreatedAt >= todayStart);
                submissionsYesterday += submissionList.Count(s => s.CreatedAt >= yesterdayStart && s.CreatedAt < todayStart);
            }
        }

        var stats = new DashboardStatsDto
        {
            ClientsCount = clients.Count(),
            UsersCount = usersResult.TotalCount,
            ResourcesCount = resources.Count(),
            IdentityResourcesCount = identityResources.Count(),
            ActiveSessionsCount = activeSessionsCount,
            RecentLoginsCount = recentLoginsCount,
            // Journey & submission stats
            JourneyPoliciesCount = journeyPoliciesCount,
            ActiveJourneysCount = activeJourneysCount,
            SubmissionsCount = submissionsCount,
            PendingSubmissionsCount = pendingSubmissionsCount,
            // Activity stats
            FailedLoginsCount = failedLoginsCount,
            NewUsersCount = newUsersCount,
            // Comparison stats
            PreviousLoginsCount = previousLoginsCount,
            PreviousFailedLoginsCount = previousFailedLoginsCount,
            PreviousNewUsersCount = previousNewUsersCount,
            SubmissionsToday = submissionsToday,
            SubmissionsYesterday = submissionsYesterday
        };

        return Ok(stats);
    }
}

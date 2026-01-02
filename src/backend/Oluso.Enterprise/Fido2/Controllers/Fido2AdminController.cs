using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Oluso.Core.Api;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;

namespace Oluso.Enterprise.Fido2.Controllers;

/// <summary>
/// Admin controller for FIDO2/WebAuthn credential management.
/// Allows administrators to manage passkeys for any user.
/// </summary>
[Route("api/admin/fido2")]
public class Fido2AdminController : AdminBaseController
{
    private readonly IFido2Service _fido2Service;
    private readonly IFido2CredentialStore _credentialStore;
    private readonly IOlusoUserService _userService;
    private readonly ILogger<Fido2AdminController> _logger;

    public Fido2AdminController(
        IFido2Service fido2Service,
        IFido2CredentialStore credentialStore,
        IOlusoUserService userService,
        ILogger<Fido2AdminController> logger,
        ITenantContext tenantContext) : base(tenantContext)
    {
        _fido2Service = fido2Service;
        _credentialStore = credentialStore;
        _userService = userService;
        _logger = logger;
    }

    /// <summary>
    /// Get FIDO2 credential statistics
    /// </summary>
    [HttpGet("stats")]
    public async Task<ActionResult<Fido2StatsResponse>> GetStats(CancellationToken cancellationToken = default)
    {
        var usersResult = await _userService.GetUsersAsync(new UsersQuery { Page = 1, PageSize = int.MaxValue }, cancellationToken);
        var now = DateTime.UtcNow;
        var thirtyDaysAgo = now.AddDays(-30);

        var totalCredentials = 0;
        var platformCredentials = 0;
        var crossPlatformCredentials = 0;
        var credentialsRegisteredLast30Days = 0;
        var credentialsUsedLast30Days = 0;
        var usersWithCredentials = new HashSet<string>();

        foreach (var user in usersResult.Users)
        {
            var credentials = await _fido2Service.GetCredentialsAsync(user.Id, cancellationToken);
            foreach (var cred in credentials)
            {
                totalCredentials++;
                usersWithCredentials.Add(user.Id);

                if (cred.AuthenticatorType?.ToLowerInvariant() == "platform")
                    platformCredentials++;
                else
                    crossPlatformCredentials++;

                if (cred.CreatedAt >= thirtyDaysAgo)
                    credentialsRegisteredLast30Days++;

                if (cred.LastUsedAt.HasValue && cred.LastUsedAt.Value >= thirtyDaysAgo)
                    credentialsUsedLast30Days++;
            }
        }

        return Ok(new Fido2StatsResponse
        {
            TotalCredentials = totalCredentials,
            TotalUsersWithCredentials = usersWithCredentials.Count,
            PlatformCredentials = platformCredentials,
            CrossPlatformCredentials = crossPlatformCredentials,
            CredentialsRegisteredLast30Days = credentialsRegisteredLast30Days,
            CredentialsUsedLast30Days = credentialsUsedLast30Days
        });
    }

    /// <summary>
    /// Get all users with FIDO2 credentials
    /// </summary>
    [HttpGet("users")]
    public async Task<ActionResult<Fido2UsersResponse>> GetUsersWithCredentials(
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var allUsersResult = await _userService.GetUsersAsync(new UsersQuery { Page = 1, PageSize = int.MaxValue, Search = search }, cancellationToken);
        var usersWithCredentials = new List<Fido2UserSummary>();

        foreach (var user in allUsersResult.Users)
        {
            var credentials = await _fido2Service.GetCredentialsAsync(user.Id, cancellationToken);
            var credentialList = credentials.ToList();

            if (credentialList.Count > 0)
            {
                usersWithCredentials.Add(new Fido2UserSummary
                {
                    UserId = user.Id,
                    UserName = user.Username,
                    DisplayName = user.DisplayName ?? user.Username,
                    Email = user.Email,
                    CredentialCount = credentialList.Count,
                    LastUsedAt = credentialList
                        .Where(c => c.LastUsedAt.HasValue)
                        .OrderByDescending(c => c.LastUsedAt)
                        .FirstOrDefault()?.LastUsedAt
                });
            }
        }

        // Apply pagination
        var totalCount = usersWithCredentials.Count;
        var paged = usersWithCredentials
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        return Ok(new Fido2UsersResponse
        {
            Users = paged,
            TotalCount = totalCount
        });
    }

    /// <summary>
    /// Get all FIDO2 credentials for a specific user
    /// </summary>
    [HttpGet("users/{userId}/credentials")]
    public async Task<ActionResult<IEnumerable<AdminCredentialInfo>>> GetUserCredentials(
        string userId,
        CancellationToken cancellationToken = default)
    {
        // Verify user exists
        var user = await _userService.FindByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            return NotFound(new { error = "User not found" });
        }

        var credentials = await _fido2Service.GetCredentialsAsync(userId, cancellationToken);

        return Ok(credentials.Select(c => new AdminCredentialInfo
        {
            Id = c.Id,
            UserId = c.UserId,
            CredentialId = c.CredentialId,
            DisplayName = c.Name,
            CreatedAt = c.CreatedAt,
            LastUsedAt = c.LastUsedAt,
            AuthenticatorType = c.AuthenticatorType ?? "unknown",
            IsResidentKey = c.IsResidentKey,
            SignCount = c.SignCount
        }));
    }

    /// <summary>
    /// Delete a specific credential for a user
    /// </summary>
    [HttpDelete("users/{userId}/credentials/{credentialId}")]
    public async Task<ActionResult> DeleteUserCredential(
        string userId,
        string credentialId,
        CancellationToken cancellationToken = default)
    {
        // Verify user exists
        var user = await _userService.FindByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            return NotFound(new { error = "User not found" });
        }

        var success = await _fido2Service.DeleteCredentialAsync(userId, credentialId, cancellationToken);

        if (!success)
        {
            return NotFound(new { error = "Credential not found" });
        }

        _logger.LogInformation(
            "Admin {AdminId} deleted FIDO2 credential {CredentialId} for user {UserId}",
            User.Identity?.Name ?? "unknown",
            credentialId,
            userId);

        return NoContent();
    }

    /// <summary>
    /// Delete all credentials for a user
    /// </summary>
    [HttpDelete("users/{userId}/credentials")]
    public async Task<ActionResult> DeleteAllUserCredentials(
        string userId,
        CancellationToken cancellationToken = default)
    {
        // Verify user exists
        var user = await _userService.FindByIdAsync(userId, cancellationToken);
        if (user == null)
        {
            return NotFound(new { error = "User not found" });
        }

        var credentials = await _fido2Service.GetCredentialsAsync(userId, cancellationToken);
        var deletedCount = 0;

        foreach (var credential in credentials)
        {
            if (await _fido2Service.DeleteCredentialAsync(userId, credential.Id, cancellationToken))
            {
                deletedCount++;
            }
        }

        _logger.LogInformation(
            "Admin {AdminId} deleted {Count} FIDO2 credentials for user {UserId}",
            User.Identity?.Name ?? "unknown",
            deletedCount,
            userId);

        return Ok(new { deletedCount });
    }
}

#region Admin DTOs

public class AdminCredentialInfo
{
    public string Id { get; set; } = null!;
    public string UserId { get; set; } = null!;
    public string CredentialId { get; set; } = null!;
    public string? DisplayName { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastUsedAt { get; set; }
    public string AuthenticatorType { get; set; } = null!;
    public bool IsResidentKey { get; set; }
    public int SignCount { get; set; }
}

public class Fido2StatsResponse
{
    public int TotalCredentials { get; set; }
    public int TotalUsersWithCredentials { get; set; }
    public int PlatformCredentials { get; set; }
    public int CrossPlatformCredentials { get; set; }
    public int CredentialsRegisteredLast30Days { get; set; }
    public int CredentialsUsedLast30Days { get; set; }
}

public class Fido2UserSummary
{
    public string UserId { get; set; } = null!;
    public string? UserName { get; set; }
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public int CredentialCount { get; set; }
    public DateTime? LastUsedAt { get; set; }
}

public class Fido2UsersResponse
{
    public List<Fido2UserSummary> Users { get; set; } = new();
    public int TotalCount { get; set; }
}

#endregion

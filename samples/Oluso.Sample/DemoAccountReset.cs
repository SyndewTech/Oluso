using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Jobs;
using Oluso.Core.Services;
using Oluso.EntityFramework;

namespace Oluso.Sample;

/// <summary>Settings for <see cref="DemoAccountReset"/> (section <c>Sample:DemoReset</c>).</summary>
public sealed class DemoAccountResetOptions
{
    public const string SectionName = "Sample:DemoReset";

    public bool Enabled { get; set; }

    /// <summary>How often the accounts are restored. The first reset runs when the app starts.</summary>
    public TimeSpan Interval { get; set; } = TimeSpan.FromDays(1);

    /// <summary>
    /// The super admin's password, seeded with it and put back by each reset. Kept out of the published
    /// test credentials, so visitors can't take over the platform while its owner can always sign in.
    /// </summary>
    public string? SuperAdminPassword { get; set; }

    public static DemoAccountResetOptions From(IConfiguration configuration) =>
        configuration.GetSection(SectionName).Get<DemoAccountResetOptions>() ?? new DemoAccountResetOptions();
}

/// <summary>
/// For a public demo: puts the sample's test accounts back as seeded on a schedule, so a visitor who
/// changes a password, turns on MFA, adds a passkey, changes the email or user name, disables the user,
/// or changes its roles, claims or organization memberships can't lock everyone else out or keep extra
/// access. A deleted account is created again.
/// </summary>
public sealed class DemoAccountReset(
    IServiceScopeFactory scopeFactory,
    DemoAccountResetOptions options,
    ILogger<DemoAccountReset> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(options.Interval);
        do
        {
            try
            {
                await ResetAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Demo reset failed; the next run in {Interval} tries again", options.Interval);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ResetAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OlusoDbContext>();
        var hasher = new PasswordHasher<OlusoUser>();

        foreach (var account in SeedData.TestAccounts)
        {
            var password = SeedData.TestAccountPassword(account, options);
            var tenantId = await SeedData.TestAccountTenantIdAsync(dbContext, account);
            var role = await SeedData.TestAccountRoleAsync(dbContext, account, cancellationToken);
            var normalized = account.Email.ToUpperInvariant();

            // By the fixed ID; by email or user name for accounts seeded before the IDs were fixed.
            var user = await dbContext.Users.IgnoreQueryFilters()
                .Where(u => u.Id == account.Id || (u.TenantId == tenantId && (u.NormalizedUserName == normalized || u.NormalizedEmail == normalized)))
                .OrderByDescending(u => u.Id == account.Id)
                .FirstOrDefaultAsync(cancellationToken);

            if (user == null)
            {
                await SeedData.AddTestAccountAsync(dbContext, account, password);
                await dbContext.SaveChangesAsync(cancellationToken);
                logger.LogInformation("Demo reset: recreated {Email}", account.Email);
                continue;
            }

            var userId = user.Id;
            user.UserName = account.Email;
            user.NormalizedUserName = normalized;
            user.Email = account.Email;
            user.NormalizedEmail = normalized;
            user.EmailConfirmed = true;
            user.FirstName = account.FirstName;
            user.LastName = account.LastName;
            user.DisplayName = account.DisplayName;
            user.IsActive = true;
            user.TwoFactorEnabled = false;
            user.LockoutEnd = null;
            user.AccessFailedCount = 0;
            // The known password isn't run through the tenant's validators: a password history policy would
            // refuse the same password every day.
            user.PasswordHash = hasher.HashPassword(user, password);
            user.PasswordChangedAt = DateTime.UtcNow;
            user.SecurityStamp = Guid.NewGuid().ToString();

            // Authenticator key and recovery codes (user tokens), external logins, and claims the seed doesn't give.
            dbContext.UserTokens.RemoveRange(await dbContext.UserTokens.IgnoreQueryFilters().Where(t => t.UserId == userId).ToListAsync(cancellationToken));
            dbContext.UserLogins.RemoveRange(await dbContext.UserLogins.IgnoreQueryFilters().Where(l => l.UserId == userId).ToListAsync(cancellationToken));
            dbContext.UserClaims.RemoveRange(await dbContext.UserClaims.IgnoreQueryFilters().Where(c => c.UserId == userId).ToListAsync(cancellationToken));

            // Sessions and refresh tokens in every tenant, saved with the password so neither survives without the other.
            dbContext.ServerSideSessions.RemoveRange(await dbContext.ServerSideSessions.IgnoreQueryFilters().Where(s => s.SubjectId == userId).ToListAsync(cancellationToken));
            dbContext.PersistedGrants.RemoveRange(await dbContext.PersistedGrants.IgnoreQueryFilters().Where(g => g.SubjectId == userId).ToListAsync(cancellationToken));

            // Exactly the seeded role assignment: the seeded role in the seeded tenant.
            var assignmentTenantId = SeedData.TestAccountRoleAssignmentTenantId(account, user, role);
            var assignments = await dbContext.Set<OlusoUserRole>().IgnoreQueryFilters().Where(ur => ur.UserId == userId).ToListAsync(cancellationToken);
            dbContext.Set<OlusoUserRole>().RemoveRange(assignments.Where(ur => ur.RoleId != role.Id || ur.TenantId != assignmentTenantId));
            if (!assignments.Any(ur => ur.RoleId == role.Id && ur.TenantId == assignmentTenantId))
            {
                dbContext.Set<OlusoUserRole>().Add(new OlusoUserRole
                {
                    UserId = userId,
                    RoleId = role.Id,
                    TenantId = assignmentTenantId,
                    AssignedAt = DateTime.UtcNow,
                    AssignedBy = "system"
                });
            }

            // Exactly the seeded organization membership: the org admin owns default-org, the others belong to none.
            var memberships = await dbContext.OrganizationMemberships.IgnoreQueryFilters().Where(m => m.UserId == userId).ToListAsync(cancellationToken);
            var seeded = account.RoleName == "OrgAdmin"
                ? memberships.FirstOrDefault(m => m.OrganizationId == SeedData.OrgAdminOrganizationId)
                : null;
            dbContext.OrganizationMemberships.RemoveRange(memberships.Where(m => m != seeded));
            if (account.RoleName == "OrgAdmin")
            {
                if (seeded == null)
                {
                    dbContext.OrganizationMemberships.Add(new OrganizationMembership
                    {
                        Id = Guid.NewGuid().ToString(),
                        OrganizationId = SeedData.OrgAdminOrganizationId,
                        UserId = userId,
                        Role = OrganizationRole.Owner,
                        JoinedAt = DateTime.UtcNow
                    });
                }
                else
                {
                    seeded.Role = OrganizationRole.Owner;
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            // Passkeys live in the FIDO2 module's tenant-scoped store; a failure here is retried by the next run.
            await TenantScope.RunAsync(scopeFactory, tenantId, async (services, ct) =>
            {
                var fido2 = services.GetService<IFido2Service>();
                if (fido2 == null)
                    return;
                foreach (var credential in await fido2.GetCredentialsAsync(userId, ct))
                    await fido2.DeleteCredentialAsync(userId, credential.Id, ct);
            }, cancellationToken);

            logger.LogInformation("Demo reset: restored {Email}", account.Email);
        }
    }
}

public static class DemoAccountResetExtensions
{
    /// <summary>
    /// Registers <see cref="DemoAccountReset"/> when <c>Sample:DemoReset:Enabled</c> is true.
    /// It then requires <c>Sample:DemoReset:SuperAdminPassword</c>.
    /// </summary>
    public static IServiceCollection AddDemoAccountReset(this IServiceCollection services, IConfiguration configuration)
    {
        var options = DemoAccountResetOptions.From(configuration);
        if (!options.Enabled)
            return services;

        if (string.IsNullOrWhiteSpace(options.SuperAdminPassword))
            throw new InvalidOperationException($"{DemoAccountResetOptions.SectionName}:Enabled is true: set {DemoAccountResetOptions.SectionName}:SuperAdminPassword.");
        if (options.Interval <= TimeSpan.Zero)
            throw new InvalidOperationException($"{DemoAccountResetOptions.SectionName}:Interval must be positive.");

        services.AddSingleton(options);
        services.AddHostedService<DemoAccountReset>();
        return services;
    }
}

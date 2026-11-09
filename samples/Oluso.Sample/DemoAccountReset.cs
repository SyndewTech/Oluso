using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
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
    /// The super admin's password after each reset. Kept out of the published test credentials, so
    /// visitors can't take over the platform while its owner can always sign in.
    /// </summary>
    public string? SuperAdminPassword { get; set; }
}

/// <summary>
/// For a public demo: puts the sample's test accounts back as seeded on a schedule, so a visitor who
/// changes a password, turns on MFA, adds a passkey, changes the email, disables the user or removes
/// its role can't lock everyone else out. Each account gets back its email and name, its password
/// (the super admin's from <see cref="DemoAccountResetOptions.SuperAdminPassword"/>), its role and,
/// for the org admin, its organization ownership; MFA, passkeys, external logins, lockout and
/// sessions are cleared. A deleted account is created again.
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
            await ResetAsync(stoppingToken);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ResetAsync(CancellationToken cancellationToken)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<OlusoDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<OlusoUser>>();
        var hasher = new PasswordHasher<OlusoUser>();

        foreach (var account in SeedData.TestAccounts)
        {
            var password = account.RoleName == "SuperAdmin" ? options.SuperAdminPassword! : account.Password;
            var tenantId = await SeedData.TestAccountTenantIdAsync(dbContext, account);
            var normalized = account.Email.ToUpperInvariant();

            var user = await dbContext.Users.IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => u.TenantId == tenantId && (u.NormalizedUserName == normalized || u.NormalizedEmail == normalized), cancellationToken);

            if (user == null)
            {
                user = await SeedData.AddTestAccountAsync(dbContext, account, password);
                await ValidatePasswordAsync(userManager, user, account, password);
                await dbContext.SaveChangesAsync(cancellationToken);
                logger.LogInformation("Demo reset: recreated {Email}", account.Email);
                continue;
            }

            await ValidatePasswordAsync(userManager, user, account, password);

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
            user.PasswordHash = hasher.HashPassword(user, password);
            user.PasswordChangedAt = null;
            user.SecurityStamp = Guid.NewGuid().ToString();

            // The authenticator key and recovery codes are user tokens; external logins could stand in for the password.
            dbContext.UserTokens.RemoveRange(await dbContext.UserTokens.IgnoreQueryFilters().Where(t => t.UserId == user.Id).ToListAsync(cancellationToken));
            dbContext.UserLogins.RemoveRange(await dbContext.UserLogins.IgnoreQueryFilters().Where(l => l.UserId == user.Id).ToListAsync(cancellationToken));

            var role = await dbContext.Roles.IgnoreQueryFilters().FirstAsync(r => r.Name == account.RoleName, cancellationToken);
            var assignments = await dbContext.Set<OlusoUserRole>().IgnoreQueryFilters().Where(ur => ur.UserId == user.Id).ToListAsync(cancellationToken);
            dbContext.Set<OlusoUserRole>().RemoveRange(assignments.Where(ur => ur.RoleId != role.Id));
            if (assignments.All(ur => ur.RoleId != role.Id))
            {
                dbContext.Set<OlusoUserRole>().Add(new OlusoUserRole
                {
                    UserId = user.Id,
                    RoleId = role.Id,
                    TenantId = SeedData.TestAccountRoleTenantId(account, user, role),
                    AssignedAt = DateTime.UtcNow,
                    AssignedBy = "system"
                });
            }

            if (account.RoleName == "OrgAdmin")
            {
                var membership = await dbContext.OrganizationMemberships.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(m => m.UserId == user.Id && m.OrganizationId == SeedData.OrgAdminOrganizationId, cancellationToken);
                if (membership == null)
                {
                    dbContext.OrganizationMemberships.Add(new OrganizationMembership
                    {
                        Id = Guid.NewGuid().ToString(),
                        OrganizationId = SeedData.OrgAdminOrganizationId,
                        UserId = user.Id,
                        Role = OrganizationRole.Owner,
                        JoinedAt = DateTime.UtcNow
                    });
                }
                else
                {
                    membership.Role = OrganizationRole.Owner;
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken);

            // Sessions, refresh tokens and passkeys are kept by tenant-scoped stores.
            var userId = user.Id;
            await TenantScope.RunAsync(scopeFactory, tenantId, async (services, ct) =>
            {
                await services.GetRequiredService<IPersistedGrantStore>().RemoveAllAsync(new PersistedGrantFilter { SubjectId = userId }, ct);

                var fido2 = services.GetService<IFido2Service>();
                if (fido2 != null)
                {
                    foreach (var credential in await fido2.GetCredentialsAsync(userId, ct))
                        await fido2.DeleteCredentialAsync(userId, credential.Id, ct);
                }
            }, cancellationToken);

            logger.LogInformation("Demo reset: restored {Email}", account.Email);
        }
    }

    private static async Task ValidatePasswordAsync(UserManager<OlusoUser> userManager, OlusoUser user, SeedData.TestAccount account, string password)
    {
        foreach (var validator in userManager.PasswordValidators)
        {
            var result = await validator.ValidateAsync(userManager, user, password);
            if (!result.Succeeded)
            {
                var errors = string.Join(" ", result.Errors.Select(e => e.Description));
                throw new InvalidOperationException(account.RoleName == "SuperAdmin"
                    ? $"{DemoAccountResetOptions.SectionName}:SuperAdminPassword doesn't meet the password policy: {errors}"
                    : $"The test password for {account.Email} doesn't meet the password policy: {errors}");
            }
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
        var options = configuration.GetSection(DemoAccountResetOptions.SectionName).Get<DemoAccountResetOptions>() ?? new DemoAccountResetOptions();
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

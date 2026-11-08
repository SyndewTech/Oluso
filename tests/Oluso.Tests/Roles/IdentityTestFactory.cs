using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.EntityFramework;
using Oluso.EntityFramework.Identity;
using Oluso.EntityFramework.Services;

namespace Oluso.Tests.Roles;

/// <summary>
/// Real Identity managers over an in-memory OlusoDbContext, so role tests run
/// against the same store code as production (validators included).
/// </summary>
internal static class IdentityTestFactory
{
    public static OlusoDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<OlusoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new OlusoDbContext(options);
    }

    /// <summary>
    /// SQLite context whose global query filters read <paramref name="tenantContext"/>,
    /// as they do in a request. Foreign keys are off so tests needn't seed
    /// organizations and tenants just to satisfy constraints.
    /// </summary>
    public static OlusoDbContext CreateSqliteContext(
        SqliteConnection connection, ITenantContext tenantContext, params IInterceptor[] interceptors)
    {
        var services = new ServiceCollection()
            .AddSingleton(tenantContext)
            .BuildServiceProvider();
        var options = new DbContextOptionsBuilder<OlusoDbContext>()
            .UseSqlite(connection)
            .UseApplicationServiceProvider(services)
            .AddInterceptors(interceptors)
            .Options;
        var context = new OlusoDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    public static SqliteConnection OpenSqliteConnection()
    {
        var connection = new SqliteConnection("DataSource=:memory:;Foreign Keys=False");
        connection.Open();
        return connection;
    }

    public static RoleManager<OlusoRole> CreateRoleManager(OlusoDbContext context)
    {
        var store = new RoleStore<OlusoRole, OlusoDbContext, string, OlusoUserRole, OlusoRoleClaim>(context);
        return new RoleManager<OlusoRole>(
            store,
            new IRoleValidator<OlusoRole>[] { new TenantAwareRoleValidator() },
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            NullLogger<RoleManager<OlusoRole>>.Instance);
    }

    public static UserManager<OlusoUser> CreateUserManager(OlusoDbContext context, ITenantContext? tenantContext = null)
    {
        var store = new OlusoUserStore<OlusoDbContext>(context, tenantContext ?? new TestTenantContext());
        return new UserManager<OlusoUser>(
            store,
            Options.Create(new IdentityOptions()),
            new PasswordHasher<OlusoUser>(),
            Array.Empty<IUserValidator<OlusoUser>>(),
            Array.Empty<IPasswordValidator<OlusoUser>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            services: null!,
            NullLogger<UserManager<OlusoUser>>.Instance);
    }

    /// <summary>
    /// The production user service over <paramref name="context"/>, raising its
    /// events through whatever <paramref name="services"/> hands out.
    /// </summary>
    public static IdentityUserService CreateUserService(
        OlusoDbContext context, ITenantContext tenantContext, IServiceProvider? services = null)
    {
        var userManager = CreateUserManager(context, tenantContext);
        var signInManager = new SignInManager<OlusoUser>(
            userManager,
            new HttpContextAccessor(),
            new Mock<IUserClaimsPrincipalFactory<OlusoUser>>().Object,
            Options.Create(new IdentityOptions()),
            NullLogger<SignInManager<OlusoUser>>.Instance,
            new Mock<IAuthenticationSchemeProvider>().Object,
            new Mock<IUserConfirmation<OlusoUser>>().Object);
        return new IdentityUserService(
            userManager, signInManager, tenantContext, new Mock<ITenantSettingsProvider>().Object, context,
            NullLogger<IdentityUserService>.Instance, services: services);
    }

    public static async Task<OlusoRole> CreateRoleAsync(
        RoleManager<OlusoRole> roleManager,
        string name,
        string? tenantId,
        string? category = null,
        bool isSystemRole = false)
    {
        var role = new OlusoRole
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            DisplayName = name,
            TenantId = tenantId,
            Category = category,
            IsSystemRole = isSystemRole
        };

        var result = await roleManager.CreateAsync(role);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(string.Join(", ", result.Errors.Select(e => e.Description)));
        }

        return role;
    }

    public static async Task<OlusoUser> CreateUserAsync(
        OlusoDbContext context,
        string tenantId,
        string email,
        string? firstName = null,
        string? lastName = null,
        string? displayName = null)
    {
        var user = new OlusoUser
        {
            Id = Guid.NewGuid().ToString(),
            UserName = email,
            NormalizedUserName = email.ToUpperInvariant(),
            Email = email,
            NormalizedEmail = email.ToUpperInvariant(),
            TenantId = tenantId,
            FirstName = firstName,
            LastName = lastName,
            DisplayName = displayName,
            IsActive = true
        };

        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }
}

/// <summary>
/// Tenant context a test can switch between tenants, the way tenant resolution
/// does per request.
/// </summary>
internal sealed class TestTenantContext : ITenantContext
{
    public string? TenantId { get; set; }
    public Tenant? Tenant => null;
    public bool HasTenant => TenantId != null;
}

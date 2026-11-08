using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Oluso.Core.Authorization;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.EntityFramework;
using Oluso.EntityFramework.Services;
using Xunit;

namespace Oluso.Tests.Roles;

/// <summary>
/// Role names are resolved to rows inside the tenant the request runs in, and
/// the write goes by row id, so a tenant row and a platform-wide row with the
/// same name never collide, and roles held in other tenants are never touched.
/// Registry-defined module roles are platform-governed: their platform row
/// wins over a leftover tenant copy. Any other name prefers the tenant's row.
/// </summary>
public class IdentityUserServiceRoleTests : IDisposable
{
    private readonly OlusoDbContext _context;
    private readonly RoleManager<OlusoRole> _roleManager;
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly IdentityUserService _service;

    public IdentityUserServiceRoleTests()
    {
        _context = IdentityTestFactory.CreateContext();
        _roleManager = IdentityTestFactory.CreateRoleManager(_context);
        var userManager = IdentityTestFactory.CreateUserManager(_context, _tenantContext.Object);
        var signInManager = new SignInManager<OlusoUser>(
            userManager,
            new Mock<IHttpContextAccessor>().Object,
            new Mock<IUserClaimsPrincipalFactory<OlusoUser>>().Object,
            Options.Create(new IdentityOptions()),
            NullLogger<SignInManager<OlusoUser>>.Instance,
            new Mock<IAuthenticationSchemeProvider>().Object,
            new Mock<IUserConfirmation<OlusoUser>>().Object);

        _tenantContext.SetupGet(c => c.TenantId).Returns("t1");

        _service = new IdentityUserService(
            userManager,
            signInManager,
            _tenantContext.Object,
            new Mock<ITenantSettingsProvider>().Object,
            _context,
            NullLogger<IdentityUserService>.Instance);
    }

    public void Dispose() => _context.Dispose();

    private Task<List<string>> HeldRoleIds(string userId) =>
        _context.UserRoles.Where(ur => ur.UserId == userId).Select(ur => ur.RoleId).ToListAsync();

    /// <summary>
    /// Assigns in the role's own tenant, or for a platform-wide role in the
    /// user's home tenant — what the backfill stamps.
    /// </summary>
    private async Task Hold(OlusoUser user, OlusoRole role)
    {
        _context.UserRoles.Add(new OlusoUserRole { UserId = user.Id, RoleId = role.Id, TenantId = role.TenantId ?? user.TenantId! });
        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// The registry is a process-wide singleton, so each test registers a
    /// uniquely named module role.
    /// </summary>
    private static string RegisterModuleRole()
    {
        var name = $"Mod{Guid.NewGuid():N}Admin";
        PermissionRegistry.Instance.RegisterModuleRole(new ModuleRoleDefinition(
            name, "Module Administrator", "Full access", "test", new[] { "test.administration" }, "test"));
        return name;
    }

    [Fact]
    public async Task SetRoles_PrefersTheTenantRow_ForARoleTheRegistryDoesNotDefine()
    {
        await IdentityTestFactory.CreateRoleAsync(_roleManager, "Reviewer", tenantId: null, isSystemRole: true);
        var tenantRow = await IdentityTestFactory.CreateRoleAsync(_roleManager, "Reviewer", tenantId: "t1");
        var user = await IdentityTestFactory.CreateUserAsync(_context, "t1", "ada@t1.test");

        var result = await _service.SetRolesAsync(user.Id, new[] { "Reviewer" });

        result.Succeeded.Should().BeTrue();
        (await HeldRoleIds(user.Id)).Should().Equal(tenantRow.Id);
    }

    [Fact]
    public async Task SetRoles_PrefersThePlatformRow_ForARegistryDefinedModuleRole()
    {
        var name = RegisterModuleRole();
        var platformRow = await IdentityTestFactory.CreateRoleAsync(_roleManager, name, tenantId: null, isSystemRole: true);
        await IdentityTestFactory.CreateRoleAsync(_roleManager, name, tenantId: "t1");
        var user = await IdentityTestFactory.CreateUserAsync(_context, "t1", "ada@t1.test");

        var result = await _service.SetRolesAsync(user.Id, new[] { name });

        result.Succeeded.Should().BeTrue();
        (await HeldRoleIds(user.Id)).Should().Equal(platformRow.Id);
    }

    [Fact]
    public async Task SetRoles_KeepsAPlatformRowTheUserAlreadyHolds()
    {
        var platformRow = await IdentityTestFactory.CreateRoleAsync(_roleManager, "Reviewer", tenantId: null, isSystemRole: true);
        await IdentityTestFactory.CreateRoleAsync(_roleManager, "Reviewer", tenantId: "t1");
        var user = await IdentityTestFactory.CreateUserAsync(_context, "t1", "ada@t1.test");
        await Hold(user, platformRow);

        var result = await _service.SetRolesAsync(user.Id, new[] { "Reviewer" });

        result.Succeeded.Should().BeTrue();
        (await HeldRoleIds(user.Id)).Should().Equal(platformRow.Id);
    }

    [Fact]
    public async Task SetRoles_KeepsALeftoverTenantCopyTheUserAlreadyHolds_ForARegistryRole()
    {
        var name = RegisterModuleRole();
        await IdentityTestFactory.CreateRoleAsync(_roleManager, name, tenantId: null, isSystemRole: true);
        var tenantCopy = await IdentityTestFactory.CreateRoleAsync(_roleManager, name, tenantId: "t1");
        var user = await IdentityTestFactory.CreateUserAsync(_context, "t1", "ada@t1.test");
        await Hold(user, tenantCopy);

        var result = await _service.SetRolesAsync(user.Id, new[] { name });

        result.Succeeded.Should().BeTrue();
        (await HeldRoleIds(user.Id)).Should().Equal(tenantCopy.Id);
    }

    [Fact]
    public async Task SetRoles_ReplacesOnlyTheRolesVisibleInTheCurrentTenant()
    {
        var otherTenantAdmin = await IdentityTestFactory.CreateRoleAsync(_roleManager, "Admin", tenantId: "t2");
        var viewer = await IdentityTestFactory.CreateRoleAsync(_roleManager, "Viewer", tenantId: "t1");
        var editor = await IdentityTestFactory.CreateRoleAsync(_roleManager, "Editor", tenantId: "t1");
        var user = await IdentityTestFactory.CreateUserAsync(_context, "t1", "ada@t1.test");
        await Hold(user, otherTenantAdmin);
        await Hold(user, viewer);

        var result = await _service.SetRolesAsync(user.Id, new[] { "Editor" });

        result.Succeeded.Should().BeTrue();
        (await HeldRoleIds(user.Id)).Should().BeEquivalentTo(new[] { otherTenantAdmin.Id, editor.Id });
    }

    [Fact]
    public async Task SetRoles_FailsWithoutChanges_WhenARoleDoesNotExistInScope()
    {
        var viewer = await IdentityTestFactory.CreateRoleAsync(_roleManager, "Viewer", tenantId: "t1");
        await IdentityTestFactory.CreateRoleAsync(_roleManager, "Ghost", tenantId: "t2");
        var user = await IdentityTestFactory.CreateUserAsync(_context, "t1", "ada@t1.test");
        await Hold(user, viewer);

        var result = await _service.SetRolesAsync(user.Id, new[] { "Ghost" });

        result.Succeeded.Should().BeFalse();
        var reported = string.Join(" ", (result.Errors ?? Array.Empty<string>()).Append(result.Error ?? ""));
        reported.Should().Contain("Ghost");
        (await HeldRoleIds(user.Id)).Should().Equal(viewer.Id);
    }

    [Fact]
    public async Task SetRolesById_ReplacesTheSetWithinScope_AndLeavesOtherTenantsAlone()
    {
        var otherTenantAdmin = await IdentityTestFactory.CreateRoleAsync(_roleManager, "Admin", tenantId: "t2");
        var viewer = await IdentityTestFactory.CreateRoleAsync(_roleManager, "Viewer", tenantId: "t1");
        var editor = await IdentityTestFactory.CreateRoleAsync(_roleManager, "Editor", tenantId: "t1");
        var auditor = await IdentityTestFactory.CreateRoleAsync(_roleManager, "Auditor", tenantId: null, isSystemRole: true);
        var user = await IdentityTestFactory.CreateUserAsync(_context, "t1", "ada@t1.test");
        await Hold(user, otherTenantAdmin);
        await Hold(user, viewer);

        var result = await _service.SetRolesByIdAsync(user.Id, new[] { editor.Id, auditor.Id });

        result.Succeeded.Should().BeTrue();
        (await HeldRoleIds(user.Id)).Should().BeEquivalentTo(new[] { otherTenantAdmin.Id, editor.Id, auditor.Id });
    }

    [Fact]
    public async Task SetRolesById_RejectsARowFromAnotherTenant_WithoutChanges()
    {
        var viewer = await IdentityTestFactory.CreateRoleAsync(_roleManager, "Viewer", tenantId: "t1");
        var foreign = await IdentityTestFactory.CreateRoleAsync(_roleManager, "Ghost", tenantId: "t2");
        var user = await IdentityTestFactory.CreateUserAsync(_context, "t1", "ada@t1.test");
        await Hold(user, viewer);

        var result = await _service.SetRolesByIdAsync(user.Id, new[] { foreign.Id });

        result.Succeeded.Should().BeFalse();
        (result.Error ?? "").Should().Contain(foreign.Id);
        (await HeldRoleIds(user.Id)).Should().Equal(viewer.Id);
    }

    [Fact]
    public async Task AddToRole_UsesTheHomeTenant_WhenTheRequestHasNoTenant()
    {
        _tenantContext.SetupGet(c => c.TenantId).Returns((string?)null);
        var viewer = await IdentityTestFactory.CreateRoleAsync(_roleManager, "Viewer", tenantId: "t1");
        var user = await IdentityTestFactory.CreateUserAsync(_context, "t1", "ada@t1.test");

        var result = await _service.AddToRoleAsync(user.Id, "Viewer");

        result.Succeeded.Should().BeTrue();
        (await HeldRoleIds(user.Id)).Should().Equal(viewer.Id);
    }

    [Fact]
    public async Task RemoveFromRole_RemovesEveryRowWithThatNameInScope()
    {
        var platformRow = await IdentityTestFactory.CreateRoleAsync(_roleManager, "Reviewer", tenantId: null, isSystemRole: true);
        var tenantRow = await IdentityTestFactory.CreateRoleAsync(_roleManager, "Reviewer", tenantId: "t1");
        var user = await IdentityTestFactory.CreateUserAsync(_context, "t1", "ada@t1.test");
        await Hold(user, platformRow);
        await Hold(user, tenantRow);

        var result = await _service.RemoveFromRoleAsync(user.Id, "Reviewer");

        result.Succeeded.Should().BeTrue();
        (await HeldRoleIds(user.Id)).Should().BeEmpty();
    }
}

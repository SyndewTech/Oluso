using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Oluso.Core.Domain.Entities;
using Oluso.Integration.Tests.Fixtures;
using Xunit;

namespace Oluso.Integration.Tests;

/// <summary>
/// Roles move between environments as a file: export carries each role's permissions, entitlements and other claims
/// plus the entitlements' definitions; import previews, creates the missing entitlements, then creates or (when asked)
/// replaces roles by name. Two tenants stand in for two environments.
/// </summary>
public class RoleTransferTests : IntegrationTestBase
{
    private const string Source = "default";
    private readonly string _target = "roles-target-" + Guid.NewGuid().ToString("N")[..8];

    public RoleTransferTests(OlusoWebApplicationFactory factory) : base(factory)
    {
    }

    public override Task InitializeAsync() => WithDbContextAsync(async db =>
    {
        var organizationId = (await db.Tenants.FindAsync(Source))!.OrganizationId;
        db.Tenants.Add(new Tenant { Id = _target, Name = _target, Identifier = _target, OrganizationId = organizationId });
        await db.SaveChangesAsync();
    });

    private async Task<(string Id, string Name, string Entitlement)> SeedSourceRoleAsync()
    {
        var id = Guid.NewGuid().ToString();
        var name = $"Support{id[..8]}";
        var entitlement = $"tickets:{id[..8]}";
        await WithDbContextAsync(async db =>
        {
            db.Entitlements.Add(new Entitlement
            {
                TenantId = Source, Name = entitlement, DisplayName = "Tickets", Description = "Work the ticket queue", Group = "Support",
            });
            db.Roles.Add(new OlusoRole
            {
                Id = id, Name = name, NormalizedName = name.ToUpperInvariant(), DisplayName = "Support agent",
                Description = "Front line", TenantId = Source,
            });
            db.RoleClaims.AddRange(
                new OlusoRoleClaim { RoleId = id, ClaimType = OlusoRole.PermissionClaimType, ClaimValue = "users.read" },
                new OlusoRoleClaim { RoleId = id, ClaimType = OlusoRole.EntitlementClaimType, ClaimValue = entitlement },
                new OlusoRoleClaim { RoleId = id, ClaimType = "department", ClaimValue = "support" });
            await db.SaveChangesAsync();
        });
        return (id, name, entitlement);
    }

    private async Task<string> ExportAsync(params string[] roleIds)
    {
        var response = await (await SuperAdminClient.CreateAsync(Factory, Source)).PostAsJsonAsync("/api/admin/roles/export", new { roleIds });
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadAsStringAsync();
    }

    private async Task<(HttpStatusCode Status, JsonElement Body)> ImportAsync(string content, bool replace = false, bool preview = false)
    {
        var admin = await SuperAdminClient.CreateAsync(Factory, _target);
        var response = await admin.PostAsJsonAsync(
            preview ? "/api/admin/roles/import/preview" : "/api/admin/roles/import", new { content, replaceExisting = replace });
        return (response.StatusCode, await response.Content.ReadFromJsonAsync<JsonElement>());
    }

    private async Task<(OlusoRole? Role, List<(string Type, string Value)> Claims)> TargetRoleAsync(string name)
    {
        OlusoRole? role = null;
        var claims = new List<(string, string)>();
        await WithDbContextAsync(async db =>
        {
            role = await db.Roles.IgnoreQueryFilters().AsNoTracking()
                .FirstOrDefaultAsync(r => r.TenantId == _target && r.NormalizedName == name.ToUpperInvariant());
            if (role != null)
            {
                claims = (await db.RoleClaims.AsNoTracking().Where(c => c.RoleId == role.Id).ToListAsync())
                    .Select(c => (c.ClaimType!, c.ClaimValue!)).ToList();
            }
        });
        return (role, claims);
    }

    [Fact]
    public async Task An_exported_role_imports_with_its_permissions_entitlements_and_claims_and_creates_the_entitlement()
    {
        var (id, name, entitlement) = await SeedSourceRoleAsync();
        var file = await ExportAsync(id);

        var (status, body) = await ImportAsync(file);

        status.Should().Be(HttpStatusCode.OK, body.ToString());
        body.GetProperty("applied").GetBoolean().Should().BeTrue();
        body.GetProperty("entitlementsToCreate").EnumerateArray().Select(e => e.GetString()).Should().Equal(entitlement);
        var (role, claims) = await TargetRoleAsync(name);
        role.Should().NotBeNull();
        role!.DisplayName.Should().Be("Support agent");
        role.Description.Should().Be("Front line");
        claims.Should().BeEquivalentTo(new[]
        {
            (OlusoRole.PermissionClaimType, "users.read"),
            (OlusoRole.EntitlementClaimType, entitlement),
            ("department", "support"),
        });

        await WithDbContextAsync(async db =>
        {
            var created = await db.Entitlements.IgnoreQueryFilters().AsNoTracking().SingleAsync(e => e.TenantId == _target && e.Name == entitlement);
            created.DisplayName.Should().Be("Tickets");
            created.Description.Should().Be("Work the ticket queue");
            created.Group.Should().Be("Support");
        });
    }

    [Fact]
    public async Task A_preview_writes_nothing()
    {
        var (id, name, entitlement) = await SeedSourceRoleAsync();
        var file = await ExportAsync(id);

        var (status, body) = await ImportAsync(file, preview: true);

        status.Should().Be(HttpStatusCode.OK);
        body.GetProperty("canImport").GetBoolean().Should().BeTrue();
        body.GetProperty("applied").GetBoolean().Should().BeFalse();
        body.GetProperty("roles")[0].GetProperty("action").GetString().Should().Be("create");
        (await TargetRoleAsync(name)).Role.Should().BeNull();
        await WithDbContextAsync(async db =>
            (await db.Entitlements.IgnoreQueryFilters().AnyAsync(e => e.TenantId == _target && e.Name == entitlement)).Should().BeFalse());
    }

    [Fact]
    public async Task An_existing_role_is_skipped_unless_replacing_and_replacing_makes_it_the_files()
    {
        var (id, name, _) = await SeedSourceRoleAsync();
        var file = await ExportAsync(id);
        (await ImportAsync(file)).Status.Should().Be(HttpStatusCode.OK);
        await WithDbContextAsync(async db =>
        {
            var targetId = (await db.Roles.IgnoreQueryFilters().FirstAsync(r => r.TenantId == _target && r.NormalizedName == name.ToUpperInvariant())).Id;
            db.RoleClaims.Add(new OlusoRoleClaim { RoleId = targetId, ClaimType = OlusoRole.PermissionClaimType, ClaimValue = "clients.read" });
            await db.SaveChangesAsync();
        });

        var (skipStatus, skipped) = await ImportAsync(file);
        skipStatus.Should().Be(HttpStatusCode.BadRequest, "nothing in the file is new and replacing is off");
        skipped.GetProperty("roles")[0].GetProperty("action").GetString().Should().Be("skip");
        (await TargetRoleAsync(name)).Claims.Should().Contain((OlusoRole.PermissionClaimType, "clients.read"));

        var (status, replaced) = await ImportAsync(file, replace: true);
        status.Should().Be(HttpStatusCode.OK, replaced.ToString());
        replaced.GetProperty("roles")[0].GetProperty("action").GetString().Should().Be("update");
        (await TargetRoleAsync(name)).Claims.Where(c => c.Type == OlusoRole.PermissionClaimType)
            .Should().Equal((OlusoRole.PermissionClaimType, "users.read"));
    }

    [Fact]
    public async Task Reserved_names_unknown_permissions_and_reserved_claims_do_not_come_in()
    {
        var name = $"Ops{Guid.NewGuid():N}"[..12];
        var file = JsonSerializer.Serialize(new
        {
            format = "oluso-roles",
            version = 1,
            roles = new object[]
            {
                new { name = "SuperAdmin", permissions = new[] { "users.read" } },
                new
                {
                    name,
                    permissions = new[] { "users.read", "no.such.permission" },
                    claims = new[] { new { type = "tenant_id", value = "elsewhere" }, new { type = "team", value = "ops" } },
                },
            },
        });

        var (status, body) = await ImportAsync(file);

        status.Should().Be(HttpStatusCode.OK, body.ToString());
        var roles = body.GetProperty("roles").EnumerateArray().ToDictionary(r => r.GetProperty("name").GetString()!);
        roles["SuperAdmin"].GetProperty("action").GetString().Should().Be("invalid");
        roles[name].GetProperty("droppedPermissions").EnumerateArray().Select(p => p.GetString()).Should().Equal("no.such.permission");
        roles[name].GetProperty("droppedClaims").EnumerateArray().Select(p => p.GetString()).Should().Equal("tenant_id");
        (await TargetRoleAsync(name)).Claims.Should().BeEquivalentTo(new[]
        {
            (OlusoRole.PermissionClaimType, "users.read"),
            ("team", "ops"),
        });
    }

    [Fact]
    public async Task A_platform_role_cannot_be_exported()
    {
        string superAdminRoleId = "";
        await WithDbContextAsync(async db =>
            superAdminRoleId = (await db.Roles.IgnoreQueryFilters().FirstAsync(r => r.TenantId == null && r.NormalizedName == "SUPERADMIN")).Id);

        var response = await (await SuperAdminClient.CreateAsync(Factory, Source))
            .PostAsJsonAsync("/api/admin/roles/export", new { roleIds = new[] { superAdminRoleId } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_file_that_is_not_a_roles_export_is_refused()
    {
        var (status, body) = await ImportAsync("{\"format\":\"something-else\",\"version\":1}");

        status.Should().Be(HttpStatusCode.BadRequest);
        body.GetProperty("error").GetString().Should().Contain("not a roles export");
    }
}

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
/// The platform views: every tenant's custom domains in one list (removed and failed included), and platform-wide
/// roles with their holders in every tenant, assigned in a tenant the person belongs to and removed from one tenant
/// at a time.
/// </summary>
public class PlatformDomainsAndRolesTests : IntegrationTestBase
{
    private const string Tenant = "default";
    private readonly string _otherTenant = "platform-view-" + Guid.NewGuid().ToString("N")[..8];
    private readonly string _servingDomain = $"login-{Guid.NewGuid():N}.invalid";

    public PlatformDomainsAndRolesTests(OlusoWebApplicationFactory factory) : base(factory)
    {
    }

    public override Task InitializeAsync() => WithDbContextAsync(async db =>
    {
        var organizationId = (await db.Tenants.FindAsync(Tenant))!.OrganizationId;
        db.Tenants.Add(new Tenant
        {
            Id = _otherTenant, Name = _otherTenant, DisplayName = "Platform View Other", Identifier = _otherTenant,
            OrganizationId = organizationId, CustomDomain = _servingDomain,
        });
        await db.SaveChangesAsync();
    });

    private Task<HttpClient> AdminAsync() => SuperAdminClient.CreateAsync(Factory, Tenant);

    private async Task<string> SeedDomainAsync(string tenantId, CustomDomainStatus status, string? domain = null)
    {
        var record = new CustomDomainVerification
        {
            TenantId = tenantId,
            Domain = domain ?? $"login-{Guid.NewGuid():N}.invalid",
            Status = status,
            VerificationToken = "token",
            TxtRecordName = "_oluso-verify.example.invalid",
            TxtRecordValue = "oluso-verify=token",
            FailureReason = status == CustomDomainStatus.Failed ? "Verification timed out" : null,
        };
        await WithDbContextAsync(async db =>
        {
            db.CustomDomainVerifications.Add(record);
            await db.SaveChangesAsync();
        });
        return record.Id;
    }

    private async Task<string> NewPlatformRoleAsync()
    {
        var id = Guid.NewGuid().ToString();
        var name = $"PlatformViewRole{id[..8]}";
        await WithDbContextAsync(async db =>
        {
            db.Roles.Add(new OlusoRole
            {
                Id = id, Name = name, NormalizedName = name.ToUpperInvariant(), DisplayName = name, TenantId = null,
            });
            await db.SaveChangesAsync();
        });
        return id;
    }

    private async Task<string> NewUserAsync(string homeTenantId, params string[] memberOf)
    {
        var id = Guid.NewGuid().ToString();
        var email = $"pv-{id[..8]}@platform-view.test";
        await WithDbContextAsync(async db =>
        {
            db.Users.Add(new OlusoUser
            {
                Id = id, UserName = email, NormalizedUserName = email.ToUpperInvariant(),
                Email = email, NormalizedEmail = email.ToUpperInvariant(),
                SecurityStamp = Guid.NewGuid().ToString(), TenantId = homeTenantId, IsActive = true,
            });
            foreach (var tenantId in memberOf)
            {
                db.TenantMemberships.Add(new TenantMembership { TenantId = tenantId, UserId = id });
            }
            await db.SaveChangesAsync();
        });
        return id;
    }

    private Task GrantAsync(string userId, string roleId, string tenantId) => WithDbContextAsync(async db =>
    {
        db.UserRoles.Add(new OlusoUserRole { UserId = userId, RoleId = roleId, TenantId = tenantId });
        await db.SaveChangesAsync();
    });

    private async Task<List<OlusoUserRole>> AssignmentsAsync(string roleId)
    {
        var rows = new List<OlusoUserRole>();
        await WithDbContextAsync(async db =>
            rows = await db.UserRoles.AsNoTracking().Where(ur => ur.RoleId == roleId).ToListAsync());
        return rows;
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    [Fact]
    public async Task The_domain_list_shows_every_tenants_records_including_removed_and_failed_ones()
    {
        var removed = await SeedDomainAsync(_otherTenant, CustomDomainStatus.Removed);
        var failed = await SeedDomainAsync(Tenant, CustomDomainStatus.Failed);
        var active = await SeedDomainAsync(_otherTenant, CustomDomainStatus.Active, _servingDomain);

        var body = await JsonAsync(await (await AdminAsync()).GetAsync("/api/admin/platform/domains"));
        var byId = body.EnumerateArray().ToDictionary(d => d.GetProperty("id").GetString()!);

        byId[removed].GetProperty("status").GetString().Should().Be("Removed");
        byId[removed].GetProperty("tenantId").GetString().Should().Be(_otherTenant);
        byId[removed].GetProperty("tenantName").GetString().Should().Be("Platform View Other");
        byId[failed].GetProperty("status").GetString().Should().Be("Failed");
        byId[failed].GetProperty("failureReason").GetString().Should().Be("Verification timed out");
        byId[failed].GetProperty("tenantId").GetString().Should().Be(Tenant);
        byId[active].GetProperty("isServing").GetBoolean().Should().BeTrue("it is the tenant's custom domain");
        byId[removed].GetProperty("isServing").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task A_platform_role_lists_its_holders_in_every_tenant()
    {
        var roleId = await NewPlatformRoleAsync();
        var userId = await NewUserAsync(Tenant, _otherTenant);
        await GrantAsync(userId, roleId, Tenant);
        await GrantAsync(userId, roleId, _otherTenant);
        var admin = await AdminAsync();

        var roles = await JsonAsync(await admin.GetAsync("/api/admin/platform/roles"));
        var role = roles.EnumerateArray().Single(r => r.GetProperty("id").GetString() == roleId);
        role.GetProperty("holderCount").GetInt32().Should().Be(1);
        role.GetProperty("assignmentCount").GetInt32().Should().Be(2);

        var holders = await JsonAsync(await admin.GetAsync($"/api/admin/platform/roles/{roleId}/holders"));
        holders.EnumerateArray().Select(h => (h.GetProperty("userId").GetString(), h.GetProperty("tenantId").GetString()))
            .Should().BeEquivalentTo(new[] { (userId, Tenant), (userId, _otherTenant) });
        holders.EnumerateArray().Single(h => h.GetProperty("tenantId").GetString() == _otherTenant)
            .GetProperty("tenantName").GetString().Should().Be("Platform View Other");
    }

    [Fact]
    public async Task A_role_is_assigned_only_in_a_tenant_the_person_belongs_to_and_only_once()
    {
        var roleId = await NewPlatformRoleAsync();
        var userId = await NewUserAsync(Tenant);
        var admin = await AdminAsync();

        var elsewhere = await admin.PostAsJsonAsync($"/api/admin/platform/roles/{roleId}/holders", new { userId, tenantId = _otherTenant });
        elsewhere.StatusCode.Should().Be(HttpStatusCode.BadRequest, "they are neither homed in nor a member of that tenant");

        var assigned = await admin.PostAsJsonAsync($"/api/admin/platform/roles/{roleId}/holders", new { userId, tenantId = Tenant });
        assigned.StatusCode.Should().Be(HttpStatusCode.NoContent, await assigned.Content.ReadAsStringAsync());
        var row = (await AssignmentsAsync(roleId)).Should().ContainSingle().Subject;
        row.UserId.Should().Be(userId);
        row.TenantId.Should().Be(Tenant);
        row.AssignedBy.Should().NotBeNullOrEmpty().And.NotBe("system");

        var again = await admin.PostAsJsonAsync($"/api/admin/platform/roles/{roleId}/holders", new { userId, tenantId = Tenant });
        again.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Removing_a_holder_in_one_tenant_keeps_their_assignment_in_another()
    {
        var roleId = await NewPlatformRoleAsync();
        var userId = await NewUserAsync(Tenant, _otherTenant);
        await GrantAsync(userId, roleId, Tenant);
        await GrantAsync(userId, roleId, _otherTenant);

        var response = await (await AdminAsync()).DeleteAsync($"/api/admin/platform/roles/{roleId}/holders/{userId}?tenantId={_otherTenant}");

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        (await AssignmentsAsync(roleId)).Select(r => r.TenantId).Should().Equal(Tenant);
    }

    [Fact]
    public async Task SuperAdmin_cannot_be_removed_from_yourself()
    {
        string roleId = "", userId = "", tenantId = "";
        await WithDbContextAsync(async db =>
        {
            roleId = (await db.Roles.IgnoreQueryFilters().FirstAsync(r => r.TenantId == null && r.NormalizedName == "SUPERADMIN")).Id;
            userId = (await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Email == "superadmin@oluso.local")).Id;
            tenantId = (await db.UserRoles.FirstAsync(ur => ur.RoleId == roleId && ur.UserId == userId)).TenantId;
        });

        var response = await (await AdminAsync()).DeleteAsync($"/api/admin/platform/roles/{roleId}/holders/{userId}?tenantId={tenantId}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await AssignmentsAsync(roleId)).Should().Contain(r => r.UserId == userId);
    }

    [Fact]
    public async Task OrgAdmin_follows_membership_and_is_not_assigned_here()
    {
        string roleId = "";
        await WithDbContextAsync(async db =>
            roleId = (await db.Roles.IgnoreQueryFilters().FirstAsync(r => r.TenantId == null && r.NormalizedName == "ORGADMIN")).Id);
        var userId = await NewUserAsync(Tenant);

        var response = await (await AdminAsync()).PostAsJsonAsync($"/api/admin/platform/roles/{roleId}/holders", new { userId, tenantId = Tenant });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await AssignmentsAsync(roleId)).Should().NotContain(r => r.UserId == userId);
    }

    [Fact]
    public async Task A_tenant_owned_role_is_not_a_platform_role()
    {
        var id = Guid.NewGuid().ToString();
        await WithDbContextAsync(async db =>
        {
            db.Roles.Add(new OlusoRole { Id = id, Name = $"TenantRole{id[..8]}", NormalizedName = $"TENANTROLE{id[..8]}".ToUpperInvariant(), TenantId = Tenant });
            await db.SaveChangesAsync();
        });

        var response = await (await AdminAsync()).GetAsync($"/api/admin/platform/roles/{id}/holders");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_people_to_pick_are_those_homed_in_or_members_of_the_chosen_tenant()
    {
        var home = await NewUserAsync(_otherTenant);
        var member = await NewUserAsync(Tenant, _otherTenant);
        var outsider = await NewUserAsync(Tenant);

        var body = await JsonAsync(await (await AdminAsync()).GetAsync(
            $"/api/admin/platform/roles/candidates?tenantId={_otherTenant}&search=platform-view.test"));
        var ids = body.EnumerateArray().Select(c => c.GetProperty("id").GetString()).ToList();

        ids.Should().Contain(new[] { home, member }).And.NotContain(outsider);
    }
}

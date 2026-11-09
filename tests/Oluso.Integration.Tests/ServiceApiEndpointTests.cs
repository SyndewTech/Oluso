using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Oluso.Admin.Authorization;
using Oluso.Admin.Controllers.Service;
using Oluso.Core.Domain.Entities;
using Oluso.EntityFramework;
using Oluso.Integration.Tests.Fixtures;
using Xunit;

namespace Oluso.Integration.Tests;

/// <summary>
/// Every Service API endpoint, end to end through /connect/token and the real
/// pipeline: a client_credentials token for a client registered in the
/// "default" tenant succeeds with the endpoint's scope plus oluso:service, is
/// refused (403) without the endpoint's scope, is refused without
/// oluso:service, and never reaches a user or role of another tenant (404).
/// </summary>
public class ServiceApiEndpointTests : IntegrationTestBase
{
    private const string Tenant = "default";
    private const string OtherTenant = "svc-other-tenant";
    private const string ClientId = "svc-default-client";
    private const string ClientSecret = "svc-test-secret";

    private static readonly ConcurrentDictionary<string, string> Tokens = new();
    private static string? _seededFor;

    public ServiceApiEndpointTests(OlusoWebApplicationFactory factory) : base(factory)
    {
    }

    /// <summary>One endpoint: how to build a request for it, the scope it needs, and whether it takes an id from another tenant.</summary>
    public sealed record Endpoint(
        string Name,
        string Scope,
        Func<ServiceApiEndpointTests, Task<HttpRequestMessage>> Build,
        Func<ServiceApiEndpointTests, Task<HttpRequestMessage>>? BuildCrossTenant);

    private static readonly Endpoint[] Endpoints =
    {
        new("list users", ServiceScopes.UsersRead,
            _ => Req(HttpMethod.Get, "api/service/users"), null),
        new("get user", ServiceScopes.UsersRead,
            async t => await Req(HttpMethod.Get, $"api/service/users/{await t.NewUserAsync(Tenant)}"),
            async t => await Req(HttpMethod.Get, $"api/service/users/{await t.NewUserAsync(OtherTenant)}")),
        new("get user by email", ServiceScopes.UsersRead,
            async t => await Req(HttpMethod.Get, $"api/service/users/by-email/{await t.EmailOfAsync(await t.NewUserAsync(Tenant))}"),
            async t => await Req(HttpMethod.Get, $"api/service/users/by-email/{await t.EmailOfAsync(await t.NewUserAsync(OtherTenant))}")),
        new("create user", ServiceScopes.UsersWrite,
            _ => Req(HttpMethod.Post, "api/service/users", new { email = $"new-{Guid.NewGuid():N}@svc.test", password = "Str0ng!Passw0rd#" }),
            null),
        new("update user", ServiceScopes.UsersWrite,
            async t => await Req(HttpMethod.Put, $"api/service/users/{await t.NewUserAsync(Tenant)}", new { firstName = "Renamed" }),
            async t => await Req(HttpMethod.Put, $"api/service/users/{await t.NewUserAsync(OtherTenant)}", new { firstName = "Renamed" })),
        new("delete user", ServiceScopes.UsersDelete,
            async t => await Req(HttpMethod.Delete, $"api/service/users/{await t.NewUserAsync(Tenant)}"),
            async t => await Req(HttpMethod.Delete, $"api/service/users/{await t.NewUserAsync(OtherTenant)}")),
        new("get user roles", ServiceScopes.UsersRead,
            async t => await Req(HttpMethod.Get, $"api/service/users/{await t.NewUserAsync(Tenant)}/roles"),
            async t => await Req(HttpMethod.Get, $"api/service/users/{await t.NewUserAsync(OtherTenant)}/roles")),
        new("set user roles", ServiceScopes.UsersManageRoles,
            async t => await Req(HttpMethod.Put, $"api/service/users/{await t.NewUserAsync(Tenant)}/roles",
                new { roleIds = new[] { await t.NewRoleAsync(Tenant) } }),
            async t => await Req(HttpMethod.Put, $"api/service/users/{await t.NewUserAsync(OtherTenant)}/roles",
                new { roleIds = Array.Empty<string>() })),
        new("set user roles (other tenant's role)", ServiceScopes.UsersManageRoles,
            async t => await Req(HttpMethod.Put, $"api/service/users/{await t.NewUserAsync(Tenant)}/roles",
                new { roleIds = Array.Empty<string>() }),
            async t => await Req(HttpMethod.Put, $"api/service/users/{await t.NewUserAsync(Tenant)}/roles",
                new { roleIds = new[] { await t.NewRoleAsync(OtherTenant) } })),
        new("add user role", ServiceScopes.UsersManageRoles,
            async t => await Req(HttpMethod.Post, $"api/service/users/{await t.NewUserAsync(Tenant)}/roles/{await t.NewRoleAsync(Tenant)}"),
            async t => await Req(HttpMethod.Post, $"api/service/users/{await t.NewUserAsync(Tenant)}/roles/{await t.NewRoleAsync(OtherTenant)}")),
        new("add user role (other tenant's user)", ServiceScopes.UsersManageRoles,
            async t => await Req(HttpMethod.Post, $"api/service/users/{await t.NewUserAsync(Tenant)}/roles/{await t.NewRoleAsync(Tenant)}"),
            async t => await Req(HttpMethod.Post, $"api/service/users/{await t.NewUserAsync(OtherTenant)}/roles/{await t.NewRoleAsync(Tenant)}")),
        new("remove user role", ServiceScopes.UsersManageRoles,
            async t =>
            {
                var role = await t.NewRoleAsync(Tenant);
                return await Req(HttpMethod.Delete, $"api/service/users/{await t.NewUserAsync(Tenant, role)}/roles/{role}");
            },
            async t =>
            {
                var role = await t.NewRoleAsync(OtherTenant);
                return await Req(HttpMethod.Delete, $"api/service/users/{await t.NewUserAsync(Tenant)}/roles/{role}");
            }),
        new("remove user role (other tenant's user)", ServiceScopes.UsersManageRoles,
            async t =>
            {
                var role = await t.NewRoleAsync(Tenant);
                return await Req(HttpMethod.Delete, $"api/service/users/{await t.NewUserAsync(Tenant, role)}/roles/{role}");
            },
            async t => await Req(HttpMethod.Delete, $"api/service/users/{await t.NewUserAsync(OtherTenant)}/roles/{await t.NewRoleAsync(Tenant)}")),
        new("list roles", ServiceScopes.RolesRead,
            _ => Req(HttpMethod.Get, "api/service/roles"), null),
        new("get role", ServiceScopes.RolesRead,
            async t => await Req(HttpMethod.Get, $"api/service/roles/{await t.NewRoleAsync(Tenant)}"),
            async t => await Req(HttpMethod.Get, $"api/service/roles/{await t.NewRoleAsync(OtherTenant)}")),
        new("get role by name", ServiceScopes.RolesRead,
            async t => await Req(HttpMethod.Get, $"api/service/roles/by-name/{await t.RoleNameOfAsync(await t.NewRoleAsync(Tenant))}"),
            async t => await Req(HttpMethod.Get, $"api/service/roles/by-name/{await t.RoleNameOfAsync(await t.NewRoleAsync(OtherTenant))}")),
        new("create role", ServiceScopes.RolesWrite,
            _ => Req(HttpMethod.Post, "api/service/roles",
                new { name = $"svc-created-{Guid.NewGuid():N}", permissions = new[] { "users.read" } }),
            null),
        new("update role", ServiceScopes.RolesWrite,
            async t => await Req(HttpMethod.Put, $"api/service/roles/{await t.NewRoleAsync(Tenant)}", new { description = "updated" }),
            async t => await Req(HttpMethod.Put, $"api/service/roles/{await t.NewRoleAsync(OtherTenant)}", new { description = "updated" })),
        new("delete role", ServiceScopes.RolesDelete,
            async t => await Req(HttpMethod.Delete, $"api/service/roles/{await t.NewRoleAsync(Tenant)}"),
            async t => await Req(HttpMethod.Delete, $"api/service/roles/{await t.NewRoleAsync(OtherTenant)}")),
        new("get role users", ServiceScopes.RolesRead,
            async t => await Req(HttpMethod.Get, $"api/service/roles/{await t.NewRoleAsync(Tenant)}/users"),
            async t => await Req(HttpMethod.Get, $"api/service/roles/{await t.NewRoleAsync(OtherTenant)}/users")),
    };

    public static IEnumerable<object[]> AllEndpoints() => Endpoints.Select(e => new object[] { e.Name });

    public static IEnumerable<object[]> CrossTenantEndpoints() =>
        Endpoints.Where(e => e.BuildCrossTenant != null).Select(e => new object[] { e.Name });

    private static Endpoint Find(string name) => Endpoints.Single(e => e.Name == name);

    public override async Task InitializeAsync()
    {
        // The fixture (and its in-memory database) is shared by every test in
        // the class; seed it once per factory.
        var key = Factory.GetHashCode().ToString();
        if (_seededFor == key) return;

        await WithDbContextAsync(async db =>
        {
            if (!await db.Tenants!.IgnoreQueryFilters().AnyAsync(t => t.Id == OtherTenant))
            {
                db.Tenants!.Add(new Tenant
                {
                    Id = OtherTenant,
                    Name = OtherTenant,
                    DisplayName = "Other tenant",
                    Identifier = OtherTenant,
                    OrganizationId = "svc-other-org",
                    Enabled = true
                });
            }

            var existingScopes = await db.ApiScopes.IgnoreQueryFilters()
                .Where(s => s.TenantId == Tenant).Select(s => s.Name).ToListAsync();
            foreach (var name in ServiceScopes.AllNames.Where(n => !existingScopes.Contains(n)))
            {
                db.ApiScopes.Add(new ApiScope
                {
                    Name = name,
                    DisplayName = name,
                    TenantId = Tenant,
                    Enabled = true,
                    ShowInDiscoveryDocument = false,
                    NonEditable = true
                });
            }

            if (!await db.Clients.IgnoreQueryFilters().AnyAsync(c => c.ClientId == ClientId))
            {
                var client = new Client
                {
                    ClientId = ClientId,
                    ClientName = "Service API test client",
                    TenantId = Tenant,
                    Enabled = true,
                    RequireClientSecret = true,
                    RequirePkce = false
                };
                client.ClientSecrets.Add(new ClientSecret { Value = HashSecret(ClientSecret), Type = "SharedSecret" });
                client.AllowedGrantTypes.Add(new ClientGrantType { GrantType = "client_credentials" });
                foreach (var scope in ServiceScopes.AllNames)
                {
                    client.AllowedScopes.Add(new ClientScope { Scope = scope });
                }
                db.Clients.Add(client);
            }

            await db.SaveChangesAsync();
        });

        Tokens.Clear();
        _seededFor = key;
    }

    [Theory]
    [MemberData(nameof(AllEndpoints))]
    public async Task Succeeds_with_its_scope_and_the_base_scope(string endpoint)
    {
        var e = Find(endpoint);
        var response = await SendAsync(e.Build, ServiceScopes.ServiceApi, e.Scope);

        response.IsSuccessStatusCode.Should().BeTrue(
            $"{e.Name} with {e.Scope} returned {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
    }

    [Theory]
    [MemberData(nameof(AllEndpoints))]
    public async Task Is_forbidden_without_its_scope(string endpoint)
    {
        var e = Find(endpoint);
        var everyOtherScope = ServiceScopes.Catalog.Select(d => d.Name).Where(s => s != e.Scope);

        var response = await SendAsync(e.Build, new[] { ServiceScopes.ServiceApi }.Concat(everyOtherScope).ToArray());

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [MemberData(nameof(AllEndpoints))]
    public async Task Is_refused_without_the_base_service_scope(string endpoint)
    {
        var e = Find(endpoint);
        var response = await SendAsync(e.Build, ServiceScopes.Catalog.Select(d => d.Name).ToArray());

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden);
    }

    [Theory]
    [MemberData(nameof(AllEndpoints))]
    public async Task Is_unauthorized_without_a_token(string endpoint)
    {
        var e = Find(endpoint);
        var request = await e.Build(this);

        var response = await Factory.CreateClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Theory]
    [MemberData(nameof(CrossTenantEndpoints))]
    public async Task Cannot_reach_another_tenants_user_or_role(string endpoint)
    {
        var e = Find(endpoint);
        var response = await SendAsync(e.BuildCrossTenant!, ServiceScopes.AllNames.ToArray());

        response.StatusCode.Should().Be(HttpStatusCode.NotFound, await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(AdminPermissions.TenantsWrite)]
    [InlineData(AdminPermissions.PlatformClientsWrite)]
    [InlineData(AdminPermissions.OrganizationsManageMembers)]
    public async Task Creating_a_role_with_permissions_above_a_service_client_is_refused(string permission)
    {
        var name = $"svc-escalate-{Guid.NewGuid():N}";
        var response = await SendAsync(
            _ => Req(HttpMethod.Post, "api/service/roles", new { name, permissions = new[] { permission } }),
            ServiceScopes.ServiceApi, ServiceScopes.RolesWrite);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var exists = false;
        await WithDbContextAsync(async db => exists = await db.Roles.IgnoreQueryFilters().AnyAsync(r => r.Name == name));
        exists.Should().BeFalse();
    }

    [Fact]
    public async Task Listing_roles_never_shows_another_tenants_role()
    {
        var otherRole = await NewRoleAsync(OtherTenant);
        var ownRole = await NewRoleAsync(Tenant);

        var response = await SendAsync(_ => Req(HttpMethod.Get, "api/service/roles"), ServiceScopes.ServiceApi, ServiceScopes.RolesRead);
        var ids = (await response.Content.ReadFromJsonAsync<List<ServiceRoleDto>>(JsonOptions))!.Select(r => r.Id).ToList();

        ids.Should().Contain(ownRole).And.NotContain(otherRole);
    }

    [Fact]
    public async Task Listing_users_never_shows_another_tenants_user()
    {
        var other = await NewUserAsync(OtherTenant);
        var own = await NewUserAsync(Tenant);

        var response = await SendAsync(_ => Req(HttpMethod.Get, "api/service/users?pageSize=500"),
            ServiceScopes.ServiceApi, ServiceScopes.UsersRead);
        var page = (await response.Content.ReadFromJsonAsync<ServicePagedResult<ServiceUserDto>>(JsonOptions))!;

        page.Items.Select(u => u.Id).Should().Contain(own).And.NotContain(other);
    }

    [Fact]
    public async Task User_roles_only_list_the_clients_tenant()
    {
        // A user of the client's tenant who is also a member of the other tenant
        // and holds a role there: that role must not be reported here.
        var otherRole = await NewRoleAsync(OtherTenant);
        var ownRole = await NewRoleAsync(Tenant);
        var userId = await NewUserAsync(Tenant, ownRole);
        await WithDbContextAsync(async db =>
        {
            db.TenantMemberships.Add(new TenantMembership { TenantId = OtherTenant, UserId = userId });
            db.UserRoles.Add(new OlusoUserRole { UserId = userId, RoleId = otherRole, TenantId = OtherTenant });
            await db.SaveChangesAsync();
        });
        var ownName = await RoleNameOfAsync(ownRole);
        var otherName = await RoleNameOfAsync(otherRole);
        var email = await EmailOfAsync(userId);

        var detail = await (await SendAsync(_ => Req(HttpMethod.Get, $"api/service/users/{userId}"),
            ServiceScopes.ServiceApi, ServiceScopes.UsersRead)).Content.ReadFromJsonAsync<ServiceUserDetailDto>(JsonOptions);
        var byEmail = await (await SendAsync(_ => Req(HttpMethod.Get, $"api/service/users/by-email/{email}"),
            ServiceScopes.ServiceApi, ServiceScopes.UsersRead)).Content.ReadFromJsonAsync<ServiceUserDto>(JsonOptions);
        var list = await (await SendAsync(_ => Req(HttpMethod.Get, "api/service/users?pageSize=500"),
            ServiceScopes.ServiceApi, ServiceScopes.UsersRead)).Content.ReadFromJsonAsync<ServicePagedResult<ServiceUserDto>>(JsonOptions);

        detail!.Roles.Should().Equal(ownName);
        detail.Claims.Select(c => c.Value).Should().NotContain(otherName);
        byEmail!.Roles.Should().Equal(ownName);
        list!.Items.Single(u => u.Id == userId).Roles.Should().Equal(ownName);
    }

    [Fact]
    public async Task Deleting_a_user_removes_the_account_and_its_role_assignments()
    {
        var role = await NewRoleAsync(Tenant);
        var userId = await NewUserAsync(Tenant, role);
        await WithDbContextAsync(async db =>
        {
            db.TenantMemberships.Add(new TenantMembership { TenantId = OtherTenant, UserId = userId });
            await db.SaveChangesAsync();
        });

        var response = await SendAsync(_ => Req(HttpMethod.Delete, $"api/service/users/{userId}"),
            ServiceScopes.ServiceApi, ServiceScopes.UsersDelete);

        response.StatusCode.Should().Be(HttpStatusCode.NoContent, await response.Content.ReadAsStringAsync());
        var remaining = -1;
        await WithDbContextAsync(async db => remaining =
            await db.Users.IgnoreQueryFilters().CountAsync(u => u.Id == userId)
            + await db.UserRoles.IgnoreQueryFilters().CountAsync(ur => ur.UserId == userId)
            + await db.TenantMemberships.IgnoreQueryFilters().CountAsync(m => m.UserId == userId));
        remaining.Should().Be(0);

        var lookup = await SendAsync(_ => Req(HttpMethod.Get, $"api/service/users/{userId}"),
            ServiceScopes.ServiceApi, ServiceScopes.UsersRead);
        lookup.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Deleting_a_member_whose_home_is_another_tenant_is_forbidden()
    {
        var userId = await NewUserAsync(OtherTenant);
        await WithDbContextAsync(async db =>
        {
            db.TenantMemberships.Add(new TenantMembership { TenantId = Tenant, UserId = userId });
            await db.SaveChangesAsync();
        });

        var response = await SendAsync(_ => Req(HttpMethod.Delete, $"api/service/users/{userId}"),
            ServiceScopes.ServiceApi, ServiceScopes.UsersDelete);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden, await response.Content.ReadAsStringAsync());
        var exists = false;
        await WithDbContextAsync(async db => exists = await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Id == userId));
        exists.Should().BeTrue();
    }

    [Fact]
    public async Task By_email_finds_a_member_whose_home_is_another_tenant()
    {
        var userId = await NewUserAsync(OtherTenant);
        await WithDbContextAsync(async db =>
        {
            db.TenantMemberships.Add(new TenantMembership { TenantId = Tenant, UserId = userId });
            await db.SaveChangesAsync();
        });
        var email = await EmailOfAsync(userId);

        var response = await SendAsync(_ => Req(HttpMethod.Get, $"api/service/users/by-email/{email}"),
            ServiceScopes.ServiceApi, ServiceScopes.UsersRead);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        (await response.Content.ReadFromJsonAsync<ServiceUserDto>(JsonOptions))!.Id.Should().Be(userId);
    }

    private async Task<HttpResponseMessage> SendAsync(
        Func<ServiceApiEndpointTests, Task<HttpRequestMessage>> build, params string[] scopes)
    {
        var token = await TokenAsync(scopes);
        var request = await build(this);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await Factory.CreateClient().SendAsync(request);
    }

    private async Task<string> TokenAsync(string[] scopes)
    {
        var scope = string.Join(' ', scopes.OrderBy(s => s, StringComparer.Ordinal));
        if (Tokens.TryGetValue(scope, out var cached)) return cached;

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Tenant-Id", Tenant);
        var response = await client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = ClientId,
            ["client_secret"] = ClientSecret,
            ["scope"] = scope,
        }));
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);

        var token = JsonDocument.Parse(body).RootElement.GetProperty("access_token").GetString()!;
        Tokens[scope] = token;
        return token;
    }

    private static Task<HttpRequestMessage> Req(HttpMethod method, string url, object? body = null)
    {
        var request = new HttpRequestMessage(method, url);
        if (body != null)
        {
            request.Content = JsonContent.Create(body);
        }
        return Task.FromResult(request);
    }

    private async Task<string> NewUserAsync(string tenantId, params string[] roleIds)
    {
        var id = Guid.NewGuid().ToString();
        var email = $"u-{id[..8]}@svc.test";
        await WithDbContextAsync(async db =>
        {
            db.Users.Add(new OlusoUser
            {
                Id = id,
                UserName = email,
                NormalizedUserName = email.ToUpperInvariant(),
                Email = email,
                NormalizedEmail = email.ToUpperInvariant(),
                SecurityStamp = Guid.NewGuid().ToString(),
                TenantId = tenantId,
                IsActive = true
            });
            foreach (var roleId in roleIds)
            {
                db.UserRoles.Add(new OlusoUserRole { UserId = id, RoleId = roleId, TenantId = tenantId });
            }
            await db.SaveChangesAsync();
        });
        return id;
    }

    private async Task<string> NewRoleAsync(string tenantId)
    {
        var id = Guid.NewGuid().ToString();
        var name = $"svc-role-{id[..8]}";
        await WithDbContextAsync(async db =>
        {
            db.Roles.Add(new OlusoRole
            {
                Id = id,
                Name = name,
                NormalizedName = name.ToUpperInvariant(),
                DisplayName = name,
                TenantId = tenantId
            });
            await db.SaveChangesAsync();
        });
        return id;
    }

    private async Task<string> EmailOfAsync(string userId)
    {
        string? email = null;
        await WithDbContextAsync(async db =>
            email = await db.Users.IgnoreQueryFilters().Where(u => u.Id == userId).Select(u => u.Email).SingleAsync());
        return email!;
    }

    private async Task<string> RoleNameOfAsync(string roleId)
    {
        string? name = null;
        await WithDbContextAsync(async db =>
            name = await db.Roles.IgnoreQueryFilters().Where(r => r.Id == roleId).Select(r => r.Name).SingleAsync());
        return name!;
    }

    private static string HashSecret(string secret)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(secret));
        return Convert.ToBase64String(hash);
    }
}

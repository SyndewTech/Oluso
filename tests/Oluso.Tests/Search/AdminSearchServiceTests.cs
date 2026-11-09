using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Admin.Dto.Search;
using Oluso.Admin.Search;
using Oluso.Core.Authorization;
using Oluso.Core.Licensing;
using Xunit;

namespace Oluso.Tests.Search;

/// <summary>
/// The aggregator consults a provider only when the tenant has its feature and
/// the caller holds its permission; the providers that pass run in isolation,
/// so one failing or slow module never hides another's results.
/// </summary>
public class AdminSearchServiceTests
{
    private const string Tenant = "tenant-1";
    private const string UserId = "user-1";
    private static readonly ClaimsPrincipal User = new(new ClaimsIdentity(new[] { new Claim("sub", UserId) }, "test"));

    private sealed class FakeProvider : IAdminSearchProvider
    {
        public FakeProvider(string providerId, string module, string permission, Func<AdminSearchContext, CancellationToken, Task<AdminSearchProviderResult>> search, params string[] morePermissions)
        {
            ProviderId = providerId;
            Module = module;
            RequiredPermissions = new[] { permission }.Concat(morePermissions).ToList();
            Search = search;
        }

        public string ProviderId { get; }
        public string Module { get; }
        public IReadOnlyList<string> RequiredPermissions { get; }
        public string Category => ProviderId.Split('.').Last();
        public string DisplayName => ProviderId;
        public string Icon => "search";
        public Func<AdminSearchContext, CancellationToken, Task<AdminSearchProviderResult>> Search { get; }
        public IReadOnlyList<AdminSearchFilterDefinition> Filters { get; init; } = Array.Empty<AdminSearchFilterDefinition>();
        public int Calls { get; private set; }

        public Task<AdminSearchProviderResult> SearchAsync(AdminSearchContext context, CancellationToken cancellationToken = default)
        {
            Calls++;
            return Search(context, cancellationToken);
        }
    }

    private static AdminSearchProviderResult Hits(params (string Id, double Score)[] items) => new()
    {
        Items = items.Select(i => new AdminSearchResultItem { Id = i.Id, Title = i.Id, Route = $"/x/{i.Id}", Score = i.Score }).ToList(),
        TotalCount = items.Length,
        SeeAllRoute = "/x"
    };

    private static (AdminSearchService Service, Mock<IFeatureGate> Gate, Mock<IAuthorizationService> Authz) Rig(
        IEnumerable<FakeProvider> providers,
        Func<string, bool>? featureAllowed = null,
        Func<string, bool>? permissionAllowed = null)
    {
        var gate = new Mock<IFeatureGate>();
        gate.Setup(g => g.CheckFeatureAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string key, string? _, CancellationToken _) =>
                (featureAllowed?.Invoke(key) ?? true) ? FeatureGateResult.Allowed() : FeatureGateResult.Denied(FeatureDenialReason.PlatformLicense));

        var authz = new Mock<IAuthorizationService>();
        authz.Setup(a => a.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object?>(), It.IsAny<string>()))
            .ReturnsAsync((ClaimsPrincipal _, object? _, string policy) =>
            {
                var permission = policy.Substring(PermissionPolicyConstants.PolicyPrefix.Length);
                return (permissionAllowed?.Invoke(permission) ?? true) ? AuthorizationResult.Success() : AuthorizationResult.Failed();
            });

        var services = new ServiceCollection();
        foreach (var provider in providers)
            services.AddSingleton<IAdminSearchProvider>(provider);
        var root = services.BuildServiceProvider();

        var service = new AdminSearchService(
            root.GetServices<IAdminSearchProvider>(),
            gate.Object,
            authz.Object,
            root.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<AdminSearchService>.Instance);
        return (service, gate, authz);
    }

    [Fact]
    public async Task Provider_is_skipped_when_the_tenant_lacks_its_feature()
    {
        var hr = new FakeProvider("hr.employees", "hr", "hr.employees.read", (_, _) => Task.FromResult(Hits(("e1", 1))));
        var users = new FakeProvider("platform.users", AdminSearchModules.Platform, "users.read", (_, _) => Task.FromResult(Hits(("u1", 1))));
        var (service, gate, _) = Rig(new[] { hr, users }, featureAllowed: key => key != "hr");

        var response = await service.SearchAsync(new AdminSearchRequest { Query = "ann" }, User, UserId, Tenant);

        response.Groups.Select(g => g.ProviderId).Should().Equal("platform.users");
        hr.Calls.Should().Be(0, "a gated-out provider is never invoked");
        gate.Verify(g => g.CheckFeatureAsync("hr", UserId, It.IsAny<CancellationToken>()), Times.Once);
        gate.Verify(g => g.CheckFeatureAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once, "core providers have no feature to check");
    }

    [Fact]
    public async Task Provider_is_skipped_when_the_caller_lacks_its_permission()
    {
        var tenants = new FakeProvider("platform.tenants", AdminSearchModules.Platform, "tenants.read", (_, _) => Task.FromResult(Hits(("t1", 1))));
        var users = new FakeProvider("platform.users", AdminSearchModules.Platform, "users.read", (_, _) => Task.FromResult(Hits(("u1", 1))));
        var (service, _, _) = Rig(new[] { tenants, users }, permissionAllowed: p => p == "users.read");

        var response = await service.SearchAsync(new AdminSearchRequest { Query = "acme" }, User, UserId, Tenant);

        response.Groups.Select(g => g.ProviderId).Should().Equal("platform.users");
        tenants.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Every_declared_permission_must_hold()
    {
        var customers = new FakeProvider("accounting.customers", "accounting", "accounting.administration",
            (_, _) => Task.FromResult(Hits(("c1", 1))), "acct.ar.customers.view");
        var (service, _, _) = Rig(new[] { customers }, permissionAllowed: p => p == "acct.ar.customers.view");

        var response = await service.SearchAsync(new AdminSearchRequest { Query = "akin" }, User, UserId, Tenant);

        response.Groups.Should().BeEmpty("the entity permission alone does not open the module's admin pages");
        customers.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Available_providers_reflect_both_gates_and_carry_filters()
    {
        var filter = new AdminSearchFilterDefinition { Key = "status", Label = "Status", Type = AdminSearchFilterTypes.Select };
        var invoices = new FakeProvider("accounting.invoices", "accounting", "acct.ar.invoices.view", (_, _) => Task.FromResult(Hits())) { Filters = new[] { filter } };
        var bills = new FakeProvider("accounting.bills", "accounting", "acct.ap.bills.view", (_, _) => Task.FromResult(Hits()));
        var crm = new FakeProvider("crm.contacts", "crm", "crm.administration", (_, _) => Task.FromResult(Hits()));
        var (service, _, _) = Rig(new[] { invoices, bills, crm }, featureAllowed: key => key == "accounting", permissionAllowed: p => p != "acct.ap.bills.view");

        var available = await service.GetAvailableProvidersAsync(User, UserId, Tenant);

        available.Select(p => p.ProviderId).Should().Equal("accounting.invoices");
        available[0].Filters.Should().ContainSingle(f => f.Key == "status");
    }

    [Fact]
    public async Task Results_are_grouped_per_provider_sorted_by_score_and_capped_at_the_limit()
    {
        var users = new FakeProvider("platform.users", AdminSearchModules.Platform, "users.read",
            (_, _) => Task.FromResult(Hits(("low", 0.5), ("exact", 3), ("prefix", 2))));
        var clients = new FakeProvider("platform.clients", AdminSearchModules.Platform, "clients.read",
            (_, _) => Task.FromResult(Hits(("c1", 1))));
        var (service, _, _) = Rig(new[] { users, clients });

        var response = await service.SearchAsync(new AdminSearchRequest { Query = "ann", Limit = 2 }, User, UserId, Tenant);

        response.Groups.Should().HaveCount(2);
        response.Groups[0].ProviderId.Should().Be("platform.users", "the group holding the best match comes first");
        response.Groups[0].Items.Select(i => i.Id).Should().Equal("exact", "prefix");
        response.Groups[0].TotalCount.Should().Be(3, "the total reflects every match, not only the returned page");
        response.Groups[0].SeeAllRoute.Should().Be("/x");
        response.TotalCount.Should().Be(4);
    }

    [Fact]
    public async Task Empty_and_failing_providers_do_not_hide_the_others()
    {
        var empty = new FakeProvider("hr.positions", "hr", "hr.positions.read", (_, _) => Task.FromResult(AdminSearchProviderResult.Empty));
        var broken = new FakeProvider("hr.departments", "hr", "hr.departments.read", (_, _) => throw new InvalidOperationException("boom"));
        var users = new FakeProvider("platform.users", AdminSearchModules.Platform, "users.read", (_, _) => Task.FromResult(Hits(("u1", 1))));
        var (service, _, _) = Rig(new[] { empty, broken, users });

        var response = await service.SearchAsync(new AdminSearchRequest { Query = "ann" }, User, UserId, Tenant);

        response.Groups.Select(g => g.ProviderId).Should().Equal("platform.users");
    }

    [Fact]
    public async Task Query_shorter_than_the_minimum_searches_nothing()
    {
        var users = new FakeProvider("platform.users", AdminSearchModules.Platform, "users.read", (_, _) => Task.FromResult(Hits(("u1", 1))));
        var (service, _, _) = Rig(new[] { users });

        var response = await service.SearchAsync(new AdminSearchRequest { Query = " a " }, User, UserId, Tenant);

        response.Groups.Should().BeEmpty();
        users.Calls.Should().Be(0);
    }

    [Fact]
    public async Task Narrowing_to_a_provider_passes_its_filters_and_skips_the_rest()
    {
        AdminSearchContext? seen = null;
        var invoices = new FakeProvider("accounting.invoices", "accounting", "acct.ar.invoices.view", (ctx, _) => { seen = ctx; return Task.FromResult(Hits(("inv-1", 1))); });
        var users = new FakeProvider("platform.users", AdminSearchModules.Platform, "users.read", (_, _) => Task.FromResult(Hits(("u1", 1))));
        var (service, _, _) = Rig(new[] { invoices, users });

        var response = await service.SearchAsync(new AdminSearchRequest
        {
            Query = "acme",
            ProviderId = "accounting.invoices",
            Filters = new Dictionary<string, string> { ["status"] = "Overdue", ["issued"] = "2026-01-01..2026-03-31" },
            Limit = 500
        }, User, UserId, Tenant);

        response.Groups.Select(g => g.ProviderId).Should().Equal("accounting.invoices");
        users.Calls.Should().Be(0);
        seen.Should().NotBeNull();
        seen!.TenantId.Should().Be(Tenant);
        seen.UserId.Should().Be(UserId);
        seen.Filter("status").Should().Be("Overdue");
        seen.Filter("issued").Should().Be("2026-01-01..2026-03-31");
        seen.Filter("missing").Should().BeNull();
        seen.Limit.Should().Be(AdminSearchDefaults.MaxLimit, "the limit is clamped");
    }

    [Fact]
    public void Context_matching_and_scoring_favour_the_primary_field()
    {
        var context = new AdminSearchContext { UserId = UserId, User = User, Query = "ann" };

        context.Matches("Joanne Smith").Should().BeTrue();
        context.Matches("Bob", null, "ann@example.com").Should().BeTrue();
        context.Matches("Bob", null).Should().BeFalse();
        context.ScoreFor("Ann").Should().Be(3);
        context.ScoreFor("Annette").Should().Be(2);
        context.ScoreFor("Joanne").Should().Be(1.5);
        context.ScoreFor("Bob", "ann@example.com").Should().Be(1);
        context.ScoreFor("Bob", "bob@example.com").Should().Be(0);
    }
}

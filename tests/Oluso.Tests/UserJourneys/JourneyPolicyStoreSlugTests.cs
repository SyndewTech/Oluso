using FluentAssertions;
using Microsoft.Data.Sqlite;
using Oluso.Core.UserJourneys;
using Oluso.EntityFramework;
using Oluso.EntityFramework.Stores;
using Oluso.Tests.Roles;
using Xunit;

namespace Oluso.Tests.UserJourneys;

/// <summary>
/// Slugs are unique per tenant: two tenants can each have a business-signin, a tenant
/// only ever reaches its own (or a global) policy by slug, and a global slug is free in
/// no tenant.
/// </summary>
public sealed class JourneyPolicyStoreSlugTests : IDisposable
{
    private readonly SqliteConnection _connection = IdentityTestFactory.OpenSqliteConnection();
    private readonly TestTenantContext _tenant = new();
    private readonly OlusoDbContext _db;
    private readonly JourneyPolicyStore _store;

    public JourneyPolicyStoreSlugTests()
    {
        _db = IdentityTestFactory.CreateSqliteContext(_connection, _tenant);
        _store = new JourneyPolicyStore(_db, _tenant);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private static JourneyPolicy Policy(string slug, string? tenantId, string name) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Slug = slug,
        Name = name,
        TenantId = tenantId,
        Type = JourneyType.SignIn,
        Steps = new List<JourneyPolicyStep> { new() { Id = "login", Type = "local_login", Order = 1 } }
    };

    private async Task<JourneyPolicy> SaveAs(string? tenantId, JourneyPolicy policy)
    {
        _tenant.TenantId = tenantId;
        await _store.SaveAsync(policy);
        _db.ChangeTracker.Clear();
        return policy;
    }

    [Fact]
    public async Task Two_tenants_can_use_the_same_slug_and_each_reaches_its_own()
    {
        await SaveAs("acme", Policy("business-signin", "acme", "Acme business"));
        await SaveAs("globex", Policy("business-signin", "globex", "Globex business"));

        _tenant.TenantId = "acme";
        (await _store.GetBySlugAsync("business-signin"))!.Name.Should().Be("Acme business");
        _tenant.TenantId = "globex";
        (await _store.GetBySlugAsync("business-signin"))!.Name.Should().Be("Globex business");
    }

    [Fact]
    public async Task Without_a_tenant_only_global_policies_are_reachable_by_slug()
    {
        await SaveAs("acme", Policy("business-signin", "acme", "Acme business"));
        await SaveAs(null, Policy("signin", null, "Default sign-in"));

        _tenant.TenantId = null;
        (await _store.GetBySlugAsync("business-signin")).Should().BeNull();
        (await _store.GetBySlugAsync("signin"))!.Name.Should().Be("Default sign-in");
    }

    [Fact]
    public async Task A_slug_is_taken_by_the_tenant_or_a_global_policy_but_not_by_another_tenant()
    {
        var acme = await SaveAs("acme", Policy("business-signin", "acme", "Acme business"));
        await SaveAs(null, Policy("signin", null, "Default sign-in"));

        _tenant.TenantId = "globex";
        (await _store.IsSlugTakenAsync("business-signin")).Should().BeFalse();
        (await _store.IsSlugTakenAsync("signin")).Should().BeTrue();

        _tenant.TenantId = "acme";
        (await _store.IsSlugTakenAsync("business-signin", exceptPolicyId: acme.Id)).Should().BeFalse("it is the policy's own slug");

        _tenant.TenantId = null;
        (await _store.IsSlugTakenAsync("business-signin")).Should().BeTrue("a new global policy would clash with acme's");
    }

    [Fact]
    public async Task A_policy_without_a_slug_is_not_saved()
    {
        var act = () => SaveAs("acme", Policy(" ", "acme", "No slug"));

        await act.Should().ThrowAsync<ArgumentException>();
    }
}

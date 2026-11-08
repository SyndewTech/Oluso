using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.EntityFramework;
using Oluso.EntityFramework.Stores;
using Xunit;

namespace Oluso.Tests.Search;

/// <summary>
/// Tenant and organisation search runs in the query: the term, the caller's
/// organisation scope and the enabled flag narrow the rows before paging, and
/// the total reflects every match rather than the page.
/// </summary>
public class PlatformSearchStoreTests : IDisposable
{
    private readonly OlusoDbContext _context;
    private readonly TenantStore _tenants;
    private readonly OrganizationStore _organizations;

    public PlatformSearchStoreTests()
    {
        var options = new DbContextOptionsBuilder<OlusoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new OlusoDbContext(options);
        _tenants = new TenantStore(_context);
        _organizations = new OrganizationStore(_context);
    }

    public void Dispose() => _context.Dispose();

    private async Task<(Organization Acme, Organization Globex)> SeedAsync()
    {
        var acme = await _organizations.CreateAsync(new Organization { Name = "Acme Holdings", Slug = "acme", Description = "Anvils" });
        var globex = await _organizations.CreateAsync(new Organization { Name = "Globex", Slug = "globex", Enabled = false });
        var disabledAcme = await _organizations.CreateAsync(new Organization { Name = "Acme Legacy", Slug = "acme-legacy", Enabled = false });

        foreach (var (name, identifier, org, enabled) in new[]
        {
            ("Acme Production", "acme-prod", acme, true),
            ("Acme Staging", "acme-staging", acme, false),
            ("Acme Sandbox", "acme-sandbox", acme, true),
            ("Globex Production", "globex-prod", globex, true),
            ("Legacy Acme", "legacy", disabledAcme, true),
        })
        {
            await _tenants.CreateAsync(new Tenant { Id = identifier, Name = name, Identifier = identifier, OrganizationId = org.Id, Enabled = enabled });
        }
        return (acme, globex);
    }

    [Fact]
    public async Task Tenants_are_matched_on_name_or_identifier_and_counted_beyond_the_page()
    {
        await SeedAsync();

        var (items, total) = await _tenants.SearchAsync(new TenantSearchQuery { Search = "acme", Take = 2 });

        total.Should().Be(4, "four tenants carry acme in their name or identifier");
        items.Should().HaveCount(2);
        items.Select(t => t.Name).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Tenant_scope_and_enabled_flag_are_applied_in_the_query()
    {
        var (acme, _) = await SeedAsync();

        var scoped = await _tenants.SearchAsync(new TenantSearchQuery { Search = "acme", OrganizationIds = new[] { acme.Id } });
        scoped.TotalCount.Should().Be(3);

        var enabledOnly = await _tenants.SearchAsync(new TenantSearchQuery { Search = "acme", OrganizationIds = new[] { acme.Id }, Enabled = true });
        enabledOnly.Items.Select(t => t.Identifier).Should().BeEquivalentTo("acme-prod", "acme-sandbox");

        var nowhere = await _tenants.SearchAsync(new TenantSearchQuery { Search = "acme", OrganizationIds = Array.Empty<string>() });
        nowhere.TotalCount.Should().Be(0, "an empty scope matches nothing rather than everything");
    }

    [Fact]
    public async Task Organisations_are_matched_on_name_slug_or_description_within_scope()
    {
        var (acme, globex) = await SeedAsync();

        var all = await _organizations.SearchAsync(new OrganizationSearchQuery { Search = "acme" });
        all.TotalCount.Should().Be(2);

        var byDescription = await _organizations.SearchAsync(new OrganizationSearchQuery { Search = "anvil" });
        byDescription.Items.Should().ContainSingle(o => o.Id == acme.Id);

        var scoped = await _organizations.SearchAsync(new OrganizationSearchQuery { Search = "acme", OrganizationIds = new[] { globex.Id } });
        scoped.TotalCount.Should().Be(0);

        var enabled = await _organizations.SearchAsync(new OrganizationSearchQuery { Search = "acme", Enabled = true });
        enabled.Items.Select(o => o.Slug).Should().Equal("acme");
    }
}

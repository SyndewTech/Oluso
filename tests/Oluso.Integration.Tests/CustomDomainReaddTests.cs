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
/// A custom domain has one record across all tenants (unique in the database). Adding a domain whose record was
/// removed or failed verification reopens that record instead of inserting a second one, which the database refuses;
/// a domain that is live answers 409, for this tenant or another; and retrying a failed domain starts it over.
/// Domains are under .invalid (RFC 6761), which never resolves, so a verification check fails without waiting.
/// </summary>
public class CustomDomainReaddTests : IntegrationTestBase
{
    private const string Tenant = "default";
    private readonly string _otherTenant = "domains-other-" + Guid.NewGuid().ToString("N")[..8];

    public CustomDomainReaddTests(OlusoWebApplicationFactory factory) : base(factory)
    {
    }

    public override Task InitializeAsync() => WithDbContextAsync(async db =>
    {
        var organizationId = (await db.Tenants.FindAsync(Tenant))!.OrganizationId;
        db.Tenants.Add(new Tenant
        {
            Id = _otherTenant, Name = _otherTenant, Identifier = _otherTenant, OrganizationId = organizationId,
        });
        await db.SaveChangesAsync();
    });

    private static string NewDomain() => $"login-{Guid.NewGuid():N}.invalid";

    private async Task<string> SeedAsync(string domain, string tenantId, CustomDomainStatus status, string token = "seededtoken")
    {
        var record = new CustomDomainVerification
        {
            TenantId = tenantId,
            Domain = domain,
            Status = status,
            VerificationToken = token,
            TxtRecordName = $"_oluso-verify.{domain}",
            TxtRecordValue = $"oluso-verify={token}",
            CreatedAt = DateTime.UtcNow.AddDays(-3),
            CheckAttempts = status == CustomDomainStatus.Failed ? 288 : 0,
            FailureReason = status == CustomDomainStatus.Failed ? "Verification timed out" : null,
            VerifiedAt = status == CustomDomainStatus.Removed ? DateTime.UtcNow.AddDays(-2) : null,
            ExternalProvisioningId = status == CustomDomainStatus.Removed ? "edge-1" : null,
        };
        await WithDbContextAsync(async db =>
        {
            db.CustomDomainVerifications.Add(record);
            await db.SaveChangesAsync();
        });
        return record.Id;
    }

    private async Task<HttpResponseMessage> AddAsync(string domain)
    {
        var admin = await SuperAdminClient.CreateAsync(Factory, Tenant);
        return await admin.PostAsJsonAsync($"/api/admin/tenants/{Tenant}/domains", new { domain });
    }

    private async Task<List<CustomDomainVerification>> RecordsAsync(string domain)
    {
        var records = new List<CustomDomainVerification>();
        await WithDbContextAsync(async db =>
            records = await db.CustomDomainVerifications.AsNoTracking().Where(d => d.Domain == domain).ToListAsync());
        return records;
    }

    [Theory]
    [InlineData(CustomDomainStatus.Removed)]
    [InlineData(CustomDomainStatus.Failed)]
    public async Task Adding_a_domain_this_tenant_removed_or_that_failed_reopens_it_with_its_token(CustomDomainStatus status)
    {
        var domain = NewDomain();
        var id = await SeedAsync(domain, Tenant, status);

        var response = await AddAsync(domain);

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("id").GetString().Should().Be(id);
        body.GetProperty("status").GetString().Should().Be("Pending");
        body.GetProperty("txtRecordValue").GetString().Should().Be("oluso-verify=seededtoken",
            "a TXT record the tenant already created still matches");

        var record = (await RecordsAsync(domain)).Should().ContainSingle().Subject;
        record.Status.Should().Be(CustomDomainStatus.Pending);
        record.CheckAttempts.Should().Be(0);
        record.FailureReason.Should().BeNull();
        record.VerifiedAt.Should().BeNull();
        record.CreatedAt.Should().BeAfter(DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public async Task Adding_a_domain_another_tenant_removed_moves_it_here_with_a_new_token()
    {
        var domain = NewDomain();
        var id = await SeedAsync(domain, _otherTenant, CustomDomainStatus.Removed);

        var response = await AddAsync(domain);

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var record = (await RecordsAsync(domain)).Should().ContainSingle().Subject;
        record.Id.Should().Be(id);
        record.TenantId.Should().Be(Tenant);
        record.Status.Should().Be(CustomDomainStatus.Pending);
        record.VerificationToken.Should().NotBe("seededtoken");
        record.TxtRecordValue.Should().Be($"oluso-verify={record.VerificationToken}");
        record.ExternalProvisioningId.Should().Be("edge-1",
            "the edge hostname is the domain's; activation checks it and resubmits it if the provider has dropped it");
    }

    [Theory]
    [InlineData(true, "already been added to this organization")]
    [InlineData(false, "in use by another organization")]
    public async Task A_live_domain_answers_conflict(bool thisTenant, string message)
    {
        var domain = NewDomain();
        await SeedAsync(domain, thisTenant ? Tenant : _otherTenant, CustomDomainStatus.Pending);

        var response = await AddAsync(domain);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadAsStringAsync()).Should().Contain(message).And.NotContain(_otherTenant);
        (await RecordsAsync(domain)).Should().ContainSingle();
    }

    [Fact]
    public async Task A_new_domain_is_added()
    {
        var domain = NewDomain();

        var response = await AddAsync(domain.ToUpperInvariant());

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        (await RecordsAsync(domain)).Should().ContainSingle(d => d.TenantId == Tenant && d.Status == CustomDomainStatus.Pending);
    }

    [Fact]
    public async Task Retrying_a_failed_domain_starts_its_verification_over()
    {
        var domain = NewDomain();
        var id = await SeedAsync(domain, Tenant, CustomDomainStatus.Failed);
        var admin = await SuperAdminClient.CreateAsync(Factory, Tenant);

        var response = await admin.PostAsync($"/api/admin/tenants/{Tenant}/domains/{id}/verify", null);

        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var record = (await RecordsAsync(domain)).Single();
        record.Status.Should().Be(CustomDomainStatus.Pending, "the background check picks it up again");
        record.CheckAttempts.Should().Be(1, "this retry is the first check of the new round");
        record.VerificationToken.Should().Be("seededtoken");
    }
}

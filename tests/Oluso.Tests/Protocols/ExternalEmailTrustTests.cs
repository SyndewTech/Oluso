using FluentAssertions;
using Moq;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Xunit;

namespace Oluso.Tests.Protocols;

/// <summary>
/// Which external providers can vouch for an address: at the tenant's verified domains one that says it's verified
/// or is pinned to the tenant's directory; elsewhere only a provider that owns the address.
/// </summary>
public class ExternalEmailTrustTests
{
    private const string Tenant = "t1";
    private readonly Mock<ITenantEmailDomainStore> _domains = new();
    private readonly Mock<IExternalProviderConfigStore> _providers = new();

    private void Provider(string scheme, string type, Dictionary<string, string>? properties = null) =>
        _providers.Setup(p => p.GetBySchemeAsync(scheme, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalProviderDefinition
            {
                Id = scheme, Scheme = scheme, ProviderType = type, ClientId = "c", ClientSecret = "s", Properties = properties
            });

    private void TenantVerified(string email) =>
        _domains.Setup(d => d.IsVerifiedForTenantAsync(Tenant, email, It.IsAny<CancellationToken>())).ReturnsAsync(true);

    private Task<bool> Trusted(string provider, string email, params (string Type, string Value)[] claims) =>
        ExternalEmailTrust.IsTrustedAsync(
            provider,
            type => claims.FirstOrDefault(c => c.Type == type).Value,
            email, Tenant, _providers.Object, _domains.Object);

    [Fact]
    public async Task Google_vouches_for_a_verified_gmail_address()
    {
        Provider("Google", "google");
        (await Trusted("Google", "ada@gmail.com", ("email_verified", "True"))).Should().BeTrue();
        (await Trusted("Google", "ada@gmail.com")).Should().BeFalse("Google didn't say it was verified");
    }

    [Fact]
    public async Task Google_does_not_vouch_for_another_domain_unless_it_is_the_workspace_domain()
    {
        Provider("Google", "google");
        (await Trusted("Google", "ceo@acme.test", ("email_verified", "true"))).Should().BeFalse();
        (await Trusted("Google", "ceo@acme.test", ("email_verified", "true"), ("hd", "acme.test"))).Should().BeTrue();
    }

    [Fact]
    public async Task Microsoft_vouches_only_at_a_verified_domain_with_a_pinned_directory()
    {
        Provider("Entra", "microsoft", new() { ["tenantId"] = "common" });
        TenantVerified("ceo@acme.test");
        (await Trusted("Entra", "ceo@acme.test")).Should().BeFalse("a multi-tenant app lets any directory claim the address");
        (await Trusted("Entra", "ceo@other.test")).Should().BeFalse();

        Provider("Entra", "microsoft", new() { ["tenantId"] = "5f1c0a6e-2b8d-4c1e-9a7b-3d2e1f0c9b8a" });
        (await Trusted("Entra", "ceo@acme.test")).Should().BeTrue();
    }

    [Fact]
    public async Task A_saml_idp_vouches_only_at_a_domain_the_tenant_verified()
    {
        TenantVerified("ceo@acme.test");
        (await Trusted("saml:corp", "ceo@acme.test")).Should().BeTrue();
        (await Trusted("saml:corp", "ceo@gmail.com")).Should().BeFalse();
    }

    [Fact]
    public async Task An_oidc_provider_needs_to_say_the_address_is_verified_even_at_a_verified_domain()
    {
        Provider("Okta", "oidc");
        TenantVerified("ceo@acme.test");
        (await Trusted("Okta", "ceo@acme.test")).Should().BeFalse();
        (await Trusted("Okta", "ceo@acme.test", ("email_verified", "true"))).Should().BeTrue();
        (await Trusted("Okta", "ceo@gmail.com", ("email_verified", "true"))).Should().BeFalse("it doesn't own gmail.com");
    }

    [Theory]
    [InlineData("a@eu.acme.test", new[] { "eu.acme.test", "acme.test" })]
    [InlineData("a@acme.test", new[] { "acme.test" })]
    [InlineData("a@localhost", new string[0])]
    [InlineData("not-an-email", new string[0])]
    public void An_address_is_covered_by_its_domain_and_each_parent(string email, string[] expected)
    {
        TenantEmailDomain.CoveringDomains(email).Should().Equal(expected);
    }
}

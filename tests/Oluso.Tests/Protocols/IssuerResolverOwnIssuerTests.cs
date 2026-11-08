using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Moq;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Xunit;

namespace Oluso.Tests.Protocols;

/// <summary>
/// Bearer tokens presented to Oluso's own APIs: the issuer must be one Oluso mints — the base issuer,
/// a tenant subdomain, or the token's own tenant's custom domain or issuer override.
/// </summary>
public class IssuerResolverOwnIssuerTests
{
    private const string Base = "https://identity.example.test";

    private static readonly Tenant Northwind = new() { Id = "t-northwind", Identifier = "northwind", Name = "Northwind", Enabled = true, CustomDomain = "identity-dev.northwind.example" };
    private static readonly Tenant Other = new() { Id = "t-other", Identifier = "other", Name = "Other", Enabled = true, CustomDomain = "login.other.test" };
    private static readonly Tenant Disabled = new() { Id = "t-off", Identifier = "off", Name = "Off", Enabled = false, CustomDomain = "login.off.test" };

    private static IssuerResolver Resolver(Tenant? inContext = null, string? issuerOverride = null)
    {
        var context = new Mock<ITenantContext>();
        context.SetupGet(c => c.TenantId).Returns(inContext?.Id);
        context.SetupGet(c => c.Tenant).Returns(inContext);
        context.SetupGet(c => c.HasTenant).Returns(inContext != null);
        var settings = new Mock<ITenantSettingsProvider>();
        settings.Setup(s => s.GetTokenSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantTokenSettings { IssuerUri = issuerOverride });
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Oluso:IssuerUri"] = Base })
            .Build();
        return new IssuerResolver(context.Object, settings.Object, configuration, Mock.Of<IHttpContextAccessor>());
    }

    [Theory]
    [InlineData(Base)]
    [InlineData(Base + "/")]
    [InlineData("https://northwind.identity.example.test")]
    public async Task The_base_issuer_and_tenant_subdomains_are_ours(string issuer) =>
        (await Resolver().IsOwnIssuerAsync(issuer, null)).Should().BeTrue();

    [Fact]
    public async Task A_token_minted_on_its_tenants_custom_domain_is_ours()
    {
        (await Resolver().IsOwnIssuerAsync("https://identity-dev.northwind.example", Northwind)).Should().BeTrue();
        (await Resolver(Northwind).IsOwnIssuerAsync("https://IDENTITY-DEV.northwind.example/", Northwind)).Should().BeTrue();
    }

    [Fact]
    public async Task Other_issuers_are_refused()
    {
        var resolver = Resolver();
        (await resolver.IsOwnIssuerAsync("https://login.other.test", Northwind)).Should().BeFalse("another tenant's custom domain");
        (await resolver.IsOwnIssuerAsync("https://login.off.test", Disabled)).Should().BeFalse("a disabled tenant");
        (await resolver.IsOwnIssuerAsync("https://evil.test", Northwind)).Should().BeFalse("not one of ours");
        (await resolver.IsOwnIssuerAsync("https://identity-dev.northwind.example", null)).Should().BeFalse("no tenant vouches for it");
        (await resolver.IsOwnIssuerAsync(null, Northwind)).Should().BeFalse();
    }

    [Fact]
    public async Task A_tenants_issuer_override_is_ours_for_that_tenant_only()
    {
        const string overrideIssuer = "https://auth.northwind.example";

        (await Resolver(Northwind, overrideIssuer).IsOwnIssuerAsync(overrideIssuer, Northwind)).Should().BeTrue();
        (await Resolver(Other, overrideIssuer).IsOwnIssuerAsync(overrideIssuer, Northwind)).Should().BeFalse();
    }
}

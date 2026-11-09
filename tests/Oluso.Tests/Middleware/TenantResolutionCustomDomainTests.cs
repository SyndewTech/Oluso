using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Middleware;
using Xunit;

namespace Oluso.Tests.Middleware;

/// <summary>
/// Tenant resolution for hosts that are custom domains rather than subdomains
/// of the configured BaseDomain.
///
/// A custom domain has three or more labels, which used to be claimed by the
/// legacy "first label is the tenant" heuristic: "identity-dev.northwind.example"
/// resolved to "identity-dev", matched no tenant, and — because it returned a
/// non-empty identifier — shadowed the custom-domain lookup that would have
/// worked. The request then fell through to the default tenant, so the OIDC
/// discovery document served on a tenant's own domain advertised the platform
/// issuer and sent clients back off the domain.
/// </summary>
public class TenantResolutionCustomDomainTests
{
    private const string BaseDomain = "platform-dev.example.com";
    private const string CustomDomain = "identity-dev.northwind.example";

    private readonly Mock<ITenantStore> _tenantStore = new();
    private readonly Mock<ITenantContextAccessor> _tenantAccessor = new();

    private readonly Tenant _tenant = new()
    {
        Id = "northwind",
        Identifier = "northwind",
        DisplayName = "Northwind",
        CustomDomain = CustomDomain,
        Enabled = true
    };

    public TenantResolutionCustomDomainTests()
    {
        _tenantStore.Setup(x => x.GetByCustomDomainAsync(CustomDomain, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_tenant);
        _tenantStore.Setup(x => x.GetByIdentifierAsync("northwind", It.IsAny<CancellationToken>()))
            .ReturnsAsync(_tenant);
        _tenantStore.Setup(x => x.GetByIdAsync("northwind", It.IsAny<CancellationToken>()))
            .ReturnsAsync(_tenant);
    }

    [Fact]
    public async Task CustomDomainHost_ResolvesTheOwningTenant()
    {
        var context = BuildContext(CustomDomain);

        var reached = await InvokeAsync(context);

        reached.Should().BeTrue();
        _tenantAccessor.Verify(
            x => x.SetTenant(It.Is<Tenant>(t => t.Id == "northwind")),
            Times.Once,
            "the host matches Tenants.CustomDomain, which is the tenant signal");
    }

    [Fact]
    public async Task CustomDomainHost_IsNotClaimedAsASubdomainOfBaseDomain()
    {
        var context = BuildContext(CustomDomain);

        await InvokeAsync(context);

        _tenantStore.Verify(
            x => x.GetByIdentifierAsync("identity-dev", It.IsAny<CancellationToken>()),
            Times.Never,
            "the first label of a custom domain is not a tenant identifier");
    }

    [Fact]
    public async Task SubdomainOfBaseDomain_StillResolvesBySubdomain()
    {
        var context = BuildContext($"northwind.{BaseDomain}");

        var reached = await InvokeAsync(context);

        reached.Should().BeTrue();
        _tenantAccessor.Verify(x => x.SetTenant(It.Is<Tenant>(t => t.Id == "northwind")), Times.Once);
        _tenantStore.Verify(
            x => x.GetByCustomDomainAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "the subdomain already identified the tenant");
    }

    [Fact]
    public async Task BaseDomainItself_ResolvesNoTenant()
    {
        var context = BuildContext(BaseDomain);

        var reached = await InvokeAsync(context);

        reached.Should().BeTrue();
        _tenantAccessor.Verify(x => x.SetTenant(It.IsAny<Tenant>()), Times.Never);
    }

    [Fact]
    public async Task UnknownHost_ResolvesNoTenant()
    {
        var context = BuildContext("not-a-tenant.example.com");

        var reached = await InvokeAsync(context);

        reached.Should().BeTrue("an unfamiliar host still gets the platform-level response");
        _tenantAccessor.Verify(x => x.SetTenant(It.IsAny<Tenant>()), Times.Never);
    }

    private static DefaultHttpContext BuildContext(string host)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/.well-known/openid-configuration";
        context.Request.Host = new HostString(host);
        context.Response.Body = new MemoryStream();
        return context;
    }

    private async Task<bool> InvokeAsync(DefaultHttpContext context)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_tenantStore.Object);
        services.AddSingleton(_tenantAccessor.Object);
        services.AddSingleton<IOptions<MultiTenancyOptions>>(
            Options.Create(new MultiTenancyOptions { BaseDomain = BaseDomain }));

        context.RequestServices = services.BuildServiceProvider();

        var reachedNext = false;
        var middleware = new TenantResolutionMiddleware(
            _ =>
            {
                reachedNext = true;
                return Task.CompletedTask;
            },
            NullLogger<TenantResolutionMiddleware>.Instance);

        await middleware.InvokeAsync(context);
        return reachedNext;
    }
}

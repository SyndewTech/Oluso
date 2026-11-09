using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Services;
using Xunit;

namespace Oluso.Tests.Cors;

/// <summary>Which origins may call the server: public metadata, clients, and each tenant on its own host.</summary>
public class CorsPolicyProviderTests
{
    private readonly ServiceProvider _services;
    private readonly OlusoCorsPolicyProvider _provider;
    private readonly TenantCorsSettings _acmeCors = new() { AllowedOrigins = new() { "https://app.acme.test" } };

    public CorsPolicyProviderTests()
    {
        var tenants = new Mock<ITenantStore>();
        tenants.Setup(t => t.GetByIdentifierAsync("acme", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant { Id = "t-acme", Identifier = "acme", Name = "Acme" });
        tenants.Setup(t => t.GetByIdentifierAsync("globex", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant { Id = "t-globex", Identifier = "globex", Name = "Globex" });
        var config = new Mock<ITenantConfigurationService>();
        config.Setup(c => c.GetSectionAsync<TenantCorsSettings>("t-acme", TenantCorsSettings.SectionKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => _acmeCors);

        _services = new ServiceCollection()
            .AddSingleton(tenants.Object)
            .AddSingleton(config.Object)
            .AddSingleton<IDistributedCache>(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())))
            .AddSingleton<IOptions<MultiTenancyOptions>>(Options.Create(new MultiTenancyOptions { BaseDomain = "auth.example.test" }))
            .BuildServiceProvider();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Oluso:Cors:Origins:0"] = "https://admin.example.test" })
            .Build();
        _provider = new OlusoCorsPolicyProvider(_services, configuration, NullLogger<OlusoCorsPolicyProvider>.Instance);
    }

    private Task<Microsoft.AspNetCore.Cors.Infrastructure.CorsPolicy?> Policy(string host, string path, string origin)
    {
        var context = new DefaultHttpContext { RequestServices = _services };
        context.Request.Host = new HostString(host);
        context.Request.Path = path;
        context.Request.Headers.Origin = origin;
        return _provider.GetPolicyAsync(context, "Oluso");
    }

    [Fact]
    public async Task Discovery_and_keys_answer_any_origin_without_credentials()
    {
        foreach (var path in new[] { "/.well-known/openid-configuration", "/.well-known/jwks" })
        {
            var policy = await Policy("auth.example.test", path, "https://anything.example");
            policy!.AllowAnyOrigin.Should().BeTrue();
            policy.SupportsCredentials.Should().BeFalse();
        }
    }

    [Fact]
    public async Task A_tenants_origins_apply_on_its_own_host_only()
    {
        var own = await Policy("acme.auth.example.test", "/connect/token", "https://app.acme.test");
        own!.Origins.Should().Equal("https://app.acme.test");
        own.SupportsCredentials.Should().BeTrue();

        var other = await Policy("globex.auth.example.test", "/connect/token", "https://app.acme.test");
        other!.Origins.Should().NotContain("https://app.acme.test");
        other.AllowAnyOrigin.Should().BeFalse();
    }

    [Fact]
    public async Task A_tenant_allowing_all_origins_gets_no_credentials()
    {
        _acmeCors.AllowAllOrigins = true;

        var policy = await Policy("acme.auth.example.test", "/connect/token", "https://anything.example");

        policy!.AllowAnyOrigin.Should().BeTrue();
        policy.SupportsCredentials.Should().BeFalse();
    }

    [Fact]
    public async Task Tunnel_origins_are_not_trusted()
    {
        var policy = await Policy("auth.example.test", "/connect/token", "https://abc.ngrok-free.app");

        policy!.AllowAnyOrigin.Should().BeFalse();
        policy.Origins.Should().NotContain("https://abc.ngrok-free.app");
    }

    [Fact]
    public async Task Configured_origins_still_apply_everywhere()
    {
        var policy = await Policy("globex.auth.example.test", "/connect/token", "https://admin.example.test");

        policy!.Origins.Should().Equal("https://admin.example.test");
    }

    [Fact]
    public async Task Browsers_can_read_the_auth_challenge_and_the_dpop_nonce_on_every_policy()
    {
        var policies = new[]
        {
            await Policy("auth.example.test", "/.well-known/jwks", "https://anything.example"),
            await Policy("acme.auth.example.test", "/api/workspace/me", "https://app.acme.test"),
            await Policy("globex.auth.example.test", "/connect/token", "https://admin.example.test"),
        };

        foreach (var policy in policies)
            policy!.ExposedHeaders.Should().Contain(new[] { "WWW-Authenticate", "DPoP-Nonce" });
    }
}

using System.Security.Claims;
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
/// Tenant resolution for client_credentials (machine-to-machine) tokens.
///
/// These tokens carry client_id + tenant_id but no subject, so they can never
/// carry allowed_tenants — that claim is issued from TenantMembership rows for
/// user tokens only. Before the machine-client branch existed, every service
/// call fell through the user gate and 403'd.
/// </summary>
public class TenantResolutionMachineClientTests
{
    private const string TenantId = "acme";

    private readonly Mock<ITenantStore> _tenantStore = new();
    private readonly Mock<ITenantContextAccessor> _tenantAccessor = new();

    public TenantResolutionMachineClientTests()
    {
        var tenant = new Tenant
        {
            Id = TenantId,
            Identifier = TenantId,
            DisplayName = "Acme",
            Enabled = true
        };

        _tenantStore.Setup(x => x.GetByIdentifierAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
        _tenantStore.Setup(x => x.GetByIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenant);
    }

    [Fact]
    public async Task ClientCredentialsToken_ResolvesTenantFromToken_AndReachesTheEndpoint()
    {
        var context = BuildContext(MachineClientPrincipal(TenantId));

        var reached = await InvokeAsync(context);

        reached.Should().BeTrue("a machine client must not be stopped by the allowed_tenants gate");
        context.Response.StatusCode.Should().Be(200);
        _tenantAccessor.Verify(x => x.SetTenant(It.Is<Tenant>(t => t.Id == TenantId)), Times.Once);
    }

    [Fact]
    public async Task ClientCredentialsToken_IgnoresTenantHeader()
    {
        var context = BuildContext(MachineClientPrincipal(TenantId));
        context.Request.Headers["X-Tenant-Id"] = "some-other-tenant";

        var reached = await InvokeAsync(context);

        reached.Should().BeTrue();
        _tenantAccessor.Verify(x => x.SetTenant(It.Is<Tenant>(t => t.Id == TenantId)), Times.Once);
        _tenantStore.Verify(
            x => x.GetByIdentifierAsync("some-other-tenant", It.IsAny<CancellationToken>()),
            Times.Never,
            "the client's registered tenant is the only tenant it may act in");
    }

    [Fact]
    public async Task UserTokenWithoutAllowedTenants_IsStillRejected()
    {
        // Same shape as a machine token except it has a subject — the user gate
        // must keep applying, or this branch would be a way around it.
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("client_id", "some-client"),
            new Claim("sub", "user-1"),
            new Claim("tenant_id", TenantId)
        }, "TestAuth"));

        var context = BuildContext(user);

        var reached = await InvokeAsync(context);

        reached.Should().BeFalse();
        context.Response.StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task MachineTokenWithoutTenant_IsRejectedAsTenantRequired()
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim("client_id", "platform-service")
        }, "TestAuth"));

        var context = BuildContext(user);
        // No tenant_id on the token and no default configured — nothing to scope to.
        var reached = await InvokeAsync(context, defaultTenantId: null);

        reached.Should().BeFalse();
        context.Response.StatusCode.Should().Be(400);
    }

    private static ClaimsPrincipal MachineClientPrincipal(string tenantId) =>
        new(new ClaimsIdentity(new[]
        {
            new Claim("client_id", "svc-client"),
            new Claim("tenant_id", tenantId),
            new Claim("scope", "oluso:service"),
            new Claim("scope", "oluso:users:read")
        }, "TestAuth"));

    private static DefaultHttpContext BuildContext(ClaimsPrincipal user)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/service/users";
        context.Request.Host = new HostString("localhost");
        context.Response.Body = new MemoryStream();
        context.User = user;
        return context;
    }

    private async Task<bool> InvokeAsync(DefaultHttpContext context, string? defaultTenantId = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(_tenantStore.Object);
        services.AddSingleton(_tenantAccessor.Object);
        services.AddSingleton<IOptions<MultiTenancyOptions>>(
            Options.Create(new MultiTenancyOptions { DefaultTenantId = defaultTenantId }));

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

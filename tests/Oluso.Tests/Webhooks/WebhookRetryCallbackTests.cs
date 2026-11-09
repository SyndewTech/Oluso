using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Controllers;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using WebhookDeliveryStatus = Oluso.Core.Events.WebhookDeliveryStatus;
using WebhookDelivery = Oluso.Core.Events.WebhookDelivery;
using Oluso.Core.Events;
using Oluso.Core.Services;
using Xunit;

namespace Oluso.Tests.Webhooks;

/// <summary>
/// Queued webhook retries come back to the platform, which sends them in the delivery's
/// tenant. Only a delivery still waiting to be retried is sent, so a repeated or late
/// message sends nothing.
/// </summary>
public class WebhookRetryCallbackTests
{
    private readonly Mock<IWebhookSubscriptionStore> _store = new();
    private readonly Mock<IWebhookDispatcher> _dispatcher = new();
    private readonly Mock<ITenantContextAccessor> _tenant = new();
    private readonly Mock<ITenantStore> _tenants = new();

    private PlatformMaintenanceController Controller(string? providedKey = "secret")
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [PlatformMaintenanceController.ApiKeyConfigKey] = "secret" })
            .Build();
        var controller = new PlatformMaintenanceController(
            Mock.Of<ISigningKeyService>(), configuration, NullLogger<PlatformMaintenanceController>.Instance,
            webhookStore: _store.Object, webhookDispatcher: _dispatcher.Object, tenantAccessor: _tenant.Object, tenantStore: _tenants.Object)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
        if (providedKey != null) controller.HttpContext.Request.Headers[PlatformMaintenanceController.ApiKeyHeader] = providedKey;
        return controller;
    }

    private void Delivery(WebhookDeliveryStatus status) =>
        _store.Setup(s => s.GetDeliveryAsync("d1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebhookDelivery { Id = "d1", EndpointId = "e1", EventType = "x.y", PayloadId = "p1", TenantId = "t1", Status = status });

    private static WebhookRetryMessage Message => new() { DeliveryId = "d1" };

    [Fact]
    public async Task A_failed_delivery_is_retried_in_its_tenant()
    {
        Delivery(WebhookDeliveryStatus.Failed);
        var tenant = new Tenant { Id = "t1", Name = "acme", Identifier = "acme" };
        _tenants.Setup(t => t.GetByIdAsync("t1", It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        _dispatcher.Setup(d => d.RetryDeliveryAsync("d1", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await Controller().RetryWebhookDelivery(Message, CancellationToken.None);

        result.Should().BeOfType<OkObjectResult>();
        _tenant.Verify(t => t.ClearTenant(), Times.Once);
        _tenant.Verify(t => t.SetTenant(tenant), Times.Once);
        _dispatcher.Verify(d => d.RetryDeliveryAsync("d1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(WebhookDeliveryStatus.Success)]
    [InlineData(WebhookDeliveryStatus.Exhausted)]
    [InlineData(WebhookDeliveryStatus.Cancelled)]
    public async Task A_delivery_that_is_no_longer_waiting_is_not_sent_again(WebhookDeliveryStatus status)
    {
        Delivery(status);

        (await Controller().RetryWebhookDelivery(Message, CancellationToken.None)).Should().BeOfType<OkObjectResult>();

        _dispatcher.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Without_the_callback_key_nothing_is_sent()
    {
        Delivery(WebhookDeliveryStatus.Failed);

        (await Controller(providedKey: "wrong").RetryWebhookDelivery(Message, CancellationToken.None)).Should().BeOfType<UnauthorizedResult>();

        _dispatcher.VerifyNoOtherCalls();
    }
}

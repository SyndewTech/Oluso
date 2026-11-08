using System.Net;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Oluso.Core.Events;
using Oluso.Webhooks;
using Xunit;
using WebhookDelivery = Oluso.Core.Events.WebhookDelivery;
using WebhookEndpoint = Oluso.Core.Events.WebhookEndpoint;

namespace Oluso.Tests.Webhooks;

/// <summary>
/// A retried delivery is signed over the timestamp it sends, so a receiver that
/// verifies HMAC(timestamp + "." + body) accepts it.
/// </summary>
public class WebhookRetrySignatureTests
{
    private const string Secret = "whsec_test";
    private const string Body = "{\"event_type\":\"user.updated\"}";

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }

    [Fact]
    public async Task Retry_signature_matches_the_timestamp_header()
    {
        var store = new Mock<IWebhookSubscriptionStore>();
        store.Setup(s => s.GetDeliveryAsync("d1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebhookDelivery { Id = "d1", EndpointId = "e1", EventType = "user.updated", PayloadId = "p1", TenantId = "t1", Payload = Body, Status = WebhookDeliveryStatus.Failed });
        store.Setup(s => s.GetEndpointAsync("e1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebhookEndpoint { Id = "e1", TenantId = "t1", Name = "crm", Url = "https://crm.example.org/hook", Secret = Secret, Enabled = true });
        var http = new CapturingHandler();
        var dispatcher = new WebhookDispatcher(
            store.Object, Mock.Of<IWebhookEventRegistry>(), new HttpClient(http),
            NullLogger<WebhookDispatcher>.Instance, Options.Create(new WebhookOptions()));

        (await dispatcher.RetryDeliveryAsync("d1")).Should().BeTrue();

        var request = http.Request!;
        var timestamp = request.Headers.GetValues("X-Webhook-Timestamp").Single();
        var signature = request.Headers.GetValues("X-Webhook-Signature").Single();
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(Secret));
        var expected = "v1=" + Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes($"{timestamp}.{Body}"))).ToLowerInvariant();
        signature.Should().Be(expected);
    }
}

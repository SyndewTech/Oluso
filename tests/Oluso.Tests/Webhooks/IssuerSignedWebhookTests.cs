using System.Net;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Services;
using Oluso.Webhooks;
using Xunit;
using WebhookDelivery = Oluso.Core.Events.WebhookDelivery;
using WebhookEndpoint = Oluso.Core.Events.WebhookEndpoint;
using WebhookDeliveryStatus = Oluso.Core.Events.WebhookDeliveryStatus;

namespace Oluso.Tests.Webhooks;

/// <summary>
/// <see cref="WebhookAuthType.IssuerSigned"/> deliveries carry a token the endpoint's tenant signs with its own key: the
/// receiver checks it against the tenant's JWKS and the body against <c>body_sha256</c>, with no shared secret.
/// </summary>
public class IssuerSignedWebhookTests
{
    private const string TenantId = "tenant-acme";
    private const string Issuer = "https://acme.oluso.app";
    private const string Url = "https://app.example.org/webhooks/oluso/oluso-tenant-acme";

    private readonly RSA _rsa = RSA.Create(2048);
    private readonly RsaSecurityKey _key;
    private readonly MutableClock _clock = new(new DateTimeOffset(2026, 10, 29, 9, 0, 0, TimeSpan.Zero));

    public IssuerSignedWebhookTests()
    {
        _key = new RsaSecurityKey(_rsa) { KeyId = "acme-key-1" };
    }

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<(HttpRequestMessage Request, byte[] Body)> Sent { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Sent.Add((request, await request.Content!.ReadAsByteArrayAsync(cancellationToken)));
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private static WebhookEndpoint Endpoint(string? secret = null, string? clientId = "chat-workitems") => new()
    {
        Id = "ep-1", TenantId = TenantId, Name = "App work items", Url = Url, Enabled = true,
        AuthType = WebhookAuthType.IssuerSigned, ClientId = clientId, Secret = secret,
        EventTypes = new[] { "approval.task.assigned" },
    };

    /// <summary>The real token issuer, in a container whose issuer and key answer only for the tenant in context.</summary>
    private IWebhookTokenIssuer TokenIssuer()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ITenantContextAccessor, TenantContextAccessor>();
        services.AddSingleton<ITenantContext>(sp => sp.GetRequiredService<ITenantContextAccessor>());
        var tenants = new Mock<ITenantStore>();
        tenants.Setup(t => t.GetByIdAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant { Id = TenantId, Identifier = "acme", Name = "Acme" });
        services.AddSingleton(tenants.Object);
        var credentials = new Mock<ISigningCredentialStore>();
        credentials.Setup(c => c.GetSigningCredentialsAsync(TenantId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SigningCredentials(_key, SecurityAlgorithms.RsaSha256));
        services.AddSingleton(credentials.Object);
        services.AddScoped<IIssuerResolver>(sp =>
        {
            var context = sp.GetRequiredService<ITenantContext>();
            var resolver = new Mock<IIssuerResolver>();
            resolver.Setup(r => r.GetIssuerAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => context.TenantId == TenantId ? Issuer : "https://wrong-tenant.example");
            return resolver.Object;
        });
        var provider = services.BuildServiceProvider();
        return new IssuerWebhookTokenIssuer(provider.GetRequiredService<IServiceScopeFactory>(), _clock);
    }

    private WebhookDispatcher Dispatcher(Mock<IWebhookSubscriptionStore> store, HttpMessageHandler http, IWebhookTokenIssuer? issuer)
        => new(store.Object, Mock.Of<IWebhookEventRegistry>(r => r.IsValidEventType(It.IsAny<string>())), new HttpClient(http),
            NullLogger<WebhookDispatcher>.Instance, Options.Create(new WebhookOptions()), tokenIssuer: issuer);

    /// <summary>What a receiver does: the tenant's JWKS (public keys only), its issuer, its own URL, the token type.</summary>
    private async Task<JsonWebToken> VerifyAsync(string token)
    {
        var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(_rsa.ExportParameters(false)) { KeyId = "acme-key-1" });
        var jwks = new JsonWebKeySet();
        jwks.Keys.Add(jwk);
        var result = await new JsonWebTokenHandler().ValidateTokenAsync(token, new TokenValidationParameters
        {
            ValidIssuer = Issuer,
            ValidAudience = Url,
            IssuerSigningKeys = jwks.GetSigningKeys(),
            ValidTypes = new[] { "webhook+jwt" },
            LifetimeValidator = (_, expires, _, _) => expires > _clock.Now.UtcDateTime,
        });
        result.IsValid.Should().BeTrue(result.Exception?.Message);
        return (JsonWebToken)result.SecurityToken;
    }

    private static string Sha256(byte[] body) => Base64UrlEncoder.Encode(SHA256.HashData(body));

    [Fact]
    public async Task A_delivery_carries_a_token_that_verifies_against_the_tenant_jwks_and_binds_the_body()
    {
        var store = new Mock<IWebhookSubscriptionStore>();
        store.Setup(s => s.GetSubscribedEndpointsAsync(TenantId, "approval.task.assigned", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Endpoint() });
        var http = new CapturingHandler();

        var result = await Dispatcher(store, http, TokenIssuer())
            .DispatchAsync(TenantId, "approval.task.assigned", new { instance_id = "inst-1", note = "Ọjọ́ àìkú" });

        result.EndpointsNotified.Should().Be(1);
        var (request, body) = http.Sent.Single();
        request.Headers.Authorization!.Scheme.Should().Be("Bearer");
        request.Headers.Contains("X-Webhook-Signature").Should().BeFalse("the endpoint has no secret");
        request.Headers.GetValues("X-Webhook-Timestamp").Should().ContainSingle();
        var deliveryId = request.Headers.GetValues("X-Webhook-ID").Single();

        var token = await VerifyAsync(request.Headers.Authorization.Parameter!);
        token.Typ.Should().Be("webhook+jwt");
        token.Alg.Should().Be("RS256");
        token.Kid.Should().Be("acme-key-1");
        token.Issuer.Should().Be(Issuer);
        token.Audiences.Should().Equal(Url);
        token.Subject.Should().Be("chat-workitems");
        token.Id.Should().Be(deliveryId);
        token.GetClaim("webhook_event").Value.Should().Be("approval.task.assigned");
        token.GetClaim("body_sha256").Value.Should().Be(Sha256(body));
        token.TryGetPayloadValue<long>("iat", out var iat).Should().BeTrue();
        token.TryGetPayloadValue<long>("exp", out var exp).Should().BeTrue();
        (exp - iat).Should().Be(300);
        iat.Should().Be(_clock.Now.ToUnixTimeSeconds());
        token.TryGetPayloadValue<long>("nbf", out _).Should().BeFalse();
        token.Claims.Select(c => c.Type).Distinct().Should().BeEquivalentTo("iss", "aud", "sub", "iat", "exp", "jti", "webhook_event", "body_sha256");
    }

    [Fact]
    public async Task Retries_mint_a_fresh_token_with_the_same_jti()
    {
        const string payload = """{"event_type":"approval.workflow.approved","data":{"instance_id":"inst-1"}}""";
        var store = new Mock<IWebhookSubscriptionStore>();
        store.Setup(s => s.GetDeliveryAsync("d-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebhookDelivery
            {
                Id = "d-1", EndpointId = "ep-1", EventType = "approval.workflow.approved", PayloadId = "p-1", TenantId = TenantId,
                Payload = payload, Status = WebhookDeliveryStatus.Failed,
            });
        store.Setup(s => s.GetEndpointAsync("ep-1", It.IsAny<CancellationToken>())).ReturnsAsync(Endpoint());
        var http = new CapturingHandler();
        var dispatcher = Dispatcher(store, http, TokenIssuer());

        (await dispatcher.RetryDeliveryAsync("d-1")).Should().BeTrue();
        _clock.Now = _clock.Now.AddMinutes(5);
        (await dispatcher.RetryDeliveryAsync("d-1")).Should().BeTrue();

        var tokens = http.Sent.Select(s => s.Request.Headers.Authorization!.Parameter!).ToList();
        tokens.Should().OnlyHaveUniqueItems("each attempt has its own token");
        var second = await VerifyAsync(tokens[1]);
        second.Id.Should().Be("d-1");
        new JsonWebToken(tokens[0]).Id.Should().Be("d-1");
        second.GetClaim("webhook_event").Value.Should().Be("approval.workflow.approved");
        second.GetClaim("body_sha256").Value.Should().Be(Sha256(Encoding.UTF8.GetBytes(payload)));
        http.Sent.Select(s => s.Request.Headers.GetValues("X-Webhook-ID").Single()).Should().AllBe("d-1");
        http.Sent.Should().OnlyContain(s => s.Request.Headers.Contains("X-Webhook-Timestamp"));
    }

    [Fact]
    public async Task An_endpoint_with_a_secret_gets_the_hmac_signature_as_well()
    {
        var store = new Mock<IWebhookSubscriptionStore>();
        store.Setup(s => s.GetSubscribedEndpointsAsync(TenantId, "approval.task.assigned", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Endpoint(secret: "whsec_x", clientId: null) });
        var http = new CapturingHandler();

        await Dispatcher(store, http, TokenIssuer()).DispatchAsync(TenantId, "approval.task.assigned", new { id = 1 });

        var (request, _) = http.Sent.Single();
        request.Headers.GetValues("X-Webhook-Signature").Single().Should().StartWith("v1=");
        (await VerifyAsync(request.Headers.Authorization!.Parameter!)).Subject.Should().Be("ep-1", "no owning client: the endpoint id");
    }

    [Fact]
    public async Task Without_a_token_issuer_nothing_is_sent_unauthenticated()
    {
        var store = new Mock<IWebhookSubscriptionStore>();
        store.Setup(s => s.GetSubscribedEndpointsAsync(TenantId, "approval.task.assigned", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Endpoint() });
        var http = new CapturingHandler();

        var result = await Dispatcher(store, http, issuer: null).DispatchAsync(TenantId, "approval.task.assigned", new { id = 1 });

        result.EndpointsFailed.Should().Be(1);
        http.Sent.Should().BeEmpty();
        store.Verify(s => s.RecordDeliveryAsync("ep-1", It.Is<WebhookDelivery>(d => d.Status == WebhookDeliveryStatus.Failed), It.IsAny<CancellationToken>()));
    }

    [Fact]
    public void A_symmetric_key_is_refused()
    {
        var hmac = new SigningCredentials(new SymmetricSecurityKey(new byte[32]), SecurityAlgorithms.HmacSha256);

        var act = () => IssuerWebhookTokenIssuer.Create(hmac, Issuer, Endpoint(), "d-1", "approval.task.assigned", "{}"u8, _clock.Now);

        act.Should().Throw<InvalidOperationException>().WithMessage("*asymmetric*");
    }
}

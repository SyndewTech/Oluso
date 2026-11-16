using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Protocols.Validation;
using Oluso.Core.Services;
using Oluso.Protocols.Mtls;
using Oluso.Protocols.Validation;
using Xunit;

namespace Oluso.Tests.Protocols.Security;

/// <summary>
/// A client's published keys (jwks_uri, OpenID Connect Dynamic Client Registration 1.0 section 2): fetched through the
/// server-side request forgery guard, cached, fetched again (at most once a minute) for a key the cache lacks, so a
/// client that rotated its keys is accepted; only public keys are taken.
/// </summary>
public class ClientJwksUriTests
{
    private const string JwksUri = "https://127.0.0.1/jwks.json";
    private const string ClientId = "jwks-uri-client";

    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Server : HttpMessageHandler
    {
        public Func<HttpResponseMessage> Respond { get; set; } = () => new HttpResponseMessage(HttpStatusCode.NotFound);
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(Respond());
        }
    }

    private readonly Clock _clock = new();
    private readonly Server _server = new();

    private ClientJwksUriKeys Keys(bool allowPrivateNetwork = true)
    {
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(ClientJwksUriKeys.HttpClientName)).Returns(() => new HttpClient(_server, disposeHandler: false));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [ClientJwksUriKeys.AllowPrivateNetworkSetting] = allowPrivateNetwork ? "true" : "false"
            })
            .Build();
        return new ClientJwksUriKeys(new MemoryCache(new MemoryCacheOptions()), factory.Object, configuration,
            NullLogger<ClientJwksUriKeys>.Instance, _clock);
    }

    private static string Jwk(ECDsa key, string kid)
    {
        var p = key.ExportParameters(false);
        return JsonSerializer.Serialize(new { kty = "EC", crv = "P-256", kid, x = Base64UrlEncoder.Encode(p.Q.X), y = Base64UrlEncoder.Encode(p.Q.Y) });
    }

    private static Func<HttpResponseMessage> KeySet(TimeSpan? maxAge, params string[] keys) => () =>
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"keys\":[" + string.Join(",", keys) + "]}", Encoding.UTF8, "application/json")
        };
        if (maxAge != null) response.Headers.CacheControl = new CacheControlHeaderValue { MaxAge = maxAge };
        return response;
    };

    [Fact]
    public async Task The_key_set_is_cached_for_its_max_age_within_the_bounds()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        _server.Respond = KeySet(TimeSpan.FromMinutes(30), Jwk(key, "k1"));
        var keys = Keys();

        (await keys.GetAsync(JwksUri, refresh: false)).Should().ContainSingle();
        _clock.Now += TimeSpan.FromMinutes(29);
        (await keys.GetAsync(JwksUri, refresh: false)).Should().ContainSingle();
        _server.Requests.Should().Be(1);

        _clock.Now += TimeSpan.FromMinutes(2);
        await keys.GetAsync(JwksUri, refresh: false);
        _server.Requests.Should().Be(2);
    }

    [Fact]
    public async Task A_refresh_fetches_again_at_most_once_a_minute()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        _server.Respond = KeySet(null, Jwk(key, "k1"));
        var keys = Keys();

        await keys.GetAsync(JwksUri, refresh: false);
        await keys.GetAsync(JwksUri, refresh: true);
        _server.Requests.Should().Be(1, "the set was fetched under a minute ago");

        _clock.Now += ClientJwksUriKeys.RefreshInterval;
        await keys.GetAsync(JwksUri, refresh: true);
        _server.Requests.Should().Be(2);
    }

    [Fact]
    public async Task Only_public_rsa_and_ec_keys_are_taken()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var p = key.ExportParameters(true);
        var withPrivate = JsonSerializer.Serialize(new
        {
            kty = "EC", crv = "P-256", kid = "private", x = Base64UrlEncoder.Encode(p.Q.X), y = Base64UrlEncoder.Encode(p.Q.Y), d = Base64UrlEncoder.Encode(p.D)
        });
        _server.Respond = KeySet(null, Jwk(key, "public"), withPrivate, "{\"kty\":\"oct\",\"k\":\"c2VjcmV0\"}");

        var keys = await Keys().GetAsync(JwksUri, refresh: false);

        keys.Should().ContainSingle().Which.Should().Contain("\"kid\":\"public\"");
    }

    [Fact]
    public async Task A_failed_fetch_keeps_the_last_good_set_until_it_expires_and_then_nothing()
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        _server.Respond = KeySet(TimeSpan.FromMinutes(10), Jwk(key, "k1"));
        var keys = Keys();
        await keys.GetAsync(JwksUri, refresh: false);

        _server.Respond = () => new HttpResponseMessage(HttpStatusCode.InternalServerError);
        _clock.Now += TimeSpan.FromMinutes(2);
        (await keys.GetAsync(JwksUri, refresh: true)).Should().ContainSingle();

        _clock.Now += TimeSpan.FromMinutes(10);
        (await keys.GetAsync(JwksUri, refresh: false)).Should().BeEmpty();
    }

    [Theory]
    [InlineData("http://keys.example.com/jwks.json")]
    [InlineData("https://127.0.0.1/jwks.json")]
    [InlineData("https://10.0.0.5/jwks.json")]
    public async Task A_url_the_request_forgery_guard_refuses_is_never_fetched(string uri)
    {
        (await Keys(allowPrivateNetwork: false).GetAsync(uri, refresh: false)).Should().BeEmpty();
        _server.Requests.Should().Be(0);
    }

    [Fact]
    public async Task A_client_that_rotated_its_published_key_is_accepted_after_one_fresh_fetch()
    {
        using var oldKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var newKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var published = new Mock<IClientJwksUriKeys>();
        published.Setup(p => p.GetAsync(JwksUri, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Jwk(oldKey, "old") });
        published.Setup(p => p.GetAsync(JwksUri, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Jwk(oldKey, "old"), Jwk(newKey, "new") });

        var result = await AuthenticateAsync(published.Object, Assertion(newKey, "new"));

        result.Error.Should().BeNull(result.ErrorDescription);
        result.Method.Should().Be(ClientAuthenticationMethod.PrivateKeyJwt);
        published.Verify(p => p.GetAsync(JwksUri, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_key_the_client_does_not_publish_is_refused_after_one_fresh_fetch()
    {
        using var publishedKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var otherKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var published = new Mock<IClientJwksUriKeys>();
        published.Setup(p => p.GetAsync(JwksUri, It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { Jwk(publishedKey, "k1") });

        // Same kid, wrong key: the signature does not verify, even after fetching again.
        var result = await AuthenticateAsync(published.Object, Assertion(otherKey, "k1"));

        result.Error.Should().Be(OidcConstants.Errors.InvalidClient);
        published.Verify(p => p.GetAsync(JwksUri, true, It.IsAny<CancellationToken>()), Times.Once);
    }

    private static Task<ClientAuthenticationResult> AuthenticateAsync(IClientJwksUriKeys published, string assertion)
    {
        var clients = new Mock<IClientStore>();
        clients.Setup(c => c.FindClientByIdAsync(ClientId, It.IsAny<CancellationToken>())).ReturnsAsync(new Client
        {
            ClientId = ClientId,
            Enabled = true,
            RequireClientSecret = true,
            TokenEndpointAuthMethod = "private_key_jwt",
            JwksUri = JwksUri,
        });
        var issuer = new Mock<IIssuerResolver>();
        issuer.Setup(i => i.GetIssuerAsync(It.IsAny<CancellationToken>())).ReturnsAsync("https://auth.example.com");
        var cache = new Mock<IDistributedCache>();
        cache.Setup(c => c.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((byte[]?)null);

        var authenticator = new ClientAuthenticator(
            clients.Object,
            issuer.Object,
            Options.Create(new OidcEndpointConfiguration { TokenEndpoint = "/connect/token" }),
            cache.Object,
            Mock.Of<IClientCertificateExtractor>(),
            new MtlsOptions { Enabled = false },
            new ClientSecretProtector(new EphemeralDataProtectionProvider(), NullLogger<ClientSecretProtector>.Instance),
            NullLogger<ClientAuthenticator>.Instance,
            published);

        var context = new DefaultHttpContext();
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("auth.example.com");
        context.Request.ContentType = "application/x-www-form-urlencoded";
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(
            "client_assertion_type=" + Uri.EscapeDataString("urn:ietf:params:oauth:client-assertion-type:jwt-bearer") +
            "&client_assertion=" + Uri.EscapeDataString(assertion)));
        return authenticator.AuthenticateAsync(context.Request, CancellationToken.None);
    }

    private static string Assertion(ECDsa key, string kid)
    {
        var now = DateTime.UtcNow;
        return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: ClientId,
            audience: "https://auth.example.com/connect/token",
            claims: new[] { new Claim("sub", ClientId), new Claim("jti", Guid.NewGuid().ToString("N")) },
            notBefore: now,
            expires: now.AddMinutes(2),
            signingCredentials: new SigningCredentials(new ECDsaSecurityKey(key) { KeyId = kid }, SecurityAlgorithms.EcdsaSha256)));
    }
}

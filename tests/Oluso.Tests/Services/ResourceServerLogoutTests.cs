using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Cryptography;
using System.Web;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Services;
using Xunit;

namespace Oluso.Tests.Services;

/// <summary>
/// Back-channel logout to resource servers that keep their own sessions (a Matrix homeserver): when sessions end,
/// the watcher hears once per session that held one of its scopes, with that session's sid; logout tokens are
/// typed <c>logout+jwt</c>.
/// </summary>
public class ResourceServerLogoutTests
{
    private const string Watcher = "chat-homeserver";
    private const string LogoutUri = "https://client.example/oauth/backchannel-logout";

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<string> LogoutTokens { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            LogoutTokens.Add(HttpUtility.ParseQueryString(body)["logout_token"]!);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private static (BackchannelLogoutService Service, CapturingHandler Http, Mock<ILogoutTokenGenerator> Tokens) Build(
        params ResourceServerLogoutWatcher[] watchers)
    {
        var clients = new Mock<IClientStore>();
        clients.Setup(c => c.FindClientByIdAsync(Watcher, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Client { ClientId = Watcher, BackChannelLogoutUri = LogoutUri, BackChannelLogoutSessionRequired = true });
        var tokens = new Mock<ILogoutTokenGenerator>();
        tokens.Setup(t => t.GenerateLogoutTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string client, string subject, string? sid, CancellationToken _) => $"{client}|{subject}|{sid}");
        var http = new CapturingHandler();
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(http, disposeHandler: false));
        var options = new ResourceServerLogoutOptions();
        options.Watchers.AddRange(watchers);
        var service = new BackchannelLogoutService(clients.Object, Mock.Of<IPersistedGrantStore>(), tokens.Object, factory.Object,
            NullLogger<BackchannelLogoutService>.Instance, Options.Create(options));
        return (service, http, tokens);
    }

    private static PersistedGrant Grant(string sid, params string[] scopes) => new()
    {
        Key = Guid.NewGuid().ToString("N"),
        Type = "refresh_token",
        SubjectId = "user-1",
        SessionId = sid,
        ClientId = "some-matrix-client",
        Data = System.Text.Json.JsonSerializer.Serialize(new { Scopes = scopes }),
    };

    private static readonly ResourceServerLogoutWatcher Homeserver = new()
    {
        ClientId = Watcher,
        Scopes = new[] { "urn:matrix:client:api:*", "urn:matrix:org.matrix.msc2967.client:api:*" },
    };

    [Fact]
    public async Task The_watcher_hears_once_per_ended_session_that_held_its_scopes()
    {
        var (service, http, _) = Build(Homeserver);

        await service.NotifyAsync(new[]
        {
            Grant("sid-a", "openid", "urn:matrix:client:api:*", "urn:matrix:client:device:A"),
            Grant("sid-b", "openid", "profile"),
            Grant("sid-c", "urn:matrix:org.matrix.msc2967.client:api:*"),
            Grant("sid-c", "urn:matrix:org.matrix.msc2967.client:api:*"),
        });

        http.LogoutTokens.Should().BeEquivalentTo($"{Watcher}|user-1|sid-a", $"{Watcher}|user-1|sid-c");
    }

    [Fact]
    public async Task Without_watchers_nothing_is_sent()
    {
        var (service, http, _) = Build();

        await service.NotifyAsync(new[] { Grant("sid-a", "urn:matrix:client:api:*") });

        http.LogoutTokens.Should().BeEmpty();
    }

    [Fact]
    public async Task Logout_tokens_are_typed_logout_jwt_and_carry_the_sid()
    {
        var key = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "k" };
        var signing = new Mock<ISigningCredentialStore>();
        signing.Setup(s => s.GetSigningCredentialsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
        var issuer = new Mock<IIssuerResolver>();
        issuer.Setup(i => i.GetIssuerAsync(It.IsAny<CancellationToken>())).ReturnsAsync("https://acme.example");
        var generator = new LogoutTokenGenerator(signing.Object, Mock.Of<ITenantSettingsProvider>(), issuer.Object,
            NullLogger<LogoutTokenGenerator>.Instance);

        var token = new JwtSecurityTokenHandler().ReadJwtToken(await generator.GenerateLogoutTokenAsync(Watcher, "user-1", "sid-a"));

        token.Header.Typ.Should().Be("logout+jwt");
        token.Audiences.Should().Equal(Watcher);
        token.Claims.Should().Contain(c => c.Type == "sid" && c.Value == "sid-a");
        token.Claims.Should().Contain(c => c.Type == "events");
    }
}

using System.Net;
using System.Web;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Core.Services;
using Oluso.Services;
using Xunit;

namespace Oluso.Tests.Services;

/// <summary>
/// Back-Channel Logout 1.0 targeting: when a sign-in session ends, the clients that took part in it are told, each
/// with that session's sid, and nobody else; ending every session tells each client once per session it holds; a
/// pairwise client hears the sub it was issued; a client requiring a sid gets nothing when there is none.
/// </summary>
public class BackchannelLogoutTargetingTests
{
    private const string UserId = "user-1";

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public List<(string Host, string Token)> Posts { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            lock (Posts) Posts.Add((request.RequestUri!.Host, HttpUtility.ParseQueryString(body)["logout_token"]!));
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private readonly Mock<IClientStore> _clients = new();
    private readonly Mock<IPersistedGrantStore> _grants = new();
    private readonly CapturingHandler _http = new();
    private readonly BackchannelLogoutService _service;

    public BackchannelLogoutTargetingTests()
    {
        // Public IP literals (TEST-NET-3), so the request forgery guard passes them without DNS.
        Client("rp-a", "https://203.0.113.1/bcl");
        Client("rp-b", "https://203.0.113.2/bcl");

        var tokens = new Mock<ILogoutTokenGenerator>();
        tokens.Setup(t => t.GenerateLogoutTokenAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string client, string subject, string? sid, CancellationToken _) => $"{client}|{subject}|{sid}");
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(_http, disposeHandler: false));

        _service = new BackchannelLogoutService(_clients.Object, _grants.Object, tokens.Object, factory.Object,
            new ConfigurationBuilder().Build(), NullLogger<BackchannelLogoutService>.Instance);
    }

    [Fact]
    public async Task Only_the_clients_of_the_ended_session_are_told_with_its_sid()
    {
        UserHas(Grant("rp-a", "sid-1", "session_client"), Grant("rp-b", "sid-1", "refresh_token"));

        await _service.SendLogoutNotificationsAsync(UserId, "sid-1");

        _http.Posts.Select(p => p.Token).Should().BeEquivalentTo($"rp-a|{UserId}|sid-1", $"rp-b|{UserId}|sid-1");
        // Only that session's grants were asked for: another sign-in's clients cannot be reached.
        _grants.Verify(g => g.GetAllAsync(It.Is<PersistedGrantFilter>(f => f.SessionId == "sid-1" && f.SubjectId == UserId), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Ending_every_session_tells_each_client_once_per_session_it_holds()
    {
        UserHas(Grant("rp-a", "sid-1", "session_client"), Grant("rp-a", "sid-2", "session_client"), Grant("rp-a", "sid-2", "refresh_token"));

        await _service.SendLogoutNotificationsAsync(UserId, sessionId: null);

        _http.Posts.Select(p => p.Token).Should().BeEquivalentTo($"rp-a|{UserId}|sid-1", $"rp-a|{UserId}|sid-2");
    }

    [Fact]
    public async Task A_pairwise_client_hears_the_sub_it_was_issued()
    {
        Client("rp-a", "https://203.0.113.1/bcl", pairwiseSalt: "salt-1");
        UserHas(Grant("rp-a", "sid-1", "session_client"));

        await _service.SendLogoutNotificationsAsync(UserId, "sid-1");

        _http.Posts.Should().ContainSingle().Which.Token.Should().Be($"rp-a|{SubjectIdentifiers.ForClient(UserId, "rp-a", "salt-1")}|sid-1");
    }

    [Fact]
    public async Task A_client_requiring_a_sid_gets_nothing_when_none_is_known()
    {
        Client("rp-a", "https://203.0.113.1/bcl", sessionRequired: true);
        UserHas(Grant("rp-a", sessionId: null, "refresh_token"));

        await _service.SendLogoutNotificationsAsync(UserId, sessionId: null);

        _http.Posts.Should().BeEmpty();
    }

    private void Client(string clientId, string logoutUri, string? pairwiseSalt = null, bool sessionRequired = false) =>
        _clients.Setup(c => c.FindClientByIdAsync(clientId, It.IsAny<CancellationToken>())).ReturnsAsync(new Client
        {
            ClientId = clientId,
            BackChannelLogoutUri = logoutUri,
            BackChannelLogoutSessionRequired = sessionRequired,
            PairWiseSubjectSalt = pairwiseSalt
        });

    private void UserHas(params PersistedGrant[] grants) =>
        _grants.Setup(g => g.GetAllAsync(It.IsAny<PersistedGrantFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PersistedGrantFilter f, CancellationToken _) =>
                grants.Where(g => g.SubjectId == f.SubjectId && (f.SessionId == null || g.SessionId == f.SessionId)).ToList());

    private static PersistedGrant Grant(string clientId, string? sessionId, string type) => new()
    {
        Key = Guid.NewGuid().ToString("N"),
        ClientId = clientId,
        SessionId = sessionId,
        SubjectId = UserId,
        Type = type,
        Data = "{}"
    };
}

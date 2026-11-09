using Microsoft.Extensions.DependencyInjection;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Oluso.Account.Events;
using Oluso.Account.PublicLinks;
using Oluso.Core.Authentication;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Notifications;
using Oluso.Core.Realtime;
using Oluso.Core.Services;
using Oluso.Protocols.Ciba;
using Xunit;

namespace Oluso.Tests.Protocols;

/// <summary>
/// A pending CIBA request reaches the user on every channel that applies: realtime to their
/// signed-in apps on the channel the mobile CIBA screen listens to, push when push is configured,
/// and otherwise an email whose approve/deny link expires with the request. One failing channel
/// never stops the others. The emailed link approves or denies only that pending request.
/// </summary>
public class CibaUserNotificationTests
{
    private readonly CibaRequest _request = new()
    {
        Id = 7,
        AuthReqId = "auth-req-secret",
        ClientId = "payments-app",
        SubjectId = "u1",
        TenantId = "t1",
        BindingMessage = "K7Q2",
        RequestedScopes = "openid",
        ExpiresAt = DateTime.UtcNow.AddMinutes(2)
    };

    private OlusoUserInfo _user = new() { Id = "u1", Username = "jo", Email = "jo@x.test", EmailVerified = true, TenantId = "t1" };

    private readonly Mock<IRealtimeBroadcaster> _realtime = new();
    private readonly Mock<IPushSender> _push = new();
    private readonly Mock<IEmailSender> _email = new();
    private readonly Mock<IActionTokenService> _tokens = new();
    private readonly List<(string To, string Subject, string Body)> _sent = new();
    private TimeSpan? _tokenLifetime;

    public CibaUserNotificationTests()
    {
        _email.Setup(e => e.SendAsync("t1", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((string _, string to, string subject, string body, CancellationToken _) => _sent.Add((to, subject, body)))
            .ReturnsAsync(EmailResult.Succeeded());
        _tokens.Setup(t => t.IssueAsync("t1", "u1", CibaNotifications.DecideAction, "7", It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Callback((string _, string _, string _, string _, TimeSpan ttl, CancellationToken _) => _tokenLifetime = ttl)
            .ReturnsAsync("raw-token");
    }

    private CibaUserNotificationService Notifier()
    {
        var users = new Mock<IOlusoUserService>();
        users.Setup(u => u.FindByIdAsync("u1", It.IsAny<CancellationToken>())).ReturnsAsync(() => _user);
        var clients = new Mock<IClientStore>();
        clients.Setup(c => c.FindClientByIdAsync("payments-app", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Client { ClientId = "payments-app", ClientName = "Payments" });
        var decideHandler = Mock.Of<IActionTokenHandler>(h => h.Action == CibaNotifications.DecideAction);

        var channels = new ICibaNotificationChannel[]
        {
            new CibaRealtimeNotificationChannel(_realtime.Object),
            new CibaPushNotificationChannel(_push.Object),
            new CibaEmailNotificationChannel(
                _tokens.Object,
                new ServiceCollection().AddSingleton(decideHandler).BuildServiceProvider(),
                Options.Create(new OlusoOptions { WorkspaceBaseUrl = "https://app.example.test/" }),
                NullLogger<CibaEmailNotificationChannel>.Instance,
                _email.Object)
        };

        return new CibaUserNotificationService(
            channels, users.Object, clients.Object, Mock.Of<ITenantContext>(), NullLogger<CibaUserNotificationService>.Instance);
    }

    private void VerifyRealtime(Times times) =>
        _realtime.Verify(r => r.BroadcastToUserAsync("t1", "u1", "ciba", "ciba.requested", It.IsAny<object?>(), It.IsAny<CancellationToken>()), times);

    private void VerifyPush(Times times) =>
        _push.Verify(p => p.SendAsync(
            "u1",
            It.Is<string>(title => title.Contains("Payments")),
            It.Is<string>(body => body.Contains("K7Q2")),
            CibaNotifications.RequestedEvent,
            It.Is<IReadOnlyDictionary<string, string>>(d => d["type"] == "ciba" && d["authReqId"] == "auth-req-secret"),
            It.IsAny<CancellationToken>()), times);

    [Fact]
    public async Task With_push_configured_the_user_gets_realtime_and_push_but_no_email()
    {
        _push.SetupGet(p => p.IsEnabled).Returns(true);

        await Notifier().NotifyUserAsync(_request);

        VerifyRealtime(Times.Once());
        VerifyPush(Times.Once());
        _sent.Should().BeEmpty();
    }

    [Fact]
    public async Task The_realtime_event_carries_what_the_mobile_screen_shows()
    {
        object? payload = null;
        _realtime.Setup(r => r.BroadcastToUserAsync("t1", "u1", "ciba", "ciba.requested", It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .Callback((string _, string _, string _, string _, object? p, CancellationToken _) => payload = p)
            .Returns(Task.CompletedTask);

        await Notifier().NotifyUserAsync(_request);

        payload.Should().BeEquivalentTo(new
        {
            authReqId = "auth-req-secret",
            clientId = "payments-app",
            clientName = "Payments",
            bindingMessage = "K7Q2",
            expiresAt = _request.ExpiresAt
        });
    }

    [Fact]
    public async Task Without_push_the_user_gets_realtime_and_an_email_with_an_absolute_link_that_expires_with_the_request()
    {
        _push.SetupGet(p => p.IsEnabled).Returns(false);

        await Notifier().NotifyUserAsync(_request);

        VerifyRealtime(Times.Once());
        _push.Verify(p => p.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<IReadOnlyDictionary<string, string>?>(), It.IsAny<CancellationToken>()), Times.Never);

        var mail = _sent.Should().ContainSingle().Subject;
        mail.To.Should().Be("jo@x.test");
        mail.Subject.Should().Contain("Payments");
        mail.Body.Should().Contain("https://app.example.test/m#tk=raw-token");
        mail.Body.Should().Contain("K7Q2");
        mail.Body.Should().NotContain("auth-req-secret");
        _tokenLifetime.Should().BeGreaterThan(TimeSpan.Zero).And.BeLessThanOrEqualTo(TimeSpan.FromMinutes(2));
    }

    [Fact]
    public async Task A_failing_channel_does_not_stop_the_others()
    {
        _realtime.Setup(r => r.BroadcastToUserAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<object?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("hub down"));

        await Notifier().NotifyUserAsync(_request);

        _sent.Should().ContainSingle();
    }

    [Fact]
    public async Task No_email_goes_to_an_unverified_address()
    {
        _user = new OlusoUserInfo { Id = "u1", Username = "jo", Email = "jo@x.test", EmailVerified = false, TenantId = "t1" };

        await Notifier().NotifyUserAsync(_request);

        VerifyRealtime(Times.Once());
        _sent.Should().BeEmpty();
        _tokens.Verify(t => t.IssueAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task An_already_expired_request_is_not_announced()
    {
        _request.ExpiresAt = DateTime.UtcNow.AddSeconds(-1);
        _push.SetupGet(p => p.IsEnabled).Returns(true);

        await Notifier().NotifyUserAsync(_request);

        VerifyRealtime(Times.Never());
        VerifyPush(Times.Never());
        _sent.Should().BeEmpty();
    }

    private (CibaDecideTokenHandler Handler, Mock<ICibaService> Ciba, Mock<IOlusoEventService> Events) DecideHandler(bool pending = true)
    {
        var store = new Mock<ICibaStore>();
        store.Setup(s => s.GetPendingBySubjectAsync("u1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(pending ? new[] { _request } : Array.Empty<CibaRequest>());
        var ciba = new Mock<ICibaService>();
        ciba.Setup(c => c.ApproveRequestAsync("auth-req-secret", "u1", null, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        ciba.Setup(c => c.DenyRequestAsync("auth-req-secret", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var clients = new Mock<IClientStore>();
        clients.Setup(c => c.FindClientByIdAsync("payments-app", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Client { ClientId = "payments-app", ClientName = "Payments" });
        var events = new Mock<IOlusoEventService>();

        return (new CibaDecideTokenHandler(clients.Object, events.Object, NullLogger<CibaDecideTokenHandler>.Instance, store.Object, ciba.Object),
            ciba, events);
    }

    private static readonly ActionTokenClaim Claim = new("t1", "u1", CibaNotifications.DecideAction, "7");

    [Fact]
    public async Task The_link_previews_the_requesting_app_and_binding_message()
    {
        var (handler, _, _) = DecideHandler();

        var preview = await handler.PreviewAsync(Claim);

        preview!.AlreadyActioned.Should().BeFalse();
        preview.Summary.Should().Contain("Payments");
        preview.Description.Should().Contain("K7Q2");
        preview.Options.Select(o => o.Key).Should().Equal("approve", "deny");
    }

    [Theory]
    [InlineData("approve")]
    [InlineData("deny")]
    public async Task The_link_approves_or_denies_that_request_for_its_user(string option)
    {
        var (handler, ciba, events) = DecideHandler();

        await handler.ExecuteAsync(Claim, option, comments: null);

        if (option == "approve")
        {
            ciba.Verify(c => c.ApproveRequestAsync("auth-req-secret", "u1", null, It.IsAny<CancellationToken>()), Times.Once);
            events.Verify(e => e.RaiseAsync(It.Is<CibaRequestApprovedEvent>(x => x.SubjectId == "u1"), It.IsAny<CancellationToken>()), Times.Once);
        }
        else
        {
            ciba.Verify(c => c.DenyRequestAsync("auth-req-secret", It.IsAny<CancellationToken>()), Times.Once);
            events.Verify(e => e.RaiseAsync(It.Is<CibaRequestDeniedEvent>(x => x.SubjectId == "u1"), It.IsAny<CancellationToken>()), Times.Once);
        }
    }

    [Fact]
    public async Task A_request_that_is_no_longer_pending_cannot_be_actioned()
    {
        var (handler, ciba, _) = DecideHandler(pending: false);

        (await handler.PreviewAsync(Claim))!.AlreadyActioned.Should().BeTrue();
        await handler.Invoking(h => h.ExecuteAsync(Claim, "approve", null)).Should().ThrowAsync<InvalidOperationException>();
        ciba.Verify(c => c.ApproveRequestAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Messaging;
using Xunit;

namespace Oluso.Tests.Messaging;

/// <summary>
/// Replies go where the tenant wants them: the message's own reply-to first,
/// then the sender profile's, then the tenant's confirmed reply-to inbox, then
/// the platform default for tenants sending from the platform address. Mail on
/// the platform address carries the tenant's name.
/// </summary>
public class TenantReplyToTests
{
    private const string Tenant = "tenant-1";

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }
    }

    private static (TenantAwareEmailSender Sender, CapturingHandler Http) Rig(
        SenderProfile? defaultProfile = null, TenantEmailSettings? tenantEmail = null, string? platformReplyTo = "hello@platform.example",
        string tenantName = "Acme Ltd", string? tenantDisplayName = null)
    {
        var http = new CapturingHandler();
        var config = new Mock<ITenantConfigurationService>();
        config.Setup(c => c.GetSectionAsync<TenantEmailSettings>(Tenant, TenantEmailSettings.SectionKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(tenantEmail);
        config.Setup(c => c.GetSectionAsync<SenderCredentials>(Tenant, It.Is<string>(k => k.StartsWith(SenderCredentials.SectionKeyPrefix)), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SenderCredentials { ApiKey = "tenant-key" });

        var profiles = new Mock<ISenderProfileStore>();
        profiles.Setup(p => p.GetDefaultAsync(Tenant, It.IsAny<CancellationToken>())).ReturnsAsync(defaultProfile);

        var tenants = new Mock<ITenantStore>();
        tenants.Setup(t => t.GetByIdAsync(Tenant, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant { Id = Tenant, Name = tenantName, DisplayName = tenantDisplayName, Identifier = "acme" });

        var options = Options.Create(new EmailOptions
        {
            Provider = "SendGrid", FromAddress = "noreply@platform.example", FromName = "Acme Suite",
            ReplyToAddress = platformReplyTo, SendGrid = { ApiKey = "platform-key" }
        });
        var sender = new TenantAwareEmailSender(new HttpClient(http), Mock.Of<ITenantContext>(), config.Object, profiles.Object, tenants.Object, options,
            NullLogger<TenantAwareEmailSender>.Instance);
        return (sender, http);
    }

    private static EmailMessage Message(string? replyTo = null) => new()
    {
        To = new[] { "customer@acme-mail.co" },
        Subject = "Quote 000001",
        HtmlBody = "<p>Quote</p>",
        ReplyTo = replyTo,
    };

    private static JsonElement Payload(CapturingHandler http) => JsonDocument.Parse(http.Body!).RootElement;

    private static string? ReplyToIn(CapturingHandler http)
        => Payload(http).TryGetProperty("reply_to", out var replyTo) && replyTo.ValueKind == JsonValueKind.Object
            ? replyTo.GetProperty("email").GetString()
            : null;

    private static string? FromNameIn(CapturingHandler http) => Payload(http).GetProperty("from").GetProperty("name").GetString();

    private static SenderProfile Profile(string? replyTo) => new()
    {
        TenantId = Tenant, Name = "Default", Provider = EmailProvider.SendGrid,
        FromAddress = "sales@tenant.example", FromName = "Acme Sales", ReplyToAddress = replyTo, IsDefault = true,
    };

    private static TenantEmailSettings Confirmed(string address) => new() { ReplyToAddress = address, ReplyToVerifiedAt = DateTime.UtcNow };

    [Fact]
    public async Task Platform_address_uses_the_tenants_confirmed_inbox()
    {
        var (sender, http) = Rig(tenantEmail: Confirmed("replies@tenant.example"));

        (await sender.SendAsync(Tenant, Message())).Success.Should().BeTrue();

        ReplyToIn(http).Should().Be("replies@tenant.example");
    }

    [Fact]
    public async Task An_unconfirmed_inbox_is_ignored_and_the_platform_default_applies()
    {
        var (sender, http) = Rig(tenantEmail: new TenantEmailSettings { ReplyToAddress = "typo@tenant.example", PendingReplyToAddress = "typo@tenant.example" });

        await sender.SendAsync(Tenant, Message());

        ReplyToIn(http).Should().Be("hello@platform.example");
    }

    [Fact]
    public async Task Platform_address_without_any_reply_to_configured_sends_none()
    {
        var (sender, http) = Rig(platformReplyTo: null);

        await sender.SendAsync(Tenant, Message());

        ReplyToIn(http).Should().BeNull();
    }

    [Fact]
    public async Task A_profile_reply_to_beats_the_tenant_inbox()
    {
        var (sender, http) = Rig(Profile("desk@tenant.example"), Confirmed("replies@tenant.example"));

        await sender.SendAsync(Tenant, Message());

        ReplyToIn(http).Should().Be("desk@tenant.example");
    }

    [Fact]
    public async Task A_profile_without_reply_to_uses_the_tenant_inbox_but_never_the_platform_default()
    {
        var withInbox = Rig(Profile(null), Confirmed("replies@tenant.example"));
        await withInbox.Sender.SendAsync(Tenant, Message());
        ReplyToIn(withInbox.Http).Should().Be("replies@tenant.example");

        var noInbox = Rig(Profile(null));
        await noInbox.Sender.SendAsync(Tenant, Message());
        ReplyToIn(noInbox.Http).Should().BeNull("replies to the tenant's own From address already reach the tenant");
    }

    [Fact]
    public async Task The_message_reply_to_wins_over_everything()
    {
        var (sender, http) = Rig(Profile("desk@tenant.example"), Confirmed("replies@tenant.example"));

        await sender.SendAsync(Tenant, Message("account.manager@tenant.example"));

        ReplyToIn(http).Should().Be("account.manager@tenant.example");
    }

    [Fact]
    public async Task Platform_address_mail_is_named_tenant_via_platform()
    {
        var plain = Rig();
        await plain.Sender.SendAsync(Tenant, Message());
        FromNameIn(plain.Http).Should().Be("Acme Ltd via Acme Suite");

        var displayName = Rig(tenantDisplayName: "Acme");
        await displayName.Sender.SendAsync(Tenant, Message());
        FromNameIn(displayName.Http).Should().Be("Acme via Acme Suite");
    }

    [Fact]
    public async Task A_tenant_profile_keeps_its_own_from_name()
    {
        var (sender, http) = Rig(Profile(null));

        await sender.SendAsync(Tenant, Message());

        FromNameIn(http).Should().Be("Acme Sales");
    }
}

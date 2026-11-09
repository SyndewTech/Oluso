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
/// A message's headers (List-Unsubscribe and List-Unsubscribe-Post on marketing mail) reach SendGrid and Brevo as
/// they are written, through the default sender or a chosen profile; Cloudflare, which can't carry them, refuses the
/// message, as does any sender that only implements the defaults.
/// </summary>
public class EmailHeadersTests
{
    private const string Tenant = "tenant-1";

    private static readonly Dictionary<string, string> Unsubscribe = new()
    {
        ["List-Unsubscribe"] = "<https://id.example/crm/unsubscribe/tok>",
        ["List-Unsubscribe-Post"] = "List-Unsubscribe=One-Click",
    };

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }

        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent("""{"messageId":"m-1"}""") };
        }
    }

    private static (TenantAwareEmailSender Sender, CapturingHandler Http) Rig(EmailProvider provider)
    {
        var http = new CapturingHandler();
        var profile = new SenderProfile
        {
            Id = "marketing", TenantId = Tenant, Name = "Marketing", Provider = provider, FromAddress = "news@tenant.example", FromName = "Acme", IsDefault = true,
        };
        var config = new Mock<ITenantConfigurationService>();
        config.Setup(c => c.GetSectionAsync<SenderCredentials>(Tenant, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SenderCredentials { ApiKey = "key", CloudflareAccountId = "acct" });
        var profiles = new Mock<ISenderProfileStore>();
        profiles.Setup(p => p.GetDefaultAsync(Tenant, It.IsAny<CancellationToken>())).ReturnsAsync(profile);
        profiles.Setup(p => p.GetAsync(Tenant, "marketing", It.IsAny<CancellationToken>())).ReturnsAsync(profile);
        var sender = new TenantAwareEmailSender(new HttpClient(http), Mock.Of<ITenantContext>(), config.Object, profiles.Object, Mock.Of<ITenantStore>(),
            Options.Create(new EmailOptions()), NullLogger<TenantAwareEmailSender>.Instance);
        return (sender, http);
    }

    private static EmailMessage Message() => new()
    {
        To = ["customer@acme-mail.co"], Subject = "Spring sale", HtmlBody = "<p>Sale</p>", TextBody = "Sale", Headers = Unsubscribe,
    };

    private static Dictionary<string, string> HeadersIn(CapturingHandler http) =>
        JsonDocument.Parse(http.Body!).RootElement.GetProperty("headers").Deserialize<Dictionary<string, string>>()!;

    [Fact]
    public async Task SendGrid_gets_the_headers_through_the_default_sender_and_a_profile()
    {
        var (sender, http) = Rig(EmailProvider.SendGrid);

        (await sender.SendAsync(Tenant, Message())).Success.Should().BeTrue();
        HeadersIn(http).Should().Equal(Unsubscribe);

        (await sender.SendThroughProfileAsync(Tenant, "marketing", Message())).Success.Should().BeTrue();
        HeadersIn(http).Should().Equal(Unsubscribe);
        JsonDocument.Parse(http.Body!).RootElement.GetProperty("content")[0].GetProperty("type").GetString().Should().Be("text/plain");
    }

    [Fact]
    public async Task Brevo_gets_the_headers()
    {
        var (sender, http) = Rig(EmailProvider.Brevo);

        (await sender.SendThroughProfileAsync(Tenant, "marketing", Message())).Success.Should().BeTrue();

        HeadersIn(http).Should().Equal(Unsubscribe);
    }

    [Fact]
    public async Task Cloudflare_refuses_a_message_with_headers_rather_than_dropping_them()
    {
        var (sender, http) = Rig(EmailProvider.Cloudflare);

        var result = await sender.SendThroughProfileAsync(Tenant, "marketing", Message());

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("List-Unsubscribe");
        http.Calls.Should().Be(0);
    }

    private sealed class MinimalSender : IProfileEmailSender
    {
        public Task<EmailResult> SendThroughProfileAsync(string tenantId, string profileId, string toEmail, string subject, string htmlMessage,
            CancellationToken cancellationToken = default) => Task.FromResult(EmailResult.Succeeded());
    }

    [Fact]
    public async Task Senders_with_only_the_defaults_refuse_headers()
    {
        IProfileEmailSender sender = new MinimalSender();

        await FluentActions.Awaiting(() => sender.SendThroughProfileAsync(Tenant, "p", Message())).Should().ThrowAsync<NotSupportedException>();
        var plain = new EmailMessage { To = ["customer@acme-mail.co"], Subject = "Hi", HtmlBody = "<p>Hi</p>" };
        (await sender.SendThroughProfileAsync(Tenant, "p", plain)).Success.Should().BeTrue();
    }
}

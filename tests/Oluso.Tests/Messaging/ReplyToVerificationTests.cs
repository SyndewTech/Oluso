using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Oluso.Core.Authentication;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Messaging;
using Xunit;

namespace Oluso.Tests.Messaging;

/// <summary>
/// A reply-to inbox is only used once its owner confirms a link mailed to it.
/// Until then the previous confirmed inbox stays in use, and a link for an
/// address that has since been replaced cannot confirm anything.
/// </summary>
public class ReplyToVerificationTests
{
    private const string Tenant = "tenant-1";

    private sealed class Rig
    {
        public TenantEmailSettings? Stored { get; set; }
        public List<EmailMessage> Sent { get; } = new();
        public bool SendFails { get; set; }
        public ReplyToVerificationService Service { get; }
        public ReplyToVerificationTokenHandler Handler { get; }

        public Rig(string? workspaceUrl = "https://workspace.example")
        {
            var config = new Mock<ITenantConfigurationService>();
            config.Setup(c => c.GetSectionAsync<TenantEmailSettings>(Tenant, TenantEmailSettings.SectionKey, It.IsAny<CancellationToken>()))
                .ReturnsAsync(() => Stored == null ? null : Copy(Stored));
            config.Setup(c => c.SetSectionAsync(Tenant, TenantEmailSettings.SectionKey, It.IsAny<TenantEmailSettings>(), It.IsAny<CancellationToken>()))
                .Callback<string, string, TenantEmailSettings, CancellationToken>((_, _, v, _) => Stored = Copy(v))
                .Returns(Task.CompletedTask);

            var tokens = new Mock<IActionTokenService>();
            tokens.Setup(t => t.IssueAsync(Tenant, It.IsAny<string>(), ReplyToVerificationTokenHandler.ActionKey, Tenant, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("raw-token");

            var email = new Mock<IEmailSender>();
            email.Setup(e => e.SendAsync(Tenant, It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
                .Callback<string, EmailMessage, CancellationToken>((_, m, _) => Sent.Add(m))
                .ReturnsAsync(() => SendFails ? EmailResult.Failed("smtp down") : EmailResult.Succeeded("m-1"));

            var tenants = new Mock<ITenantStore>();
            tenants.Setup(t => t.GetByIdAsync(Tenant, It.IsAny<CancellationToken>()))
                .ReturnsAsync(new Tenant { Id = Tenant, Name = "Acme Ltd", Identifier = "acme" });

            Service = new ReplyToVerificationService(config.Object, tokens.Object, email.Object, tenants.Object,
                Options.Create(new OlusoOptions { WorkspaceBaseUrl = workspaceUrl }), NullLogger<ReplyToVerificationService>.Instance);
            Handler = new ReplyToVerificationTokenHandler(config.Object, tenants.Object);
        }

        private static TenantEmailSettings Copy(TenantEmailSettings s) => new()
        {
            ReplyToAddress = s.ReplyToAddress, ReplyToVerifiedAt = s.ReplyToVerifiedAt,
            PendingReplyToAddress = s.PendingReplyToAddress, PendingReplyToSentAt = s.PendingReplyToSentAt,
        };
    }

    private static ActionTokenClaim ClaimFor(string address)
        => new(Tenant, ReplyToVerificationTokenHandler.RedeemerFor(address), ReplyToVerificationTokenHandler.ActionKey, Tenant);

    [Fact]
    public async Task Requesting_mails_a_confirmation_link_and_keeps_the_address_pending()
    {
        var rig = new Rig();

        var result = await rig.Service.RequestAsync(Tenant, "replies@acme.example");

        result.Sent.Should().BeTrue(result.Error);
        var mail = rig.Sent.Should().ContainSingle().Subject;
        mail.To.Should().Equal("replies@acme.example");
        mail.HtmlBody.Should().Contain("https://workspace.example/m#tk=raw-token");
        rig.Stored!.PendingReplyToAddress.Should().Be("replies@acme.example");
        rig.Stored.VerifiedReplyTo.Should().BeNull("nothing is used until the link is confirmed");
    }

    [Fact]
    public async Task Confirming_the_link_makes_the_address_the_reply_to()
    {
        var rig = new Rig();
        await rig.Service.RequestAsync(Tenant, "replies@acme.example");

        var preview = await rig.Handler.PreviewAsync(ClaimFor("replies@acme.example"));
        preview!.Options.Should().ContainSingle(o => o.Key == ReplyToVerificationTokenHandler.ConfirmOption);
        await rig.Handler.ExecuteAsync(ClaimFor("replies@acme.example"), ReplyToVerificationTokenHandler.ConfirmOption, null);

        rig.Stored!.VerifiedReplyTo.Should().Be("replies@acme.example");
        rig.Stored.PendingReplyToAddress.Should().BeNull();
        (await rig.Service.GetStatusAsync(Tenant)).ReplyToAddress.Should().Be("replies@acme.example");
    }

    [Fact]
    public async Task A_link_for_a_replaced_address_cannot_confirm()
    {
        var rig = new Rig();
        await rig.Service.RequestAsync(Tenant, "first@acme.example");
        await rig.Service.RequestAsync(Tenant, "second@acme.example");

        var preview = await rig.Handler.PreviewAsync(ClaimFor("first@acme.example"));
        preview!.AlreadyActioned.Should().BeTrue();
        var act = () => rig.Handler.ExecuteAsync(ClaimFor("first@acme.example"), ReplyToVerificationTokenHandler.ConfirmOption, null);

        await act.Should().ThrowAsync<InvalidOperationException>();
        rig.Stored!.VerifiedReplyTo.Should().BeNull();
    }

    [Fact]
    public async Task The_confirmed_inbox_stays_in_use_while_a_new_one_is_pending()
    {
        var rig = new Rig { Stored = new TenantEmailSettings { ReplyToAddress = "old@acme.example", ReplyToVerifiedAt = DateTime.UtcNow } };

        await rig.Service.RequestAsync(Tenant, "new@acme.example");

        var status = await rig.Service.GetStatusAsync(Tenant);
        status.ReplyToAddress.Should().Be("old@acme.example");
        status.PendingAddress.Should().Be("new@acme.example");
    }

    [Fact]
    public async Task A_failed_send_leaves_nothing_pending()
    {
        var rig = new Rig { SendFails = true };

        var result = await rig.Service.RequestAsync(Tenant, "replies@acme.example");

        result.Sent.Should().BeFalse();
        result.Error.Should().NotBeNullOrEmpty();
        rig.Stored.Should().BeNull();
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("a@example.org, b@example.org")]
    [InlineData("Acme <a@example.org>")]
    public async Task Only_a_single_plain_address_is_accepted(string address)
    {
        var rig = new Rig();

        var result = await rig.Service.RequestAsync(Tenant, address);

        result.Error.Should().NotBeNullOrEmpty();
        rig.Sent.Should().BeEmpty();
    }

    [Fact]
    public async Task Resending_straight_away_is_throttled()
    {
        var rig = new Rig();
        await rig.Service.RequestAsync(Tenant, "replies@acme.example");

        var again = await rig.Service.ResendAsync(Tenant);

        again.Sent.Should().BeFalse();
        again.Error.Should().Contain("less than a minute");
        rig.Sent.Should().ContainSingle();
    }

    [Fact]
    public async Task Removing_clears_the_confirmed_and_pending_addresses()
    {
        var rig = new Rig { Stored = new TenantEmailSettings { ReplyToAddress = "old@acme.example", ReplyToVerifiedAt = DateTime.UtcNow, PendingReplyToAddress = "new@acme.example" } };

        var status = await rig.Service.RemoveAsync(Tenant);

        status.ReplyToAddress.Should().BeNull();
        status.PendingAddress.Should().BeNull();
    }
}

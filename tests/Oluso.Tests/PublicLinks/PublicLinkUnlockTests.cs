using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Oluso.Controllers;
using Oluso.Core.Authentication;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Dto.PublicLinks;
using Oluso.PublicLinks;
using Xunit;

namespace Oluso.Tests.PublicLinks;

/// <summary>
/// A locked link shows nothing but its title until the visitor confirms the
/// address it was sent to, and with the code option a one-time code mailed
/// there. The proof works for that link only, and repeated failures lock it.
/// </summary>
public class PublicLinkUnlockTests
{
    private const string Raw = "raw-token-abcdefghijklmnop";
    private const string Tenant = "tenant-1";
    private const string Recipient = "bola@example.org";

    private readonly List<EmailMessage> _sent = new();

    private PublicLinkUnlockService Service()
    {
        var email = new Mock<IEmailSender>();
        email.Setup(e => e.SendAsync(Tenant, It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .Callback<string, EmailMessage, CancellationToken>((_, m, _) => _sent.Add(m))
            .ReturnsAsync(EmailResult.Succeeded("m"));
        return new PublicLinkUnlockService(new EphemeralDataProtectionProvider(), Cache, email.Object, NullLogger<PublicLinkUnlockService>.Instance);
    }

    private readonly IDistributedCache Cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

    private static ActionTokenAccessRequirement Requirement(ActionTokenLockMode mode, string? email = Recipient) =>
        new() { Mode = mode, ExpectedEmail = email, Title = "Quote from Acme Ltd" };

    private string CodeFromMail() => System.Text.RegularExpressions.Regex.Match(_sent.Last().TextBody!, @"\b(\d{6})\b").Groups[1].Value;

    [Fact]
    public async Task The_right_email_unlocks_and_the_proof_is_bound_to_that_link()
    {
        var service = Service();

        var result = await service.UnlockAsync(Raw, Tenant, Requirement(ActionTokenLockMode.Email), "  BOLA@example.org ", null);

        result.Outcome.Should().Be(UnlockOutcome.Unlocked);
        service.IsUnlocked(Raw, result.Proof).Should().BeTrue();
        service.IsUnlocked("another-link", result.Proof).Should().BeFalse();
        service.IsUnlocked(Raw, "forged").Should().BeFalse();
    }

    [Fact]
    public async Task A_wrong_email_is_refused_and_repeated_failures_lock_the_link()
    {
        var service = Service();
        var requirement = Requirement(ActionTokenLockMode.Email);

        for (var i = 0; i < PublicLinkUnlockService.MaxFailures; i++)
            (await service.UnlockAsync(Raw, Tenant, requirement, "someone@else.org", null)).Outcome.Should().Be(UnlockOutcome.Mismatch);

        (await service.UnlockAsync(Raw, Tenant, requirement, Recipient, null)).Outcome.Should().Be(UnlockOutcome.LockedOut, "even the right address waits out the lock");
    }

    [Fact]
    public async Task The_code_option_mails_a_code_that_then_unlocks()
    {
        var service = Service();
        var requirement = Requirement(ActionTokenLockMode.EmailCode);

        (await service.UnlockAsync(Raw, Tenant, requirement, Recipient, null)).Outcome.Should().Be(UnlockOutcome.CodeSent);
        _sent.Should().ContainSingle().Which.To.Should().Equal(Recipient);

        (await service.UnlockAsync(Raw, Tenant, requirement, Recipient, "000000" == CodeFromMail() ? "111111" : "000000")).Outcome.Should().Be(UnlockOutcome.WrongCode);
        var result = await service.UnlockAsync(Raw, Tenant, requirement, Recipient, CodeFromMail());

        result.Outcome.Should().Be(UnlockOutcome.Unlocked);
        service.IsUnlocked(Raw, result.Proof).Should().BeTrue();
    }

    [Fact]
    public async Task A_wrong_address_in_code_mode_looks_the_same_but_sends_nothing()
    {
        var service = Service();

        var result = await service.UnlockAsync(Raw, Tenant, Requirement(ActionTokenLockMode.EmailCode), "guess@else.org", null);

        result.Outcome.Should().Be(UnlockOutcome.CodeSent, "the form must not reveal which address a link went to");
        _sent.Should().BeEmpty();
    }

    [Fact]
    public async Task A_code_cannot_be_requested_again_straight_away()
    {
        var service = Service();
        var requirement = Requirement(ActionTokenLockMode.EmailCode);
        await service.UnlockAsync(Raw, Tenant, requirement, Recipient, null);

        (await service.UnlockAsync(Raw, Tenant, requirement, Recipient, null)).Outcome.Should().Be(UnlockOutcome.CodeCooldown);
        _sent.Should().ContainSingle();
    }

    [Fact]
    public async Task A_customer_without_an_email_cannot_be_unlocked()
    {
        (await Service().UnlockAsync(Raw, Tenant, Requirement(ActionTokenLockMode.Email, email: null), Recipient, null))
            .Outcome.Should().Be(UnlockOutcome.NoEmailOnFile);
    }

    private sealed class GuardedHandler : IActionTokenHandler, IActionTokenFileProvider, IActionTokenAccessGuard
    {
        public ActionTokenAccessRequirement? Requirement { get; init; }
        public int Previews { get; private set; }
        public string Action => "test.document";
        public bool SingleUse => false;

        public Task<ActionTokenAccessRequirement?> GetAccessRequirementAsync(ActionTokenClaim claim, CancellationToken cancellationToken = default)
            => Task.FromResult(Requirement);

        public Task<ActionTokenPreview?> PreviewAsync(ActionTokenClaim claim, CancellationToken cancellationToken = default)
        {
            Previews++;
            return Task.FromResult<ActionTokenPreview?>(new ActionTokenPreview { Title = "Quote 000001", Data = new() { ["document"] = "secret" } });
        }

        public Task<ActionTokenExecuteResult> ExecuteAsync(ActionTokenClaim claim, string option, string? comments, CancellationToken cancellationToken = default)
            => Task.FromResult(new ActionTokenExecuteResult { Message = "done" });

        public Task<ActionTokenFile?> GetFileAsync(ActionTokenClaim claim, CancellationToken cancellationToken = default)
            => Task.FromResult<ActionTokenFile?>(new ActionTokenFile(new byte[] { 1 }, "application/pdf", "q.pdf"));
    }

    private (PublicLinksController Controller, GuardedHandler Handler, PublicLinkUnlockService Unlock) ControllerRig(ActionTokenLockMode? mode)
    {
        var claim = new ActionTokenClaim("", "customer:c1", "test.document", "q-1");
        var tokens = new Mock<IActionTokenService>();
        tokens.Setup(t => t.PeekAsync(Raw, It.IsAny<CancellationToken>())).ReturnsAsync(claim);
        var handler = new GuardedHandler { Requirement = mode == null ? null : Requirement(mode.Value) };
        var unlock = Service();
        var controller = new PublicLinksController(tokens.Object, new IActionTokenHandler[] { handler }, Mock.Of<ITenantStore>(),
            new Oluso.Core.Services.TenantContextAccessor(), NullLogger<PublicLinksController>.Instance, unlock)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        return (controller, handler, unlock);
    }

    [Fact]
    public async Task A_locked_link_previews_only_its_title_and_refuses_the_file_and_actions()
    {
        var (controller, handler, _) = ControllerRig(ActionTokenLockMode.Email);

        var preview = (await controller.Preview(new PreviewActionTokenRequest { Token = Raw })).Result
            .Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<ActionTokenPreview>().Subject;
        var file = await controller.File(new FileActionTokenRequest { Token = Raw });
        var redeem = await controller.Redeem(new RedeemActionTokenRequest { Token = Raw, Option = "accept" });

        preview.Locked.Should().BeTrue();
        preview.LockMode.Should().Be("email");
        preview.Title.Should().Be("Quote from Acme Ltd");
        preview.Data.Should().BeNull("no document content leaks to a locked page");
        handler.Previews.Should().Be(0);
        file.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        redeem.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task After_unlocking_the_proof_opens_the_preview_and_file()
    {
        var (controller, handler, _) = ControllerRig(ActionTokenLockMode.Email);

        var unlocked = (await controller.Unlock(new UnlockActionTokenRequest { Token = Raw, Email = Recipient })).Result
            .Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<UnlockActionTokenResponse>().Subject;
        var preview = (await controller.Preview(new PreviewActionTokenRequest { Token = Raw, Unlock = unlocked.Unlock })).Result
            .Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<ActionTokenPreview>().Subject;
        var file = await controller.File(new FileActionTokenRequest { Token = Raw, Unlock = unlocked.Unlock });

        preview.Locked.Should().BeFalse();
        handler.Previews.Should().Be(1);
        file.Should().BeOfType<FileContentResult>();
    }

    [Fact]
    public async Task A_wrong_email_gets_a_clear_refusal()
    {
        var (controller, _, _) = ControllerRig(ActionTokenLockMode.Email);

        var result = await controller.Unlock(new UnlockActionTokenRequest { Token = Raw, Email = "someone@else.org" });

        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task An_unguarded_link_opens_as_before()
    {
        var (controller, handler, _) = ControllerRig(null);

        var preview = (await controller.Preview(new PreviewActionTokenRequest { Token = Raw })).Result
            .Should().BeOfType<OkObjectResult>().Which.Value.Should().BeOfType<ActionTokenPreview>().Subject;

        preview.Locked.Should().BeFalse();
        handler.Previews.Should().Be(1);
    }
}

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Controllers;
using Oluso.Core.Authentication;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Dto.PublicLinks;
using Xunit;

namespace Oluso.Tests.PublicLinks;

/// <summary>
/// A single-use link is consumed when it is acted on, and given back when the
/// action fails, so an error the recipient did not cause never loses them the
/// link. A reusable link is never consumed.
/// </summary>
public class PublicLinksRedeemTests
{
    private sealed class Handler : IActionTokenHandler
    {
        public bool SingleUseValue { get; init; } = true;
        public Exception? Throw { get; init; }
        public int Executions { get; private set; }

        public string Action => "test.decide";
        public bool SingleUse => SingleUseValue;

        public Task<ActionTokenPreview?> PreviewAsync(ActionTokenClaim claim, CancellationToken cancellationToken = default)
            => Task.FromResult<ActionTokenPreview?>(new ActionTokenPreview { Title = "t" });

        public Task<ActionTokenExecuteResult> ExecuteAsync(ActionTokenClaim claim, string option, string? comments, CancellationToken cancellationToken = default)
        {
            Executions++;
            if (Throw != null) throw Throw;
            return Task.FromResult(new ActionTokenExecuteResult { Message = "done" });
        }
    }

    private static (PublicLinksController Controller, Mock<IActionTokenService> Tokens) Rig(Handler handler, bool alreadyConsumed = false)
    {
        var claim = new ActionTokenClaim("", "user-1", "test.decide", "subject-1");
        var tokens = new Mock<IActionTokenService>();
        tokens.Setup(t => t.PeekAsync("raw", It.IsAny<CancellationToken>())).ReturnsAsync(claim);
        tokens.Setup(t => t.RedeemAsync("raw", It.IsAny<CancellationToken>())).ReturnsAsync(alreadyConsumed ? null : claim);

        var controller = new PublicLinksController(tokens.Object, new IActionTokenHandler[] { handler }, Mock.Of<ITenantStore>(),
            new TenantContextAccessor(), NullLogger<PublicLinksController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        return (controller, tokens);
    }

    private static RedeemActionTokenRequest Request => new() { Token = "raw", Option = "approve" };

    [Fact]
    public async Task A_single_use_link_is_consumed_and_kept_on_success()
    {
        var handler = new Handler();
        var (controller, tokens) = Rig(handler);

        var result = await controller.Redeem(Request);

        result.Result.Should().BeOfType<OkObjectResult>();
        tokens.Verify(t => t.RedeemAsync("raw", It.IsAny<CancellationToken>()), Times.Once);
        tokens.Verify(t => t.ReleaseAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_single_use_link_is_given_back_when_the_action_is_refused()
    {
        var (controller, tokens) = Rig(new Handler { Throw = new InvalidOperationException("Already decided") });

        var result = await controller.Redeem(Request);

        result.Result.Should().BeOfType<ConflictObjectResult>();
        tokens.Verify(t => t.ReleaseAsync("raw", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_single_use_link_is_given_back_when_the_action_crashes()
    {
        var (controller, tokens) = Rig(new Handler { Throw = new TimeoutException("database") });

        var act = () => controller.Redeem(Request);

        await act.Should().ThrowAsync<TimeoutException>();
        tokens.Verify(t => t.ReleaseAsync("raw", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task A_used_single_use_link_does_not_act_again()
    {
        var handler = new Handler();
        var (controller, _) = Rig(handler, alreadyConsumed: true);

        var result = await controller.Redeem(Request);

        result.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status410Gone);
        handler.Executions.Should().Be(0);
    }

    [Fact]
    public async Task A_reusable_link_is_never_consumed()
    {
        var handler = new Handler { SingleUseValue = false };
        var (controller, tokens) = Rig(handler);

        var result = await controller.Redeem(Request);

        result.Result.Should().BeOfType<OkObjectResult>();
        handler.Executions.Should().Be(1);
        tokens.Verify(t => t.RedeemAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

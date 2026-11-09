using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Controllers;
using Oluso.Core.Authentication;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Dto.PublicLinks;
using Xunit;

namespace Oluso.Tests.PublicLinks;

/// <summary>
/// A public link runs in the tenant it was issued for. A request that arrives
/// in another tenant's context, the platform default on the API host or a
/// signed-in session's header, must not hide the document behind the wrong
/// tenant's data filter.
/// </summary>
public class PublicLinksTenantTests
{
    private const string IssuingTenant = "tenant-b";
    private const string RequestTenant = "_platform";

    private sealed class RecordingHandler : IActionTokenHandler, IActionTokenFileProvider
    {
        private readonly ITenantContext _tenant;
        public RecordingHandler(ITenantContext tenant) => _tenant = tenant;

        public string Action => "test.document";
        public string? TenantSeenOnPreview { get; private set; }
        public string? TenantSeenOnExecute { get; private set; }
        public string? TenantSeenOnFile { get; private set; }

        public Task<ActionTokenPreview?> PreviewAsync(ActionTokenClaim claim, CancellationToken cancellationToken = default)
        {
            TenantSeenOnPreview = _tenant.TenantId;
            return Task.FromResult<ActionTokenPreview?>(new ActionTokenPreview { Title = "Quote 000001" });
        }

        public Task<ActionTokenExecuteResult> ExecuteAsync(ActionTokenClaim claim, string option, string? comments, CancellationToken cancellationToken = default)
        {
            TenantSeenOnExecute = _tenant.TenantId;
            return Task.FromResult(new ActionTokenExecuteResult());
        }

        public Task<ActionTokenFile?> GetFileAsync(ActionTokenClaim claim, CancellationToken cancellationToken = default)
        {
            TenantSeenOnFile = _tenant.TenantId;
            return Task.FromResult<ActionTokenFile?>(new ActionTokenFile(new byte[] { 1 }, "application/pdf", "q.pdf"));
        }
    }

    private static (PublicLinksController Controller, RecordingHandler Handler) Rig(string? tokenTenant, bool issuingTenantExists = true)
    {
        var context = new TenantContextAccessor();
        context.SetTenant(new Tenant { Id = RequestTenant, Name = "Platform", Identifier = RequestTenant });

        var claim = new ActionTokenClaim(tokenTenant ?? "", "customer:c1", "test.document", "quote-1");
        var tokens = new Mock<IActionTokenService>();
        tokens.Setup(t => t.PeekAsync("raw", It.IsAny<CancellationToken>())).ReturnsAsync(claim);
        tokens.Setup(t => t.RedeemAsync("raw", It.IsAny<CancellationToken>())).ReturnsAsync(claim);

        var tenants = new Mock<ITenantStore>();
        tenants.Setup(s => s.GetByIdAsync(IssuingTenant, It.IsAny<CancellationToken>()))
            .ReturnsAsync(issuingTenantExists ? new Tenant { Id = IssuingTenant, Name = "B", Identifier = "b" } : null);

        var handler = new RecordingHandler(context);
        var controller = new PublicLinksController(tokens.Object, new IActionTokenHandler[] { handler }, tenants.Object, context,
            NullLogger<PublicLinksController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        return (controller, handler);
    }

    [Fact]
    public async Task Preview_runs_in_the_issuing_tenant_not_the_request_tenant()
    {
        var (controller, handler) = Rig(IssuingTenant);

        var result = await controller.Preview(new PreviewActionTokenRequest { Token = "raw" });

        result.Result.Should().BeOfType<OkObjectResult>();
        handler.TenantSeenOnPreview.Should().Be(IssuingTenant);
    }

    [Fact]
    public async Task Redeem_and_file_run_in_the_issuing_tenant()
    {
        var (controller, handler) = Rig(IssuingTenant);

        await controller.Redeem(new RedeemActionTokenRequest { Token = "raw", Option = "accept" });
        await controller.File(new FileActionTokenRequest { Token = "raw" });

        handler.TenantSeenOnExecute.Should().Be(IssuingTenant);
        handler.TenantSeenOnFile.Should().Be(IssuingTenant);
    }

    [Fact]
    public async Task A_link_whose_tenant_is_gone_is_refused_before_the_handler()
    {
        var (controller, handler) = Rig(IssuingTenant, issuingTenantExists: false);

        var result = await controller.Preview(new PreviewActionTokenRequest { Token = "raw" });

        result.Result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status410Gone);
        handler.TenantSeenOnPreview.Should().BeNull();
    }

    [Fact]
    public async Task A_link_without_a_tenant_keeps_the_request_context()
    {
        var (controller, handler) = Rig(tokenTenant: null);

        await controller.Preview(new PreviewActionTokenRequest { Token = "raw" });

        handler.TenantSeenOnPreview.Should().Be(RequestTenant);
    }
}

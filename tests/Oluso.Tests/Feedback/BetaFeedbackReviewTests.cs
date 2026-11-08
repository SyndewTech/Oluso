using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Oluso.Admin.Controllers;
using Oluso.Admin.Dto.Feedback;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Storage;
using Xunit;

namespace Oluso.Tests.Feedback;

/// <summary>
/// End users file beta feedback from their own tenant; the platform reviews it from any tenant, and screenshots are
/// read from storage rather than through a link that expires or that the browser can't follow.
/// </summary>
public class BetaFeedbackReviewTests
{
    private readonly Mock<IFeedbackStore> _store = new();
    private readonly Mock<IFileUploader> _files = new();

    private FeedbackController Controller()
    {
        var context = new Mock<ITenantContext>();
        context.Setup(c => c.TenantId).Returns("_platform");
        var tenants = new Mock<ITenantStore>();
        tenants.Setup(t => t.GetByIdAsync("tenant-b", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Tenant { Id = "tenant-b", Name = "Hygeia Clinic", Identifier = "hygeia" });
        return new FeedbackController(_store.Object, _files.Object, Mock.Of<IOlusoEventService>(), tenants.Object, context.Object);
    }

    [Fact]
    public async Task The_platform_lists_feedback_from_every_tenant_with_the_tenants_name()
    {
        _store.Setup(s => s.ListAsync(null, 1, 25, null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((new[] { Submission("fb-1", "tenant-b", null) }, 1));

        var page = (await Controller().List()).Result.Should().BeOfType<OkObjectResult>()
            .Which.Value.Should().BeOfType<Oluso.Core.Api.PagedResult<FeedbackResponse>>().Subject;

        page.Items.Should().ContainSingle().Which.TenantName.Should().Be("Hygeia Clinic");
    }

    [Theory]
    [InlineData("feedback/tenant-b/u-1/shot.png")]
    [InlineData("https://acct.blob.core.windows.net/files/feedback/tenant-b/u-1/shot.png?sv=2024&sig=abc%2B")]
    [InlineData("/api/workspace/files/feedback/tenant-b/u-1/shot.png")]
    public async Task A_screenshot_is_served_from_storage_whatever_was_saved(string saved)
    {
        _store.Setup(s => s.GetByIdAsync(null, "fb-1", It.IsAny<CancellationToken>())).ReturnsAsync(Submission("fb-1", "tenant-b", saved));
        _files.Setup(f => f.DownloadAsync("feedback/tenant-b/u-1/shot.png", It.IsAny<CancellationToken>())).ReturnsAsync([1, 2, 3]);

        (await Controller().GetScreenshot("fb-1", CancellationToken.None)).Should().BeOfType<FileContentResult>();
    }

    [Theory]
    [InlineData("feedback/tenant-a/u-1/shot.png")]
    [InlineData("feedback/tenant-b/../tenant-a/shot.png")]
    public async Task A_screenshot_outside_the_submitters_feedback_folder_is_not_served(string saved)
    {
        _store.Setup(s => s.GetByIdAsync(null, "fb-1", It.IsAny<CancellationToken>())).ReturnsAsync(Submission("fb-1", "tenant-b", saved));

        (await Controller().GetScreenshot("fb-1", CancellationToken.None)).Should().BeOfType<NotFoundResult>();
        _files.Verify(f => f.DownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void A_submitted_screenshot_path_must_be_in_the_submitters_feedback_folder()
    {
        FeedbackScreenshots.IsInTenantFolder("tenant-b", "feedback/tenant-b/u-1/shot.png").Should().BeTrue();
        FeedbackScreenshots.IsInTenantFolder("tenant-b", "feedback/tenant-a/u-1/shot.png").Should().BeFalse();
        FeedbackScreenshots.IsInTenantFolder("tenant-b", "feedback/tenant-b/../tenant-a/shot.png").Should().BeFalse();
        FeedbackScreenshots.IsInTenantFolder("tenant-b", "uploads/tenant-b/shot.png").Should().BeFalse();
    }

    private static FeedbackSubmission Submission(string id, string tenantId, string? screenshot) => new()
    {
        Id = id, TenantId = tenantId, Title = "Broken", Description = "It broke", Category = "bug",
        SubmittedByUserId = "u-1", Portal = "workspace", ScreenshotPath = screenshot,
    };
}

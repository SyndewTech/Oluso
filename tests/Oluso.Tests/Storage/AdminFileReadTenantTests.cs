using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Moq;
using Oluso.Admin.Controllers;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Storage;
using Xunit;

namespace Oluso.Tests.Storage;

/// <summary>
/// The admin file read serves a tenant only the files it uploaded ({folder}/{tenantId}/...), never another tenant's.
/// </summary>
public class AdminFileReadTenantTests
{
    private readonly Mock<IFileUploader> _files = new();

    private FilesController Controller(string tenantId)
    {
        _files.Setup(f => f.DownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync([1, 2, 3]);
        var context = new Mock<ITenantContext>();
        context.Setup(c => c.TenantId).Returns(tenantId);
        context.Setup(c => c.HasTenant).Returns(true);
        return new FilesController(_files.Object, Options.Create(new PublicAssetOptions()), Mock.Of<IPublicAssetPathRegistry>(),
            Mock.Of<IPublicImageVariantGenerator>(), Mock.Of<IFileAttachmentAccessService>(), context.Object);
    }

    [Theory]
    [InlineData("uploads/tenant-a/f.png")]
    [InlineData("branding/legal-entities/tenant-a/f.png")]
    [InlineData("ecommerce/products/tenant-a/p-1/f.png")]
    public async Task A_tenant_reads_its_own_uploads(string path)
    {
        (await Controller("tenant-a").GetFile(path, CancellationToken.None)).Should().BeOfType<FileContentResult>();
    }

    [Theory]
    [InlineData("uploads/tenant-b/f.png")]
    [InlineData("uploads/tenant-a/../tenant-b/f.png")]
    [InlineData("uploads/tenant-a")]
    public async Task Another_tenants_file_or_a_path_stepping_out_is_not_found(string path)
    {
        (await Controller("tenant-a").GetFile(path, CancellationToken.None)).Should().BeOfType<NotFoundResult>();
        _files.Verify(f => f.DownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

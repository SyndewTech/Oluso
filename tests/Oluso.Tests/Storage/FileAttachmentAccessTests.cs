using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using Oluso.Controllers;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Storage;
using Oluso.Storage;
using Xunit;

namespace Oluso.Tests.Storage;

public class FileAttachmentAccessTests
{
    private const string Tenant = "tenant-1";
    private const string User = "user-1";

    private sealed class TestProvider : IFileAccessProvider
    {
        public required string EntityType { get; init; }
        public string? Root { get; init; }
        public bool AllowRead { get; init; }
        public bool AllowDelete { get; init; }
        public FileAttachmentView? SeenRead { get; private set; }
        public FileAttachmentView? SeenDelete { get; private set; }

        string? IFileAccessProvider.StorageRoot => Root;

        public Task<bool> CanReadAsync(string tenantId, string userId, string entityId, string? subEntityType, string? subEntityId,
            CancellationToken cancellationToken = default) => Task.FromResult(AllowRead);

        public Task<bool> CanDeleteAsync(string tenantId, string userId, FileAttachment file, CancellationToken cancellationToken = default)
            => Task.FromResult(false);

        public Task<bool> CanReadFileAsync(string tenantId, string userId, FileAttachmentView file, CancellationToken cancellationToken = default)
        {
            SeenRead = file;
            return Task.FromResult(AllowRead);
        }

        public Task<bool> CanDeleteFileAsync(string tenantId, string userId, FileAttachment file, FileAttachmentView view,
            CancellationToken cancellationToken = default)
        {
            SeenDelete = view;
            return Task.FromResult(AllowDelete);
        }
    }

    private sealed class DefaultProvider : IFileAccessProvider
    {
        public string EntityType => "Plain";
        public string? ReadEntityId { get; private set; }

        public Task<bool> CanReadAsync(string tenantId, string userId, string entityId, string? subEntityType, string? subEntityId,
            CancellationToken cancellationToken = default)
        {
            ReadEntityId = entityId;
            return Task.FromResult(true);
        }

        public Task<bool> CanDeleteAsync(string tenantId, string userId, FileAttachment file, CancellationToken cancellationToken = default)
            => Task.FromResult(false);
    }

    private static Mock<IFileAttachmentService> Attachments(string decryptedEntityId = "entity-1")
    {
        var attachments = new Mock<IFileAttachmentService>();
        attachments.Setup(a => a.ResolveAsync(It.IsAny<FileAttachment>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((FileAttachment f, CancellationToken _) => new FileAttachmentView(
                f.Id, f.TenantId, f.UploadedByUserId, null, f.StoragePath, f.StorageProvider, f.FileName, f.ContentType, f.SizeBytes,
                null, f.Folder, f.EntityType, decryptedEntityId, f.SubEntityType, f.SubEntityId, f.IsProtected, f.ProtectionPurpose, f.CreatedAt));
        return attachments;
    }

    private static FileAttachment Row(string? entityType, string? tenantId = Tenant, string uploadedBy = User) => new()
    {
        TenantId = tenantId,
        UploadedByUserId = uploadedBy,
        StoragePath = "secure/tenant-1/ab/file.pdf",
        FileName = "file.pdf",
        ContentType = "application/pdf",
        EntityType = entityType,
    };

    [Fact]
    public async Task Reads_are_decided_by_the_provider_with_the_decrypted_file()
    {
        var allow = new TestProvider { EntityType = "Secure", AllowRead = true };
        var deny = new TestProvider { EntityType = "Locked" };
        var service = new FileAttachmentAccessService(Attachments().Object, [allow, deny]);

        var row = Row("Secure");
        var view = await service.AuthorizeReadAsync(Tenant, User, row);
        view.Should().NotBeNull();
        allow.SeenRead!.Id.Should().Be(row.Id);
        allow.SeenRead.EntityId.Should().Be("entity-1");

        (await service.AuthorizeReadAsync(Tenant, User, Row("Locked"))).Should().BeNull();
    }

    [Fact]
    public async Task Personal_unregistered_deleted_and_other_tenant_files_are_refused()
    {
        var provider = new TestProvider { EntityType = "Secure", AllowRead = true, AllowDelete = true };
        var service = new FileAttachmentAccessService(Attachments().Object, [provider]);
        var deleted = Row("Secure");
        deleted.IsDeleted = true;

        (await service.AuthorizeReadAsync(Tenant, User, Row(null))).Should().BeNull();
        (await service.AuthorizeReadAsync(Tenant, User, Row("Unknown"))).Should().BeNull();
        (await service.AuthorizeReadAsync(Tenant, User, deleted)).Should().BeNull();
        (await service.AuthorizeReadAsync(Tenant, User, Row("Secure", tenantId: "tenant-2"))).Should().BeNull();
        (await service.CanDeleteAsync(Tenant, User, Row("Secure", tenantId: "tenant-2"))).Should().BeFalse();
        provider.SeenRead.Should().BeNull();
    }

    [Fact]
    public async Task A_file_whose_entity_id_cannot_be_recovered_is_refused()
    {
        var provider = new TestProvider { EntityType = "Secure", AllowRead = true };
        var service = new FileAttachmentAccessService(Attachments(decryptedEntityId: null!).Object, [provider]);

        (await service.AuthorizeReadAsync(Tenant, User, Row("Secure"))).Should().BeNull();
    }

    [Fact]
    public async Task Providers_without_a_file_level_check_fall_back_to_the_entity_check()
    {
        var provider = new DefaultProvider();
        var service = new FileAttachmentAccessService(Attachments().Object, [provider]);

        (await service.AuthorizeReadAsync(Tenant, User, Row("Plain"))).Should().NotBeNull();
        provider.ReadEntityId.Should().Be("entity-1");
    }

    [Fact]
    public async Task Deletes_are_decided_by_the_provider_with_the_decrypted_file()
    {
        var provider = new TestProvider { EntityType = "Secure", AllowDelete = true };
        var service = new FileAttachmentAccessService(Attachments().Object, [provider]);

        (await service.CanDeleteAsync(Tenant, User, Row("Secure"))).Should().BeTrue();
        provider.SeenDelete!.EntityId.Should().Be("entity-1");
    }

    [Theory]
    [InlineData("attachments/tenant-1/Invoice/a.pdf", true)]
    [InlineData("/Attachments/tenant-1/personal/a.pdf", true)]
    [InlineData("secure/tenant-1/ab/a.pdf", true)]
    [InlineData("uploads/tenant-1/user-1/a.pdf", false)]
    [InlineData("secured/tenant-1/a.pdf", false)]
    public void Attachment_storage_roots_are_recognised(string path, bool expected)
    {
        var service = new FileAttachmentAccessService(Attachments().Object,
            [new TestProvider { EntityType = "Secure", Root = "secure" }, new DefaultProvider()]);

        service.IsAttachmentStoragePath(path).Should().Be(expected);
    }

    [Fact]
    public async Task The_raw_storage_path_endpoint_does_not_serve_attachment_files()
    {
        var uploader = new Mock<IFileUploader>();
        uploader.Setup(u => u.DownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync([1, 2, 3]);
        var access = new FileAttachmentAccessService(Attachments().Object, [new TestProvider { EntityType = "Secure", Root = "secure" }]);
        var controller = new WorkspaceFilesController(uploader.Object, access, NoTenantContext()) { ControllerContext = SignedIn() };

        (await controller.GetFile("secure/tenant-1/ab/file.pdf", CancellationToken.None)).Should().BeOfType<NotFoundResult>();
        (await controller.GetFile("attachments/tenant-1/Invoice/file.pdf", CancellationToken.None)).Should().BeOfType<NotFoundResult>();
        (await controller.GetFile("uploads/tenant-1/file.pdf", CancellationToken.None)).Should().BeOfType<FileContentResult>();
        uploader.Verify(u => u.DownloadAsync(It.Is<string>(p => !p.StartsWith("uploads")), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task My_files_only_serves_the_uploaders_personal_files()
    {
        var entityFile = Row("Secure");
        var personal = Row(null);
        var store = new Mock<IFileAttachmentStore>();
        store.Setup(s => s.GetByIdAsync(entityFile.Id, It.IsAny<CancellationToken>())).ReturnsAsync(entityFile);
        store.Setup(s => s.GetByIdAsync(personal.Id, It.IsAny<CancellationToken>())).ReturnsAsync(personal);
        var attachments = Attachments();
        attachments.Setup(a => a.DownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) => new FileDownloadResult
            {
                Content = new MemoryStream([1]),
                Metadata = id == personal.Id ? personal : entityFile,
            });
        var controller = new WorkspaceMyFilesController(NoTenantContext(), attachments.Object, store.Object, Mock.Of<IFileUploader>())
        {
            ControllerContext = SignedIn(),
        };

        (await controller.Download(entityFile.Id)).Should().BeOfType<NotFoundResult>();
        (await controller.GetFileUrl(entityFile.Id)).Result.Should().BeOfType<NotFoundResult>();
        (await controller.Delete(entityFile.Id)).Should().BeOfType<NotFoundResult>();
        (await controller.Download(personal.Id)).Should().BeOfType<FileStreamResult>();
        attachments.Verify(a => a.DeleteAsync(entityFile.Id, It.IsAny<CancellationToken>()), Times.Never);
    }

    private static ITenantContext NoTenantContext()
    {
        var context = new Mock<ITenantContext>();
        context.SetupGet(c => c.HasTenant).Returns(false);
        return context.Object;
    }

    private static ControllerContext SignedIn() => new()
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", User), new Claim("tenant_id", Tenant)], "test")),
        },
    };
}

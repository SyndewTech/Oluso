using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Storage;
using Oluso.EntityFramework;
using Oluso.EntityFramework.Stores;
using Oluso.Storage;
using Oluso.Tests.Roles;
using Xunit;

namespace Oluso.Tests.Storage;

/// <summary>
/// Purging an entity's attachments removes them for good in its tenant, soft-deleted ones too, content before row, so
/// a file whose content stays keeps its row for a later purge; owners are listed a page at a time for sweeps.
/// </summary>
public sealed class FileAttachmentPurgeTests : IDisposable
{
    private readonly SqliteConnection _connection = IdentityTestFactory.OpenSqliteConnection();
    private readonly OlusoDbContext _db;
    private readonly Mock<IFileUploader> _uploader = new();
    private readonly HashSet<string> _blobs = new(StringComparer.Ordinal);

    public FileAttachmentPurgeTests()
    {
        _db = IdentityTestFactory.CreateSqliteContext(_connection, new TestTenantContext());
        _uploader.Setup(u => u.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, CancellationToken _) => _blobs.Remove(path));
        _uploader.Setup(u => u.ExistsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string path, CancellationToken _) => _blobs.Contains(path));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private FileAttachmentService Service() =>
        new(_uploader.Object, new FileAttachmentStore(_db), [], Mock.Of<IEntityReferenceProtector>(), NullLogger<FileAttachmentService>.Instance);

    private async Task Add(string id, string tenantId, string entityType, string entityId, bool deleted = false)
    {
        var path = $"attachments/{tenantId}/{id}";
        _blobs.Add(path);
        _db.FileAttachments.Add(new FileAttachment
        {
            Id = id, TenantId = tenantId, UploadedByUserId = "u", FileName = $"{id}.pdf", StoragePath = path,
            EntityType = entityType, EntityId = entityId, IsDeleted = deleted,
        });
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    private Task<List<string>> RowsAsync() => _db.FileAttachments.IgnoreQueryFilters().AsNoTracking().OrderBy(f => f.Id).Select(f => f.Id).ToListAsync();

    [Fact]
    public async Task Purging_removes_the_entitys_files_in_its_tenant_soft_deleted_ones_too()
    {
        await Add("a1", "t1", "CrmRecord", "contact:c1");
        await Add("a2", "t1", "CrmRecord", "contact:c1", deleted: true);
        await Add("b1", "t2", "CrmRecord", "contact:c1");
        await Add("c1", "t1", "WorkOrder", "contact:c1");

        (await Service().PurgeEntityAsync("t1", "CrmRecord", "contact:c1")).Should().BeTrue();

        (await RowsAsync()).Should().Equal("b1", "c1");
        _blobs.Should().BeEquivalentTo(["attachments/t2/b1", "attachments/t1/c1"]);
    }

    [Fact]
    public async Task A_file_whose_content_stays_keeps_its_row_for_a_later_purge()
    {
        await Add("a1", "t1", "CrmRecord", "contact:c1");
        await Add("a2", "t1", "CrmRecord", "contact:c1");
        // One store reports a failed delete as not found; the other throws.
        _uploader.Setup(u => u.DeleteAsync("attachments/t1/a1", It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _uploader.Setup(u => u.DeleteAsync("attachments/t1/a2", It.IsAny<CancellationToken>())).ThrowsAsync(new IOException("Storage is unavailable"));

        (await Service().PurgeEntityAsync("t1", "CrmRecord", "contact:c1")).Should().BeFalse();
        (await RowsAsync()).Should().Equal("a1", "a2");

        _uploader.Setup(u => u.DeleteAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((string path, CancellationToken _) => _blobs.Remove(path));
        (await Service().PurgeEntityAsync("t1", "CrmRecord", "contact:c1")).Should().BeTrue();
        (await RowsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task A_file_already_gone_from_storage_loses_its_row()
    {
        await Add("a1", "t1", "CrmRecord", "contact:c1");
        _blobs.Clear();

        (await Service().PurgeEntityAsync("t1", "CrmRecord", "contact:c1")).Should().BeTrue();
        (await RowsAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task Owners_are_listed_once_each_a_page_at_a_time()
    {
        await Add("a1", "t2", "CrmRecord", "contact:c1");
        await Add("a2", "t1", "CrmRecord", "deal:d1");
        await Add("a3", "t1", "CrmRecord", "contact:c1");
        await Add("a4", "t1", "CrmRecord", "contact:c1", deleted: true);
        await Add("a5", "t1", "WorkOrder", "w1");

        var service = Service();
        var first = await service.ListOwnersAsync("CrmRecord", null, 2);
        first.Should().Equal(new FileAttachmentOwner("t1", "contact:c1"), new FileAttachmentOwner("t1", "deal:d1"));
        var second = await service.ListOwnersAsync("CrmRecord", first[^1], 2);
        second.Should().Equal(new FileAttachmentOwner("t2", "contact:c1"));
        (await service.ListOwnersAsync("CrmRecord", second[^1], 2)).Should().BeEmpty();
    }
}

using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using Oluso.Controllers;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Core.Storage;
using Oluso.Storage;
using Xunit;

namespace Oluso.Enterprise.Verification.Tests;

/// <summary>Registering the module without its database fails at startup, and never breaks other modules' files.</summary>
public class RegistrationTests
{
    private const string Tenant = "t1";

    private sealed class OtherModuleFiles : IFileAccessProvider
    {
        public const string Type = "other_module_record";

        public string EntityType => Type;

        public Task<bool> CanReadAsync(string tenantId, string userId, string entityId, string? subEntityType, string? subEntityId, CancellationToken cancellationToken = default) =>
            Task.FromResult(userId == "user-1");

        public Task<bool> CanDeleteAsync(string tenantId, string userId, FileAttachment file, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }

    private static void AddModuleWithoutDatabase(IServiceCollection services) =>
        services.AddOluso(new ConfigurationBuilder().Build()).AddUserJourneyEngine().AddOlusoVerification();

    [Fact]
    public async Task A_host_without_the_module_database_fails_to_start_saying_what_to_add()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        AddModuleWithoutDatabase(builder.Services);
        using var host = builder.Build();

        var start = () => host.StartAsync();

        (await start.Should().ThrowAsync<OptionsValidationException>())
            .Which.Message.Should().Contain(nameof(VerificationExtensions.AddOlusoVerificationForProvider));
    }

    [Fact]
    public async Task A_host_with_the_module_database_passes_the_startup_check()
    {
        var services = new ServiceCollection();
        AddModuleWithoutDatabase(services);
        services.AddOlusoVerificationForProvider("sqlite", "Data Source=:memory:");
        await using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IStartupValidator>().Invoking(v => v.Validate()).Should().NotThrow();
    }

    [Fact]
    public async Task Files_of_other_modules_are_served_without_touching_the_module_store()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        AddModuleWithoutDatabase(services);
        var uploader = new InMemoryFileUploader();
        var attachments = new InMemoryFileAttachmentStore();
        services.AddSingleton(uploader.Mock.Object);
        services.AddSingleton<IFileAttachmentStore>(attachments);
        services.AddSingleton(Mock.Of<IEntityReferenceProtector>());
        services.AddSingleton(Mock.Of<IProfileService>());
        services.AddScoped<IFileAttachmentService, FileAttachmentService>();
        services.AddScoped<IFileAttachmentAccessService, FileAttachmentAccessService>();
        services.AddScoped<IFileAccessProvider, OtherModuleFiles>();
        await using var provider = services.BuildServiceProvider();

        uploader.Files["attachments/t1/other/f1.pdf"] = Encoding.ASCII.GetBytes("%PDF-1.7");
        await attachments.CreateAsync(new FileAttachment
        {
            Id = "f1",
            TenantId = Tenant,
            StoragePath = "attachments/t1/other/f1.pdf",
            StorageProvider = FileStorageProvider.Local,
            FileName = "f1.pdf",
            ContentType = "application/pdf",
            EntityType = OtherModuleFiles.Type,
            EntityId = "record-1",
        });

        using var scope = provider.CreateScope();
        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.TenantId).Returns(Tenant);
        tenant.SetupGet(t => t.HasTenant).Returns(true);
        var controller = ActivatorUtilities.CreateInstance<WorkspaceFileAttachmentsController>(scope.ServiceProvider, tenant.Object);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", "user-1"), new Claim("tenant_id", Tenant) }, "test")),
            },
        };

        var result = await controller.Get("f1", default);

        result.Should().BeOfType<FileContentResult>();
        scope.ServiceProvider.GetServices<IFileAccessProvider>().Select(p => p.EntityType)
            .Should().Contain(VerificationFileEntityTypes.BusinessApplicant, "the module still owns its own entity type");
    }
}

using System.Reflection;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Oluso.Core.Authorization;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Core.Storage;
using Oluso.Enterprise.Verification.Controllers;
using Oluso.Enterprise.Verification.Dto;
using Oluso.Enterprise.Verification.Services;
using Xunit;

namespace Oluso.Enterprise.Verification.Tests;

/// <summary>Who can open an applicant's documents, through which door.</summary>
public class DocumentAccessTests : IDisposable
{
    private const string Tenant = VerificationHarness.Tenant;
    private readonly VerificationHarness _h = new();

    public DocumentAccessTests()
    {
        _h.Provider.Answer = _ => ScriptedBusinessProvider.Found("ACME FOODS LIMITED");
        _h.Profiles.Setup(p => p.GetUserPermissionsAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());
        _h.Profiles.Setup(p => p.GetUserPermissionsAsync("admin-1", Tenant, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { VerificationPermissions.Read });
    }

    public void Dispose() => _h.Dispose();

    private async Task<BusinessApplicant> ApplicantWithLetterAsync(string subject, string journey)
    {
        using var scope = _h.Scope();
        var letter = new AcceptedJourneyFile(Upload.File("documents", "authority.pdf", Upload.Pdf), FileContentSniffer.Types[FileContentSniffer.Pdf]);
        var attempt = await _h.Service(scope).SubmitAsync(VerificationHarness.Request(subject: subject, journey: journey,
            evidence: ApplicantEvidenceRequirement.Documents, documents: new[] { letter }));
        return attempt.Applicant!;
    }

    private static ITenantContext TenantContext()
    {
        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.TenantId).Returns(Tenant);
        tenant.SetupGet(t => t.HasTenant).Returns(true);
        return tenant.Object;
    }

    private static ControllerContext As(string userId) => new()
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", userId), new Claim("tenant_id", Tenant) }, "test")),
        },
    };

    private AccountBusinessesController Account(IServiceScope scope, string userId) => new(
        TenantContext(),
        _h.Store(scope),
        scope.ServiceProvider.GetRequiredService<IFileAttachmentService>(),
        _h.Attachments,
        _h.Uploader.Mock.Object) { ControllerContext = As(userId) };

    [Fact]
    public async Task People_linked_to_the_business_and_administrators_can_read_documents_through_the_attachment_endpoint()
    {
        var owner = await ApplicantWithLetterAsync("user-1", "j1");
        await ApplicantWithLetterAsync("user-2", "j2");

        using var scope = _h.Scope();
        var provider = new BusinessApplicantFileAccessProvider(scope.ServiceProvider);

        provider.EntityType.Should().Be(VerificationFileEntityTypes.BusinessApplicant);
        (await provider.CanReadAsync(Tenant, "user-1", owner.Id, null, null)).Should().BeTrue();
        (await provider.CanReadAsync(Tenant, "user-2", owner.Id, null, null)).Should().BeTrue("they act for the same business");
        (await provider.CanReadAsync(Tenant, "admin-1", owner.Id, null, null)).Should().BeTrue();
        (await provider.CanReadAsync(Tenant, "stranger", owner.Id, null, null)).Should().BeFalse();
        (await provider.CanDeleteAsync(Tenant, "user-1", _h.Attachments.Rows.Values.First())).Should().BeFalse();
    }

    [Fact]
    public async Task The_account_api_shows_a_user_only_their_businesses_and_their_documents()
    {
        var owner = await ApplicantWithLetterAsync("user-1", "j1");
        var documentId = owner.DocumentIds.Single();

        using var scope = _h.Scope();
        var mine = (OkObjectResult)(await Account(scope, "user-1").List(default)).Result!;
        var list = ((IEnumerable<MyBusinessDto>)mine.Value!).ToList();
        list.Should().ContainSingle().Which.Documents.Should().ContainSingle().Which.FileName.Should().Be("authority.pdf");

        var file = await Account(scope, "user-1").Document(owner.BusinessId, documentId, default);
        var content = file.Should().BeOfType<FileContentResult>().Subject;
        content.ContentType.Should().Be("application/pdf");
        content.FileContents.Should().Equal(Upload.Pdf);

        var strangerList = (OkObjectResult)(await Account(scope, "stranger").List(default)).Result!;
        ((IEnumerable<MyBusinessDto>)strangerList.Value!).Should().BeEmpty();
        (await Account(scope, "stranger").Document(owner.BusinessId, documentId, default)).Should().BeOfType<NotFoundResult>();
        (await Account(scope, "stranger").Get(owner.BusinessId, default)).Result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task An_administrator_downloads_only_documents_that_belong_to_the_applicant()
    {
        var owner = await ApplicantWithLetterAsync("user-1", "j1");
        var other = await ApplicantWithLetterAsync("user-2", "j2");

        using var scope = _h.Scope();
        var issuers = new Mock<IIssuerResolver>();
        var controller = new BusinessVerificationAdminController(
            TenantContext(),
            scope.ServiceProvider.GetRequiredService<IVerificationProviderRegistry>(),
            _h.Settings,
            _h.Store(scope),
            _h.Service(scope),
            scope.ServiceProvider.GetRequiredService<IFileAttachmentService>(),
            _h.Attachments,
            _h.Uploader.Mock.Object,
            _h.Users.Object,
            Mock.Of<ITenantStore>(),
            issuers.Object) { ControllerContext = As("admin-1") };

        (await controller.DownloadDocument(owner.Id, owner.DocumentIds.Single(), default)).Should().BeOfType<FileContentResult>();
        (await controller.DownloadDocument(owner.Id, other.DocumentIds.Single(), default)).Should().BeOfType<NotFoundResult>();

        var detail = (OkObjectResult)(await controller.GetBusiness(owner.BusinessId, default)).Result!;
        var dto = (VerifiedBusinessDetailDto)detail.Value!;
        dto.Applicants.Should().HaveCount(2);
        dto.Applicants.SelectMany(a => a.Documents).Should().HaveCount(2);
        dto.Checks.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(nameof(BusinessVerificationAdminController.GetProviders), VerificationPermissions.Manage)]
    [InlineData(nameof(BusinessVerificationAdminController.UpdateSettings), VerificationPermissions.Manage)]
    [InlineData(nameof(BusinessVerificationAdminController.ListBusinesses), VerificationPermissions.Read)]
    [InlineData(nameof(BusinessVerificationAdminController.GetBusiness), VerificationPermissions.Read)]
    [InlineData(nameof(BusinessVerificationAdminController.DownloadDocument), VerificationPermissions.Read)]
    [InlineData(nameof(BusinessVerificationAdminController.ReviewQueue), VerificationPermissions.Read)]
    [InlineData(nameof(BusinessVerificationAdminController.ReviewBusiness), VerificationPermissions.Review)]
    [InlineData(nameof(BusinessVerificationAdminController.ReviewApplicant), VerificationPermissions.Review)]
    [InlineData(nameof(BusinessVerificationAdminController.RefreshApplicant), VerificationPermissions.Review)]
    public void Admin_actions_require_their_permission(string action, string permission)
    {
        var method = typeof(BusinessVerificationAdminController).GetMethod(action)!;
        method.GetCustomAttribute<RequirePermissionAttribute>()!.Policy.Should().EndWith(permission);
    }

    [Fact]
    public void Webhooks_are_anonymous_and_account_endpoints_need_a_signed_in_user()
    {
        typeof(VerificationWebhooksController).GetCustomAttribute<Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute>().Should().NotBeNull();
        typeof(AccountBusinessesController).BaseType.Should().Be(typeof(Oluso.Core.Api.AccountBaseController));
    }
}

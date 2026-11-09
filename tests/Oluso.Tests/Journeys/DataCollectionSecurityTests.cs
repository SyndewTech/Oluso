using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Admin.Authorization;
using Oluso.Admin.Controllers;
using Oluso.Admin.Dto.Submissions;
using Oluso.Core.Api;
using Oluso.Core.Authorization;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.UserJourneys;
using Oluso.Core.Services;
using Oluso.Core.Storage;
using Oluso.EntityFramework.Events;
using Oluso.Storage;
using Xunit;

namespace Oluso.Tests.Journeys;

/// <summary>Data collection answers are read only by those granted to, and never copied into the audit trail.</summary>
public class DataCollectionSecurityTests
{
    private const string Tenant = "tenant-1";

    [Fact]
    public void Every_endpoint_needs_submissions_read_and_changes_need_write_or_delete()
    {
        typeof(SubmissionsController).GetCustomAttribute<RequirePermissionAttribute>()!.Permission
            .Should().Be(AdminPermissions.SubmissionsRead);

        string? Extra(string action) => typeof(SubmissionsController).GetMethod(action)!
            .GetCustomAttribute<RequirePermissionAttribute>()?.Permission;

        Extra(nameof(SubmissionsController.UpdateSubmission)).Should().Be(AdminPermissions.SubmissionsWrite);
        Extra(nameof(SubmissionsController.BulkUpdateSubmissions)).Should().Be(AdminPermissions.SubmissionsWrite);
        Extra(nameof(SubmissionsController.DeleteSubmission)).Should().Be(AdminPermissions.SubmissionsDelete);
        Extra(nameof(SubmissionsController.BulkDeleteSubmissions)).Should().Be(AdminPermissions.SubmissionsDelete);
    }

    [Fact]
    public async Task The_audit_row_records_the_submission_but_not_its_answers()
    {
        AuditLog? written = null;
        var store = new Mock<IAuditLogStore>();
        store.Setup(s => s.WriteAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Callback<AuditLog, CancellationToken>((log, _) => written = log)
            .Returns(Task.CompletedTask);

        await new AuditEventSink(store.Object, NullLogger<AuditEventSink>.Instance).HandleAsync(new JourneySubmissionCreatedEvent
        {
            TenantId = Tenant,
            PolicyId = "intake",
            PolicyName = "Patient intake",
            SubmissionId = "sub-1",
            FieldNames = ["full_name", "diagnosis"],
            Fields = new Dictionary<string, object> { ["full_name"] = "Ada Obi", ["diagnosis"] = "Type 2 diabetes" },
            FieldCount = 2,
        });

        written!.Action.Should().Be("journey.submission_created");
        written.ResourceType.Should().Be("JourneySubmission");
        written.ResourceId.Should().Be("sub-1");
        written.ResourceName.Should().Be("Patient intake");
        written.Details.Should().Contain("full_name").And.Contain("intake");
        written.Details.Should().NotContain("Ada Obi").And.NotContain("Type 2 diabetes");
    }

    [Fact]
    public async Task Filters_search_every_submission_and_the_page_is_taken_from_the_matches()
    {
        var submissions = new InMemoryJourneySubmissionStore();
        for (var i = 0; i < 30; i++)
        {
            await submissions.SaveAsync(new JourneySubmission
            {
                PolicyId = "waitlist",
                TenantId = Tenant,
                Status = i % 3 == 0 ? SubmissionStatus.Approved : SubmissionStatus.New,
                Data = new Dictionary<string, object> { ["email"] = $"person{i}@example.test" },
                CreatedAt = DateTime.UtcNow.AddMinutes(-i),
            });
        }
        var controller = Controller(submissions);

        var approved = Page(await controller.GetSubmissions("waitlist", skip: 5, take: 5, status: "Approved"));
        approved.TotalCount.Should().Be(10);
        approved.Items.Should().HaveCount(5).And.OnlyContain(s => s.Status == SubmissionStatus.Approved);

        var found = Page(await controller.GetSubmissions("waitlist", skip: 0, take: 5, search: "person27@"));
        found.TotalCount.Should().Be(1, "the match is on a later page of the unfiltered list");

        var unfiltered = Page(await controller.GetSubmissions("waitlist", skip: 25, take: 10));
        unfiltered.TotalCount.Should().Be(30);
        unfiltered.Items.Should().HaveCount(5);
    }

    [Fact]
    public async Task A_module_journeys_answers_never_show_even_those_kept_before_the_module_owned_it()
    {
        var submissions = new InMemoryJourneySubmissionStore();
        var earlier = new JourneySubmission
        {
            PolicyId = "intake", TenantId = Tenant, JourneyId = "j1",
            Data = new Dictionary<string, object> { ["diagnosis"] = "Type 2 diabetes" },
        };
        await submissions.SaveAsync(earlier);
        var controller = Controller(submissions, ownerModule: "emr");

        var list = Page(await controller.GetSubmissions("intake"));
        list.TotalCount.Should().Be(1);
        list.Items.Single().Data.Should().BeEmpty();
        list.Items.Single().CreatedAt.Should().Be(earlier.CreatedAt);

        Page(await controller.GetSubmissions("intake", search: "diabetes")).TotalCount.Should().Be(0, "search never looks inside the answers");

        var one = (SubmissionDto)((OkObjectResult)(await controller.GetSubmission(earlier.Id)).Result!).Value!;
        one.Data.Should().BeEmpty();

        var csv = (FileContentResult)await controller.ExportSubmissions("intake", null, null);
        System.Text.Encoding.UTF8.GetString(csv.FileContents).Should().NotContain("diagnosis").And.NotContain("diabetes");

        var listed = ((IEnumerable<DataCollectionPolicyDto>)((OkObjectResult)(await controller.GetDataCollectionPolicies()).Result!).Value!).Single();
        listed.AnswersHidden.Should().BeTrue();
        listed.OwnerModule.Should().Be("emr");
        listed.SubmissionCount.Should().Be(1);
    }

    [Fact]
    public async Task An_answer_a_spreadsheet_would_run_as_a_formula_is_exported_as_text()
    {
        var submissions = new InMemoryJourneySubmissionStore();
        await submissions.SaveAsync(new JourneySubmission
        {
            PolicyId = "waitlist", TenantId = Tenant,
            Data = new Dictionary<string, object> { ["name"] = "=HYPERLINK(\"https://evil.test\",\"Open\")", ["note"] = "@SUM(A1)", ["age"] = "42" },
        });

        var csv = System.Text.Encoding.UTF8.GetString(((FileContentResult)await Controller(submissions).ExportSubmissions("waitlist", null, null)).FileContents);

        csv.Should().Contain("\"'=HYPERLINK(").And.Contain("'@SUM(A1)").And.Contain(",42");
        csv.Should().NotContain(",=HYPERLINK").And.NotContain("\"=HYPERLINK");
    }

    [Fact]
    public async Task Files_of_a_module_journey_are_not_opened_with_the_submissions_permission()
    {
        var files = new Mock<IFileAttachmentStore>();
        files.Setup(f => f.GetByEntityAsync(JourneyUploads.EntityType, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<FileAttachment>());
        var profiles = new Mock<IExtendedProfileService>();
        profiles.Setup(p => p.GetUserPermissionsAsync("admin", Tenant, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { AdminPermissions.SubmissionsRead });
        var submissions = new InMemoryJourneySubmissionStore();
        await submissions.SaveAsync(new JourneySubmission { PolicyId = "intake", TenantId = Tenant, JourneyId = "module-run" });
        await submissions.SaveAsync(new JourneySubmission { PolicyId = "waitlist", TenantId = Tenant, JourneyId = "platform-run" });
        var policies = Policies(ownerModule: "emr");

        var provider = new JourneyUploadFileAccessProvider(files.Object, profiles.Object, policies.Object, submissions);

        (await provider.CanReadAsync(Tenant, "admin", "module-run", null, null)).Should().BeFalse();
        (await provider.CanReadAsync(Tenant, "admin", "platform-run", null, null)).Should().BeTrue();
    }

    private static SubmissionsController Controller(IJourneySubmissionStore submissions, string? ownerModule = null)
    {
        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.TenantId).Returns(Tenant);
        return new SubmissionsController(Policies(ownerModule).Object, tenant.Object, NullLogger<SubmissionsController>.Instance, submissions);
    }

    /// <summary>A platform waitlist, and a patient intake owned by <paramref name="ownerModule"/>.</summary>
    private static Mock<IJourneyPolicyStore> Policies(string? ownerModule)
    {
        var waitlist = new JourneyPolicy { Id = "waitlist", Slug = "waitlist", Name = "Waitlist", TenantId = Tenant, Type = JourneyType.Waitlist };
        var intake = new JourneyPolicy
        {
            Id = "intake", Slug = "intake", Name = "Patient intake", TenantId = Tenant, Type = JourneyType.DataCollection,
            PersistSubmissions = true, OwnerModule = ownerModule,
        };
        var policies = new Mock<IJourneyPolicyStore>();
        policies.Setup(p => p.GetAsync("waitlist", It.IsAny<CancellationToken>())).ReturnsAsync(waitlist);
        policies.Setup(p => p.GetAsync("intake", It.IsAny<CancellationToken>())).ReturnsAsync(intake);
        policies.Setup(p => p.GetByTenantAsync(Tenant, It.IsAny<CancellationToken>())).ReturnsAsync(new[] { intake });
        return policies;
    }

    private static PagedResult<SubmissionDto> Page(ActionResult<PagedResult<SubmissionDto>> result) =>
        (PagedResult<SubmissionDto>)((OkObjectResult)result.Result!).Value!;
}

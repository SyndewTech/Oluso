using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Admin.Controllers;
using Oluso.Admin.Dto.Journeys;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Xunit;

namespace Oluso.Tests.UserJourneys;

/// <summary>
/// Clients name a journey by its slug, so the slug survives export and import, new
/// journeys get one from their name, and a slug in use is never taken over: it is a
/// 409 with a free suggestion. IDs are internal and always new.
/// </summary>
public class JourneyPolicySlugTests
{
    private static UserJourneyPoliciesController Controller(IJourneyPolicyStore store)
    {
        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.TenantId).Returns("acme");
        var sanitizer = new Mock<IHtmlSanitizationService>();
        sanitizer.Setup(s => s.Sanitize(It.IsAny<string?>())).Returns((string? html) => html ?? "");
        return new UserJourneyPoliciesController(
            store, tenant.Object, sanitizer.Object, Mock.Of<IIssuerResolver>(), Mock.Of<ITenantConfigurationService>(),
            NullLogger<UserJourneyPoliciesController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", "admin-1") }, "test"))
                }
            }
        };
    }

    private static JourneyPolicy Policy(string slug, string name) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Slug = slug,
        Name = name,
        TenantId = "acme",
        Type = JourneyType.SignIn,
        Enabled = true,
        Steps = new List<JourneyPolicyStep> { new() { Id = "login", Type = "local_login", Order = 1 } }
    };

    private static List<JourneyPolicyStepDto> Steps() => new() { new() { Id = "login", Type = "local_login", Order = 1 } };

    private static async Task<InMemoryJourneyPolicyStore> StoreWith(params JourneyPolicy[] policies)
    {
        var store = new InMemoryJourneyPolicyStore();
        foreach (var policy in policies) await store.SaveAsync(policy);
        return store;
    }

    private static async Task<string> ExportAsync(IJourneyPolicyStore store, string policyId)
    {
        var file = (FileContentResult)await Controller(store).ExportPolicy(policyId);
        return System.Text.Encoding.UTF8.GetString(file.FileContents);
    }

    private static T Created<T>(ActionResult<T> result) => (T)((ObjectResult)result.Result!).Value!;

    [Fact]
    public async Task Import_into_another_environment_keeps_the_slug_and_name_with_a_new_id()
    {
        var source = Policy("business-signin", "Business sign-in");
        var target = new InMemoryJourneyPolicyStore();

        var imported = Created(await Controller(target).ImportPolicy(
            new ImportPolicyRequest { Content = await ExportAsync(await StoreWith(source), source.Id) }));

        imported.Policy.Slug.Should().Be("business-signin");
        imported.Policy.Name.Should().Be("Business sign-in");
        imported.Policy.Id.Should().NotBe(source.Id);
        imported.Policy.Enabled.Should().BeFalse();
    }

    [Fact]
    public async Task A_slug_in_use_is_a_conflict_with_a_free_suggestion_and_nothing_is_taken_over()
    {
        var existing = Policy("business-signin", "Business sign-in");
        var store = await StoreWith(existing);

        var result = await Controller(store).ImportPolicy(
            new ImportPolicyRequest { Content = await ExportAsync(store, existing.Id), NewName = "Changed" });

        var conflict = (PolicySlugConflictDto)((ConflictObjectResult)result.Result!).Value!;
        conflict.Code.Should().Be(PolicySlugConflictDto.ErrorCode);
        conflict.SuggestedSlug.Should().Be("business-signin-2");
        (await store.GetBySlugAsync("business-signin"))!.Name.Should().Be("Business sign-in");
    }

    [Fact]
    public async Task The_admin_can_import_under_a_new_or_derived_slug()
    {
        var existing = Policy("business-signin", "Business sign-in");
        var store = await StoreWith(existing);
        var content = await ExportAsync(store, existing.Id);

        var renamed = Created(await Controller(store).ImportPolicy(new ImportPolicyRequest { Content = content, NewSlug = "partner-signin" }));
        var derived = Created(await Controller(store).ImportPolicy(new ImportPolicyRequest { Content = content, GenerateSlug = true }));

        renamed.Policy.Slug.Should().Be("partner-signin");
        derived.Policy.Slug.Should().Be("business-sign-in");
    }

    [Fact]
    public async Task Create_derives_a_free_slug_from_the_name_and_ids_are_guids()
    {
        var store = await StoreWith(Policy("business-sign-in", "Business sign-in"));

        var created = Created(await Controller(store).CreatePolicy(
            new CreatePolicyRequest { Name = "Business Sign-in", Type = "SignIn", Steps = Steps() }));

        created.Slug.Should().Be("business-sign-in-2");
        Guid.TryParse(created.Id, out _).Should().BeTrue();
    }

    [Fact]
    public async Task Create_clone_and_update_never_take_a_slug_in_use()
    {
        var existing = Policy("business-signin", "Business sign-in");
        var other = Policy("partner-signin", "Partner sign-in");
        var store = await StoreWith(existing, other);
        var controller = Controller(store);

        var create = await controller.CreatePolicy(
            new CreatePolicyRequest { Slug = "business-signin", Name = "Impostor", Type = "SignIn", Steps = Steps() });
        var clone = await controller.ClonePolicy(existing.Id, new ClonePolicyRequest { NewSlug = "business-signin" });
        var update = await controller.UpdatePolicy(other.Id, new UpdatePolicyRequest { Slug = "business-signin" });

        create.Result.Should().BeOfType<ConflictObjectResult>();
        clone.Result.Should().BeOfType<ConflictObjectResult>();
        update.Result.Should().BeOfType<ConflictObjectResult>();
        (await store.GetBySlugAsync("business-signin"))!.Id.Should().Be(existing.Id);
    }

    [Fact]
    public async Task Keeping_its_own_slug_on_update_is_not_a_conflict()
    {
        var existing = Policy("business-signin", "Business sign-in");
        var store = await StoreWith(existing);

        var update = await Controller(store).UpdatePolicy(existing.Id, new UpdatePolicyRequest { Slug = "business-signin", Name = "Renamed" });

        update.Result.Should().NotBeOfType<ConflictObjectResult>();
        (await store.GetBySlugAsync("business-signin"))!.Name.Should().Be("Renamed");
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("-leading-dash")]
    [InlineData("a/b")]
    public async Task Slugs_must_be_url_safe(string badSlug)
    {
        var existing = Policy("business-signin", "Business sign-in");
        var store = await StoreWith(existing);

        var clone = await Controller(store).ClonePolicy(existing.Id, new ClonePolicyRequest { NewSlug = badSlug });

        clone.Result.Should().BeOfType<BadRequestObjectResult>();
    }
}

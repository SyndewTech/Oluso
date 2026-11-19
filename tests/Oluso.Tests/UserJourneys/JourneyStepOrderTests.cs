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
/// A journey's steps run in the order they are listed, as the designer shows its boxes: saving, importing or
/// copying a journey numbers the steps by position, whatever order values the caller or file sent. A step added
/// in the designer to a journey that came with numbered steps no longer jumps to the front. Reading, exporting and
/// copying a journey list its steps in the order they run, so a journey stored before that shows what it runs.
/// </summary>
public class JourneyStepOrderTests
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

    /// <summary>Listed signup, verify, assign; the order values put the last-added step first.</summary>
    private static List<JourneyPolicyStepDto> StepsWithStaleOrder() => new()
    {
        new() { Id = "signup", Type = "local_login", Order = 1 },
        new() { Id = "verify", Type = "local_login", Order = 2 },
        new() { Id = "assign", Type = "local_login", Order = 0 },
    };

    private static string FirstToRun(JourneyPolicy policy) => policy.Steps.OrderBy(s => s.Order).First().Id;

    private static IEnumerable<string> RunOrder(JourneyPolicy policy) => policy.Steps.OrderBy(s => s.Order).Select(s => s.Id);

    [Fact]
    public async Task Creating_numbers_the_steps_by_position()
    {
        var store = new InMemoryJourneyPolicyStore();

        var created = (PolicyDetailDto)((ObjectResult)(await Controller(store).CreatePolicy(
            new CreatePolicyRequest { Name = "Customer sign-up", Type = "SignIn", Steps = StepsWithStaleOrder() })).Result!).Value!;

        var saved = (await store.GetAsync(created.Id))!;
        FirstToRun(saved).Should().Be("signup");
        RunOrder(saved).Should().Equal("signup", "verify", "assign");
    }

    [Fact]
    public async Task Saving_a_journey_whose_stored_numbers_disagree_with_its_boxes_runs_the_boxes_in_order()
    {
        var store = new InMemoryJourneyPolicyStore();
        var policy = new JourneyPolicy
        {
            Id = Guid.NewGuid().ToString(), Slug = "customer-signup", Name = "Customer sign-up", TenantId = "acme",
            Type = JourneyType.SignIn, Enabled = true,
            Steps = StepsWithStaleOrder().Select(s => new JourneyPolicyStep { Id = s.Id, Type = s.Type, Order = s.Order }).ToList(),
        };
        await store.SaveAsync(policy);
        FirstToRun((await store.GetAsync(policy.Id))!).Should().Be("assign", "this is the bug: the step added last ran first");

        var result = await Controller(store).UpdatePolicy(policy.Id, new UpdatePolicyRequest { Steps = StepsWithStaleOrder() });

        result.Result.Should().BeOfType<OkObjectResult>();
        RunOrder((await store.GetAsync(policy.Id))!).Should().Equal("signup", "verify", "assign");
    }

    [Fact]
    public async Task Moving_a_box_changes_the_order_it_runs_in()
    {
        var store = new InMemoryJourneyPolicyStore();
        var created = (PolicyDetailDto)((ObjectResult)(await Controller(store).CreatePolicy(
            new CreatePolicyRequest { Name = "Customer sign-up", Type = "SignIn", Steps = StepsWithStaleOrder() })).Result!).Value!;
        var moved = StepsWithStaleOrder();
        (moved[1], moved[2]) = (moved[2], moved[1]);

        await Controller(store).UpdatePolicy(created.Id, new UpdatePolicyRequest { Steps = moved });

        RunOrder((await store.GetAsync(created.Id))!).Should().Equal("signup", "assign", "verify");
    }

    private static async Task<JourneyPolicy> StoredWithStaleOrderAsync(InMemoryJourneyPolicyStore store)
    {
        var policy = new JourneyPolicy
        {
            Id = Guid.NewGuid().ToString(), Slug = $"customer-signup-{Guid.NewGuid():N}"[..24], Name = "Customer sign-up", TenantId = "acme",
            Type = JourneyType.SignIn, Enabled = true,
            Steps = StepsWithStaleOrder().Select(s => new JourneyPolicyStep { Id = s.Id, Type = s.Type, Order = s.Order }).ToList(),
        };
        await store.SaveAsync(policy);
        return policy;
    }

    [Fact]
    public async Task Reading_a_journey_lists_its_steps_in_the_order_they_run()
    {
        var store = new InMemoryJourneyPolicyStore();
        var policy = await StoredWithStaleOrderAsync(store);

        var read = (PolicyDetailDto)((OkObjectResult)(await Controller(store).GetPolicy(policy.Id)).Result!).Value!;

        read.Steps.Select(s => s.Id).Should().Equal("assign", "signup", "verify");
    }

    [Fact]
    public async Task Saving_what_was_read_back_keeps_the_order_it_ran_in()
    {
        var store = new InMemoryJourneyPolicyStore();
        var policy = await StoredWithStaleOrderAsync(store);
        var read = (PolicyDetailDto)((OkObjectResult)(await Controller(store).GetPolicy(policy.Id)).Result!).Value!;

        await Controller(store).UpdatePolicy(policy.Id, new UpdatePolicyRequest { Steps = read.Steps });

        var saved = (await store.GetAsync(policy.Id))!;
        RunOrder(saved).Should().Equal("assign", "signup", "verify");
        saved.Steps.Select(s => s.Order).Should().Equal(0, 1, 2);
    }

    [Fact]
    public async Task Copying_a_journey_keeps_the_order_it_runs_in()
    {
        var store = new InMemoryJourneyPolicyStore();
        var policy = await StoredWithStaleOrderAsync(store);

        var copy = (PolicyDetailDto)((ObjectResult)(await Controller(store).ClonePolicy(policy.Id, new ClonePolicyRequest { NewName = "Copy" })).Result!).Value!;

        RunOrder((await store.GetAsync(copy.Id))!).Should().Equal("assign", "signup", "verify");
    }
}

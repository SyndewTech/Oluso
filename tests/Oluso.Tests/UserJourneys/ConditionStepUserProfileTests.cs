using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Oluso.UserJourneys.Steps;
using Xunit;

namespace Oluso.Tests.UserJourneys;

/// <summary>
/// The condition step's "user" source reads the stored profile, so a policy can skip a
/// form the signed-in user has already filled in.
/// </summary>
public class ConditionStepUserProfileTests
{
    private static StepExecutionContext Rig(IDictionary<string, string>? customProperties, string key, string op)
    {
        var users = new Mock<IOlusoUserService>();
        users.Setup(u => u.FindByIdAsync("u1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OlusoUserInfo { Id = "u1", Username = "jo", CustomProperties = customProperties });

        return new StepExecutionContext
        {
            JourneyId = "business-journey",
            StepId = "needs-details",
            Configuration = new JourneyStepConfiguration
            {
                Id = "needs-details",
                Type = "condition",
                Settings = new Dictionary<string, object>
                {
                    ["conditions"] = new List<StepCondition> { new() { Type = "user", Field = key, Operator = op, Value = "" } },
                    ["onTrue"] = "details-form",
                    ["onFalse"] = "done"
                }
            },
            ServiceProvider = new ServiceCollection().AddLogging().AddSingleton(users.Object).BuildServiceProvider(),
            UserId = "u1",
        };
    }

    [Fact]
    public async Task Context_source_and_greaterThan_from_the_settings_form_evaluate()
    {
        var context = Rig(null, "company_name", "notexists");
        context.Configuration.Settings!["conditions"] = new List<StepCondition>
        {
            new() { Type = "context", Field = "userId", Operator = "equals", Value = "u1" },
            new() { Type = "previous_step", Field = "seats", Operator = "greaterThan", Value = "10" }
        };
        context.JourneyData["seats"] = 25;

        var result = await new ConditionStepHandler().ExecuteAsync(context);

        result.BranchId.Should().Be("details-form");
    }

    [Fact]
    public async Task A_missing_profile_field_takes_the_true_branch()
    {
        var result = await new ConditionStepHandler().ExecuteAsync(Rig(null, "company_name", "notexists"));

        result.BranchId.Should().Be("details-form");
    }

    [Fact]
    public async Task A_filled_profile_field_takes_the_false_branch()
    {
        var context = Rig(new Dictionary<string, string> { ["company_name"] = "Acme" }, "company_name", "notexists");

        var result = await new ConditionStepHandler().ExecuteAsync(context);

        result.BranchId.Should().Be("done");
        context.JourneyData.Should().NotContainKey("company_name");
    }
}

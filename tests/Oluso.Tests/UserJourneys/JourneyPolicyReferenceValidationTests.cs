using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
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
/// Saving a journey catches step references that would misroute it at runtime: next steps
/// that don't exist, failure loops, step targets in settings, and sign-up verification
/// with nothing to verify.
/// </summary>
public class JourneyPolicyReferenceValidationTests
{
    private static async Task<ValidationResultDto> ValidateAsync(params JourneyPolicyStepDto[] steps)
    {
        var registry = new DefaultStepHandlerRegistry(new ServiceCollection().BuildServiceProvider());
        var controller = new UserJourneyPoliciesController(
            Mock.Of<IJourneyPolicyStore>(), Mock.Of<ITenantContext>(), Mock.Of<IHtmlSanitizationService>(), Mock.Of<IIssuerResolver>(),
            Mock.Of<ITenantConfigurationService>(), NullLogger<UserJourneyPoliciesController>.Instance, stepRegistry: registry)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };

        var result = await controller.ValidatePolicy(new ValidatePolicyRequest { Steps = steps.ToList() });
        return (ValidationResultDto)((OkObjectResult)result.Result!).Value!;
    }

    private static Dictionary<string, object> Settings(string json) => JsonSerializer.Deserialize<Dictionary<string, object>>(json)!;

    [Fact]
    public async Task Next_steps_and_failure_targets_must_exist_and_not_loop()
    {
        var result = await ValidateAsync(
            new JourneyPolicyStepDto { Id = "a", Type = "local_login", Order = 1, OnSuccess = "missing" },
            new JourneyPolicyStepDto { Id = "b", Type = "local_login", Order = 2, OnFailure = "b" });

        result.Errors.Should().Contain(e => e.Contains("non-existent step: missing"))
            .And.Contain(e => e.Contains("continues on failure to itself"));
    }

    [Fact]
    public async Task Step_targets_in_settings_must_be_steps_or_branches()
    {
        var result = await ValidateAsync(
            new JourneyPolicyStepDto
            {
                Id = "check", Type = "condition", Order = 1,
                Configuration = Settings("""{ "conditions": [ { "type": "data", "field": "x", "operator": "exists" } ], "onTrue": "next", "onFalse": "nowhere" }"""),
            },
            new JourneyPolicyStepDto { Id = "next", Type = "local_login", Order = 2 });

        result.Errors.Should().ContainSingle(e => e.Contains("'onFalse'") && e.Contains("nowhere"));
    }

    [Fact]
    public async Task Sign_up_that_needs_verification_warns_without_a_verification_step()
    {
        var result = await ValidateAsync(new JourneyPolicyStepDto { Id = "signup", Type = "signup", Order = 1 });

        result.Warnings.Should().Contain(w => w.Contains("requires email verification"));
    }
}

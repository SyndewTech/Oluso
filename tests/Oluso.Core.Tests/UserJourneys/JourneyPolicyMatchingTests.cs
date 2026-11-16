using FluentAssertions;
using Oluso.Core.UserJourneys;
using Xunit;

namespace Oluso.Core.Tests.UserJourneys;

/// <summary>
/// Choosing a journey policy by its conditions: one whose conditions the request meets is more specific than one
/// without conditions, so the built-in "Sign In with MFA" (priority 85, acr_values contains mfa) is chosen over the
/// default sign-in (priority 100, no conditions) when the request asks for MFA, and never otherwise.
/// </summary>
public class JourneyPolicyMatchingTests
{
    private readonly InMemoryJourneyPolicyStore _store = new();

    [Fact]
    public async Task A_request_asking_for_mfa_gets_the_mfa_policy_despite_its_lower_priority()
    {
        var policy = await _store.FindMatchingAsync(new JourneyPolicyMatchContext
        {
            Type = JourneyType.SignIn,
            AcrValues = "https://oluso.io/acr/mfa",
        });

        policy!.Id.Should().Be("signin-mfa");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("https://oluso.io/acr/sfa")]
    public async Task Otherwise_the_policy_without_conditions_runs(string? acrValues)
    {
        var policy = await _store.FindMatchingAsync(new JourneyPolicyMatchContext { Type = JourneyType.SignIn, AcrValues = acrValues });

        policy!.Id.Should().Be("signin");
    }

    [Fact]
    public async Task A_policy_whose_conditions_fail_is_never_chosen()
    {
        await _store.DeleteAsync("signin");

        var policy = await _store.FindMatchingAsync(new JourneyPolicyMatchContext { Type = JourneyType.SignIn });

        policy.Should().BeNull("the only sign-in policy left requires acr_values to ask for mfa");
    }
}

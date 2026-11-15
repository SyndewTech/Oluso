using FluentAssertions;
using Oluso.Core.Authentication;
using Xunit;

namespace Oluso.Tests.Authentication;

public class SecondFactorRequirementTests
{
    [Theory]
    // A tenant that requires MFA: password, external and unrecorded sign-ins need one.
    [InlineData(true, false, new[] { "pwd" }, true)]
    [InlineData(true, false, new[] { "external" }, true)]
    [InlineData(true, false, new string[0], true)]
    // A second factor, or a passkey, meets it.
    [InlineData(true, false, new[] { "pwd", "mfa" }, false)]
    [InlineData(true, false, new[] { "external", "mfa" }, false)]
    [InlineData(true, false, new[] { "hwk" }, false)]
    [InlineData(true, false, new[] { "otp" }, false)]
    // Without the tenant setting: only a password sign-in by a user who turned MFA on.
    [InlineData(false, true, new[] { "pwd" }, true)]
    [InlineData(false, true, new[] { "external" }, false)]
    [InlineData(false, false, new[] { "pwd" }, false)]
    [InlineData(false, true, new[] { "pwd", "mfa" }, false)]
    public void A_sign_in_needs_a_second_factor_when_the_tenant_or_account_requires_one_and_it_has_none(
        bool tenantRequiresMfa, bool userHasMfa, string[] methods, bool missing)
    {
        SecondFactorRequirement.IsMissing(tenantRequiresMfa, userHasMfa, methods).Should().Be(missing);
    }
}

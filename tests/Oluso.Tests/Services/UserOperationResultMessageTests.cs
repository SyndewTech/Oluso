using FluentAssertions;
using Oluso.Core.Services;
using Xunit;

namespace Oluso.Tests.Services;

/// <summary>What a person is told when a user operation fails: never a bare code.</summary>
public class UserOperationResultMessageTests
{
    [Fact]
    public void A_coded_failure_reads_as_its_description()
    {
        var result = UserOperationResult.Failed("email_domain_not_allowed", "Registrations from this email domain are not allowed.");

        result.Message.Should().Be("Registrations from this email domain are not allowed.");
        result.MessagesOr("x").Should().Equal("Registrations from this email domain are not allowed.");
    }

    [Fact]
    public void Identity_errors_read_as_themselves()
    {
        var result = UserOperationResult.Failed(new[] { "Passwords must have at least one digit.", "Passwords must be at least 12 characters." });

        result.Message.Should().Be("Passwords must have at least one digit. Passwords must be at least 12 characters.");
        result.MessagesOr("x").Should().Equal("Passwords must have at least one digit.", "Passwords must be at least 12 characters.");
    }

    [Fact]
    public void A_plain_failure_reads_as_its_text_and_nothing_falls_back()
    {
        UserOperationResult.Failed("User not found").MessageOr("x").Should().Be("User not found");
        new UserOperationResult { Succeeded = false }.MessageOr("Something went wrong").Should().Be("Something went wrong");
    }
}

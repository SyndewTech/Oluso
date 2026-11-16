using FluentAssertions;
using Oluso.Core.Protocols.Models;
using Xunit;

namespace Oluso.Tests.Protocols;

public class AuthenticationContextClassesTests
{
    [Theory]
    [InlineData(new[] { "pwd" }, AuthenticationContextClasses.SingleFactor)]
    [InlineData(new[] { "hwk" }, AuthenticationContextClasses.SingleFactor)]
    [InlineData(new[] { "external", "google" }, AuthenticationContextClasses.SingleFactor)]
    [InlineData(new[] { "pwd", "mfa" }, AuthenticationContextClasses.MultiFactor)]
    [InlineData(new[] { "external", "mfa" }, AuthenticationContextClasses.MultiFactor)]
    public void The_class_follows_the_authentication_methods(string[] amr, string expected)
    {
        AuthenticationContextClasses.Resolve(null, amr).Should().Be(expected);
    }

    [Fact]
    public void An_acr_recorded_by_the_sign_in_is_kept()
    {
        AuthenticationContextClasses.Resolve("urn:example:loa:3", new[] { "pwd" }).Should().Be("urn:example:loa:3");
    }

    [Fact]
    public void No_recorded_method_asserts_no_class()
    {
        AuthenticationContextClasses.Resolve(null, null).Should().BeNull();
        AuthenticationContextClasses.Resolve(null, Array.Empty<string>()).Should().BeNull();
    }
}

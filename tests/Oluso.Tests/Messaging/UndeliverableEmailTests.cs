using FluentAssertions;
using Oluso.Core.Services;
using Xunit;

namespace Oluso.Tests.Messaging;

/// <summary>
/// Seeded and test accounts (reserved domains, domains without a dot) are never handed to an email provider; real
/// domains, including ones that merely contain a reserved word, still are.
/// </summary>
public class UndeliverableEmailTests
{
    [Theory]
    [InlineData("admin@default.local")]
    [InlineData("user@tenant.localhost")]
    [InlineData("a@b.test")]
    [InlineData("a@shop.example")]
    [InlineData("a@nowhere.invalid")]
    [InlineData("a@corp.internal")]
    [InlineData("a@example.com")]
    [InlineData("a@mail.example.org")]
    [InlineData("a@localhost")]
    [InlineData("root@intranet")]
    [InlineData("not-an-address")]
    [InlineData("a@")]
    [InlineData("")]
    public void Reserved_and_malformed_addresses_are_undeliverable(string address)
        => UndeliverableEmail.IsUndeliverable(address).Should().BeTrue();

    [Theory]
    [InlineData("akin.odeyale@syndew.com")]
    [InlineData("ops@local.ng")]
    [InlineData("hello@testing.io")]
    [InlineData("a@myexample.com")]
    [InlineData("A@Syndew.COM.")]
    public void Real_domains_are_deliverable(string address)
        => UndeliverableEmail.IsUndeliverable(address).Should().BeFalse();
}

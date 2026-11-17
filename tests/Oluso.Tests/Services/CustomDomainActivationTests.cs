using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Xunit;

namespace Oluso.Tests.Services;

/// <summary>
/// A reopened domain keeps the edge hostname id it had: activation uses that hostname while the edge provider still
/// has it, and submits the hostname again once the provider reports it gone.
/// </summary>
public class CustomDomainActivationTests
{
    private const string Domain = "login.example.test";

    private readonly Mock<ICustomDomainProvisioningService> _edge = new();
    private readonly Tenant _tenant = new() { Id = "t1", Name = "T1", Identifier = "t1" };

    private CustomDomainActivationService Service() => new(
        Mock.Of<ITenantStore>(),
        new ConfigurationBuilder().Build(),
        NullLogger<CustomDomainActivationService>.Instance,
        _edge.Object);

    private static CustomDomainVerification Reopened() => new()
    {
        TenantId = "t1",
        Domain = Domain,
        Status = CustomDomainStatus.Verified,
        VerificationToken = "token",
        TxtRecordName = $"_oluso-verify.{Domain}",
        TxtRecordValue = "oluso-verify=token",
        ExternalProvisioningId = "edge-1",
    };

    [Fact]
    public async Task A_hostname_the_edge_still_has_is_used_without_submitting_it_again()
    {
        _edge.Setup(e => e.GetStatusAsync(Domain, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DomainProvisioningStatus.Active("edge-1"));
        var domain = Reopened();

        var outcome = await Service().TryActivateAsync(domain, _tenant);

        outcome.Should().Be(CustomDomainActivationOutcome.Activated);
        _tenant.CustomDomain.Should().Be(Domain);
        _edge.Verify(e => e.ProvisionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task A_hostname_the_edge_dropped_is_submitted_again_on_the_next_pass()
    {
        _edge.SetupSequence(e => e.GetStatusAsync(Domain, It.IsAny<CancellationToken>()))
            .ReturnsAsync(DomainProvisioningStatus.NotFound())
            .ReturnsAsync(DomainProvisioningStatus.Active("edge-2"));
        _edge.Setup(e => e.ProvisionAsync(Domain, "t1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(DomainProvisioningResult.Ok("edge-2"));
        var domain = Reopened();

        (await Service().TryActivateAsync(domain, _tenant)).Should().Be(CustomDomainActivationOutcome.AwaitingEdge);
        domain.ExternalProvisioningId.Should().BeNull();

        (await Service().TryActivateAsync(domain, _tenant)).Should().Be(CustomDomainActivationOutcome.Activated);
        domain.ExternalProvisioningId.Should().Be("edge-2");
        _edge.Verify(e => e.ProvisionAsync(Domain, "t1", It.IsAny<CancellationToken>()), Times.Once);
    }
}

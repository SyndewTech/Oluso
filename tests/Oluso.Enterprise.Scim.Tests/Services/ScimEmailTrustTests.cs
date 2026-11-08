using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Services;
using Oluso.Enterprise.Scim.Entities;
using Oluso.Enterprise.Scim.Models;
using Oluso.Enterprise.Scim.Services;
using Oluso.Enterprise.Scim.Stores;
using Xunit;

namespace Oluso.Enterprise.Scim.Tests.Services;

/// <summary>
/// A provisioned address is the SCIM client's word, not proof of the inbox: it counts as verified only at a domain
/// the tenant has verified.
/// </summary>
public class ScimEmailTrustTests
{
    private const string Tenant = "t1";

    private readonly Mock<UserManager<OlusoUser>> _userManager;
    private readonly Mock<IScimUserMapper> _mapper = new();
    private readonly Mock<IScimResourceMappingStore> _mappings = new();
    private readonly Mock<IScimAttributeMappingStore> _attributeMappings = new();
    private readonly Mock<ITenantEmailDomainStore> _emailDomains = new();
    private readonly ScimClient _client = new() { Id = "sc1", TenantId = Tenant, Name = "Payroll", CanCreateUsers = true };
    private OlusoUser? _created;

    public ScimEmailTrustTests()
    {
        _userManager = new Mock<UserManager<OlusoUser>>(Mock.Of<IUserStore<OlusoUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);
        _userManager.Setup(m => m.CreateAsync(It.IsAny<OlusoUser>()))
            .Callback<OlusoUser>(u => _created = u)
            .ReturnsAsync(IdentityResult.Success);
        _attributeMappings.Setup(s => s.GetByClientAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ScimAttributeMapping>());
        _mappings.Setup(s => s.CreateAsync(It.IsAny<ScimResourceMapping>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ScimResourceMapping m, CancellationToken _) => m);
        _mapper.Setup(m => m.ToScimUser(It.IsAny<OlusoUser>(), It.IsAny<string>(), It.IsAny<string?>()))
            .Returns((OlusoUser u, string _, string? _) => new ScimUser { Id = u.Id, UserName = u.UserName! });
    }

    private ScimProvisioningService Service()
    {
        var notifier = Mock.Of<IModuleAdminAssignmentNotifier>();
        var events = new OlusoEventService(Array.Empty<IOlusoEventSink>(), NullLogger<OlusoEventService>.Instance);
        return new ScimProvisioningService(
            _userManager.Object,
            new Mock<RoleManager<OlusoRole>>(Mock.Of<IRoleStore<OlusoRole>>(), null!, null!, null!, null!).Object,
            _mapper.Object, _mappings.Object, _attributeMappings.Object,
            Mock.Of<IRoleStore>(), Mock.Of<IRoleGrantService>(), notifier, events, _emailDomains.Object,
            NullLogger<ScimProvisioningService>.Instance);
    }

    private void Maps(string email) =>
        _mapper.Setup(m => m.CreateFromScimUser(It.IsAny<ScimUser>(), Tenant, _client, It.IsAny<IReadOnlyList<ScimAttributeMapping>>()))
            .Returns(new OlusoUser { Id = "u1", TenantId = Tenant, UserName = email, Email = email, EmailConfirmed = false });

    [Fact]
    public async Task An_address_at_a_domain_the_tenant_verified_is_provisioned_verified()
    {
        Maps("ada@acme.test");
        _emailDomains.Setup(d => d.IsVerifiedForTenantAsync(Tenant, "ada@acme.test", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await Service().CreateUserAsync(_client, new ScimUser { UserName = "ada@acme.test" }, "https://idp/scim/v2");

        _created!.EmailConfirmed.Should().BeTrue();
    }

    [Fact]
    public async Task An_address_anywhere_else_is_provisioned_unverified()
    {
        Maps("ada@gmail.com");

        await Service().CreateUserAsync(_client, new ScimUser { UserName = "ada@gmail.com" }, "https://idp/scim/v2");

        _created!.EmailConfirmed.Should().BeFalse();
    }
}

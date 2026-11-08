using FluentAssertions;
using Moq;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Core.Protocols.Grants;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Services;
using Oluso.Protocols.Grants;
using Xunit;

namespace Oluso.Tests.Protocols;

/// <summary>
/// The client's user restrictions: allowed users or allowed roles say who is assigned; the required permission and a
/// verified email must hold on top. They apply on every way a user gets a token.
/// </summary>
public class ClientUserAccessTests
{
    private readonly Mock<IExtendedProfileService> _profiles = new();
    private readonly Mock<IOlusoUserService> _users = new();

    private IClientUserAccess Access() => ClientUserAccessKit.For(_profiles.Object, _users.Object);

    private void Roles(params string[] roles) =>
        _profiles.Setup(p => p.GetUserRolesAsync("u1", It.IsAny<CancellationToken>())).ReturnsAsync(roles);

    private void Permissions(string? tenant, params string[] permissions) =>
        _profiles.Setup(p => p.GetUserPermissionsAsync("u1", tenant, It.IsAny<CancellationToken>())).ReturnsAsync(permissions);

    private void Email(bool verified) =>
        _users.Setup(u => u.FindByIdsAcrossTenantsAsync(It.IsAny<IReadOnlyCollection<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, OlusoUserInfo>
            {
                ["u1"] = new() { Id = "u1", Username = "u1", Email = "u1@acme.test", EmailVerified = verified }
            });

    private static ClientUserRestrictions Restrictions(
        string[]? users = null, string[]? roles = null, string? permission = null, bool verifiedEmail = false) =>
        new(users ?? Array.Empty<string>(), roles ?? Array.Empty<string>(), permission, verifiedEmail);

    [Fact]
    public async Task A_client_with_no_restrictions_admits_anyone()
    {
        (await Access().FindDenialAsync("app", Restrictions(), "u1", "t1")).Should().BeNull();
    }

    [Fact]
    public async Task An_allowed_user_or_a_holder_of_an_allowed_role_is_assigned()
    {
        Roles("Viewer");
        var listedOrAdmins = Restrictions(users: new[] { "u1" }, roles: new[] { "Admin" });
        var otherOrViewers = Restrictions(users: new[] { "u2" }, roles: new[] { "Viewer" });
        var otherOrAdmins = Restrictions(users: new[] { "u2" }, roles: new[] { "Admin" });

        (await Access().FindDenialAsync("app", listedOrAdmins, "u1", "t1")).Should().BeNull("u1 is listed by name");
        (await Access().FindDenialAsync("app", otherOrViewers, "u1", "t1")).Should().BeNull("u1 holds an allowed role");
        (await Access().FindDenialAsync("app", otherOrAdmins, "u1", "t1")).Should().Be(ClientUserAccess.NotAllowedDescription);
    }

    [Fact]
    public async Task Being_assigned_does_not_waive_the_required_permission()
    {
        Permissions("t1");

        var denial = await Access().FindDenialAsync("app", Restrictions(users: new[] { "u1" }, permission: "app.access"), "u1", "t1");

        denial.Should().Be(ClientUserAccess.MissingPermissionDescription);
    }

    [Fact]
    public async Task The_required_permission_is_looked_up_in_the_tokens_tenant()
    {
        Permissions("t1", "app.access");
        Permissions("t2");

        (await Access().FindDenialAsync("app", Restrictions(permission: "app.access"), "u1", "t1")).Should().BeNull();
        (await Access().FindDenialAsync("app", Restrictions(permission: "app.access"), "u1", "t2"))
            .Should().Be(ClientUserAccess.MissingPermissionDescription);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task A_client_that_requires_a_verified_email_admits_only_verified_addresses(bool verified, bool admitted)
    {
        Email(verified);

        var denial = await Access().FindDenialAsync("mail", Restrictions(verifiedEmail: true), "u1", "t1");

        (denial == null).Should().Be(admitted);
    }

    [Fact]
    public async Task A_device_code_is_refused_for_a_user_the_client_does_not_admit()
    {
        Email(verified: false);
        var store = new Mock<IDeviceFlowStore>();
        store.Setup(s => s.FindByDeviceCodeAsync("dc", It.IsAny<CancellationToken>())).ReturnsAsync(new Core.Domain.Entities.DeviceFlowCode
        {
            DeviceCode = "dc", UserCode = "UC", ClientId = "mail", SubjectId = "u1",
            Expiration = DateTime.UtcNow.AddMinutes(5),
            Data = System.Text.Json.JsonSerializer.Serialize(new DeviceCodeData { Status = DeviceCodeStatus.Authorized })
        });

        var result = await new DeviceCodeGrantHandler(store.Object, Access()).HandleAsync(new TokenRequest
        {
            GrantType = OidcConstants.GrantTypes.DeviceCode,
            DeviceCode = "dc",
            Client = new ValidatedClient { ClientId = "mail", RequireVerifiedEmail = true }
        });

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.AccessDenied);
    }

    [Fact]
    public async Task An_approved_ciba_request_is_refused_for_a_user_the_client_does_not_admit()
    {
        Email(verified: false);
        var ciba = new Mock<ICibaService>();
        ciba.Setup(c => c.GetStatusAsync("req", "mail", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CibaStatusResult { Status = CibaRequestStatus.Approved, SubjectId = "u1" });

        var result = await new CibaGrantHandler(ciba.Object, Access()).HandleAsync(new TokenRequest
        {
            AuthReqId = "req",
            Client = new ValidatedClient { ClientId = "mail", RequireVerifiedEmail = true }
        });

        result.Error.Should().Be(OidcConstants.Errors.AccessDenied);
    }
}

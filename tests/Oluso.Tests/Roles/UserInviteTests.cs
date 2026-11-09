using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Admin.Controllers;
using Oluso.Admin.Dto.Users;
using AdminCreateUser = Oluso.Admin.Dto.Users.CreateUserRequest;
using ServiceCreateUser = Oluso.Core.Services.CreateUserRequest;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Services;
using Xunit;

namespace Oluso.Tests.Roles;

/// <summary>
/// Creating a user with an invite emails them a set-password link on their tenant's own sign-in host, valid for the
/// tenant's account-link lifetime (and says so); the invite can be resent until they set a password.
/// </summary>
public class UserInviteTests
{
    private const string Tenant = "tenant-a";

    private readonly Mock<IOlusoUserService> _users = new();
    private readonly Mock<IEmailSender> _email = new();
    private readonly Mock<IIssuerResolver> _issuer = new();
    private readonly Mock<ITenantSettingsProvider> _settings = new();
    private readonly Mock<ITenantStore> _tenants = new();
    private readonly Mock<IRoleStore> _roles = new();
    private readonly List<(string To, string Subject, string Html)> _sent = new();

    public UserInviteTests()
    {
        var tenant = new Tenant { Id = Tenant, Name = "Hygeia Clinic", Identifier = "hygeia", OrganizationId = "org-1" };
        _tenants.Setup(t => t.GetByIdAsync(Tenant, It.IsAny<CancellationToken>())).ReturnsAsync(tenant);
        _issuer.Setup(i => i.GetIssuerForTenantAsync(tenant, It.IsAny<CancellationToken>())).ReturnsAsync("https://hygeia.example.org/");
        _settings.Setup(s => s.GetAccountEmailSettingsAsync(Tenant, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantAccountEmailSettings { AccountLinkLifetimeMinutes = 48 * 60 });
        _users.Setup(u => u.GeneratePasswordResetTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync("tok+en");
        _roles.Setup(r => r.GetUserRolesInTenantAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OlusoRole>());
        _email.Setup(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, CancellationToken>((to, subject, html, _) => _sent.Add((to, subject, html)))
            .ReturnsAsync(new EmailResult { Success = true });
    }

    private UsersController Controller()
    {
        var context = new Mock<ITenantContext>();
        context.Setup(c => c.TenantId).Returns(Tenant);
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("sub", "admin-1"), new Claim("name", "Ada"), new Claim("tenant_id", Tenant),
            }, "test")),
        };
        return new UsersController(context.Object, _users.Object, Mock.Of<IOlusoEventService>(), _roles.Object,
            Mock.Of<ITenantMembershipStore>(), _tenants.Object, Mock.Of<IModuleAdminAssignmentNotifier>(), NullLogger<UsersController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = http },
        };
    }

    private void UserExists(string id, bool hasPassword)
    {
        _users.Setup(u => u.FindByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OlusoUserInfo { Id = id, Username = "ngozi@example.org", Email = "ngozi@example.org", TenantId = Tenant, FirstName = "Ngozi" });
        _users.Setup(u => u.HasPasswordAsync(id, It.IsAny<CancellationToken>())).ReturnsAsync(hasPassword);
    }

    [Fact]
    public async Task Creating_a_user_with_an_invite_emails_a_set_password_link_on_the_tenants_host_with_its_expiry()
    {
        _users.Setup(u => u.CreateUserAsync(It.Is<ServiceCreateUser>(r => r.Password == ""), It.IsAny<CancellationToken>()))
            .ReturnsAsync(UserOperationResult.Success("u-1"));
        UserExists("u-1", hasPassword: false);

        var result = await Controller().CreateUser(
            new AdminCreateUser { Email = "ngozi@example.org", FirstName = "Ngozi", SendInvite = true },
            _issuer.Object, _settings.Object, _email.Object, null, Mock.Of<ITenantEmailDomainStore>(), CancellationToken.None);

        var created = result.Result.Should().BeOfType<CreatedAtActionResult>().Subject.Value.Should().BeOfType<UserDto>().Subject;
        created.InviteError.Should().BeNull();
        var mail = _sent.Should().ContainSingle().Subject;
        mail.To.Should().Be("ngozi@example.org");
        mail.Subject.Should().Contain("Hygeia Clinic");
        mail.Html.Should().Contain("https://hygeia.example.org/account/reset-password?token=tok%2Ben&email=ngozi%40example.org")
            .And.Contain("2 days");
    }

    [Fact]
    public async Task An_invite_with_a_password_is_refused_before_anything_is_created()
    {
        var result = await Controller().CreateUser(
            new AdminCreateUser { Email = "a@example.org", Password = "Secret-123!", SendInvite = true },
            _issuer.Object, _settings.Object, _email.Object, null, Mock.Of<ITenantEmailDomainStore>(), CancellationToken.None);

        result.Result.Should().BeOfType<BadRequestObjectResult>();
        _users.Verify(u => u.CreateUserAsync(It.IsAny<ServiceCreateUser>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task The_invite_can_be_resent_until_the_user_sets_a_password()
    {
        UserExists("u-2", hasPassword: false);
        (await Controller().ResendInvite("u-2", _issuer.Object, _settings.Object, _email.Object, null, CancellationToken.None))
            .Should().BeOfType<OkObjectResult>();
        _sent.Should().ContainSingle();

        UserExists("u-3", hasPassword: true);
        (await Controller().ResendInvite("u-3", _issuer.Object, _settings.Object, _email.Object, null, CancellationToken.None))
            .Should().BeOfType<ConflictObjectResult>();
        _sent.Should().ContainSingle();
    }

    [Fact]
    public async Task Without_email_set_up_the_resend_says_so_instead_of_pretending()
    {
        UserExists("u-4", hasPassword: false);

        var result = await Controller().ResendInvite("u-4", _issuer.Object, _settings.Object, null, null, CancellationToken.None);

        result.Should().BeOfType<ObjectResult>().Which.StatusCode.Should().Be(StatusCodes.Status502BadGateway);
    }
}

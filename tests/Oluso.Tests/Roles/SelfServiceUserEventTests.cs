using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Account.Controllers;
using Oluso.Account.Dto.Profile;
using Oluso.Account.Dto.Security;
using Oluso.Core.Events;
using Oluso.Core.Services;
using Oluso.EntityFramework;
using Oluso.EntityFramework.Services;
using Xunit;

namespace Oluso.Tests.Roles;

/// <summary>
/// A signed-in user editing or verifying their own account raises the same
/// core events an admin's change does, attributed to the user themselves.
/// </summary>
public class SelfServiceUserEventTests : IDisposable
{
    private const string Tenant = "tenant-a";

    private readonly SqliteConnection _connection;
    private readonly TestTenantContext _tenant = new() { TenantId = Tenant };
    private readonly OlusoDbContext _context;
    private readonly HttpContextAccessor _http = new();
    private readonly CapturedEvents _captured;
    private readonly IdentityUserService _users;

    public SelfServiceUserEventTests()
    {
        _connection = IdentityTestFactory.OpenSqliteConnection();
        _context = IdentityTestFactory.CreateSqliteContext(_connection, _tenant);
        _captured = new CapturedEvents(new HttpContextEventOriginResolver(_http));
        _users = IdentityTestFactory.CreateUserService(_context, _tenant, _captured.Services);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private ControllerContext SignedIn(string userId)
    {
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", userId) }, "test")),
        };
        http.Request.Path = "/api/account/profile";
        _http.HttpContext = http;
        return new ControllerContext { HttpContext = http };
    }

    [Fact]
    public async Task A_profile_edit_raises_one_user_updated_attributed_to_the_user()
    {
        var user = await IdentityTestFactory.CreateUserAsync(_context, Tenant, "me@a.test", "Ada", "Obi");
        var controller = new ProfileController(_tenant, _users, NullLogger<ProfileController>.Instance)
        {
            ControllerContext = SignedIn(user.Id),
        };

        var result = await controller.UpdateProfile(new UpdateProfileRequest { FirstName = "Adaeze", LastName = "Obi" }, default);

        result.Result.Should().BeOfType<OkObjectResult>();
        var updated = _captured.Raised.Should().ContainSingle().Which.Should().BeOfType<UserUpdatedEvent>().Subject;
        updated.WebhookEventType.Should().Be("user.updated");
        updated.SubjectId.Should().Be(user.Id);
        updated.TenantId.Should().Be(Tenant);
        updated.ChangedFields.Should().Equal(new Dictionary<string, string> { ["firstName"] = "Adaeze" });
        updated.Origin!.Kind.Should().Be(EventOriginKinds.User);
        updated.Origin.Id.Should().Be(user.Id);
    }

    [Fact]
    public async Task Verifying_an_email_from_the_account_page_raises_user_email_verified_once()
    {
        var user = await IdentityTestFactory.CreateUserAsync(_context, Tenant, "me@a.test");
        var mfa = new Mock<IMfaService>();
        mfa.Setup(m => m.VerifyEmailCodeAsync(user.Id, "123456", It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var controller = new SecurityController(_tenant, _users, _captured.Service, NullLogger<SecurityController>.Instance, mfa.Object)
        {
            ControllerContext = SignedIn(user.Id),
        };

        (await controller.VerifyEmail(new VerifyCodeRequest { Code = "123456" }, default)).Should().BeOfType<OkObjectResult>();

        var verified = _captured.Raised.Should().ContainSingle().Which.Should().BeOfType<UserEmailVerifiedEvent>().Subject;
        verified.SubjectId.Should().Be(user.Id);
        verified.Email.Should().Be("me@a.test");
        verified.Origin!.Kind.Should().Be(EventOriginKinds.User);
    }
}

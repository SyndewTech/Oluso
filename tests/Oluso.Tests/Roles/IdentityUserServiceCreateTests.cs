using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.EntityFramework;
using Oluso.EntityFramework.Services;
using Xunit;

namespace Oluso.Tests.Roles;

/// <summary>
/// Creating an account returns the new user: sign-up and first-time external sign-in read it
/// straight from the result.
/// </summary>
public class IdentityUserServiceCreateTests : IDisposable
{
    private readonly OlusoDbContext _context = IdentityTestFactory.CreateContext();
    private readonly Mock<ITenantContext> _tenantContext = new();
    private readonly IdentityUserService _service;

    public IdentityUserServiceCreateTests()
    {
        var userManager = IdentityTestFactory.CreateUserManager(_context, _tenantContext.Object);
        var signInManager = new SignInManager<OlusoUser>(
            userManager,
            new Mock<IHttpContextAccessor>().Object,
            new Mock<IUserClaimsPrincipalFactory<OlusoUser>>().Object,
            Options.Create(new IdentityOptions()),
            NullLogger<SignInManager<OlusoUser>>.Instance,
            new Mock<IAuthenticationSchemeProvider>().Object,
            new Mock<IUserConfirmation<OlusoUser>>().Object);
        _tenantContext.SetupGet(c => c.TenantId).Returns("t1");

        _service = new IdentityUserService(
            userManager, signInManager, _tenantContext.Object, new Mock<ITenantSettingsProvider>().Object,
            _context, NullLogger<IdentityUserService>.Instance);
    }

    public void Dispose() => _context.Dispose();

    [Fact]
    public async Task Creating_an_account_returns_the_new_user()
    {
        var result = await _service.CreateUserAsync(new CreateUserRequest
        {
            Email = "ada@t1.test", FirstName = "Ada", LastName = "Obi", TenantId = "t1"
        });

        result.Succeeded.Should().BeTrue(result.Error);
        result.User.Should().NotBeNull();
        result.User!.Id.Should().Be(result.UserId);
        result.User.Email.Should().Be("ada@t1.test");
        result.User.DisplayName.Should().Be("Ada Obi");
    }
}

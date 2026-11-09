using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Core.Services;
using Oluso.UI.Pages.Account;
using Xunit;

namespace Oluso.Tests.UserJourneys;

/// <summary>
/// The standalone reset email goes to the address on the account: never a differently-written
/// address that the normalised lookup happens to match to the account.
/// </summary>
public class ForgotPasswordRecipientTests
{
    [Fact]
    public async Task The_reset_link_goes_to_the_address_on_the_account_not_the_one_typed()
    {
        const string lookalike = "joı@x.test";
        var users = new Mock<IOlusoUserService>();
        users.Setup(u => u.FindByEmailAsync(lookalike, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OlusoUserInfo { Id = "u1", Username = "jo", Email = "jo@x.test" });
        users.Setup(u => u.GeneratePasswordResetTokenAsync("u1", It.IsAny<CancellationToken>())).ReturnsAsync("identity-token");
        var settings = new Mock<ITenantSettingsProvider>();
        settings.Setup(s => s.GetAccountEmailSettingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantAccountEmailSettings());
        var sent = new List<(string To, string Body)>();
        var messaging = new Mock<IMessagingService>();
        messaging.Setup(m => m.SendEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((string to, string _, string body, CancellationToken _) => sent.Add((to, body)))
            .Returns(Task.CompletedTask);

        var http = new DefaultHttpContext();
        http.Request.Scheme = "https";
        http.Request.Host = new HostString("acme.id.test");
        var page = new ForgotPasswordModel(users.Object, Mock.Of<IProtocolStateStore>(), settings.Object,
            NullLogger<ForgotPasswordModel>.Instance, messaging.Object)
        {
            Input = new ForgotPasswordInputModel { Email = lookalike },
            PageContext = new PageContext { HttpContext = http },
        };

        await page.OnPostAsync();

        var email = sent.Should().ContainSingle().Subject;
        email.To.Should().Be("jo@x.test");
        email.Body.Should().Contain("email=jo%40x.test").And.NotContain("%C4%B1");
        page.EmailSent.Should().BeTrue();
    }
}

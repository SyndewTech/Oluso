using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Oluso.UI.Pages.Account;
using Oluso.UserJourneys.Steps;
using Xunit;

namespace Oluso.Tests.UserJourneys;

/// <summary>
/// A correct but expired password never yields a session. In a journey, the local and composite
/// login steps switch to the password-change form and only authenticate once a new password is
/// accepted. On the standalone login page, the user is handed (via TempData, not the URL) to the
/// reset-password page, then signs in again with the new password.
/// </summary>
public class ExpiredPasswordSignInTests
{
    private static readonly ValidatedUser Jo = new() { Id = "u1", UserName = "jo", Email = "jo@x.test", TenantId = "t1" };
    private static readonly OlusoUserInfo JoInfo = new() { Id = "u1", Username = "jo", Email = "jo@x.test", EmailVerified = true, TenantId = "t1" };

    private readonly Dictionary<string, object> _journeyData = new();
    private readonly Mock<IOlusoUserService> _users = new();

    public ExpiredPasswordSignInTests()
    {
        _users.Setup(u => u.ValidateCredentialsAsync("jo", "old-password", It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(UserValidationResult.PasswordChangeRequired(Jo));
        _users.Setup(u => u.GeneratePasswordResetTokenAsync("u1", It.IsAny<CancellationToken>())).ReturnsAsync("reset-token");
        _users.Setup(u => u.FindByIdAsync("u1", It.IsAny<CancellationToken>())).ReturnsAsync(JoInfo);
    }

    private StepExecutionContext Context(string stepType, Dictionary<string, object> input)
    {
        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.TenantId).Returns("t1");
        var settings = new Mock<ITenantSettingsProvider>();
        settings.Setup(s => s.GetPasswordSettingsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(TenantPasswordSettings.Default);

        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(_users.Object)
            .AddSingleton(tenant.Object)
            .AddSingleton(settings.Object)
            .BuildServiceProvider();

        return new StepExecutionContext
        {
            JourneyId = "journey-1",
            StepId = "login",
            Configuration = new JourneyStepConfiguration { Id = "login", Type = stepType },
            ServiceProvider = services,
            JourneyData = _journeyData,
            Input = new JourneyStepInput { StepId = "login", Values = input }
        };
    }

    private static Dictionary<string, object> Credentials() => new() { ["username"] = "jo", ["password"] = "old-password" };

    private static Dictionary<string, object> NewPassword(string password, string? confirm = null) =>
        new() { ["newPassword"] = password, ["confirmPassword"] = confirm ?? password };

    public static IEnumerable<object[]> LoginSteps() => new[]
    {
        new object[] { new LocalLoginStepHandler() },
        new object[] { new CompositeLoginStepHandler() }
    };

    [Theory]
    [MemberData(nameof(LoginSteps))]
    public async Task An_expired_password_shows_the_change_form_without_authenticating(IStepHandler step)
    {
        var context = Context(step.StepType, Credentials());

        var result = await step.ExecuteAsync(context);

        result.StepResult!.ViewName.Should().Be("Journey/_PasswordChange");
        var model = result.StepResult.ViewModel.Should().BeOfType<PasswordChangeViewModel>().Subject;
        model.RequireCurrentPassword.Should().BeFalse();
        model.NoticeMessage.Should().NotBeNullOrEmpty();
        context.UserId.Should().BeNull();
        _journeyData[ExpiredPasswordChange.UserIdKey].Should().Be("u1");
        _users.Verify(u => u.RecordLoginAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Theory]
    [MemberData(nameof(LoginSteps))]
    public async Task Signing_in_completes_only_after_a_new_password_is_accepted(IStepHandler step)
    {
        await step.ExecuteAsync(Context(step.StepType, Credentials()));
        _users.Setup(u => u.ResetPasswordAsync("u1", "new-password-1", "reset-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(UserOperationResult.Success("u1"));

        var context = Context(step.StepType, NewPassword("new-password-1"));
        var result = await step.ExecuteAsync(context);

        result.Outcome.Should().NotBe(StepOutcome.RequireInput);
        result.Error.Should().BeNull();
        result.OutputData!["sub"].Should().Be("u1");
        context.UserId.Should().Be("u1");
        _journeyData.Should().NotContainKey(ExpiredPasswordChange.UserIdKey);
        _journeyData.Should().NotContainKey(ExpiredPasswordChange.TokenKey);
        _users.Verify(u => u.RecordLoginAsync("u1", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [MemberData(nameof(LoginSteps))]
    public async Task A_rejected_new_password_keeps_the_user_on_the_form_unauthenticated(IStepHandler step)
    {
        await step.ExecuteAsync(Context(step.StepType, Credentials()));
        _users.Setup(u => u.ResetPasswordAsync("u1", "old-password", "reset-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(UserOperationResult.Failed("Your new password must be different from your current password."));

        var context = Context(step.StepType, NewPassword("old-password"));
        var result = await step.ExecuteAsync(context);

        result.StepResult!.ViewName.Should().Be("Journey/_PasswordChange");
        ((PasswordChangeViewModel)result.StepResult.ViewModel!).ErrorMessage.Should().Contain("different from your current password");
        context.UserId.Should().BeNull();
        _journeyData[ExpiredPasswordChange.UserIdKey].Should().Be("u1");
    }

    [Fact]
    public async Task A_mismatched_confirmation_never_reaches_the_user_service()
    {
        var step = new LocalLoginStepHandler();
        await step.ExecuteAsync(Context(step.StepType, Credentials()));

        var result = await step.ExecuteAsync(Context(step.StepType, NewPassword("new-password-1", "new-password-2")));

        result.StepResult!.ViewName.Should().Be("Journey/_PasswordChange");
        _users.Verify(u => u.ResetPasswordAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static (HttpContext Http, Mock<IAuthenticationService> Auth) Http()
    {
        var auth = new Mock<IAuthenticationService>();
        var http = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection()
                .AddSingleton(auth.Object)
                .AddSingleton(Mock.Of<IExternalProviderConfigStore>(s =>
                    s.GetEnabledProvidersAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>())
                    == Task.FromResult<IReadOnlyList<ExternalProviderDefinition>>(Array.Empty<ExternalProviderDefinition>())))
                .BuildServiceProvider()
        };
        return (http, auth);
    }

    private static void Attach(PageModel page, HttpContext http, ITempDataDictionary tempData)
    {
        page.PageContext = new PageContext
        {
            HttpContext = http,
            ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary()),
        };
        page.TempData = tempData;
        var url = new Mock<IUrlHelper>();
        url.Setup(u => u.IsLocalUrl(It.IsAny<string?>())).Returns((string? u) => u != null && u.StartsWith('/'));
        page.Url = url.Object;
    }

    private const string ReturnUrl = "/oauth/authorize/callback?correlation_id=c1";

    [Fact]
    public async Task The_standalone_login_page_hands_an_expired_password_to_the_reset_page_without_a_session()
    {
        var (http, auth) = Http();
        var tempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>());
        var page = new LoginModel(_users.Object, Mock.Of<IProtocolStateStore>(), NullLogger<LoginModel>.Instance)
        {
            ReturnUrl = ReturnUrl,
            Input = new LoginInputModel { Username = "jo", Password = "old-password" }
        };
        Attach(page, http, tempData);

        var result = await page.OnPostAsync();

        var redirect = result.Should().BeOfType<RedirectToPageResult>().Subject;
        redirect.PageName.Should().Be("/Account/ResetPassword");
        redirect.RouteValues!["expired"].Should().Be(true);
        redirect.RouteValues["returnUrl"].Should().Be(ReturnUrl);
        ExpiredPasswordTicket.Peek(tempData).Should().Be(new ExpiredPasswordTicket("u1", "reset-token", "jo"));
        auth.Verify(a => a.SignInAsync(It.IsAny<HttpContext>(), It.IsAny<string?>(), It.IsAny<System.Security.Claims.ClaimsPrincipal>(), It.IsAny<AuthenticationProperties?>()), Times.Never);
        _users.Verify(u => u.RecordLoginAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static ITenantSettingsProvider TenantPolicy(TenantPasswordSettings policy)
    {
        var settings = new Mock<ITenantSettingsProvider>();
        settings.Setup(s => s.GetPasswordSettingsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(policy);
        return settings.Object;
    }

    [Fact]
    public async Task The_reset_page_shows_the_tenants_password_policy_not_identitys_permissive_defaults()
    {
        var page = new ResetPasswordModel(_users.Object,
            TenantPolicy(new TenantPasswordSettings { MinimumLength = 12, RequireDigit = true, RequireNonAlphanumeric = false }),
            NullLogger<ResetPasswordModel>.Instance) { Token = "t", Email = "a@b.c" };
        Attach(page, new DefaultHttpContext(), new TempDataDictionary(new DefaultHttpContext(), Mock.Of<ITempDataProvider>()));

        await page.OnGetAsync();

        page.MinLength.Should().Be(12);
        page.RequireDigit.Should().BeTrue();
        page.RequireNonAlphanumeric.Should().BeFalse();
    }

    private ResetPasswordModel ResetPage(ITempDataDictionary tempData, HttpContext http, string password, string? confirm = null)
    {
        var page = new ResetPasswordModel(_users.Object, TenantPolicy(new TenantPasswordSettings()), NullLogger<ResetPasswordModel>.Instance)
        {
            Expired = true,
            ReturnUrl = ReturnUrl,
            Input = new ResetPasswordInputModel { NewPassword = password, ConfirmPassword = confirm ?? password }
        };
        Attach(page, http, tempData);
        return page;
    }

    [Fact]
    public async Task The_reset_page_replaces_the_expired_password_and_sends_the_user_back_to_sign_in()
    {
        var (http, _) = Http();
        var tempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>());
        ExpiredPasswordTicket.Store(tempData, new ExpiredPasswordTicket("u1", "reset-token"));
        _users.Setup(u => u.ResetPasswordAsync("u1", "New-Passw0rd!", "reset-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(UserOperationResult.Success("u1"));
        var page = ResetPage(tempData, http, "New-Passw0rd!");

        (await page.OnPostAsync()).Should().BeOfType<PageResult>();

        page.PasswordReset.Should().BeTrue();
        page.SignInUrl.Should().Be($"/account/login?returnUrl={Uri.EscapeDataString(ReturnUrl)}");
        ExpiredPasswordTicket.Peek(tempData).Should().BeNull();
    }

    [Fact]
    public async Task A_password_the_policy_rejects_keeps_the_hand_off_for_another_try()
    {
        var (http, _) = Http();
        var tempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>());
        ExpiredPasswordTicket.Store(tempData, new ExpiredPasswordTicket("u1", "reset-token"));
        _users.Setup(u => u.ResetPasswordAsync("u1", "Old-Passw0rd!", "reset-token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(UserOperationResult.Failed("You can't reuse any of your last 5 passwords."));
        var page = ResetPage(tempData, http, "Old-Passw0rd!");

        (await page.OnPostAsync()).Should().BeOfType<PageResult>();

        page.PasswordReset.Should().BeFalse();
        page.ErrorMessage.Should().Contain("last 5 passwords");
        ExpiredPasswordTicket.Peek(tempData).Should().NotBeNull();
    }

    [Fact]
    public async Task Without_the_hand_off_the_reset_page_sends_the_user_back_to_sign_in()
    {
        var (http, _) = Http();
        var tempData = new TempDataDictionary(http, Mock.Of<ITempDataProvider>());
        var page = ResetPage(tempData, http, "New-Passw0rd!");

        var result = await page.OnPostAsync();

        result.Should().BeOfType<RedirectResult>().Which.Url
            .Should().StartWith($"/account/login?returnUrl={Uri.EscapeDataString(ReturnUrl)}&error=");
        _users.Verify(u => u.ResetPasswordAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

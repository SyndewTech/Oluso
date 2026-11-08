using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Oluso.UserJourneys.Steps;
using Xunit;

namespace Oluso.Tests.UserJourneys;

/// <summary>
/// The update-user step's email change: a new address gets a code, the old one
/// stays on the account until that code is entered, and only then is the new
/// address applied, as verified.
/// </summary>
public class UpdateUserEmailChangeTests
{
    private const string UserId = "u1";
    private const string OldEmail = "old@example.com";
    private const string NewEmail = "new@example.com";
    private const string CodeKey = "email_change_code";

    private readonly Mock<IOlusoUserService> _users = new();
    private readonly Mock<IEmailSender> _email = new();
    private readonly List<UpdateUserRequest> _updates = new();
    private readonly List<(string To, string Body)> _sent = new();
    private readonly Dictionary<string, object> _journeyData = new();
    private string _storedEmail = OldEmail;
    private bool _storedVerified = true;

    public UpdateUserEmailChangeTests()
    {
        _users.Setup(u => u.FindByIdAsync(UserId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new OlusoUserInfo
            {
                Id = UserId, Username = OldEmail, Email = _storedEmail, EmailVerified = _storedVerified,
                FirstName = "Jo", LastName = "Doe", TenantId = "t1"
            });
        _users.Setup(u => u.UpdateUserAsync(UserId, It.IsAny<UpdateUserRequest>(), It.IsAny<CancellationToken>()))
            .Callback((string _, UpdateUserRequest r, CancellationToken _) =>
            {
                _updates.Add(r);
                if (r.Email != null)
                {
                    _storedVerified = r.EmailVerified || string.Equals(r.Email, _storedEmail, StringComparison.OrdinalIgnoreCase) && _storedVerified;
                    _storedEmail = r.Email;
                }
            })
            .ReturnsAsync(UserOperationResult.Success(UserId));
        _email.Setup(e => e.SendAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback((string to, string _, string body, CancellationToken _) => _sent.Add((to, body)))
            .ReturnsAsync(EmailResult.Succeeded());
    }

    private StepExecutionContext Rig(Dictionary<string, object>? input, Dictionary<string, object>? settings = null)
    {
        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.TenantId).Returns("t1");
        var tenantSettings = new Mock<ITenantSettingsProvider>();
        tenantSettings.Setup(s => s.GetAccountEmailSettingsAsync("t1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantAccountEmailSettings { MfaCodeLifetimeMinutes = 10 });

        return new StepExecutionContext
        {
            JourneyId = "j1",
            StepId = "update",
            UserId = UserId,
            JourneyData = _journeyData,
            Configuration = new JourneyStepConfiguration { Id = "update", Type = "update_user", Settings = settings ?? new() },
            ServiceProvider = new ServiceCollection().AddLogging()
                .AddSingleton(_users.Object).AddSingleton(tenant.Object).AddSingleton(tenantSettings.Object)
                .AddSingleton(_email.Object).BuildServiceProvider(),
            Input = input == null ? null : new JourneyStepInput { StepId = "update", Values = input },
        };
    }

    private static Dictionary<string, object> Form(string email) => new()
    {
        ["_submitted"] = "true",
        ["firstName"] = "Joanna",
        ["lastName"] = "Doe",
        ["email"] = email,
    };

    private Task<StepHandlerResult> RunAsync(Dictionary<string, object>? input, Dictionary<string, object>? settings = null) =>
        new UpdateUserStepHandler().ExecuteAsync(Rig(input, settings));

    private string IssuedCode => (string)_journeyData[CodeKey];

    [Fact]
    public async Task A_new_email_gets_a_code_at_the_new_address_and_the_old_one_stays()
    {
        var result = await RunAsync(Form(NewEmail));

        result.Outcome.Should().Be(StepOutcome.RequireInput);
        result.StepResult!.ViewName.Should().Be("Journey/_EmailVerificationConfirm");
        _sent.Should().ContainSingle().Which.To.Should().Be(NewEmail);
        _sent[0].Body.Should().Contain(IssuedCode);
        _updates.Should().ContainSingle().Which.Email.Should().BeNull();
        _updates[0].FirstName.Should().Be("Joanna", "the rest of the profile is saved straight away");
        (_storedEmail, _storedVerified).Should().Be((OldEmail, true));
    }

    [Fact]
    public async Task The_new_email_is_applied_as_verified_once_the_code_is_entered()
    {
        await RunAsync(Form(NewEmail));

        var wrong = await RunAsync(new() { ["code"] = IssuedCode == "000000" ? "111111" : "000000" });
        wrong.Outcome.Should().Be(StepOutcome.RequireInput);
        ((EmailVerificationConfirmViewModel)wrong.StepResult!.ViewModel!).ErrorMessage.Should().Contain("Invalid code");
        _storedEmail.Should().Be(OldEmail);

        var result = await RunAsync(new() { ["code"] = IssuedCode });

        result.Outcome.Should().Be(StepOutcome.Continue);
        _updates.Last().Should().BeEquivalentTo(new { Email = NewEmail, EmailVerified = true }, o => o.ExcludingMissingMembers());
        (_storedEmail, _storedVerified).Should().Be((NewEmail, true));
        result.OutputData!["email"].Should().Be(NewEmail);
        result.OutputData["email_verified"].Should().Be(true);
        _journeyData.Should().NotContainKey(CodeKey);
    }

    [Fact]
    public async Task Too_many_wrong_codes_abandon_the_change()
    {
        await RunAsync(Form(NewEmail));
        var wrong = IssuedCode == "000000" ? "111111" : "000000";

        await RunAsync(new() { ["code"] = wrong });
        await RunAsync(new() { ["code"] = wrong });
        var result = await RunAsync(new() { ["code"] = wrong });

        result.Outcome.Should().Be(StepOutcome.Failed);
        result.Error.Should().Be("too_many_attempts");
        _storedEmail.Should().Be(OldEmail);
        _journeyData.Should().NotContainKey(CodeKey);
    }

    [Fact]
    public async Task An_expired_code_is_refused()
    {
        await RunAsync(Form(NewEmail));
        _journeyData["email_change_sent_at"] = DateTime.UtcNow.AddMinutes(-11).ToString("O");

        var result = await RunAsync(new() { ["code"] = IssuedCode });

        ((EmailVerificationConfirmViewModel)result.StepResult!.ViewModel!).ErrorMessage.Should().Contain("expired");
        _storedEmail.Should().Be(OldEmail);
    }

    [Fact]
    public async Task Resending_issues_a_fresh_code_to_the_new_address()
    {
        await RunAsync(Form(NewEmail));

        await RunAsync(new() { ["resend"] = "true" });

        _sent.Should().HaveCount(2).And.OnlyContain(s => s.To == NewEmail);
        _sent[1].Body.Should().Contain(IssuedCode);
        _storedEmail.Should().Be(OldEmail);
    }

    [Fact]
    public async Task Choosing_a_different_email_abandons_the_pending_change()
    {
        await RunAsync(Form(NewEmail));

        var result = await RunAsync(new() { ["change_email"] = "true" });

        result.StepResult!.ViewName.Should().Be("Journey/_UpdateUser");
        _journeyData.Should().NotContainKey(CodeKey);
        _storedEmail.Should().Be(OldEmail);
    }

    [Fact]
    public async Task An_address_used_by_another_account_is_refused_before_any_code_is_sent()
    {
        _users.Setup(u => u.FindByEmailAsync(NewEmail, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OlusoUserInfo { Id = "someone-else", Username = NewEmail, Email = NewEmail });

        var result = await RunAsync(Form(NewEmail));

        ((UpdateUserViewModel)result.StepResult!.ViewModel!).Fields.Single(f => f.Name == "email").Error.Should().NotBeNullOrEmpty();
        _sent.Should().BeEmpty();
        _updates.Should().BeEmpty();
    }

    [Fact]
    public async Task Without_verification_the_email_applies_directly_and_unverified()
    {
        var result = await RunAsync(Form(NewEmail), new() { ["requireEmailVerification"] = false });

        result.Outcome.Should().Be(StepOutcome.Continue);
        _sent.Should().BeEmpty();
        _updates.Should().ContainSingle().Which.Email.Should().Be(NewEmail);
        _updates[0].EmailVerified.Should().BeFalse();
    }

    [Fact]
    public async Task A_casing_only_change_is_the_same_mailbox_and_needs_no_code()
    {
        var result = await RunAsync(Form(OldEmail.ToUpperInvariant()));

        result.Outcome.Should().Be(StepOutcome.Continue);
        _sent.Should().BeEmpty();
        _storedVerified.Should().BeTrue();
    }
}

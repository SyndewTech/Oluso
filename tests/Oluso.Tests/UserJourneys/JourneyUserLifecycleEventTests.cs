using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Oluso.EntityFramework;
using Oluso.EntityFramework.Services;
using Oluso.Tests.Roles;
using Oluso.UserJourneys.Steps;
using Xunit;

namespace Oluso.Tests.UserJourneys;

/// <summary>
/// Journey steps that create or verify accounts, run against the real user
/// service: each fact reaches subscribers once, with the journey's client.
/// </summary>
public class JourneyUserLifecycleEventTests : IDisposable
{
    private const string Tenant = "tenant-a";
    private const string Client = "web-app";
    private const string Policy = "signup-policy";

    private readonly SqliteConnection _connection;
    private readonly TestTenantContext _tenant = new() { TenantId = Tenant };
    private readonly OlusoDbContext _context;
    private readonly CapturedEvents _captured = new();
    private readonly IdentityUserService _users;
    private readonly Dictionary<string, object> _journeyData = new();

    public JourneyUserLifecycleEventTests()
    {
        _connection = IdentityTestFactory.OpenSqliteConnection();
        _context = IdentityTestFactory.CreateSqliteContext(_connection, _tenant);
        _users = IdentityTestFactory.CreateUserService(_context, _tenant, _captured.Services);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private List<OlusoEvent> Raised => _captured.Raised;

    private StepExecutionContext Step(string type, Dictionary<string, object> settings, Dictionary<string, object> input) => new()
    {
        JourneyId = "journey-1",
        PolicyId = Policy,
        StepId = type,
        ClientId = Client,
        Configuration = new JourneyStepConfiguration { Id = type, Type = type, Settings = settings },
        ServiceProvider = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IOlusoUserService>(_users)
            .AddSingleton<ITenantContext>(_tenant)
            .AddSingleton(_captured.Service)
            .BuildServiceProvider(),
        JourneyData = _journeyData,
        Input = new JourneyStepInput { StepId = type, Values = input },
    };

    private void CodeSentTo(string email)
    {
        _journeyData["passwordless_email"] = email;
        _journeyData["passwordless_code"] = "123456";
        _journeyData["passwordless_sent_at"] = DateTime.UtcNow.ToString("O");
    }

    [Fact]
    public async Task Passwordless_auto_provisioning_raises_user_created_with_the_client_then_email_verified()
    {
        CodeSentTo("new@a.test");

        var result = await new PasswordlessEmailStepHandler().ExecuteAsync(Step("passwordless_email",
            new() { ["allowSignUp"] = true, ["autoProvision"] = true },
            new() { ["code"] = "123456" }));

        result.OutputData.Should().ContainKey("sub");
        Raised.Select(e => e.GetType()).Should().Equal(typeof(UserRegisteredEvent), typeof(UserEmailVerifiedEvent));
        var created = (UserRegisteredEvent)Raised[0];
        created.Email.Should().Be("new@a.test");
        created.ClientId.Should().Be(Client);
        created.TenantId.Should().Be(Tenant);
        ((UserEmailVerifiedEvent)Raised[1]).SubjectId.Should().Be(created.SubjectId);
    }

    [Fact]
    public async Task Passwordless_sign_in_verifies_an_unverified_email_once()
    {
        var user = await IdentityTestFactory.CreateUserAsync(_context, Tenant, "known@a.test");

        CodeSentTo("known@a.test");
        await new PasswordlessEmailStepHandler().ExecuteAsync(Step("passwordless_email", new(), new() { ["code"] = "123456" }));
        CodeSentTo("known@a.test");
        await new PasswordlessEmailStepHandler().ExecuteAsync(Step("passwordless_email", new(), new() { ["code"] = "123456" }));

        var verified = Raised.Should().ContainSingle().Which.Should().BeOfType<UserEmailVerifiedEvent>().Subject;
        verified.SubjectId.Should().Be(user.Id);
        verified.Email.Should().Be("known@a.test");
    }

    [Fact]
    public async Task Sign_up_raises_a_single_user_created_with_the_client_and_policy()
    {
        _journeyData["client_id"] = Client;

        await new SignUpStepHandler().ExecuteAsync(Step("signup",
            new() { ["requireEmailVerification"] = false },
            new()
            {
                ["email"] = "jo@a.test",
                ["password"] = "Passw0rd!",
                ["confirmPassword"] = "Passw0rd!",
            }));

        var created = Raised.Should().ContainSingle().Which.Should().BeOfType<UserRegisteredEvent>().Subject;
        created.Email.Should().Be("jo@a.test");
        created.ClientId.Should().Be(Client);
        created.PolicyId.Should().Be(Policy);
    }

    [Fact]
    public async Task Email_verification_completing_a_sign_up_raises_user_email_verified_once()
    {
        var user = await IdentityTestFactory.CreateUserAsync(_context, Tenant, "pending@a.test");
        _journeyData[SignUpStepHandler.PendingVerificationUserDataKey] = user.Id;
        _journeyData["email_verify_email"] = "pending@a.test";
        _journeyData["email_verify_code"] = "123456";
        _journeyData["email_verify_sent_at"] = DateTime.UtcNow.ToString("O");

        await new EmailVerificationStepHandler().ExecuteAsync(Step("email_verification", new(), new() { ["code"] = "123456" }));

        var verified = Raised.Should().ContainSingle().Which.Should().BeOfType<UserEmailVerifiedEvent>().Subject;
        verified.SubjectId.Should().Be(user.Id);
        verified.TenantId.Should().Be(Tenant);
    }
}

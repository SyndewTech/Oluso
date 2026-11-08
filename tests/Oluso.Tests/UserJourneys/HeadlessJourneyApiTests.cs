using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Core.Api;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Tests.Protocols;
using Oluso.Core.UserJourneys;
using Oluso.Dto.Journeys;
using Xunit;

namespace Oluso.Tests.UserJourneys;

/// <summary>
/// Any journey runs step by step through the public headless API. Authentication journeys
/// need a known client, a PKCE challenge and an allowed origin, and complete with an
/// authorization code bound to the client and challenge, never twice. Sessions continue only
/// through this API and only from the origin that started them.
/// </summary>
public class HeadlessJourneyApiTests
{
    private const string AppOrigin = "https://app.acme.test";
    private const string Challenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";

    private readonly InMemoryJourneyPolicyStore _policies = new();
    private readonly InMemoryJourneyStateStore _states = new();
    private readonly CodeIssuerKit _issuer = new();
    private List<AuthorizationCode> _codes => _issuer.Codes;
    private readonly FakeModuleAnswers _emrAnswers = new();

    public HeadlessJourneyApiTests()
    {
        _policies.SaveAsync(Policy("inline-signin", requiresAuthentication: true, ("login", "fake_login"), ("mfa", "fake_mfa"))).Wait();
        _policies.SaveAsync(Policy("survey", requiresAuthentication: false, ("q1", "fake_form"), ("q2", "fake_form"))).Wait();
        var moduleForm = Policy("intake", requiresAuthentication: false, ("upload", "fake_upload"));
        moduleForm.OwnerModule = "emr";
        _policies.SaveAsync(moduleForm).Wait();
    }

    private static JourneyPolicy Policy(string slug, bool requiresAuthentication, params (string Id, string Type)[] steps) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Slug = slug,
        Name = slug,
        TenantId = "acme",
        Type = requiresAuthentication ? JourneyType.SignIn : JourneyType.DataCollection,
        Enabled = true,
        RequiresAuthentication = requiresAuthentication,
        Steps = steps.Select((s, i) => new JourneyPolicyStep { Id = s.Id, Type = s.Type, DisplayName = s.Id, Order = i + 1 }).ToList()
    };

    private HeadlessJourneySessions _sessions = null!;

    /// <param name="bearer">The user whose access token the request presents, if any.</param>
    private PublicJourneyController Controller(string? origin = AppOrigin, string? bearer = null)
    {
        var services = new ServiceCollection().AddLogging()
            .AddSingleton<IAuthenticationService>(new FakeAccessTokens())
            .AddSingleton<IJourneyResultSink>(_emrAnswers)
            .BuildServiceProvider();
        var registry = new Mock<IStepHandlerRegistry>();
        registry.Setup(r => r.GetHandler("fake_login")).Returns(new FakeLogin());
        registry.Setup(r => r.GetHandler("fake_mfa")).Returns(new FakeMfa());
        registry.Setup(r => r.GetHandler("fake_form")).Returns(new FakeForm());
        registry.Setup(r => r.GetHandler("fake_upload")).Returns(new FakeUpload());
        var orchestrator = new DefaultJourneyOrchestrator(
            services, _policies, _states, registry.Object, new DefaultConditionEvaluator(),
            NullLogger<DefaultJourneyOrchestrator>.Instance);

        var tenant = new Mock<ITenantContext>();
        tenant.Setup(t => t.TenantId).Returns("acme");

        var http = new DefaultHttpContext { RequestServices = services };
        if (origin != null) http.Request.Headers.Origin = origin;
        if (bearer != null) http.Request.Headers.Authorization = $"Bearer {bearer}";

        var sessions = _sessions = new HeadlessJourneySessions(
            orchestrator, _policies, _states, _issuer.Issuer, new JourneyStepForms(Array.Empty<IJourneyStepFormDescriber>()),
            new HttpContextAccessor { HttpContext = http }, NullLogger<HeadlessJourneySessions>.Instance);

        return new PublicJourneyController(
            tenant.Object, _policies, orchestrator, _states, _issuer.Issuer, sessions,
            NullLogger<PublicJourneyController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };
    }

    private static JourneySessionResponse Session(IActionResult result) =>
        (JourneySessionResponse)((OkObjectResult)result).Value!;

    private static StartJourneyRequest SignIn(Dictionary<string, object?>? data = null) => new()
    {
        Data = data,
        ClientId = "spa",
        CodeChallenge = Challenge,
        CodeChallengeMethod = "S256"
    };

    private static Dictionary<string, object?> Input(params (string Key, object? Value)[] values) =>
        values.ToDictionary(v => v.Key, v => v.Value);

    [Fact]
    public async Task A_data_collection_journey_runs_step_by_step_to_completion()
    {
        var api = Controller();

        var started = Session(await api.StartJourney("survey", new StartJourneyRequest(), default));
        started.Status.Should().Be("in_progress");
        started.Step!.Type.Should().Be("fake_form");
        started.Step.Model!.Value.GetProperty("question").GetString().Should().Be("q1");

        var second = Session(await api.SubmitStep(started.SessionId, new JourneyStepRequest { Data = Input(("answer", "yes")) }, default));
        second.CurrentStep.Should().Be("q2");
        second.CompletedSteps.Should().Contain("q1");

        var done = Session(await api.SubmitStep(started.SessionId, new JourneyStepRequest { Data = Input(("answer", 42)) }, default));
        done.Completed.Should().BeTrue();
        done.Code.Should().BeNull();
        _codes.Should().BeEmpty();
    }

    [Fact]
    public async Task Inline_sign_in_with_mfa_completes_with_a_code_bound_to_the_client_and_challenge()
    {
        var api = Controller();

        var mfa = Session(await api.StartJourney("inline-signin", SignIn(Input(("username", "jo"), ("password", "pw"))), default));
        mfa.Status.Should().Be("in_progress");
        mfa.Step!.Type.Should().Be("fake_mfa");

        var done = Session(await api.SubmitStep(mfa.SessionId, new JourneyStepRequest { Data = Input(("code", "123456")) }, default));

        done.Completed.Should().BeTrue();
        done.Code.Should().NotBeNullOrEmpty();
        var code = _codes.Single();
        code.Code.Should().Be(done.Code);
        code.ClientId.Should().Be("spa");
        code.SubjectId.Should().Be("u1");
        code.CodeChallenge.Should().Be(Challenge);
        code.RedirectUri.Should().Be(DirectAuthorizationRequest.PostMessage);
        code.Scopes.Should().BeEquivalentTo("openid", "profile", "email");
        code.SessionId.Should().Be(done.SessionId, "the journey is the login session");
    }

    [Fact]
    public async Task A_wrong_password_keeps_the_step_with_its_error()
    {
        var retry = Session(await Controller().StartJourney("inline-signin", SignIn(Input(("username", "jo"), ("password", "nope"))), default));

        retry.Status.Should().Be("in_progress");
        retry.Step!.Type.Should().Be("fake_login");
        retry.Step.Model!.Value.GetProperty("errorMessage").GetString().Should().Be("Invalid username or password");
        _codes.Should().BeEmpty();
    }

    [Fact]
    public async Task A_completed_session_never_issues_a_second_code()
    {
        var api = Controller();
        var mfa = Session(await api.StartJourney("inline-signin", SignIn(Input(("username", "jo"), ("password", "pw"))), default));
        await api.SubmitStep(mfa.SessionId, new JourneyStepRequest { Data = Input(("code", "123456")) }, default);

        var again = Session(await api.SubmitStep(mfa.SessionId, new JourneyStepRequest { Data = Input(("code", "123456")) }, default));

        again.Status.Should().Be("completed");
        again.Code.Should().BeNull();
        _codes.Should().ContainSingle();
    }

    [Fact]
    public async Task An_authentication_journey_needs_a_client_and_a_pkce_challenge()
    {
        var api = Controller();

        (await api.StartJourney("inline-signin", new StartJourneyRequest { Data = Input(("username", "jo")) }, default))
            .Should().BeOfType<BadRequestObjectResult>();
        (await api.StartJourney("inline-signin", new StartJourneyRequest { ClientId = "spa", CodeChallenge = "short" }, default))
            .Should().BeOfType<BadRequestObjectResult>();
        (await api.StartJourney("inline-signin", new StartJourneyRequest { ClientId = "spa", CodeChallenge = Challenge, CodeChallengeMethod = "plain" }, default))
            .Should().BeOfType<BadRequestObjectResult>();
        (await api.StartJourney("inline-signin", new StartJourneyRequest { ClientId = "unknown", CodeChallenge = Challenge }, default))
            .Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task An_authentication_journey_asking_only_for_scopes_the_client_is_not_allowed_is_refused()
    {
        var request = SignIn();
        request.Scope = "permissions";

        var result = await Controller().StartJourney("inline-signin", request, default);

        result.Should().BeOfType<BadRequestObjectResult>();
        _codes.Should().BeEmpty();
    }

    [Fact]
    public async Task Scopes_the_client_is_not_allowed_are_left_out_of_the_journeys_code()
    {
        var api = Controller();
        var request = SignIn(Input(("username", "jo"), ("password", "pw")));
        request.Scope = "openid profile permissions";

        var mfa = Session(await api.StartJourney("inline-signin", request, default));
        var done = Session(await api.SubmitStep(mfa.SessionId, new JourneyStepRequest { Data = Input(("code", "123456")) }, default));

        done.Code.Should().NotBeNullOrEmpty();
        _codes.Single().Scopes.Should().Equal("openid", "profile");
    }

    [Fact]
    public async Task A_client_disabled_while_the_journey_runs_gets_no_code()
    {
        var api = Controller();
        var mfa = Session(await api.StartJourney("inline-signin", SignIn(Input(("username", "jo"), ("password", "pw"))), default));
        _issuer.Spa.Enabled = false;

        var done = Session(await api.SubmitStep(mfa.SessionId, new JourneyStepRequest { Data = Input(("code", "123456")) }, default));

        done.Code.Should().BeNull();
        done.Status.Should().Be("failed");
        done.Error.Should().Be("invalid_client");
        _codes.Should().BeEmpty();
    }

    [Fact]
    public async Task An_authentication_journey_only_runs_from_an_allowed_origin()
    {
        var foreign = await Controller("https://evil.test").StartJourney("inline-signin", SignIn(), default);
        var none = await Controller(origin: null).StartJourney("inline-signin", SignIn(), default);

        ((ObjectResult)foreign).StatusCode.Should().Be(403);
        ((ObjectResult)none).StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task A_session_continues_only_from_the_origin_that_started_it()
    {
        var started = Session(await Controller().StartJourney("survey", new StartJourneyRequest(), default));

        var hijack = await Controller("https://evil.test").SubmitStep(started.SessionId, new JourneyStepRequest { Data = Input(("answer", "x")) }, default);

        ((ObjectResult)hijack).StatusCode.Should().Be(403);
    }

    [Fact]
    public async Task Journeys_not_started_through_the_headless_api_cannot_be_driven_by_it()
    {
        var policy = await _policies.GetBySlugAsync("survey");
        var hosted = await _states.SaveAndReturnAsync(new JourneyState
        {
            Id = Guid.NewGuid().ToString(),
            TenantId = "acme",
            ClientId = "",
            PolicyId = policy!.Id,
            CurrentStepId = "q1",
            Status = JourneyStatus.InProgress,
            Data = new Dictionary<string, object>()
        });

        (await Controller().SubmitStep(hosted.Id, new JourneyStepRequest(), default)).Should().BeOfType<NotFoundObjectResult>();
        (await Controller().GetSession(hosted.Id, default)).Should().BeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task Get_session_reports_where_it_stands_without_running_it()
    {
        var api = Controller();
        var started = Session(await api.StartJourney("survey", new StartJourneyRequest(), default));

        var session = Session(await api.GetSession(started.SessionId, default));

        session.Status.Should().Be("in_progress");
        session.CurrentStep.Should().Be("q1");
        session.Step!.Model.Should().BeNull();
    }

    [Fact]
    public async Task A_module_starts_its_journey_for_a_user_and_only_that_users_token_continues_it()
    {
        Controller(bearer: "nurse-1");
        var started = await _sessions.StartForUserAsync(new UserJourneySessionStart(
            "acme", (await _policies.GetBySlugAsync("intake"))!.Id, "emr", "nurse-1",
            new Dictionary<string, string> { ["emr.patientId"] = "p1" }, AppOrigin));
        started.Step!.Type.Should().Be("fake_upload");

        (await Controller().SubmitStep(started.SessionId, new JourneyStepRequest(), default)).Should().BeOfType<NotFoundObjectResult>();
        (await Controller(bearer: "someone-else").SubmitStep(started.SessionId, new JourneyStepRequest(), default)).Should().BeOfType<NotFoundObjectResult>();
        (await Controller(origin: "https://evil.test", bearer: "nurse-1").GetSession(started.SessionId, default)).Should().BeOfType<ObjectResult>()
            .Which.StatusCode.Should().Be(403);

        var state = await _states.GetAsync(started.SessionId);
        state!.UserId.Should().Be("nurse-1");
        JourneyLaunchData.ReadContext(state.Data)!["emr.patientId"].Should().Be("p1");
    }

    [Fact]
    public async Task A_module_cannot_start_another_modules_journey()
    {
        Controller(bearer: "nurse-1");
        var start = () => _sessions.StartForUserAsync(new UserJourneySessionStart(
            "acme", _policies.GetBySlugAsync("intake").Result!.Id, "crm", "nurse-1", new Dictionary<string, string>(), AppOrigin));
        await start.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task An_app_starts_a_journey_for_its_signed_in_user_with_their_access_token()
    {
        var started = Session(await Controller(bearer: "u7").StartJourney("survey", new StartJourneyRequest(), default));
        (await _states.GetAsync(started.SessionId))!.UserId.Should().Be("u7");

        (await Controller().SubmitStep(started.SessionId, new JourneyStepRequest { Data = Input(("answer", "a")) }, default))
            .Should().BeOfType<NotFoundObjectResult>();
        Session(await Controller(bearer: "u7").SubmitStep(started.SessionId, new JourneyStepRequest { Data = Input(("answer", "a")) }, default))
            .CurrentStep.Should().Be("q2");
    }

    [Fact]
    public async Task A_token_that_does_not_validate_is_refused_rather_than_ignored()
    {
        (await Controller(bearer: "invalid").StartJourney("survey", new StartJourneyRequest(), default))
            .Should().BeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task A_step_posted_as_a_form_reaches_the_step_with_its_files()
    {
        Controller(bearer: "nurse-1");
        var started = await _sessions.StartForUserAsync(new UserJourneySessionStart(
            "acme", (await _policies.GetBySlugAsync("intake"))!.Id, "emr", "nurse-1", new Dictionary<string, string>(), AppOrigin));

        var api = Controller(bearer: "nurse-1");
        var request = api.HttpContext.Request;
        request.ContentType = "multipart/form-data; boundary=x";
        request.Form = new FormCollection(
            new Dictionary<string, Microsoft.Extensions.Primitives.StringValues> { ["note"] = "scan", ["__journey_action"] = "submit" },
            new FormFileCollection
            {
                new FormFile(new MemoryStream(new byte[] { 1, 2, 3 }), 0, 3, "document", "scan.pdf")
                {
                    Headers = new HeaderDictionary(), ContentType = "application/pdf"
                }
            });

        var done = Session(await api.SubmitStepForm(started.SessionId, default));
        done.Completed.Should().BeTrue();
        FakeUpload.Received.Should().Be("scan:document/scan.pdf/3:submit");
        _emrAnswers.Delivered.Should().ContainSingle().Which.UserId.Should().Be("nurse-1");
    }

    private sealed class FakeLogin : IStepHandler
    {
        public string StepType => "fake_login";

        public Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default)
        {
            var username = context.GetInput("username");
            if (username == null) return Task.FromResult(StepHandlerResult.ShowUi("Journey/_Login", new LoginModel()));
            if (username != "jo" || context.GetInput("password") != "pw")
            {
                return Task.FromResult(StepHandlerResult.ShowUi("Journey/_Login", new LoginModel { ErrorMessage = "Invalid username or password" }));
            }
            context.UserId = "u1";
            return Task.FromResult(StepHandlerResult.Success(new Dictionary<string, object> { ["sub"] = "u1" }));
        }
    }

    private sealed class FakeMfa : IStepHandler
    {
        public string StepType => "fake_mfa";

        public Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(context.GetInput("code") == "123456"
                ? StepHandlerResult.Success()
                : StepHandlerResult.ShowUi("Journey/_Mfa", new { provider = "totp" }));
    }

    private sealed class FakeForm : IStepHandler
    {
        public string StepType => "fake_form";

        public Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(context.GetInput("answer") is { } answer
                ? StepHandlerResult.Success(new Dictionary<string, object> { [context.StepId] = answer })
                : StepHandlerResult.ShowUi("Journey/_Form", new { question = context.StepId }));
    }

    private sealed class FakeModuleAnswers : IJourneyResultSink
    {
        public List<JourneyAnswers> Delivered { get; } = new();

        public string Module => "emr";

        public Task DeliverAsync(JourneyAnswers answers, CancellationToken cancellationToken = default)
        {
            Delivered.Add(answers);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeUpload : IStepHandler
    {
        public static string? Received;

        public string StepType => "fake_upload";

        public Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default)
        {
            if (context.GetInput("note") is not { } note) return Task.FromResult(StepHandlerResult.ShowUi("Journey/_Upload"));
            var file = context.InputFiles.Single();
            Received = $"{note}:{file.FieldName}/{file.FileName}/{file.Length}:{context.Input?.Action}";
            return Task.FromResult(StepHandlerResult.Success());
        }
    }

    /// <summary>Access tokens for tests: "Bearer {user}" signs in that user; "invalid" fails.</summary>
    private sealed class FakeAccessTokens : IAuthenticationService
    {
        public Task<AuthenticateResult> AuthenticateAsync(HttpContext context, string? scheme)
        {
            var header = context.Request.Headers.Authorization.ToString();
            if (!header.StartsWith("Bearer ")) return Task.FromResult(AuthenticateResult.NoResult());
            var user = header["Bearer ".Length..];
            if (user == "invalid") return Task.FromResult(AuthenticateResult.Fail("invalid token"));
            var principal = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("sub", user), new Claim("tenant_id", "acme") }, "test"));
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, scheme ?? "test")));
        }

        public Task ChallengeAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task ForbidAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task SignInAsync(HttpContext context, string? scheme, ClaimsPrincipal principal, AuthenticationProperties? properties) => Task.CompletedTask;
        public Task SignOutAsync(HttpContext context, string? scheme, AuthenticationProperties? properties) => Task.CompletedTask;
    }

    private sealed class LoginModel
    {
        public string? ErrorMessage { get; init; }
    }
}

internal static class JourneyStateStoreTestExtensions
{
    public static async Task<JourneyState> SaveAndReturnAsync(this IJourneyStateStore store, JourneyState state)
    {
        await store.SaveAsync(state);
        return state;
    }
}

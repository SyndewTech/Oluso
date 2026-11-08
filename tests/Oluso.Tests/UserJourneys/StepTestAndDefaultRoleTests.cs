using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Admin.Controllers;
using Oluso.Admin.Dto.Journeys;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Oluso.UserJourneys.Steps;
using Xunit;

namespace Oluso.Tests.UserJourneys;

/// <summary>
/// The builder's step Test runs the step's own check with its real (saved) secrets, and the
/// sign-up step's default roles are role ids, granted only within the organisation.
/// </summary>
public class StepTestAndDefaultRoleTests
{
    private const string Tenant = "acme";

    private static Mock<ITenantContext> TenantContext()
    {
        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.TenantId).Returns(Tenant);
        return tenant;
    }

    // ── test-step endpoint ────────────────────────────────────────────────

    private sealed class RecordingTester : IStepHandler, IStepConfigurationTester
    {
        public string StepType => "recording";
        public string? SecretSeen;

        public Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default) =>
            Task.FromResult(StepHandlerResult.Success());

        public Task<StepConfigurationTestResult> TestConfigurationAsync(StepExecutionContext context, CancellationToken cancellationToken = default)
        {
            SecretSeen = context.GetConfig<string>("secret");
            return Task.FromResult(StepConfigurationTestResult.Passed("ok", "checked"));
        }
    }

    private static (UserJourneyPoliciesController Controller, RecordingTester Tester) TestRig(JourneyPolicy? stored)
    {
        var tester = new RecordingTester();
        var services = new ServiceCollection().AddSingleton(tester);
        var provider = services.BuildServiceProvider();
        var registry = new DefaultStepHandlerRegistry(provider);
        registry.Register<RecordingTester>("recording", b => b.WithConfigurationSchema(new Dictionary<string, object>
        {
            ["secret"] = new Dictionary<string, object> { ["type"] = "string", ["x-control"] = "secret-input" },
        }));
        var resolver = new JourneyStepSecretResolver(registry);
        var requestServices = new ServiceCollection().AddSingleton<IJourneyStepSecretResolver>(resolver).BuildServiceProvider();

        var store = new Mock<IJourneyPolicyStore>();
        store.Setup(s => s.GetByIdAsync("p1", It.IsAny<CancellationToken>())).ReturnsAsync(stored);

        var controller = new UserJourneyPoliciesController(
            store.Object, TenantContext().Object, Mock.Of<IHtmlSanitizationService>(), Mock.Of<IIssuerResolver>(),
            Mock.Of<ITenantConfigurationService>(), NullLogger<UserJourneyPoliciesController>.Instance,
            stepRegistry: registry,
            secretProtector: new JourneyStepSecretProtector(resolver, new EphemeralDataProtectionProvider(), NullLogger<JourneyStepSecretProtector>.Instance))
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext { RequestServices = requestServices } },
        };
        return (controller, tester);
    }

    private static JourneyPolicy Policy(string? tenantId = Tenant) => new()
    {
        Id = "p1", Slug = "p1", Name = "P1", TenantId = tenantId,
        Steps = new List<JourneyPolicyStep>
        {
            new() { Id = "s1", Type = "recording", Configuration = new Dictionary<string, object> { ["secret"] = "stored-secret" } },
        },
    };

    private static async Task<StepTestResultDto> RunTestAsync(UserJourneyPoliciesController controller, TestStepRequest request) =>
        (StepTestResultDto)((OkObjectResult)(await controller.TestStepConfiguration(request)).Result!).Value!;

    [Fact]
    public async Task A_redacted_secret_is_tested_with_the_saved_value()
    {
        var (controller, tester) = TestRig(Policy());

        var result = await RunTestAsync(controller, new TestStepRequest
        {
            Type = "recording", PolicyId = "p1", StepId = "s1",
            Configuration = new Dictionary<string, object> { ["secret"] = JsonSerializer.SerializeToElement(IJourneyStepSecretProtector.RedactedValue) },
        });

        result.Success.Should().BeTrue();
        result.Details.Should().Equal("checked");
        tester.SecretSeen.Should().Be("stored-secret");
    }

    [Fact]
    public async Task A_secret_typed_in_the_builder_is_tested_as_typed()
    {
        var (controller, tester) = TestRig(Policy());

        await RunTestAsync(controller, new TestStepRequest
        {
            Type = "recording", PolicyId = "p1", StepId = "s1",
            Configuration = new Dictionary<string, object> { ["secret"] = JsonSerializer.SerializeToElement("new-secret") },
        });

        tester.SecretSeen.Should().Be("new-secret");
    }

    [Fact]
    public async Task An_unsaved_redacted_secret_asks_to_save_first_and_other_tenants_journeys_are_refused()
    {
        var (unsaved, tester) = TestRig(null);
        var redacted = new Dictionary<string, object> { ["secret"] = IJourneyStepSecretProtector.RedactedValue };

        (await RunTestAsync(unsaved, new TestStepRequest { Type = "recording", Configuration = redacted })).Message.Should().Contain("Save the journey");
        tester.SecretSeen.Should().BeNull();

        var (foreign, _) = TestRig(Policy(tenantId: "other"));
        (await foreign.TestStepConfiguration(new TestStepRequest { Type = "recording", PolicyId = "p1", StepId = "s1", Configuration = redacted }))
            .Result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task Steps_without_a_test_say_so()
    {
        var (controller, _) = TestRig(Policy());

        var result = await RunTestAsync(controller, new TestStepRequest { Type = "condition" });

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("isn't available");
    }

    // ── Step testers ──────────────────────────────────────────────────────

    private sealed class FakeHttp : HttpMessageHandler
    {
        public readonly List<HttpRequestMessage> Requests = new();
        public Func<HttpRequestMessage, HttpResponseMessage> Respond = _ => new HttpResponseMessage(HttpStatusCode.OK);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(Respond(request));
        }
    }

    private static StepExecutionContext StepContext(string type, Dictionary<string, object> settings, FakeHttp? http = null, Action<IServiceCollection>? services = null)
    {
        var collection = new ServiceCollection().AddLogging();
        if (http != null)
        {
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(http, false));
            collection.AddSingleton(factory.Object);
        }
        services?.Invoke(collection);
        return new StepExecutionContext
        {
            JourneyId = "test", StepId = "s1", TenantId = Tenant,
            Configuration = new JourneyStepConfiguration { Id = "s1", Type = type, Settings = settings },
            ServiceProvider = collection.BuildServiceProvider(),
        };
    }

    [Theory]
    [InlineData("invalid-input-secret", false)]
    [InlineData("invalid-input-response", true)]
    public async Task Captcha_test_asks_the_provider_whether_the_secret_is_valid(string errorCode, bool accepted)
    {
        var http = new FakeHttp
        {
            Respond = _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($$"""{"success":false,"error-codes":["{{errorCode}}"]}""", Encoding.UTF8, "application/json"),
            },
        };

        var result = await new CaptchaStepHandler().TestConfigurationAsync(StepContext("captcha",
            new() { ["provider"] = "hcaptcha", ["siteKey"] = "site", ["secretKey"] = "secret" }, http));

        result.Success.Should().Be(accepted);
        http.Requests.Single().RequestUri!.ToString().Should().Be("https://hcaptcha.com/siteverify");
    }

    [Fact]
    public async Task Api_call_test_says_it_sent_HEAD_and_that_a_405_left_the_call_unchecked()
    {
        var http = new FakeHttp { Respond = _ => new HttpResponseMessage(HttpStatusCode.MethodNotAllowed) };

        var result = await new ApiCallStepHandler().TestConfigurationAsync(StepContext("api_call",
            new() { ["url"] = "https://api.test/merchants", ["method"] = "POST" }, http));

        result.Success.Should().BeTrue();
        result.Message.Should().Be("The API is reachable, but the call couldn't be checked");
        result.Details.Should().Contain(d => d.StartsWith("HEAD https://api.test/merchants → 405 MethodNotAllowed") && d.Contains("POST"));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, true)]
    [InlineData(HttpStatusCode.MethodNotAllowed, true)]
    [InlineData(HttpStatusCode.NotFound, false)]
    [InlineData(HttpStatusCode.Unauthorized, false)]
    public async Task Api_call_test_only_asks_the_endpoint_for_headers(HttpStatusCode status, bool ok)
    {
        var http = new FakeHttp { Respond = _ => new HttpResponseMessage(status) };

        var result = await new ApiCallStepHandler().TestConfigurationAsync(StepContext("api_call",
            new() { ["url"] = "https://api.test/users", ["method"] = "POST" }, http));

        result.Success.Should().Be(ok);
        http.Requests.Single().Method.Should().Be(HttpMethod.Head, "the test must not create anything");
    }

    [Fact]
    public async Task Api_call_test_fails_when_no_client_credentials_token_comes_back()
    {
        var tokens = new Mock<IClientCredentialsTokenCache>();
        var settings = new Dictionary<string, object>
        {
            ["url"] = "https://api.test/users",
            ["authentication"] = JsonSerializer.SerializeToElement(new { type = "client_credentials", tokenEndpoint = "https://idp.test/token", clientId = "c", clientSecret = "s" }),
        };

        var result = await new ApiCallStepHandler().TestConfigurationAsync(StepContext("api_call", settings, new FakeHttp(), s => s.AddSingleton(tokens.Object)));

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("access token");
    }

    [Fact]
    public async Task External_login_test_reports_missing_and_disabled_providers()
    {
        var store = new Mock<IExternalProviderConfigStore>();
        store.Setup(s => s.GetEnabledProvidersAsync(true, It.IsAny<CancellationToken>())).ReturnsAsync(new List<ExternalProviderDefinition>
        {
            new() { Id = "1", Scheme = "google", ProviderType = "Google", ClientId = "", ClientSecret = "" },
        });
        store.Setup(s => s.GetBySchemeAsync("github", true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ExternalProviderDefinition { Id = "2", Scheme = "github", ProviderType = "GitHub", Enabled = false, ClientId = "", ClientSecret = "" });

        var result = await new ExternalLoginStepHandler().TestConfigurationAsync(StepContext("external_login",
            new() { ["providers"] = JsonSerializer.SerializeToElement(new[] { "google", "github", "nope" }) }, services: s => s.AddSingleton(store.Object)));

        result.Success.Should().BeFalse();
        result.Details.Should().Contain(d => d.Contains("google") && d.Contains("enabled"))
            .And.Contain("github is disabled")
            .And.Contain("nope doesn't exist");
    }

    // ── Sign-up default roles ─────────────────────────────────────────────

    [Fact]
    public async Task Sign_up_grants_default_roles_by_id_and_only_within_the_organisation()
    {
        var users = new Mock<IOlusoUserService>();
        users.Setup(u => u.CreateUserAsync(It.IsAny<CreateUserRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(UserOperationResult.Success("u1", new OlusoUserInfo { Id = "u1", Username = "jo@example.com", Email = "jo@example.com" }));
        var roles = new Mock<IRoleStore>();
        roles.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>())).ReturnsAsync(new List<OlusoRole>
        {
            new() { Id = "r-member", Name = "Member", TenantId = Tenant },
            new() { Id = "r-platform", Name = "Reader", TenantId = null },
            new() { Id = "r-foreign", Name = "Admin", TenantId = "other" },
        });
        var grants = new Mock<IRoleGrantService>();
        var context = new StepExecutionContext
        {
            JourneyId = "j1", StepId = "signup", TenantId = Tenant,
            Configuration = new JourneyStepConfiguration
            {
                Id = "signup", Type = "signup",
                Settings = new Dictionary<string, object>
                {
                    ["requireEmailVerification"] = false,
                    ["defaultRoles"] = JsonSerializer.SerializeToElement(new[] { "r-member", "r-platform", "r-foreign", "r-missing" }),
                },
            },
            ServiceProvider = new ServiceCollection().AddLogging()
                .AddSingleton(users.Object).AddSingleton(TenantContext().Object).AddSingleton(roles.Object).AddSingleton(grants.Object)
                .BuildServiceProvider(),
            Input = new JourneyStepInput
            {
                StepId = "signup",
                Values = new Dictionary<string, object> { ["email"] = "jo@example.com", ["password"] = "Passw0rd!", ["confirmPassword"] = "Passw0rd!" },
            },
        };

        await new SignUpStepHandler().ExecuteAsync(context);

        grants.Verify(g => g.GrantAsync("r-member", "u1", null, It.IsAny<RoleGrantActor>(), It.IsAny<CancellationToken>()), Times.Once);
        grants.Verify(g => g.GrantAsync("r-platform", "u1", null, It.IsAny<RoleGrantActor>(), It.IsAny<CancellationToken>()), Times.Once);
        grants.Verify(g => g.GrantAsync("r-foreign", It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<RoleGrantActor>(), It.IsAny<CancellationToken>()), Times.Never);
        roles.Verify(r => r.GetByNameAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static (StepExecutionContext Context, Mock<IRoleGrantService> Grants) AssignRolesContext(string? userId, params string[] roleIds)
    {
        var roles = new Mock<IRoleStore>();
        roles.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IEnumerable<string> ids, CancellationToken _) => new List<OlusoRole>
            {
                new() { Id = "r-owner", Name = "MerchantOwner", DisplayName = "Merchant owner", TenantId = Tenant },
                new() { Id = "r-platform", Name = "Reader", TenantId = null },
                new() { Id = "r-foreign", Name = "Admin", TenantId = "other" },
            }.Where(r => ids.Contains(r.Id)).ToList());
        var grants = new Mock<IRoleGrantService>();
        var context = StepContext("assign_roles",
            new() { ["roles"] = JsonSerializer.SerializeToElement(roleIds) },
            services: s => s.AddSingleton(TenantContext().Object).AddSingleton(roles.Object).AddSingleton(grants.Object));
        context.UserId = userId;
        return (context, grants);
    }

    [Fact]
    public async Task Assign_roles_gives_the_journeys_user_the_chosen_roles()
    {
        var (context, grants) = AssignRolesContext("u1", "r-owner", "r-platform");

        var result = await new AssignRolesStepHandler().ExecuteAsync(context);

        result.Outcome.Should().Be(StepOutcome.Continue);
        grants.Verify(g => g.GrantAsync("r-owner", "u1", null, It.IsAny<RoleGrantActor>(), It.IsAny<CancellationToken>()), Times.Once);
        grants.Verify(g => g.GrantAsync("r-platform", "u1", null, It.IsAny<RoleGrantActor>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(null, "r-owner")]
    [InlineData("u1", "r-foreign")]
    [InlineData("u1", "r-missing")]
    public async Task Assign_roles_fails_without_a_user_or_with_a_role_the_organisation_cannot_grant(string? userId, string roleId)
    {
        var (context, grants) = AssignRolesContext(userId, "r-owner", roleId);

        var result = await new AssignRolesStepHandler().ExecuteAsync(context);

        result.Outcome.Should().Be(StepOutcome.Failed);
        grants.Verify(g => g.GrantAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<RoleGrantActor>(), It.IsAny<CancellationToken>()), Times.Never,
            "nothing is granted unless every role can be");
    }

    [Fact]
    public async Task Assign_roles_test_names_roles_that_cannot_be_assigned()
    {
        var (context, _) = AssignRolesContext(null, "r-owner", "r-foreign");

        var result = await new AssignRolesStepHandler().TestConfigurationAsync(context);

        result.Success.Should().BeFalse();
        result.Details.Should().Contain("Merchant owner will be assigned").And.Contain("r-foreign isn't a role of this organisation");
    }

    [Fact]
    public async Task Saving_a_journey_rejects_default_roles_that_are_not_the_organisations_role_ids()
    {
        var roles = new Mock<IRoleStore>();
        roles.Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OlusoRole> { new() { Id = "r-member", Name = "Member", TenantId = Tenant } });
        var controller = new UserJourneyPoliciesController(
            Mock.Of<IJourneyPolicyStore>(), TenantContext().Object, Mock.Of<IHtmlSanitizationService>(), Mock.Of<IIssuerResolver>(),
            Mock.Of<ITenantConfigurationService>(), NullLogger<UserJourneyPoliciesController>.Instance,
            stepRegistry: new DefaultStepHandlerRegistry(new ServiceCollection().BuildServiceProvider()))
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { RequestServices = new ServiceCollection().AddSingleton(roles.Object).BuildServiceProvider() },
            },
        };

        var result = (ValidationResultDto)((OkObjectResult)(await controller.ValidatePolicy(new ValidatePolicyRequest
        {
            Steps = new List<JourneyPolicyStepDto>
            {
                new()
                {
                    Id = "signup", Type = "signup", Order = 1,
                    Configuration = JsonSerializer.Deserialize<Dictionary<string, object>>("""{ "requireEmailVerification": false, "defaultRoles": ["r-member", "Member"] }""")!,
                },
            },
        })).Result!).Value!;

        result.Errors.Should().ContainSingle(e => e.Contains("'Member'"));
    }
}

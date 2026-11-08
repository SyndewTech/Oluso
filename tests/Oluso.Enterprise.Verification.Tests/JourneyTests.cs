using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Oluso.Enterprise.Verification.Controllers;
using Oluso.Enterprise.Verification.Events;
using Oluso.Enterprise.Verification.Journey;
using Oluso.Enterprise.Verification.Services;
using Xunit;

namespace Oluso.Enterprise.Verification.Tests;

/// <summary>Signs a user up when posted <c>sign_up</c>; shows a page until then.</summary>
internal sealed class TestSignUpStep : IStepHandler
{
    public string StepType => "test_signup";

    public Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (context.GetInput("sign_up") is { } user)
        {
            context.SetAuthenticated(user, "pwd");
            return Task.FromResult(StepHandlerResult.Success());
        }
        return Task.FromResult(StepHandlerResult.ShowUi("signup"));
    }
}

/// <summary>A page the journey stops on after verification.</summary>
internal sealed class TestNextStep : IStepHandler
{
    public string StepType => "test_next";

    public Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default) =>
        Task.FromResult(StepHandlerResult.ShowUi("next"));
}

/// <summary>The business_verification step running inside the journey engine.</summary>
public class JourneyTests : IDisposable
{
    private const string Tenant = VerificationHarness.Tenant;

    private readonly ScriptedBusinessProvider _lookup = new();
    private readonly ScriptedHostedProvider _hosted = new();
    private readonly InMemoryJourneyPolicyStore _policies = new();
    private readonly VerificationHarness _h;

    public JourneyTests()
    {
        _h = new VerificationHarness(services =>
        {
            services.AddSingleton<IJourneyPolicyStore>(_policies);
            services.AddScoped<DefaultStepHandlerRegistry>();
            services.AddScoped<IStepHandlerRegistry>(sp => sp.GetRequiredService<DefaultStepHandlerRegistry>());
            services.AddSingleton<IConditionEvaluator, DefaultConditionEvaluator>();
            services.AddScoped<IJourneyOrchestrator, DefaultJourneyOrchestrator>();
            services.AddSingleton<IStepHandler, TestSignUpStep>();
            services.AddSingleton<IStepHandler, TestNextStep>();
            services.AddScoped<BusinessVerificationStepHandler>();
            services.AddSingleton<IConfigureStepHandlers, BusinessVerificationStepConfiguration>();
            services.AddScoped<IJourneyUserAttachedHandler, BusinessApplicantJourneyLinker>();
        }, _lookup, _hosted);

        _lookup.Answer = _ => ScriptedBusinessProvider.Found("ACME FOODS LIMITED", BusinessActivityStatus.Active, new BusinessPerson("JOHN MUSA", "Director"));
        _hosted.Answer = _lookup.Answer;
        var settings = VerificationHarness.Routed("scripted");
        settings.Providers["hosted"] = new VerificationProviderSettings { Enabled = true, Credentials = { ["key"] = "secret" } };
        _h.Settings.ByTenant[Tenant] = settings;
        _h.Users.Setup(u => u.FindByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync((OlusoUserInfo?)null);

        var http = _h.Services.GetRequiredService<IHttpContextAccessor>();
        http.HttpContext = new DefaultHttpContext();
        http.HttpContext.Request.Scheme = "https";
        http.HttpContext.Request.Host = new HostString("acme.test");
    }

    public void Dispose() => _h.Dispose();

    private void Policy(Dictionary<string, object>? kyb = null, Dictionary<string, string>? branches = null)
    {
        _policies.SaveAsync(new JourneyPolicy
        {
            Id = "policy-1",
            Slug = "merchant",
            Name = "Merchant sign-up",
            TenantId = Tenant,
            Steps = new List<JourneyPolicyStep>
            {
                new() { Id = "kyb", Type = BusinessVerificationStepHandler.StepTypeName, Order = 1, Configuration = kyb ?? Settings(), Branches = branches },
                new() { Id = "signup", Type = "test_signup", Order = 2 },
                new() { Id = "done", Type = "test_next", Order = 3 },
            },
        }).GetAwaiter().GetResult();
    }

    private static Dictionary<string, object> Settings(params (string Key, object Value)[] extra)
    {
        var settings = new Dictionary<string, object> { ["allowedCountries"] = new List<string> { "NG" } };
        foreach (var (key, value) in extra) settings[key] = value;
        return settings;
    }

    private async Task<string> StartAsync(string? userId = null, Dictionary<string, object>? data = null)
    {
        var id = Guid.NewGuid().ToString("N");
        await _h.Journeys.SaveAsync(new JourneyState
        {
            Id = id,
            TenantId = Tenant,
            ClientId = "client",
            UserId = userId,
            PolicyId = "policy-1",
            CurrentStepId = "kyb",
            Status = JourneyStatus.InProgress,
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            Data = data ?? new Dictionary<string, object>(),
        });
        return id;
    }

    private async Task<JourneyResult> ContinueAsync(string journeyId, Dictionary<string, object>? values = null, string? action = null, IReadOnlyList<JourneyInputFile>? files = null)
    {
        using var scope = _h.Scope();
        var state = await _h.Journeys.GetAsync(journeyId);
        return await scope.ServiceProvider.GetRequiredService<IJourneyOrchestrator>().ContinueJourneyAsync(journeyId, new JourneyStepInput
        {
            StepId = state!.CurrentStepId,
            Values = values,
            Action = action,
            Files = files,
        });
    }

    private static Dictionary<string, object> Form(string number = "RC1234567", string name = "Acme Foods Ltd") => new()
    {
        ["action"] = "submit",
        ["country"] = "NG",
        ["registrationNumber"] = number,
        ["businessName"] = name,
    };

    private void RouteHosted() => _h.Settings.ByTenant[Tenant].BusinessRouting["NG"] = "hosted";

    // ── registration ───────────────────────────────────────────────────────

    [Fact]
    public void The_step_appears_only_when_the_module_is_added()
    {
        var core = new DefaultStepHandlerRegistry(new ServiceCollection().BuildServiceProvider());
        core.GetRegisteredTypes().Should().NotContain(t => t.Type == BusinessVerificationStepHandler.StepTypeName);
        core.GetHandler(BusinessVerificationStepHandler.StepTypeName).Should().BeNull();

        using var scope = _h.Scope();
        var registry = scope.ServiceProvider.GetRequiredService<DefaultStepHandlerRegistry>();
        var info = registry.GetRegisteredTypes().Single(t => t.Type == BusinessVerificationStepHandler.StepTypeName);
        info.Category.Should().Be("Verification");
        info.Module.Should().Be(BusinessVerificationStepConfiguration.ModuleName);
        info.AvailableBranches!.Select(b => b.Id).Should().Equal("verified", "not_verified", "pending", "provider_unavailable");
        info.ConfigurationSchema.Should().ContainKeys("allowedCountries", "mode", "applicantEvidence", "documentTypes", "maxDocumentSizeMb", "allowRetry", "pendingWaitSeconds", "registrationNumberClaim");
        registry.GetHandler(BusinessVerificationStepHandler.StepTypeName).Should().BeOfType<BusinessVerificationStepHandler>();
    }

    [Fact]
    public void AddOlusoVerification_registers_the_step_services_and_permissions()
    {
        var services = new ServiceCollection();
        services.AddOluso(new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build()).AddUserJourneyEngine().AddOlusoVerification();
        services.AddOlusoVerificationForProvider("sqlite", "Data Source=:memory:");

        services.Should().Contain(d => d.ServiceType == typeof(IConfigureStepHandlers) && d.ImplementationType == typeof(BusinessVerificationStepConfiguration));
        services.Should().Contain(d => d.ServiceType == typeof(IJourneyUserAttachedHandler) && d.ImplementationType == typeof(BusinessApplicantJourneyLinker));
        services.Should().Contain(d => d.ServiceType == typeof(IBusinessVerificationService));
        services.Should().Contain(d => d.ServiceType == typeof(Stores.IBusinessVerificationStore));
        Oluso.Core.Authorization.PermissionRegistry.Instance.GetPluginPermissions().Select(p => p.Name)
            .Should().Contain(new[] { VerificationPermissions.Read, VerificationPermissions.Review, VerificationPermissions.Manage });
        FluentActions.Invoking(() => new ServiceCollection().AddOlusoVerificationForProvider("oracle", "x"))
            .Should().Throw<ArgumentException>("an unknown provider fails fast instead of falling back to SQLite");
    }

    // ── lookup ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_verified_business_continues_to_the_next_step_with_its_claims()
    {
        Policy();
        var id = await StartAsync();

        var form = await ContinueAsync(id);
        form.CurrentStep!.ViewName.Should().Be(BusinessVerificationStepHandler.FormView);

        var result = await ContinueAsync(id, Form());

        result.CurrentStep!.ViewName.Should().Be("signup");
        var data = (await _h.Journeys.GetAsync(id))!.Data!;
        data["kyb_status"].Should().Be("verified");
        data["kyb_business_name"].Should().Be("ACME FOODS LIMITED");
        data["kyb_relationship"].Should().Be(BusinessApplicantRelationships.AuthorizedRepresentativeDeclared);
    }

    [Fact]
    public async Task A_link_made_before_sign_up_belongs_to_the_user_the_same_journey_signs_up()
    {
        Policy();
        var id = await StartAsync();
        await ContinueAsync(id, Form());

        var result = await ContinueAsync(id, new Dictionary<string, object> { ["sign_up"] = "new-user" });

        result.CurrentStep!.ViewName.Should().Be("next");
        using var scope = _h.Scope();
        var links = await _h.Store(scope).ListApplicantsForSubjectAsync(Tenant, "new-user");
        links.Should().ContainSingle().Which.Status.Should().Be(BusinessVerificationStatus.Verified);
        _h.Events<BusinessApplicantLinkedEvent>().Should().ContainSingle().Which.SubjectId.Should().Be("new-user");
    }

    // ── resuming ───────────────────────────────────────────────────────────

    [Fact]
    public async Task A_verified_link_is_reused_on_a_later_journey_without_a_new_lookup()
    {
        Policy(Settings(("registrationNumberClaim", "rc"), ("businessNameClaim", "bn")));
        await ContinueAsync(await StartAsync("user-1"), Form());
        _lookup.Lookups.Should().HaveCount(1);

        var later = await StartAsync("user-1", new Dictionary<string, object> { ["rc"] = "RC1234567", ["bn"] = "Acme Foods" });
        var result = await ContinueAsync(later);

        result.CurrentStep!.ViewName.Should().Be("signup");
        _lookup.Lookups.Should().HaveCount(1, "the user's verified link is resumed");
    }

    [Fact]
    public async Task An_in_review_link_waits_on_re_entry_or_takes_the_pending_branch()
    {
        Policy(Settings(("applicantEvidence", "documents")));
        var id = await StartAsync("user-1");
        var pdf = Upload.File(BusinessVerificationStepHandler.DocumentsField, "letter.pdf", Upload.Pdf);
        var submitted = await ContinueAsync(id, Form(), files: new[] { pdf });
        submitted.CurrentStep!.ViewName.Should().Be(BusinessVerificationStepHandler.WaitingView);
        var waiting = (BusinessVerificationWaitingViewModel)submitted.CurrentStep.ViewModel!;
        waiting.ReasonKey.Should().Be("business_verification_waiting_review");

        var again = await ContinueAsync(await StartAsync("user-1"), Form());
        again.CurrentStep!.ViewName.Should().Be(BusinessVerificationStepHandler.WaitingView, "the same user's link is still in review");
        _lookup.Lookups.Should().HaveCount(1);

        Policy(Settings(("applicantEvidence", "documents")), new Dictionary<string, string> { ["pending"] = "done" });
        var branched = await ContinueAsync(await StartAsync("user-1"), Form());
        branched.CurrentStep!.ViewName.Should().Be("next");
    }

    [Fact]
    public async Task A_rejected_link_shows_the_reason_and_a_retry_that_checks_again()
    {
        Policy();
        var id = await StartAsync("user-1");
        var rejected = await ContinueAsync(id, Form(name: "Zenith Foods"));
        rejected.CurrentStep!.ViewName.Should().Be(BusinessVerificationStepHandler.RejectedView);
        var model = (BusinessVerificationRejectedViewModel)rejected.CurrentStep.ViewModel!;
        model.ReasonKey.Should().Be("business_verification_error_name_mismatch");
        model.AllowRetry.Should().BeTrue();

        var reentered = await ContinueAsync(id);
        reentered.CurrentStep!.ViewName.Should().Be(BusinessVerificationStepHandler.RejectedView);

        var form = await ContinueAsync(id, new Dictionary<string, object> { ["action"] = "start_over" });
        form.CurrentStep!.ViewName.Should().Be(BusinessVerificationStepHandler.FormView);
        ((BusinessVerificationViewModel)form.CurrentStep.ViewModel!).RegistrationNumber.Should().Be("RC1234567");

        var fixedUp = await ContinueAsync(id, Form(name: "Acme Foods"));
        fixedUp.CurrentStep!.ViewName.Should().Be("signup");
        _lookup.Lookups.Should().HaveCount(2);
    }

    [Fact]
    public async Task Without_retries_a_rejected_link_is_shown_without_checking_again()
    {
        Policy(Settings(("allowRetry", false)));
        await ContinueAsync(await StartAsync("user-1"), Form(name: "Zenith Foods"));

        var again = await ContinueAsync(await StartAsync("user-1"), Form(name: "Acme Foods"));

        again.CurrentStep!.ViewName.Should().Be(BusinessVerificationStepHandler.RejectedView);
        ((BusinessVerificationRejectedViewModel)again.CurrentStep.ViewModel!).AllowRetry.Should().BeFalse();
        _lookup.Lookups.Should().HaveCount(1);
    }

    [Fact]
    public async Task A_rejection_takes_the_not_verified_branch_when_there_is_one()
    {
        Policy(branches: new Dictionary<string, string> { ["not_verified"] = "done" });

        var result = await ContinueAsync(await StartAsync("user-1"), Form(name: "Zenith Foods"));

        result.CurrentStep!.ViewName.Should().Be("next");
    }

    // ── documents ──────────────────────────────────────────────────────────

    [Fact]
    public async Task Documents_are_checked_by_content_and_size()
    {
        Policy(Settings(("applicantEvidence", "documents"), ("maxDocumentSizeMb", 1)));
        var id = await StartAsync("user-1");

        var html = await ContinueAsync(id, Form(), files: new[] { Upload.File("documents", "letter.pdf", Upload.Html) });
        ((BusinessVerificationViewModel)html.CurrentStep!.ViewModel!).ErrorKey.Should().Be("journey_upload_error_file_type_not_allowed");

        var renamed = await ContinueAsync(id, Form(), files: new[] { Upload.File("documents", "letter.pdf", Upload.Jpeg) });
        ((BusinessVerificationViewModel)renamed.CurrentStep!.ViewModel!).ErrorKey.Should().Be("journey_upload_error_file_extension_mismatch");

        var big = new JourneyInputFile("documents", "letter.pdf", "application/pdf", 2 * 1024 * 1024, () => new MemoryStream(Upload.Pdf));
        var tooLarge = await ContinueAsync(id, Form(), files: new[] { big });
        ((BusinessVerificationViewModel)tooLarge.CurrentStep!.ViewModel!).ErrorKey.Should().Be("journey_upload_error_file_too_large");

        _h.Attachments.Rows.Should().BeEmpty();
        _lookup.Lookups.Should().BeEmpty("nothing is bought for a submission with bad files");

        var photo = await ContinueAsync(id, Form(), files: new[] { Upload.File("documents", "letter.jpg", Upload.Jpeg) });
        photo.CurrentStep!.ViewName.Should().Be(BusinessVerificationStepHandler.WaitingView);
        _h.Attachments.Rows.Values.Should().ContainSingle().Which.ContentType.Should().Be("image/jpeg");
    }

    [Fact]
    public async Task A_document_request_asks_for_files_then_accepts_them()
    {
        Policy(Settings(("applicantEvidence", "director_or_documents")));
        _h.Users.Setup(u => u.FindByIdAsync("user-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OlusoUserInfo { Id = "user-1", Username = "ada", FirstName = "Ada", LastName = "Eze", Email = "ada@acme.test" });
        var id = await StartAsync("user-1");

        var asked = await ContinueAsync(id, Form());
        var form = (BusinessVerificationViewModel)asked.CurrentStep!.ViewModel!;
        form.ErrorKey.Should().Be("business_verification_documents_required");
        form.ShowDocuments.Should().BeTrue();

        var sent = await ContinueAsync(id, Form(), files: new[] { Upload.File("documents", "letter.pdf", Upload.Pdf) });
        sent.CurrentStep!.ViewName.Should().Be(BusinessVerificationStepHandler.WaitingView);
        _lookup.Lookups.Should().ContainSingle();
    }

    // ── hosted sessions ────────────────────────────────────────────────────

    [Fact]
    public async Task A_hosted_session_redirects_out_and_resumes_through_the_callback()
    {
        RouteHosted();
        Policy(Settings(("mode", "hosted")));
        var id = await StartAsync();

        var started = await ContinueAsync(id, Form());

        started.CurrentStep!.ViewName.Should().Be("_Redirect");
        started.CurrentStep.ViewData!["redirectUrl"].Should().Be($"https://provider.test/kyb/sess-1?return={Uri.EscapeDataString($"https://acme.test/journey/{id}/callback")}");
        _hosted.Started.Single().ReturnUrl.Should().Be($"https://acme.test/journey/{id}/callback");
        _hosted.Lookups.Should().BeEmpty();

        _hosted.Results["sess-1"] = new HostedSessionResult
        {
            State = HostedSessionState.Approved,
            ProviderReference = "dj-1",
            Record = new BusinessRegistryRecord { RegisteredName = "ACME FOODS LIMITED", RegistrationNumber = "RC1234567", ActivityStatus = BusinessActivityStatus.Active },
        };
        var back = await ContinueAsync(id, new Dictionary<string, object> { ["reference_id"] = "forged" }, action: "callback");

        back.CurrentStep!.ViewName.Should().Be("signup");
        var data = (await _h.Journeys.GetAsync(id))!.Data!;
        data["kyb_relationship"].Should().Be(BusinessApplicantRelationships.ProviderVerified);
        data["kyb_business_name"].Should().Be("ACME FOODS LIMITED");
    }

    [Fact]
    public async Task The_waiting_page_continues_once_the_webhook_decides_the_session()
    {
        RouteHosted();
        Policy(Settings(("mode", "hosted")));
        var id = await StartAsync();
        await ContinueAsync(id, Form());

        var returned = await ContinueAsync(id, action: "callback");
        returned.CurrentStep!.ViewName.Should().Be(BusinessVerificationStepHandler.WaitingView);
        var waiting = (BusinessVerificationWaitingViewModel)returned.CurrentStep.ViewModel!;
        waiting.ReasonKey.Should().Be("business_verification_waiting_provider");
        waiting.CanResumeHosted.Should().BeTrue();
        var revisionBefore = JourneyStatusRevision.Read((await _h.Journeys.GetAsync(id))!.Data);

        _hosted.Results["sess-1"] = new HostedSessionResult { State = HostedSessionState.Approved };
        var response = await WebhookAsync("hosted", "sess-1", ScriptedHostedProvider.Secret);
        response.Should().BeOfType<OkResult>();

        var state = await _h.Journeys.GetAsync(id);
        JourneyStatusRevision.Read(state!.Data).Should().Be(revisionBefore + 1, "the page polling /status sees the change and reloads");
        state.CurrentStepId.Should().Be("kyb", "the webhook doesn't run the journey outside the browser's request");

        var reloaded = await ContinueAsync(id, action: "init");
        reloaded.CurrentStep!.ViewName.Should().Be("signup");
        var evt = _h.Events<BusinessVerificationCompletedEvent>().Should().ContainSingle().Subject;
        evt.Origin!.Kind.Should().Be(EventOriginKinds.Integration);
        evt.Origin.Id.Should().Be("verification:hosted");
    }

    [Fact]
    public async Task A_webhook_with_a_bad_signature_changes_nothing()
    {
        RouteHosted();
        Policy(Settings(("mode", "hosted")));
        var id = await StartAsync();
        await ContinueAsync(id, Form());
        _hosted.Results["sess-1"] = new HostedSessionResult { State = HostedSessionState.Approved };

        var response = await WebhookAsync("hosted", "sess-1", "not-the-secret");

        response.Should().BeOfType<UnauthorizedResult>();
        _hosted.ResultReads.Should().Be(0);
        JourneyStatusRevision.Read((await _h.Journeys.GetAsync(id))!.Data).Should().Be(0);
        _h.Events<BusinessVerificationCompletedEvent>().Should().BeEmpty();
    }

    [Fact]
    public async Task Webhooks_on_the_platform_host_or_for_an_unknown_provider_are_not_found()
    {
        (await WebhookAsync("hosted", "sess-1", ScriptedHostedProvider.Secret, Oluso.Core.PlatformConstants.TenantId)).Should().BeOfType<NotFoundResult>();
        (await WebhookAsync("nobody", "sess-1", ScriptedHostedProvider.Secret)).Should().BeOfType<NotFoundResult>();
        (await WebhookAsync("scripted", "sess-1", ScriptedHostedProvider.Secret)).Should().BeOfType<NotFoundResult>("a lookup-only provider has no webhooks");
    }

    [Fact]
    public async Task A_duplicate_webhook_doesnt_decide_twice()
    {
        RouteHosted();
        Policy(Settings(("mode", "hosted")));
        var id = await StartAsync();
        await ContinueAsync(id, Form());
        _hosted.Results["sess-1"] = new HostedSessionResult { State = HostedSessionState.Rejected, Message = "documents unreadable" };

        await WebhookAsync("hosted", "sess-1", ScriptedHostedProvider.Secret);
        await WebhookAsync("hosted", "sess-1", ScriptedHostedProvider.Secret);

        _hosted.ResultReads.Should().Be(1);
        _h.Events<BusinessVerificationCompletedEvent>().Should().ContainSingle().Which.Reason.Should().Be("rejected_by_provider");
        var rejected = await ContinueAsync(id, action: "init");
        ((BusinessVerificationRejectedViewModel)rejected.CurrentStep!.ViewModel!).ReasonKey.Should().Be("business_verification_error_rejected_by_provider");
    }

    [Fact]
    public async Task A_webhook_and_the_browser_callback_racing_on_one_session_decide_it_once()
    {
        RouteHosted();
        Policy(Settings(("mode", "hosted")));
        _h.Email.Setup(e => e.SendAsync(Tenant, "ada@acme.test", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmailResult.Succeeded());
        var id = await StartAsync();
        await ContinueAsync(id, Form());
        await ContinueAsync(id, action: "callback");
        await ContinueAsync(id, new Dictionary<string, object> { ["action"] = "notify", ["email"] = "ada@acme.test" });
        var readsBefore = _hosted.ResultReads;

        _hosted.Results["sess-1"] = new HostedSessionResult { State = HostedSessionState.Approved };
        var bothRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrived = 0;
        _hosted.BeforeResult = () =>
        {
            if (Interlocked.Increment(ref arrived) == 2) bothRead.TrySetResult();
            return bothRead.Task.WaitAsync(TimeSpan.FromSeconds(10));
        };

        var webhook = Task.Run(() => WebhookAsync("hosted", "sess-1", ScriptedHostedProvider.Secret));
        var callback = Task.Run(() => ContinueAsync(id, action: "callback"));
        await Task.WhenAll(webhook, callback);

        _hosted.ResultReads.Should().Be(readsBefore + 2, "both deciders saw the session open and asked the provider");
        webhook.Result.Should().BeOfType<OkResult>();
        callback.Result.CurrentStep!.ViewName.Should().Be("signup", "whichever lost still sees the decision that won");
        _h.Events<BusinessVerificationCompletedEvent>().Should().ContainSingle().Which.Status.Should().Be("verified");
        _h.Email.Verify(e => e.SendAsync(Tenant, "ada@acme.test", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);

        using var scope = _h.Scope();
        var store = _h.Store(scope);
        var applicant = (await store.ListUnclaimedApplicantsForJourneyAsync(Tenant, id)).Should().ContainSingle().Subject;
        applicant.Status.Should().Be(BusinessVerificationStatus.Verified);
        var checks = await store.ListChecksForBusinessAsync(Tenant, applicant.BusinessId);
        checks.Where(c => c.Kind == BusinessCheckKinds.HostedSession).Should().ContainSingle()
            .Which.Outcome.Should().Be(BusinessCheckOutcomes.Approved);
    }

    [Fact]
    public async Task Concurrent_deciders_on_separate_contexts_close_the_session_once_and_the_loser_sees_the_decision()
    {
        RouteHosted();
        Policy(Settings(("mode", "hosted")));
        var id = await StartAsync();
        await ContinueAsync(id, Form());
        var revisionBefore = JourneyStatusRevision.Read((await _h.Journeys.GetAsync(id))!.Data);

        _hosted.Results["sess-1"] = new HostedSessionResult { State = HostedSessionState.Rejected, Message = "documents unreadable" };
        var bothRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var arrived = 0;
        _hosted.BeforeResult = () =>
        {
            if (Interlocked.Increment(ref arrived) == 2) bothRead.TrySetResult();
            return bothRead.Task.WaitAsync(TimeSpan.FromSeconds(10));
        };

        async Task<BusinessApplicantView?> DecideAsync()
        {
            using var scope = _h.Scope();
            return await _h.Service(scope).CompleteHostedSessionAsync(Tenant, "hosted", "sess-1");
        }

        var views = await Task.WhenAll(Task.Run(DecideAsync), Task.Run(DecideAsync));

        arrived.Should().Be(2);
        _h.Events<BusinessVerificationCompletedEvent>().Should().ContainSingle().Which.Reason.Should().Be("rejected_by_provider");
        views.Should().AllSatisfy(v =>
        {
            v!.Applicant.Status.Should().Be(BusinessVerificationStatus.Rejected);
            v.Applicant.StatusReason.Should().Be(BusinessVerificationReasons.RejectedByProvider);
            v.Business.Status.Should().Be(BusinessVerificationStatus.Rejected);
        });
        JourneyStatusRevision.Read((await _h.Journeys.GetAsync(id))!.Data).Should().Be(revisionBefore + 1, "only the winner nudges the waiting page");
    }

    [Fact]
    public async Task An_applicant_who_stops_waiting_is_emailed_the_outcome()
    {
        RouteHosted();
        Policy(Settings(("mode", "hosted")));
        _h.Email.Setup(e => e.SendAsync(Tenant, "ada@acme.test", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(EmailResult.Succeeded());
        var id = await StartAsync();
        await ContinueAsync(id, Form());
        await ContinueAsync(id, action: "callback");

        var asked = await ContinueAsync(id, new Dictionary<string, object> { ["action"] = "notify", ["email"] = "ada@acme.test" });
        ((BusinessVerificationWaitingViewModel)asked.CurrentStep!.ViewModel!).EmailRequested.Should().BeTrue();

        _hosted.Results["sess-1"] = new HostedSessionResult { State = HostedSessionState.Approved };
        await WebhookAsync("hosted", "sess-1", ScriptedHostedProvider.Secret);

        _h.Email.Verify(e => e.SendAsync(Tenant, "ada@acme.test", It.IsAny<string>(),
            It.Is<string>(body => body.Contains("href=\"https://acme.test/journey/start/merchant\"")), It.IsAny<CancellationToken>()), Times.Once);
    }

    private async Task<IActionResult> WebhookAsync(string providerId, string reference, string signature, string tenant = Tenant)
    {
        using var scope = _h.Scope();
        var tenantContext = new Mock<ITenantContext>();
        tenantContext.SetupGet(t => t.TenantId).Returns(tenant);
        tenantContext.SetupGet(t => t.HasTenant).Returns(true);
        var controller = new VerificationWebhooksController(
            scope.ServiceProvider.GetRequiredService<IBusinessVerificationRouter>(),
            scope.ServiceProvider.GetRequiredService<IBusinessVerificationService>(),
            tenantContext.Object,
            NullLogger<VerificationWebhooksController>.Instance);
        var http = new DefaultHttpContext();
        http.Request.Headers["x-signature"] = signature;
        http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(reference));
        controller.ControllerContext = new ControllerContext { HttpContext = http };
        return await controller.Receive(providerId, default);
    }
}

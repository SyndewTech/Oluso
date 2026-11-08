using FluentAssertions;
using Oluso.Authentication.OrganizationSignIn;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Events;
using Oluso.Core.UserJourneys;
using Moq;
using Xunit;

namespace Oluso.Tests.OrganizationSignIn;

/// <summary>
/// "Sign in with your organisation": the platform hands a pending request to the
/// tenant, the tenant mints a single-use code once the user is authenticated, and
/// the platform redeems it only for the browser that started, the tenant it
/// targeted and a usable account. Organisation lookups never reveal which
/// organisations exist.
/// </summary>
public class OrganizationSignInServiceTests
{
    private static async Task<(string RequestId, string StartUrl)> BeginAsync(OrganizationSignInRig rig, string? prompt = null, string? loginHint = null)
    {
        var correlation = await rig.StartPlatformAuthorizeAsync(prompt, loginHint);
        rig.AtPlatform();
        var platform = rig.PlatformRequest();
        var begin = await rig.Service.BeginAsync(platform, "acme", OrganizationSignInRig.CallbackFor(correlation), null);
        begin.Succeeded.Should().BeTrue(begin.Error);
        rig.AcceptCookies(platform);
        return (OrganizationSignInRig.RequestIdFrom(begin.RedirectUrl!), begin.RedirectUrl!);
    }

    private static async Task<string> CompleteAtTenantAsync(OrganizationSignInRig rig, string requestId, string userId, string? idp = "AcmeEntra")
    {
        var tenant = rig.TenantRequest();
        var lookup = await rig.Service.GetRequestForTenantHostAsync(tenant, requestId, OrganizationSignInEvent.StageComplete);
        lookup.Succeeded.Should().BeTrue(lookup.Error);
        var callback = await rig.Service.IssueHandoffAsync(tenant, lookup.Request!,
            new OrganizationAuthentication(userId, idp, new[] { "mfa" }));
        rig.AtPlatform();
        return callback;
    }

    // ---- Organisation lookup -------------------------------------------------

    [Fact]
    public async Task Begin_sends_the_browser_to_the_tenant_issuer_and_binds_it_with_a_host_only_cookie()
    {
        using var rig = new OrganizationSignInRig();
        var correlation = await rig.StartPlatformAuthorizeAsync();
        var platform = rig.PlatformRequest();

        var begin = await rig.Service.BeginAsync(platform, " acme ", OrganizationSignInRig.CallbackFor(correlation), null);

        begin.Succeeded.Should().BeTrue();
        begin.RedirectUrl.Should().StartWith($"{OrganizationSignInRig.TenantIssuer}{OrganizationSignInDefaults.StartPath}?request=");
        var cookie = platform.Response.Headers.SetCookie.ToString();
        var requestId = OrganizationSignInRig.RequestIdFrom(begin.RedirectUrl!);
        cookie.Should().StartWith(OrganizationSignInService.BindingCookieName(requestId) + "=");
        cookie.Should().ContainEquivalentOf("httponly").And.ContainEquivalentOf("secure").And.ContainEquivalentOf("samesite=lax");
        cookie.Should().ContainEquivalentOf($"path={OrganizationSignInDefaults.BindingCookiePath}");
        cookie.Should().NotContainEquivalentOf("domain=");
        rig.Events.Should().ContainSingle(e => e.Stage == OrganizationSignInEvent.StageBegin && e.Succeeded && e.TenantId == rig.Tenant.Id);
    }

    [Theory]
    [InlineData("nope")]
    [InlineData("")]
    [InlineData("_platform")]
    public async Task Unknown_empty_or_platform_identifiers_get_the_same_generic_failure(string identifier)
    {
        using var rig = new OrganizationSignInRig();
        var correlation = await rig.StartPlatformAuthorizeAsync();
        var platform = rig.PlatformRequest();

        var begin = await rig.Service.BeginAsync(platform, identifier, OrganizationSignInRig.CallbackFor(correlation), null);

        begin.Succeeded.Should().BeFalse();
        begin.Error.Should().Be(OrganizationSignInErrors.UnknownOrganization);
        platform.Response.Headers.SetCookie.Should().BeEmpty();
    }

    [Fact]
    public async Task A_tenant_that_opted_out_is_indistinguishable_from_an_unknown_one()
    {
        using var rig = new OrganizationSignInRig();
        rig.Tenant.AllowPlatformOrgSignIn = false;
        var correlation = await rig.StartPlatformAuthorizeAsync();

        var begin = await rig.Service.BeginAsync(rig.PlatformRequest(), "acme", OrganizationSignInRig.CallbackFor(correlation), null);

        begin.Error.Should().Be(OrganizationSignInErrors.UnknownOrganization);
    }

    [Fact]
    public async Task Repeated_failed_lookups_are_throttled_per_client_address()
    {
        using var rig = new OrganizationSignInRig();
        var correlation = await rig.StartPlatformAuthorizeAsync();
        var callback = OrganizationSignInRig.CallbackFor(correlation);

        for (var i = 0; i < OrganizationSignInDefaults.MaxFailedLookups; i++)
        {
            (await rig.Service.BeginAsync(rig.PlatformRequest(), $"guess-{i}", callback, null))
                .Error.Should().Be(OrganizationSignInErrors.UnknownOrganization);
        }

        var throttled = await rig.Service.BeginAsync(rig.PlatformRequest(), "acme", callback, null);

        throttled.Error.Should().Be(OrganizationSignInErrors.TooManyAttempts);
    }

    [Fact]
    public async Task The_option_is_offered_only_on_the_platform_host_and_only_when_enabled()
    {
        using var rig = new OrganizationSignInRig();
        var correlation = await rig.StartPlatformAuthorizeAsync();
        var callback = OrganizationSignInRig.CallbackFor(correlation);

        (await rig.Service.GetEntryUrlAsync(rig.PlatformRequest(), callback))
            .Should().Be($"{OrganizationSignInDefaults.EntryPath}?returnUrl={Uri.EscapeDataString($"/connect/authorize/callback?correlation_id={correlation}")}");

        rig.Service.IsOffered(rig.TenantRequest()).Should().BeFalse();
        rig.AtPlatform();

        rig.Options_.EnableOrganizationSignIn = false;
        rig.Service.IsOffered(rig.PlatformRequest()).Should().BeFalse();
        (await rig.Service.BeginAsync(rig.PlatformRequest(), "acme", callback, null)).Error.Should().Be(OrganizationSignInErrors.NotOffered);
    }

    // ---- Return URL validation ----------------------------------------------

    [Theory]
    [InlineData("https://evil.example.com/connect/authorize/callback?correlation_id={0}")]
    [InlineData("//evil.example.com/connect/authorize/callback?correlation_id={0}")]
    [InlineData("/\\evil.example.com/connect/authorize/callback?correlation_id={0}")]
    [InlineData("javascript:alert(1)//{0}")]
    [InlineData("/connect/authorize/callback")]
    [InlineData("/connect/authorize/callback?correlation_id=not-a-live-flow")]
    public async Task Return_urls_off_host_or_without_a_live_flow_are_refused(string template)
    {
        using var rig = new OrganizationSignInRig();
        var correlation = await rig.StartPlatformAuthorizeAsync();

        var returnUrl = string.Format(template, correlation);

        (await rig.Service.CanBeginAsync(rig.PlatformRequest(), returnUrl, null)).Should().BeFalse();
        (await rig.Service.BeginAsync(rig.PlatformRequest(), "acme", returnUrl, null))
            .Error.Should().Be(OrganizationSignInErrors.InvalidReturnUrl);
    }

    [Fact]
    public async Task A_same_host_absolute_return_url_is_stored_as_a_relative_path()
    {
        using var rig = new OrganizationSignInRig();
        rig.AddUser("u1", rig.Tenant.Id);
        var (requestId, _) = await BeginAsync(rig);

        var callback = await CompleteAtTenantAsync(rig, requestId, "u1");
        var redeem = await rig.Service.RedeemAsync(rig.PlatformRequest(), OrganizationSignInRig.CodeFrom(callback));

        redeem.Request!.ReturnUrl.Should().StartWith("/connect/authorize/callback?correlation_id=");
    }

    [Fact]
    public async Task The_form_needs_exactly_one_of_a_return_url_or_a_journey()
    {
        using var rig = new OrganizationSignInRig();
        var correlation = await rig.StartPlatformAuthorizeAsync();
        var journey = await rig.StartPlatformJourneyAsync(correlation);

        (await rig.Service.CanBeginAsync(rig.PlatformRequest(), null, null)).Should().BeFalse();
        (await rig.Service.CanBeginAsync(rig.PlatformRequest(), OrganizationSignInRig.CallbackFor(correlation), journey.Id)).Should().BeFalse();
        (await rig.Service.CanBeginAsync(rig.PlatformRequest(), null, journey.Id)).Should().BeTrue();
    }

    // ---- Round trip, single use, binding -------------------------------------

    [Fact]
    public async Task Round_trip_redeems_once_for_the_starting_browser_and_drops_the_request()
    {
        using var rig = new OrganizationSignInRig();
        rig.AddUser("u1", rig.Tenant.Id);
        var (requestId, _) = await BeginAsync(rig);

        var callback = await CompleteAtTenantAsync(rig, requestId, "u1");
        callback.Should().StartWith($"{OrganizationSignInRig.PlatformIssuer}{OrganizationSignInDefaults.CallbackPath}?code=");

        var platform = rig.PlatformRequest();
        var redeem = await rig.Service.RedeemAsync(platform, OrganizationSignInRig.CodeFrom(callback));
        redeem.Succeeded.Should().BeTrue(redeem.Error);
        redeem.UserId.Should().Be("u1");
        redeem.IdentityProvider.Should().Be("AcmeEntra");
        redeem.AuthenticationMethods.Should().Contain("mfa");
        redeem.Request!.JourneyId.Should().BeNull();

        await rig.Service.FinishAsync(platform, redeem);
        rig.AcceptCookies(platform);

        var again = await rig.Service.RedeemAsync(rig.PlatformRequest(), OrganizationSignInRig.CodeFrom(callback));
        again.Succeeded.Should().BeFalse();
        again.Error.Should().Be(OrganizationSignInErrors.InvalidCode);
        (await rig.ProtocolState.GetAsync(requestId)).Should().BeNull();
        rig.Events.Should().Contain(e => e.Stage == OrganizationSignInEvent.StageRedeem && e.Succeeded && e.SubjectId == "u1");
    }

    [Fact]
    public async Task A_code_consumes_atomically_so_a_concurrent_second_redeem_fails()
    {
        using var rig = new OrganizationSignInRig();
        var raw = await rig.ActionTokens.IssueAsync(rig.Tenant.Id, "u1", OrganizationSignInDefaults.HandoffAction, "req", OrganizationSignInDefaults.HandoffCodeLifetime);
        var claim = await rig.ActionTokens.PeekAsync(raw);
        claim.Should().NotBeNull();

        var stored = await rig.ActionTokenStore.GetByHashAsync(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(raw))));
        var first = await rig.ActionTokenStore.TryConsumeAsync(stored!.Id, DateTime.UtcNow);
        var second = await rig.ActionTokenStore.TryConsumeAsync(stored.Id, DateTime.UtcNow);

        first.Should().BeTrue();
        second.Should().BeFalse();
        (await rig.ActionTokens.RedeemAsync(raw)).Should().BeNull();
    }

    [Fact]
    public async Task A_code_redeemed_from_another_browser_is_refused()
    {
        using var rig = new OrganizationSignInRig();
        rig.AddUser("u1", rig.Tenant.Id);
        var (requestId, _) = await BeginAsync(rig);
        var callback = await CompleteAtTenantAsync(rig, requestId, "u1");

        rig.ReplaceBrowserCookie(OrganizationSignInService.BindingCookieName(requestId), "someone-elses-nonce");
        var redeem = await rig.Service.RedeemAsync(rig.PlatformRequest(), OrganizationSignInRig.CodeFrom(callback));

        redeem.Succeeded.Should().BeFalse();
        redeem.Error.Should().Be(OrganizationSignInErrors.BindingMismatch);
        rig.Events.Should().Contain(e => e.Stage == OrganizationSignInEvent.StageRedeem && !e.Succeeded && e.Error == OrganizationSignInErrors.BindingMismatch);
    }

    [Fact]
    public async Task A_code_without_the_binding_cookie_is_refused()
    {
        using var rig = new OrganizationSignInRig();
        rig.AddUser("u1", rig.Tenant.Id);
        var (requestId, _) = await BeginAsync(rig);
        var callback = await CompleteAtTenantAsync(rig, requestId, "u1");

        rig.ForgetBrowserCookies();

        (await rig.Service.RedeemAsync(rig.PlatformRequest(), OrganizationSignInRig.CodeFrom(callback)))
            .Error.Should().Be(OrganizationSignInErrors.BindingMismatch);
    }

    // ---- Tenant host checks ---------------------------------------------------

    [Fact]
    public async Task The_tenant_host_accepts_only_requests_targeting_its_own_tenant()
    {
        using var rig = new OrganizationSignInRig();
        var (requestId, _) = await BeginAsync(rig);

        var wrongTenant = await rig.Service.GetRequestForTenantHostAsync(rig.TenantRequest(rig.OtherTenant), requestId, OrganizationSignInEvent.StageStart);
        wrongTenant.Succeeded.Should().BeFalse();
        wrongTenant.Error.Should().Be(OrganizationSignInErrors.TenantMismatch);

        rig.TenantContext.Tenant = rig.Tenant;
        var onPlatformHost = rig.PlatformRequest();
        var hostMismatch = await rig.Service.GetRequestForTenantHostAsync(onPlatformHost, requestId, OrganizationSignInEvent.StageStart);
        hostMismatch.Error.Should().Be(OrganizationSignInErrors.HostMismatch);

        var ok = await rig.Service.GetRequestForTenantHostAsync(rig.TenantRequest(), requestId, OrganizationSignInEvent.StageStart);
        ok.Succeeded.Should().BeTrue();
        rig.Events.Should().Contain(e => e.Stage == OrganizationSignInEvent.StageStart && e.Succeeded);
    }

    [Fact]
    public async Task A_code_minted_for_another_tenant_does_not_redeem_the_request()
    {
        using var rig = new OrganizationSignInRig();
        rig.AddUser("u1", rig.Tenant.Id);
        var (requestId, _) = await BeginAsync(rig);
        var forged = await rig.ActionTokens.IssueAsync(rig.OtherTenant.Id, "u1", OrganizationSignInDefaults.HandoffAction, requestId, OrganizationSignInDefaults.HandoffCodeLifetime);

        (await rig.Service.RedeemAsync(rig.PlatformRequest(), forged)).Error.Should().Be(OrganizationSignInErrors.TenantMismatch);
    }

    [Fact]
    public async Task A_code_for_some_other_action_is_neither_accepted_nor_consumed()
    {
        using var rig = new OrganizationSignInRig();
        var other = await rig.ActionTokens.IssueAsync(rig.Tenant.Id, "u1", "approval.task.decide", "task-1", TimeSpan.FromHours(1));

        (await rig.Service.RedeemAsync(rig.PlatformRequest(), other)).Error.Should().Be(OrganizationSignInErrors.InvalidCode);
        (await rig.ActionTokens.PeekAsync(other)).Should().NotBeNull();
    }

    // ---- User checks ----------------------------------------------------------

    [Fact]
    public async Task An_inactive_user_is_refused()
    {
        using var rig = new OrganizationSignInRig();
        rig.AddUser("u1", rig.Tenant.Id, active: false);
        var (requestId, _) = await BeginAsync(rig);
        var callback = await CompleteAtTenantAsync(rig, requestId, "u1");

        (await rig.Service.RedeemAsync(rig.PlatformRequest(), OrganizationSignInRig.CodeFrom(callback)))
            .Error.Should().Be(OrganizationSignInErrors.UserInactive);
    }

    [Fact]
    public async Task A_locked_out_user_is_refused()
    {
        using var rig = new OrganizationSignInRig();
        rig.AddUser("u1", rig.Tenant.Id, lockoutEnd: rig.Clock.GetUtcNow().AddHours(1));
        var (requestId, _) = await BeginAsync(rig);
        var callback = await CompleteAtTenantAsync(rig, requestId, "u1");

        (await rig.Service.RedeemAsync(rig.PlatformRequest(), OrganizationSignInRig.CodeFrom(callback)))
            .Error.Should().Be(OrganizationSignInErrors.UserLockedOut);
    }

    [Fact]
    public async Task A_user_of_another_tenant_is_refused_unless_a_member_of_the_target()
    {
        using var rig = new OrganizationSignInRig();
        rig.AddUser("u-globex", rig.OtherTenant.Id);
        var (requestId, _) = await BeginAsync(rig);
        var callback = await CompleteAtTenantAsync(rig, requestId, "u-globex");

        (await rig.Service.RedeemAsync(rig.PlatformRequest(), OrganizationSignInRig.CodeFrom(callback)))
            .Error.Should().Be(OrganizationSignInErrors.NotAMember);

        using var memberRig = new OrganizationSignInRig();
        memberRig.AddUser("u-globex", memberRig.OtherTenant.Id);
        memberRig.Memberships.Setup(m => m.GetByTenantAndUserAsync(memberRig.Tenant.Id, "u-globex", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TenantMembership { TenantId = memberRig.Tenant.Id, UserId = "u-globex" });
        var (memberRequest, _) = await BeginAsync(memberRig);
        var memberCallback = await CompleteAtTenantAsync(memberRig, memberRequest, "u-globex");

        (await memberRig.Service.RedeemAsync(memberRig.PlatformRequest(), OrganizationSignInRig.CodeFrom(memberCallback)))
            .Succeeded.Should().BeTrue();
    }

    // ---- Lifetimes ------------------------------------------------------------

    [Fact]
    public async Task A_code_expires_after_its_short_lifetime()
    {
        using var rig = new OrganizationSignInRig();
        rig.AddUser("u1", rig.Tenant.Id);
        var (requestId, _) = await BeginAsync(rig);
        var callback = await CompleteAtTenantAsync(rig, requestId, "u1");

        rig.Clock.Advance(OrganizationSignInDefaults.HandoffCodeLifetime + TimeSpan.FromSeconds(1));

        (await rig.Service.RedeemAsync(rig.PlatformRequest(), OrganizationSignInRig.CodeFrom(callback)))
            .Error.Should().Be(OrganizationSignInErrors.InvalidCode);
    }

    [Fact]
    public async Task A_pending_request_that_expired_before_completion_is_reported_as_expired_not_minted()
    {
        using var rig = new OrganizationSignInRig();
        var (requestId, _) = await BeginAsync(rig);

        rig.Clock.Advance(rig.Endpoints.StateExpiration + TimeSpan.FromSeconds(1));

        var lookup = await rig.Service.GetRequestForTenantHostAsync(rig.TenantRequest(), requestId, OrganizationSignInEvent.StageComplete);
        lookup.Succeeded.Should().BeFalse();
        lookup.IsExpired.Should().BeTrue();
        rig.Service.BuildExpiredUrl(requestId).Should().Be(
            $"{OrganizationSignInRig.PlatformIssuer}{OrganizationSignInDefaults.ExpiredPath}?request={Uri.EscapeDataString(requestId)}");
    }

    [Fact]
    public async Task A_slow_tenant_sign_in_that_completes_at_ten_minutes_still_succeeds()
    {
        using var rig = new OrganizationSignInRig();
        rig.AddUser("u1", rig.Tenant.Id);
        var (requestId, _) = await BeginAsync(rig);

        rig.Clock.Advance(TimeSpan.FromMinutes(10));
        var callback = await CompleteAtTenantAsync(rig, requestId, "u1");
        rig.Clock.Advance(TimeSpan.FromSeconds(5));

        (await rig.Service.RedeemAsync(rig.PlatformRequest(), OrganizationSignInRig.CodeFrom(callback)))
            .Succeeded.Should().BeTrue();
    }

    [Fact]
    public async Task The_pending_request_never_outlives_the_authorize_request_it_resumes()
    {
        using var rig = new OrganizationSignInRig();
        var correlation = await rig.StartPlatformAuthorizeAsync();
        rig.Clock.Advance(TimeSpan.FromMinutes(5));

        var begin = await rig.Service.BeginAsync(rig.PlatformRequest(), "acme", OrganizationSignInRig.CallbackFor(correlation), null);
        var requestId = OrganizationSignInRig.RequestIdFrom(begin.RedirectUrl!);

        rig.Clock.Advance(rig.Endpoints.StateExpiration - TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));

        (await rig.Service.GetRequestForTenantHostAsync(rig.TenantRequest(), requestId, OrganizationSignInEvent.StageStart))
            .IsExpired.Should().BeTrue();
    }

    [Fact]
    public async Task The_expired_page_offers_the_original_authorize_request_only_to_the_starting_browser()
    {
        using var rig = new OrganizationSignInRig();
        var (requestId, _) = await BeginAsync(rig, prompt: "login");
        rig.Clock.Advance(rig.Endpoints.StateExpiration + TimeSpan.FromMinutes(1));

        (await rig.Service.TakeRestartUrlAsync(StrangerRequest(), requestId)).Should().BeNull();

        var restart = await rig.Service.TakeRestartUrlAsync(rig.PlatformRequest(), requestId);
        restart.Should().StartWith("/connect/authorize?");
        restart.Should().Contain("client_id=mail-server").And.Contain("prompt=login");

        static Microsoft.AspNetCore.Http.HttpContext StrangerRequest()
        {
            var http = new Microsoft.AspNetCore.Http.DefaultHttpContext();
            http.Request.Scheme = "https";
            http.Request.Host = new Microsoft.AspNetCore.Http.HostString("auth.example.com");
            return http;
        }
    }

    // ---- Authorize parameters ---------------------------------------------------

    [Fact]
    public async Task Prompt_login_and_login_hint_from_the_original_request_reach_the_tenant()
    {
        using var rig = new OrganizationSignInRig();
        var (requestId, _) = await BeginAsync(rig, prompt: "login", loginHint: "jo@acme.test");

        var lookup = await rig.Service.GetRequestForTenantHostAsync(rig.TenantRequest(), requestId, OrganizationSignInEvent.StageStart);

        lookup.Request!.ForceFreshLogin.Should().BeTrue();
        lookup.Request.LoginHint.Should().Be("jo@acme.test");
        lookup.Request.ClientId.Should().Be(OrganizationSignInRig.ClientId);
    }

    // ---- Platform journey ------------------------------------------------------

    [Fact]
    public async Task A_journey_platform_login_resumes_its_journey_and_hands_the_result_to_it_once()
    {
        using var rig = new OrganizationSignInRig();
        rig.AddUser("u1", rig.Tenant.Id);
        var correlation = await rig.StartPlatformAuthorizeAsync();
        var journey = await rig.StartPlatformJourneyAsync(correlation);

        (await rig.Service.GetJourneyEntryUrlAsync(rig.PlatformRequest(), journey.Id))
            .Should().Be($"{OrganizationSignInDefaults.EntryPath}?journey={journey.Id}");

        var platform = rig.PlatformRequest();
        var begin = await rig.Service.BeginAsync(platform, "acme", null, journey.Id);
        rig.AcceptCookies(platform);
        var requestId = OrganizationSignInRig.RequestIdFrom(begin.RedirectUrl!);
        var callback = await CompleteAtTenantAsync(rig, requestId, "u1");

        var redeemContext = rig.PlatformRequest();
        var redeem = await rig.Service.RedeemAsync(redeemContext, OrganizationSignInRig.CodeFrom(callback));
        redeem.Succeeded.Should().BeTrue(redeem.Error);
        redeem.Request!.JourneyId.Should().Be(journey.Id);
        redeem.Request.ReturnUrl.Should().Be($"/journey/{journey.Id}");

        await rig.Service.FinishAsync(redeemContext, redeem);

        var handed = await rig.Service.TakeJourneyResultAsync(journey.Id);
        handed.Should().NotBeNull();
        handed!.UserId.Should().Be("u1");
        handed.IdentityProvider.Should().Be("AcmeEntra");
        (await rig.Service.TakeJourneyResultAsync(journey.Id)).Should().BeNull();
    }

    [Fact]
    public async Task A_standalone_platform_login_resumes_the_authorize_callback_and_never_a_journey()
    {
        using var rig = new OrganizationSignInRig();
        rig.AddUser("u1", rig.Tenant.Id);
        var correlation = await rig.StartPlatformAuthorizeAsync();
        var journey = await rig.StartPlatformJourneyAsync(correlation);
        var (requestId, _) = await BeginAsync(rig);
        var callback = await CompleteAtTenantAsync(rig, requestId, "u1");

        var redeemContext = rig.PlatformRequest();
        var redeem = await rig.Service.RedeemAsync(redeemContext, OrganizationSignInRig.CodeFrom(callback));
        await rig.Service.FinishAsync(redeemContext, redeem);

        redeem.Request!.JourneyId.Should().BeNull();
        redeem.Request.ReturnUrl.Should().StartWith("/connect/authorize/callback?correlation_id=");
        (await rig.Service.TakeJourneyResultAsync(journey.Id)).Should().BeNull();
    }

    [Fact]
    public async Task A_finished_or_expired_journey_cannot_start_an_organisation_sign_in()
    {
        using var rig = new OrganizationSignInRig();
        var correlation = await rig.StartPlatformAuthorizeAsync();
        var journey = await rig.StartPlatformJourneyAsync(correlation);
        await rig.Journeys.SaveAsync(journey with { Status = JourneyStatus.Completed });

        (await rig.Service.GetJourneyEntryUrlAsync(rig.PlatformRequest(), journey.Id)).Should().BeNull();
        (await rig.Service.BeginAsync(rig.PlatformRequest(), "acme", null, journey.Id))
            .Error.Should().Be(OrganizationSignInErrors.InvalidReturnUrl);
    }
}

using System.Net;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Oluso.Core.Domain.Entities;
using Oluso.Core.UserJourneys;
using Oluso.Integration.Tests.Fixtures;
using Xunit;

namespace Oluso.Integration.Tests;

/// <summary>
/// The sign-in journey for an authorization request is chosen by the policies' conditions: a policy whose conditions
/// the request meets (here acr_values) is more specific than one without conditions and is chosen even at a lower
/// priority; without a match, the policy without conditions runs; a policy whose conditions fail never does.
/// </summary>
public class JourneyPolicyConditionTests : IntegrationTestBase
{
    private const string ClientId = "policy-condition-client";
    private const string RedirectUri = "http://localhost:5100/signin-oidc";
    private readonly string _defaultPolicyId = "cond-default-" + Guid.NewGuid().ToString("N")[..8];
    private readonly string _mfaPolicyId = "cond-mfa-" + Guid.NewGuid().ToString("N")[..8];
    private bool _tenantJourneyBefore;

    public JourneyPolicyConditionTests(OlusoWebApplicationFactory factory) : base(factory)
    {
    }

    public override async Task InitializeAsync()
    {
        await WithDbContextAsync(async db =>
        {
            var tenant = (await db.Tenants.FindAsync("default"))!;
            _tenantJourneyBefore = tenant.UseJourneyFlow;
            tenant.UseJourneyFlow = true;
            if (!await db.Clients.IgnoreQueryFilters().AnyAsync(c => c.ClientId == ClientId))
            {
                var client = new Client
                {
                    ClientId = ClientId, TenantId = "default", Enabled = true, RequireClientSecret = false,
                    TokenEndpointAuthMethod = "none", RequirePkce = false, RequireConsent = false, UseJourneyFlow = true,
                };
                client.AllowedGrantTypes.Add(new ClientGrantType { GrantType = "authorization_code", TenantId = "default" });
                client.RedirectUris.Add(new ClientRedirectUri { RedirectUri = RedirectUri, TenantId = "default" });
                client.AllowedScopes.Add(new ClientScope { Scope = "openid", TenantId = "default" });
                db.Clients.Add(client);
            }
            await db.SaveChangesAsync();
        });

        using var scope = Factory.Services.CreateScope();
        var policies = scope.ServiceProvider.GetRequiredService<IJourneyPolicyStore>();
        var login = new List<JourneyPolicyStep> { new() { Id = "login", Type = "local_login", Order = 1 } };
        await policies.SaveAsync(new JourneyPolicy
        {
            Id = _defaultPolicyId, Slug = _defaultPolicyId, Name = "Default sign-in", TenantId = "default",
            Type = JourneyType.SignIn, Priority = 1000, Steps = login,
        });
        await policies.SaveAsync(new JourneyPolicy
        {
            Id = _mfaPolicyId, Slug = _mfaPolicyId, Name = "Sign-in asked for MFA", TenantId = "default",
            Type = JourneyType.SignIn, Priority = 500, Steps = login,
            Conditions = new List<JourneyPolicyCondition> { new() { Type = "AcrValue", Operator = "contains", Value = "mfa" } },
        });
    }

    public override async Task DisposeAsync()
    {
        using (var scope = Factory.Services.CreateScope())
        {
            var policies = scope.ServiceProvider.GetRequiredService<IJourneyPolicyStore>();
            await policies.DeleteAsync(_defaultPolicyId);
            await policies.DeleteAsync(_mfaPolicyId);
        }
        await WithDbContextAsync(async db =>
        {
            (await db.Tenants.FindAsync("default"))!.UseJourneyFlow = _tenantJourneyBefore;
            await db.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task A_request_asking_for_mfa_gets_the_policy_whose_condition_it_meets()
    {
        (await PolicyChosenAsync("&acr_values=" + Uri.EscapeDataString("https://oluso.io/acr/mfa"))).Should().Be(_mfaPolicyId);
    }

    [Fact]
    public async Task Without_a_match_the_policy_without_conditions_runs()
    {
        (await PolicyChosenAsync("")).Should().Be(_defaultPolicyId);
        (await PolicyChosenAsync("&acr_values=" + Uri.EscapeDataString("https://oluso.io/acr/sfa"))).Should().Be(_defaultPolicyId);
    }

    private async Task<string?> PolicyChosenAsync(string extra)
    {
        var http = Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        http.DefaultRequestHeaders.Add("X-Tenant-Id", "default");
        var authorize = await http.GetAsync("/connect/authorize?response_type=code&scope=openid&state=s1" +
            $"&client_id={ClientId}&redirect_uri={Uri.EscapeDataString(RedirectUri)}" + extra);
        authorize.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = authorize.Headers.Location!.ToString();
        location.Should().Contain("/journey/");

        var journeyId = location.Split("/journey/")[1].Split('?', '/')[0];
        using var scope = Factory.Services.CreateScope();
        var journey = await scope.ServiceProvider.GetRequiredService<IJourneyStateStore>().GetAsync(journeyId);
        return journey?.PolicyId;
    }
}

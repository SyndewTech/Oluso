using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Oluso.UserJourneys.Steps;
using Xunit;

namespace Oluso.Tests.UserJourneys;

/// <summary>
/// The consent step shows an instance of a prefix scope (a Matrix device scope) by the prefix scope's display name,
/// never the client-chosen suffix, and the user consents to the full value.
/// </summary>
public class ConsentPrefixScopeTests
{
    private const string Device = "urn:matrix:client:device:AbCdEf1234";

    private static StepExecutionContext Context(JourneyStepInput? input = null)
    {
        var resources = new Mock<IResourceStore>();
        var prefix = new ApiScope { Name = "urn:matrix:client:device:", DisplayName = "Use this device with your Matrix account" };
        prefix.Properties.Add(new ApiScopeProperty { Key = ApiScopePrefixes.MatchProperty, Value = ApiScopePrefixes.PrefixMatch });
        resources.Setup(r => r.FindIdentityResourcesByScopeNameAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<IdentityResource>());
        resources.Setup(r => r.FindApiScopesByNameAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ApiScope>());
        resources.Setup(r => r.FindPrefixApiScopesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<ApiScope> { prefix });

        var clients = new Mock<IClientStore>();
        clients.Setup(c => c.FindClientByIdAsync("matrix", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Client { ClientId = "matrix", ClientName = "Matrix client", RequireConsent = true, AllowRememberConsent = false });

        var tenant = new Mock<ITenantContext>();
        var context = new StepExecutionContext
        {
            JourneyId = "journey-1",
            StepId = "consent",
            ClientId = "matrix",
            Configuration = new JourneyStepConfiguration { Id = "consent", Type = "consent" },
            Input = input,
            ServiceProvider = new ServiceCollection()
                .AddLogging()
                .AddSingleton(resources.Object)
                .AddSingleton(clients.Object)
                .AddSingleton(new Mock<IConsentStore>().Object)
                .AddSingleton(tenant.Object)
                .BuildServiceProvider(),
        };
        context.SetData("scope", $"openid {Device}");
        return context;
    }

    [Fact]
    public async Task The_device_scope_is_shown_by_display_name_and_consented_in_full()
    {
        var shown = await new ConsentStepHandler().ExecuteAsync(Context());

        var model = shown.StepResult!.ViewModel.Should().BeOfType<ConsentViewModel>().Subject;
        var scope = model.ApiScopes.Should().ContainSingle().Subject;
        scope.Name.Should().Be(Device);
        scope.DisplayName.Should().Be("Use this device with your Matrix account");

        var allowed = await new ConsentStepHandler().ExecuteAsync(Context(new JourneyStepInput
        {
            StepId = "consent",
            Values = new Dictionary<string, object> { ["consent_action"] = "allow", [$"scope_{Device}"] = "true" },
        }));

        allowed.OutputData!["consented_scopes"].ToString()!.Split(' ').Should().BeEquivalentTo("openid", Device);
    }
}

using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Core.Protocols;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Oluso.Dto.Journeys;
using Oluso.Enterprise.Saml.Headless;
using Oluso.Enterprise.Saml.Integration;
using Oluso.Enterprise.Saml.ServiceProvider;
using Xunit;

namespace Oluso.Enterprise.Saml.Tests;

/// <summary>Journey/_SamlIdpSelection described as a form: one button per identity provider, posting saml_idp.</summary>
public class SamlIdpSelectionDescriberTests
{
    private static JourneyStepForm Describe(SamlIdpSelectionViewModel? model) =>
        new SamlIdpSelectionDescriber(new KeyLocalizerFactory())
            .Describe(new JourneyStepDescription("step", "saml", "Journey/_SamlIdpSelection", model, null));

    [Fact]
    public void Each_provider_posts_its_name_under_saml_idp()
    {
        var form = Describe(new SamlIdpSelectionViewModel
        {
            TenantName = "acme",
            ErrorMessage = "Sign-in failed",
            IdentityProviders =
            [
                new SamlIdpDisplayInfo { Name = "okta", DisplayName = "Okta" },
                new SamlIdpDisplayInfo { Name = "entra", DisplayName = "Microsoft Entra" }
            ]
        });

        form.Title.Should().Be("Choose Your Organization");
        form.Description.Should().Be("Sign in to acme");
        form.Error.Should().Be("Sign-in failed");
        form.Fields.Should().BeEmpty();
        form.Actions.Select(a => (a.Name, a.Value, a.Label)).Should().Equal(("saml_idp", "okta", "Okta"), ("saml_idp", "entra", "Microsoft Entra"));
    }

    [Fact]
    public void Without_an_organization_name_asks_for_the_provider()
    {
        Describe(new SamlIdpSelectionViewModel()).Description.Should().Be("Select your identity provider to continue");
    }

    [Fact]
    public async Task The_step_shows_the_view_the_describer_and_hosted_page_know()
    {
        var sp = new Mock<ISamlServiceProvider>();
        sp.Setup(s => s.GetConfiguredIdpsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<SamlIdpInfo>
        {
            new() { Name = "okta", DisplayName = "Okta", Enabled = true },
            new() { Name = "entra", DisplayName = "Microsoft Entra", Enabled = true }
        });
        var services = new ServiceCollection()
            .AddSingleton(sp.Object)
            .AddSingleton(Mock.Of<IOlusoUserService>())
            .AddSingleton(Mock.Of<IProtocolStateStore>())
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .BuildServiceProvider();

        var result = await new SamlStepHandler().ExecuteAsync(new StepExecutionContext
        {
            JourneyId = "j1",
            StepId = "saml",
            Configuration = new JourneyStepConfiguration { Id = "saml", Type = "saml", Settings = new Dictionary<string, object>() },
            Input = new JourneyStepInput { StepId = "saml", Values = new Dictionary<string, object>() },
            ServiceProvider = services,
        });

        result.StepResult!.ViewName.Should().Be("Journey/_SamlIdpSelection");
        new SamlIdpSelectionDescriber(new KeyLocalizerFactory()).Views.Should().Contain(result.StepResult.ViewName);
    }

    private sealed class KeyLocalizerFactory : IStringLocalizerFactory
    {
        public IStringLocalizer Create(Type resourceSource) => new KeyLocalizer();
        public IStringLocalizer Create(string baseName, string location) => new KeyLocalizer();
    }

    private sealed class KeyLocalizer : IStringLocalizer
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, $"{name}:{string.Join(",", arguments)}");
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}

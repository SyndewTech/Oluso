using Microsoft.Extensions.Localization;
using Oluso.Core.UserJourneys;
using Oluso.Dto.Journeys;
using Oluso.Enterprise.Saml.Integration;
using Oluso.UserJourneys.Headless;

namespace Oluso.Enterprise.Saml.Headless;

/// <summary>
/// Journey/_SamlIdpSelection: one button per SAML identity provider, posting saml_idp; the step then redirects to the
/// chosen provider. The view's text isn't localized, so the same text is used here.
/// </summary>
public sealed class SamlIdpSelectionDescriber : StepFormDescriber<SamlIdpSelectionViewModel>
{
    public SamlIdpSelectionDescriber(IStringLocalizerFactory localizers) : base(localizers) { }

    public override IReadOnlyCollection<string> Views { get; } = ["Journey/_SamlIdpSelection"];

    protected override JourneyStepForm Describe(JourneyStepDescription step, SamlIdpSelectionViewModel? model) => new()
    {
        Title = "Choose Your Organization",
        Description = string.IsNullOrEmpty(model?.TenantName)
            ? "Select your identity provider to continue"
            : $"Sign in to {model.TenantName}",
        Error = NullIfEmpty(model?.ErrorMessage),
        Actions = (model?.IdentityProviders ?? [])
            .Select(idp => Submit(idp.DisplayName, "saml_idp", idp.Name))
            .ToList()
    };
}

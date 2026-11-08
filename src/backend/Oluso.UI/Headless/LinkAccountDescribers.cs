using Microsoft.Extensions.Localization;
using Oluso.Core.UserJourneys;
using Oluso.Dto.Journeys;
using Oluso.UserJourneys.Headless;
using Oluso.UserJourneys.Steps;

namespace Oluso.UI.Headless;

/// <summary>
/// Journey/_LinkAccount: the linked sign-in accounts, an unlink button for each when the step allows it, and the
/// button that continues the journey.
/// </summary>
public sealed class LinkAccountDescriber : StepFormDescriber<LinkAccountViewModel>
{
    public LinkAccountDescriber(IStringLocalizerFactory localizers) : base(localizers) { }

    public override IReadOnlyCollection<string> Views { get; } = ["Journey/_LinkAccount"];

    protected override JourneyStepForm Describe(JourneyStepDescription step, LinkAccountViewModel? model)
    {
        var linked = model?.LinkedLogins ?? [];
        var linkedText = linked.Count > 0
            ? string.Join("\n", linked.Select(l => l.ProviderDisplayName))
            : T("link_account_no_linked");

        return new JourneyStepForm
        {
            Title = T("link_account_title"),
            Description = T("link_account_description"),
            Notice = NullIfEmpty(model?.SuccessMessage),
            Error = NullIfEmpty(model?.ErrorMessage),
            Fields = [Labelled(Content("linked_accounts", linkedText), T("link_account_currently_linked"))],
            Actions = [.. Unlinks(model), Alone(Submit(T("link_account_continue_button"), "done", "true"))]
        };
    }

    private IEnumerable<JourneyFormAction> Unlinks(LinkAccountViewModel? model)
    {
        if (model?.AllowUnlink != true) yield break;
        foreach (var login in model.LinkedLogins)
        {
            var unlink = Alone(Secondary(T("link_account_unlink_button"), "unlink_provider", login.Provider), login.ProviderDisplayName);
            unlink.Values = new Dictionary<string, string> { ["unlink_key"] = login.ProviderKey };
            unlink.Confirm = T("link_account_unlink_confirm");
            yield return unlink;
        }
    }

    private static JourneyFormField Labelled(JourneyFormField field, string label)
    {
        field.Label = label;
        return field;
    }
}

/// <summary>Journey/_LinkAccountConfirm: confirms linking an external account to the signed-in user.</summary>
public sealed class LinkAccountConfirmDescriber : StepFormDescriber<LinkAccountConfirmViewModel>
{
    public LinkAccountConfirmDescriber(IStringLocalizerFactory localizers) : base(localizers) { }

    public override IReadOnlyCollection<string> Views { get; } = ["Journey/_LinkAccountConfirm"];

    protected override JourneyStepForm Describe(JourneyStepDescription step, LinkAccountConfirmViewModel? model)
    {
        var name = model?.ProviderDisplayName ?? string.Empty;
        var meaning = string.Join("\n",
            T("link_account_confirm_alert_header"),
            T("link_account_confirm_alert_item1", name),
            T("link_account_confirm_alert_item2", name),
            T("link_account_confirm_alert_item3"));

        return new JourneyStepForm
        {
            Title = T("link_account_confirm_title", name),
            Description = T("link_account_confirm_description", name),
            Fields =
            [
                Content("link_meaning", meaning),
                Hidden("_external_provider", model?.Provider),
                Hidden("_external_provider_key", model?.ProviderKey),
                Hidden("_external_provider_name", model?.ProviderDisplayName),
                Hidden("_confirmed", "true")
            ],
            Actions =
            [
                SubmitFields(T("link_account_confirm_button", name)),
                // The hosted "?cancel=true" link reaches the step without the external login, which shows the linked accounts again.
                Alone(Cancel(T("link_account_confirm_cancel"), "cancel", "true"))
            ]
        };
    }
}

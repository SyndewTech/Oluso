using Microsoft.Extensions.Localization;
using Oluso.Core.UserJourneys;
using Oluso.Dto.Journeys;
using Oluso.UserJourneys.Headless;
using Oluso.UserJourneys.Steps;

namespace Oluso.UI.Headless;

/// <summary>Journey/_Consent: the scopes an application asks for, allowed or denied.</summary>
public sealed class ConsentDescriber : StepFormDescriber<ConsentViewModel>
{
    public ConsentDescriber(IStringLocalizerFactory localizers) : base(localizers) { }

    public override IReadOnlyCollection<string> Views { get; } = ["Journey/_Consent"];

    protected override JourneyStepForm Describe(JourneyStepDescription step, ConsentViewModel? model)
    {
        var fields = new List<JourneyFormField>();
        if (model?.IdentityScopes is { Count: > 0 } identity)
        {
            fields.Add(Section("identity_scopes", T("consent_personal_info_label"), T("consent_personal_info_description"), "identity"));
            fields.AddRange(identity.Select(s => Scope(s, "identity")));
        }
        if (model?.ApiScopes is { Count: > 0 } api)
        {
            fields.Add(Section("api_scopes", T("consent_api_access_label"), null, "api"));
            fields.AddRange(api.Select(s => Scope(s, "api")));
        }
        if (model?.AllowRememberConsent == true)
        {
            fields.Add(new JourneyFormField { Name = "remember_consent", Type = "checkbox", Label = T("consent_remember") });
        }

        var links = new List<JourneyFormLink>();
        if (!string.IsNullOrEmpty(model?.ClientUri))
        {
            links.Add(new JourneyFormLink { Label = model.ClientUri, Url = model.ClientUri });
        }

        return new JourneyStepForm
        {
            Title = T("consent_title", model?.ClientName ?? string.Empty),
            Description = T("consent_subtitle"),
            Error = NullIfEmpty(model?.ErrorMessage),
            Fields = fields,
            Actions =
            [
                Cancel(T("consent_deny_button"), "consent_action", "deny"),
                Submit(T("consent_allow_button"), "consent_action", "allow")
            ],
            Links = links
        };
    }

    private static JourneyFormField Section(string name, string label, string? description, string group) => new()
    {
        Name = name,
        Type = "content",
        Label = label,
        Value = description,
        Group = group
    };

    /// <summary>
    /// A required scope is a ticked checkbox that can't be cleared; the hosted page posts its hidden "true" in its
    /// place, which is the same value.
    /// </summary>
    private JourneyFormField Scope(ScopeViewModel scope, string group) => new()
    {
        Name = $"scope_{scope.Name}",
        Type = "checkbox",
        Label = scope.DisplayName,
        Description = NullIfEmpty(scope.Description),
        Hint = scope.Required ? T("consent_scope_required") : null,
        Required = scope.Required,
        ReadOnly = scope.Required,
        Value = scope.Checked || scope.Required ? "true" : null,
        Group = group
    };
}

/// <summary>Journey/_TermsAcceptance: the terms (and privacy policy) to accept, inline or linked, and the decline button.</summary>
public sealed class TermsAcceptanceDescriber : StepFormDescriber<TermsAcceptanceViewModel>
{
    public TermsAcceptanceDescriber(IStringLocalizerFactory localizers) : base(localizers) { }

    public override IReadOnlyCollection<string> Views { get; } = ["Journey/_TermsAcceptance"];

    protected override JourneyStepForm Describe(JourneyStepDescription step, TermsAcceptanceViewModel? model)
    {
        var fields = new List<JourneyFormField>();
        var links = new List<JourneyFormLink>();

        if (model?.ShowTermsInline == true && !string.IsNullOrEmpty(model.TermsContent))
        {
            fields.Add(Document("terms_content", model.TermsTitle, model.TermsContent));
        }
        else if (!string.IsNullOrEmpty(model?.TermsUrl))
        {
            links.Add(new JourneyFormLink { Label = Versioned(model.TermsTitle, model.TermsVersion), Url = model.TermsUrl });
        }

        if (model?.RequireBoth == true)
        {
            if (model.ShowPrivacyInline && !string.IsNullOrEmpty(model.PrivacyContent))
            {
                fields.Add(Document("privacy_content", model.PrivacyTitle, model.PrivacyContent));
            }
            else if (!string.IsNullOrEmpty(model.PrivacyUrl))
            {
                links.Add(new JourneyFormLink { Label = Versioned(model.PrivacyTitle, model.PrivacyVersion), Url = model.PrivacyUrl });
            }
        }

        fields.Add(Hidden("_submitted", "true"));

        if (model?.RequireCheckbox == true)
        {
            fields.Add(new JourneyFormField
            {
                Name = "acceptTerms",
                Type = "checkbox",
                Label = T("terms_acceptance_agree_label", model.TermsTitle),
                Required = true
            });
            if (model.RequireBoth)
            {
                fields.Add(new JourneyFormField
                {
                    Name = "acceptPrivacy",
                    Type = "checkbox",
                    Label = T("terms_acceptance_agree_label", model.PrivacyTitle),
                    Required = true
                });
            }
        }
        else
        {
            var agreed = model?.RequireBoth == true
                ? $"{model.TermsTitle} {T("terms_acceptance_review_and")} {model.PrivacyTitle}"
                : model?.TermsTitle;
            fields.Add(Content("implicit_acceptance", $"{T("terms_acceptance_implicit")} {agreed}."));
        }

        var actions = new List<JourneyFormAction> { SubmitFields(model?.AcceptButtonText ?? T("terms_acceptance_accept_button")) };
        if (model?.CanDecline == true && !string.IsNullOrEmpty(model.DeclineButtonText))
        {
            actions.Add(Cancel(model.DeclineButtonText, "decline", "true"));
        }

        return new JourneyStepForm
        {
            Title = NullIfEmpty(model?.TermsTitle),
            Error = NullIfEmpty(model?.ErrorMessage),
            Fields = fields,
            Actions = actions,
            Links = links
        };
    }

    private string Versioned(string title, string version) => $"{title} ({T("terms_acceptance_version", version)})";

    private static JourneyFormField Document(string name, string title, string html) => new()
    {
        Name = name,
        Type = "content",
        Label = title,
        Value = HtmlText.ToPlainText(html)
    };
}

/// <summary>Journey/_Captcha: the provider's challenge, whose token is posted under the name the step reads.</summary>
public sealed class CaptchaDescriber : StepFormDescriber<CaptchaViewModel>
{
    public CaptchaDescriber(IStringLocalizerFactory localizers) : base(localizers) { }

    public override IReadOnlyCollection<string> Views { get; } = ["Journey/_Captcha"];

    protected override JourneyStepForm Describe(JourneyStepDescription step, CaptchaViewModel? model)
    {
        var provider = (model?.Provider ?? string.Empty).ToLowerInvariant();
        object data = provider switch
        {
            "recaptcha" or "recaptcha_v2" => new { provider = "recaptcha", siteKey = model!.SiteKey, theme = model.Theme, size = model.Size },
            "recaptcha_v3" => new { provider = "recaptcha_v3", siteKey = model!.SiteKey, action = model.Action ?? "submit" },
            "hcaptcha" => new { provider = "hcaptcha", siteKey = model!.SiteKey, theme = model.Theme, size = model.Size },
            "turnstile" or "cloudflare" => new { provider = "turnstile", siteKey = model!.SiteKey, theme = model.Theme, size = model.Size, language = model.Language },
            _ => new { provider, siteKey = model?.SiteKey }
        };

        return new JourneyStepForm
        {
            Title = T("captcha_title"),
            Description = T("captcha_description"),
            Error = NullIfEmpty(model?.ErrorMessage),
            Actions = [SubmitFields(T("captcha_button"))],
            Widget = Widget("captcha", data, ResponseField(provider))
        };
    }

    /// <summary>The input the hosted view posts the token under.</summary>
    private static string ResponseField(string provider) => provider switch
    {
        "recaptcha" or "recaptcha_v2" or "recaptcha_v3" => "g-recaptcha-response",
        "hcaptcha" => "h-captcha-response",
        "cloudflare" or "turnstile" => "cf-turnstile-response",
        _ => "captcha-response"
    };
}

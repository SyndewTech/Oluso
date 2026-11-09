using Microsoft.Extensions.Localization;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Oluso.Dto.Journeys;
using Oluso.Enterprise.Fido2.Services;
using Oluso.UserJourneys.Headless;

namespace Oluso.Enterprise.Fido2.Headless;

/// <summary>
/// The JSON the passkey views post: the standard credential shape plus the top-level <c>authenticatorAttachment</c>
/// (the credential's, or null), with <c>userHandle</c> null when the authenticator gives none.
/// </summary>
internal static class Fido2WidgetFormat
{
    public const string WithAttachment = "with_attachment";

    public static object? AllowCredentials(IEnumerable<(string? Type, string Id, IEnumerable<string>? Transports)>? credentials)
    {
        var list = credentials?.Select(c => (object)new
        {
            type = c.Type ?? "public-key",
            id = c.Id,
            transports = c.Transports?.ToList() is { Count: > 0 } transports ? transports : null
        }).ToList();
        return list is { Count: > 0 } ? list : null;
    }
}

/// <summary>
/// Journey/_Fido2Login. The webauthn step's model shows the username form and a passkey button with the assertion
/// options the step issued for the journey. The fido2_login step's model (which the hosted view can't render) asks for an
/// optional username and posts action=passkey_login, the input that step reads.
/// </summary>
public sealed class Fido2LoginDescriber : StepFormDescriber<object>
{
    public Fido2LoginDescriber(IStringLocalizerFactory localizers) : base(localizers) { }

    public override IReadOnlyCollection<string> Views { get; } = ["Journey/_Fido2Login"];

    protected override JourneyStepForm Describe(JourneyStepDescription step, object? model) => model switch
    {
        Steps.Fido2LoginViewModel login => DescribeLoginStep(login),
        _ => DescribeWebAuthnStep(model as Fido2LoginViewModel)
    };

    private JourneyStepForm DescribeWebAuthnStep(Fido2LoginViewModel? model)
    {
        var fields = new List<JourneyFormField>();
        var actions = new List<JourneyFormAction>();
        var passkeyOnly = model?.AllowPasskeyOnly == true;

        if (!passkeyOnly)
        {
            fields.Add(new JourneyFormField
            {
                Name = "username",
                Type = "text",
                Label = T("fido2_login_username_label"),
                Value = model?.Username,
                Required = true,
                Autocomplete = "username webauthn",
                Autofocus = true
            });
            actions.Add(SubmitFields(T("fido2_login_continue")));
        }
        else
        {
            fields.Add(Content("passkey_hint", T("fido2_login_passkey_hint")));
        }

        var passkey = Alone(SubmitFields(T(passkeyOnly ? "fido2_login_passkey_button" : "fido2_login_use_passkey")));

        return new JourneyStepForm
        {
            Title = T("fido2_login_title"),
            Description = T("fido2_login_subtitle"),
            Error = NullIfEmpty(model?.ErrorMessage),
            Fields = fields,
            Actions = actions,
            // The options the step issued for this journey: the step only accepts their challenge.
            Widget = model?.Options is { } options
                ? Widget("webauthn_get", new
                {
                    publicKey = new
                    {
                        challenge = options.Challenge,
                        timeout = options.Timeout,
                        rpId = options.RpId,
                        allowCredentials = Fido2WidgetFormat.AllowCredentials(
                            options.AllowCredentials?.Select(c => ((string?)c.Type, c.Id, (IEnumerable<string>?)c.Transports))),
                        userVerification = options.UserVerification
                    },
                    format = Fido2WidgetFormat.WithAttachment
                }, "fido2_response", passkey)
                : null
        };
    }

    private JourneyStepForm DescribeLoginStep(Steps.Fido2LoginViewModel model) => new()
    {
        Title = NullIfEmpty(model.Title) ?? T("fido2_login_title"),
        Description = NullIfEmpty(model.Description) ?? T("fido2_login_subtitle"),
        Error = NullIfEmpty(model.ErrorMessage),
        Fields =
        [
            new JourneyFormField
            {
                Name = "username",
                Type = "text",
                Label = T("fido2_login_username_label"),
                Required = !model.AllowUsernameless,
                Autocomplete = "username webauthn",
                Autofocus = true
            }
        ],
        Actions = [Submit(T("fido2_login_use_passkey"), "action", "passkey_login")]
    };
}

/// <summary>Journey/_Fido2Assert: the webauthn step's passkey prompt with the options it created.</summary>
public sealed class Fido2AssertDescriber : StepFormDescriber<Fido2AssertViewModel>
{
    public Fido2AssertDescriber(IStringLocalizerFactory localizers) : base(localizers) { }

    public override IReadOnlyCollection<string> Views { get; } = ["Journey/_Fido2Assert"];

    protected override JourneyStepForm Describe(JourneyStepDescription step, Fido2AssertViewModel? model)
    {
        var showUser = model is { IsPasskeyOnly: false } && !string.IsNullOrEmpty(model.DisplayName);
        var fields = new List<JourneyFormField>();
        if (showUser && !string.IsNullOrEmpty(model!.Username))
        {
            fields.Add(Content("username", model.Username));
        }

        var options = model?.Options;
        if (options != null)
        {
            fields.Add(Hidden("fido2_challenge", options.Challenge));
        }

        return new JourneyStepForm
        {
            Title = showUser ? model!.DisplayName : T("passkey_assert_title"),
            Description = T("passkey_assert_description"),
            Error = NullIfEmpty(model?.ErrorMessage),
            Notice = options == null ? T("passkey_assert_not_supported") : null,
            Fields = fields,
            Widget = options == null ? null : Widget("webauthn_get", new
            {
                publicKey = new
                {
                    challenge = options.Challenge,
                    timeout = options.Timeout,
                    rpId = options.RpId,
                    allowCredentials = Fido2WidgetFormat.AllowCredentials(
                        options.AllowCredentials?.Select(c => ((string?)c.Type, c.Id, (IEnumerable<string>?)c.Transports))),
                    userVerification = options.UserVerification
                },
                format = Fido2WidgetFormat.WithAttachment
            }, "fido2_response", Alone(SubmitFields(T("passkey_assert_prompt"))))
        };
    }
}

/// <summary>
/// Journey/_Fido2Assertion: the passkey prompt the fido2_login, local_login and composite_login steps show after
/// action=passkey_login. Both assertion view models (Oluso.Core.Services and the FIDO2 steps') have the same shape.
/// </summary>
public sealed class Fido2AssertionDescriber : StepFormDescriber<object>
{
    public Fido2AssertionDescriber(IStringLocalizerFactory localizers) : base(localizers) { }

    public override IReadOnlyCollection<string> Views { get; } = ["Journey/_Fido2Assertion"];

    protected override JourneyStepForm Describe(JourneyStepDescription step, object? model)
    {
        var (options, assertionId) = model switch
        {
            Fido2AssertionViewModel core => (core.Options, core.AssertionId),
            Steps.Fido2AssertionViewModel own => (own.Options, own.AssertionId),
            _ => ((Fido2AssertionOptions?)null, (string?)null)
        };

        return new JourneyStepForm
        {
            Title = T("passkey_assert_title"),
            Description = T("passkey_assert_description"),
            Notice = options == null ? T("passkey_assert_not_supported") : null,
            Fields = [Hidden("assertionId", assertionId)],
            Widget = options == null ? null : Widget("webauthn_get", new
            {
                publicKey = new
                {
                    challenge = options.Challenge,
                    timeout = options.Timeout ?? 60000,
                    rpId = options.RpId,
                    userVerification = options.UserVerification ?? "preferred",
                    allowCredentials = Fido2WidgetFormat.AllowCredentials(
                        options.AllowCredentials?.Select(c => ((string?)c.Type, c.Id, (IEnumerable<string>?)c.Transports)))
                },
                format = Fido2WidgetFormat.WithAttachment
            }, "assertionResponse", Alone(SubmitFields(T("passkey_assert_prompt"))))
        };
    }
}

/// <summary>Journey/_Fido2RegistrationPrompt: offer to create a passkey, or skip.</summary>
public sealed class Fido2RegistrationPromptDescriber : StepFormDescriber<Steps.Fido2RegistrationPromptViewModel>
{
    public Fido2RegistrationPromptDescriber(IStringLocalizerFactory localizers) : base(localizers) { }

    public override IReadOnlyCollection<string> Views { get; } = ["Journey/_Fido2RegistrationPrompt"];

    protected override JourneyStepForm Describe(JourneyStepDescription step, Steps.Fido2RegistrationPromptViewModel? model)
    {
        model ??= new Steps.Fido2RegistrationPromptViewModel();
        var fields = new List<JourneyFormField>
        {
            Content("benefit_secure", T("fido2_reg_prompt_benefit_secure")),
            Content("benefit_instant", T("fido2_reg_prompt_benefit_instant")),
            Content("benefit_device", T("fido2_reg_prompt_benefit_device"))
        };
        if (model.ExistingCredentialCount > 0)
        {
            fields.Add(Content("existing", T(
                model.ExistingCredentialCount > 1 ? "fido2_reg_prompt_existing_many" : "fido2_reg_prompt_existing_one",
                model.ExistingCredentialCount)));
        }

        var actions = new List<JourneyFormAction> { Submit(T("fido2_reg_prompt_setup_button"), "action", "register") };
        if (model.AllowSkip)
        {
            actions.Add(Link(T("fido2_reg_prompt_skip"), "action", "skip"));
        }

        return new JourneyStepForm
        {
            Title = model.Title ?? T("fido2_reg_prompt_default_title"),
            Description = model.Description ?? T("fido2_reg_prompt_default_description"),
            Error = NullIfEmpty(model.ErrorMessage),
            Fields = fields,
            Actions = actions
        };
    }
}

/// <summary>
/// Journey/_Fido2Registration: creates the passkey with the step's options. Besides the credential, the view posts
/// credentialDisplayName, named after the authenticator's attachment; the widget data carries those names.
/// </summary>
public sealed class Fido2RegistrationDescriber : StepFormDescriber<Steps.Fido2RegistrationViewModel>
{
    private static readonly object[] DefaultCredentialParameters =
    [
        new { type = "public-key", alg = -7 },
        new { type = "public-key", alg = -257 }
    ];

    public Fido2RegistrationDescriber(IStringLocalizerFactory localizers) : base(localizers) { }

    public override IReadOnlyCollection<string> Views { get; } = ["Journey/_Fido2Registration"];

    protected override JourneyStepForm Describe(JourneyStepDescription step, Steps.Fido2RegistrationViewModel? model)
    {
        var actions = new List<JourneyFormAction>();
        if (model?.AllowSkip == true)
        {
            actions.Add(Alone(Link(T("fido2_reg_cancel"), "action", "skip")));
        }

        var options = model?.Options;
        return new JourneyStepForm
        {
            Title = T("fido2_reg_title"),
            Description = T("fido2_reg_subtitle"),
            Error = NullIfEmpty(model?.ErrorMessage),
            Notice = T("fido2_reg_hint"),
            Fields = [Hidden("registrationId", model?.RegistrationId)],
            Actions = actions,
            Widget = options == null ? null : Widget("webauthn_create", new
            {
                publicKey = new
                {
                    challenge = options.Challenge,
                    rp = new { id = options.RelyingParty.Id, name = options.RelyingParty.Name },
                    user = new { id = options.User.Id, name = options.User.Name, displayName = options.User.DisplayName },
                    pubKeyCredParams = options.PublicKeyCredentialParameters?
                        .Select(p => (object)new { type = p.Type, alg = p.Algorithm }).ToArray() ?? DefaultCredentialParameters,
                    timeout = options.Timeout ?? 60000,
                    attestation = options.AttestationConveyance ?? "none",
                    authenticatorSelection = (object?)options.AuthenticatorSelection ?? new { },
                    excludeCredentials = Fido2WidgetFormat.AllowCredentials(
                        options.ExcludeCredentials?.Select(c => ((string?)c.Type, c.Id, (IEnumerable<string>?)c.Transports)))
                },
                format = Fido2WidgetFormat.WithAttachment,
                credentialName = new
                {
                    field = "credentialDisplayName",
                    platform = T("fido2_reg_display_this_device"),
                    crossPlatform = T("fido2_reg_display_security_key"),
                    other = "Passkey"
                }
            }, "attestationResponse", Alone(SubmitFields(T("fido2_reg_title"))))
        };
    }
}

/// <summary>Journey/_Fido2RegistrationSuccess: the passkey was created; continue.</summary>
public sealed class Fido2RegistrationSuccessDescriber : StepFormDescriber<Steps.Fido2RegistrationSuccessViewModel>
{
    public Fido2RegistrationSuccessDescriber(IStringLocalizerFactory localizers) : base(localizers) { }

    public override IReadOnlyCollection<string> Views { get; } = ["Journey/_Fido2RegistrationSuccess"];

    protected override JourneyStepForm Describe(JourneyStepDescription step, Steps.Fido2RegistrationSuccessViewModel? model) => new()
    {
        Title = T("fido2_reg_success_heading"),
        Description = T("fido2_reg_success_detail", model?.DisplayName ?? T("fido2_reg_success_default_name")),
        Fields =
        [
            new JourneyFormField
            {
                Name = "whats_next",
                Type = "content",
                Label = T("fido2_reg_success_whats_next"),
                Value = T("fido2_reg_success_next_description")
            }
        ],
        Actions = [Submit(T("fido2_reg_success_continue"), "action", "continue")]
    };
}

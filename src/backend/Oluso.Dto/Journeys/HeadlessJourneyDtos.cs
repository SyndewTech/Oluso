using System.Text.Json;

namespace Oluso.Dto.Journeys;

/// <summary>
/// Starts a journey through the headless API. Authentication journeys need the client and a
/// PKCE S256 challenge: they complete with an authorization code bound to both.
/// </summary>
public class StartJourneyRequest
{
    /// <summary>Input for the first step (e.g. username and password). Omit to only render it.</summary>
    public Dictionary<string, object?>? Data { get; set; }

    public string? ClientId { get; set; }
    public string? CodeChallenge { get; set; }
    public string? CodeChallengeMethod { get; set; }

    /// <summary>Scopes for the authorization code (default "openid profile email").</summary>
    public string? Scope { get; set; }
}

/// <summary>Input for the session's current step.</summary>
public class JourneyStepRequest
{
    public Dictionary<string, object?>? Data { get; set; }

    /// <summary>A step action instead of (or with) input, e.g. "resend" or "cancel".</summary>
    public string? Action { get; set; }
}

/// <summary>Where a headless journey stands, returned by every headless endpoint.</summary>
public class JourneySessionResponse
{
    public string SessionId { get; set; } = null!;
    public string Slug { get; set; } = null!;

    /// <summary>in_progress, completed, failed, cancelled or expired.</summary>
    public string Status { get; set; } = null!;

    public bool Success { get; set; }
    public bool Completed { get; set; }

    /// <summary>The step waiting for input, while in progress.</summary>
    public string? CurrentStep { get; set; }

    /// <summary>Alias of <see cref="CurrentStep"/> for clients that branch on "is there a next step".</summary>
    public string? NextStep { get; set; }

    public IReadOnlyList<string> CompletedSteps { get; set; } = Array.Empty<string>();
    public DateTime? ExpiresAt { get; set; }

    /// <summary>What to render for the current step.</summary>
    public JourneyStepView? Step { get; set; }

    /// <summary>Set when the step continues elsewhere (an external identity provider).</summary>
    public string? RedirectUrl { get; set; }

    /// <summary>
    /// For an authentication journey that completed: the authorization code to redeem at the
    /// token endpoint with the PKCE verifier and redirect_uri "postmessage".
    /// </summary>
    public string? Code { get; set; }

    /// <summary>For a data-collection journey that completed: the policy's success message.</summary>
    public string? Message { get; set; }

    public string? Error { get; set; }
    public string? ErrorDescription { get; set; }
}

/// <summary>The step a client renders: its definition plus the model the server would render it from.</summary>
public class JourneyStepView
{
    public string Id { get; set; } = null!;
    public string Type { get; set; } = null!;
    public string? DisplayName { get; set; }

    /// <summary>The server view the step renders as (e.g. "Journey/_LocalLogin").</summary>
    public string? View { get; set; }

    /// <summary>The step's view model: fields, options, messages and errors.</summary>
    public JsonElement? Model { get; set; }

    /// <summary>
    /// The step described the way any client can draw it: posting its fields with the chosen action's name and value
    /// is the same input the hosted page posts. Null when the step can only be shown on the hosted page.
    /// </summary>
    public JourneyStepForm? Form { get; set; }

    /// <summary>
    /// The hosted view's markup, for a step with no <see cref="Form"/> (custom pages, plugin views). Its form posts the
    /// same input as the hosted page; a client submits it to the step endpoint.
    /// </summary>
    public string? Html { get; set; }

    public IReadOnlyList<JourneyStepActionView> Actions { get; set; } = Array.Empty<JourneyStepActionView>();
}

/// <summary>A step as a form: text, fields, the buttons that submit it and anything the browser has to do.</summary>
public class JourneyStepForm
{
    public string? Title { get; set; }
    public string? Description { get; set; }

    /// <summary>What went wrong with the last submission, for the whole step.</summary>
    public string? Error { get; set; }

    /// <summary>Something to tell the person that isn't an error, e.g. "We sent a new code".</summary>
    public string? Notice { get; set; }

    public IReadOnlyList<JourneyFormField> Fields { get; set; } = Array.Empty<JourneyFormField>();

    /// <summary>The buttons, in order. Each submits the fields with its own name and value.</summary>
    public IReadOnlyList<JourneyFormAction> Actions { get; set; } = Array.Empty<JourneyFormAction>();

    /// <summary>Pages the step points to, e.g. the terms of service.</summary>
    public IReadOnlyList<JourneyFormLink> Links { get; set; } = Array.Empty<JourneyFormLink>();

    /// <summary>Something only the browser can do for the step (passkey, captcha, QR code).</summary>
    public JourneyFormWidget? Widget { get; set; }
}

public class JourneyFormField
{
    /// <summary>The name the value is posted under.</summary>
    public string Name { get; set; } = null!;

    /// <summary>
    /// text, email, password, number, date, tel, url, textarea, select, radio, checkbox, file, hidden, otp, or
    /// content (text to read, no input). A checkbox posts "true" when ticked.
    /// </summary>
    public string Type { get; set; } = "text";

    public string? Label { get; set; }
    public string? Placeholder { get; set; }
    public string? Description { get; set; }

    /// <summary>Guidance shown under the input, e.g. upload limits.</summary>
    public string? Hint { get; set; }

    public bool Required { get; set; }
    public bool ReadOnly { get; set; }

    /// <summary>The current value: what was entered before, or the default.</summary>
    public string? Value { get; set; }

    /// <summary>What's wrong with the value entered.</summary>
    public string? Error { get; set; }

    public IReadOnlyList<JourneyFormOption>? Options { get; set; }

    public string? Pattern { get; set; }
    public string? PatternError { get; set; }
    public int? MinLength { get; set; }
    public int? MaxLength { get; set; }
    public string? Min { get; set; }
    public string? Max { get; set; }
    public int? Rows { get; set; }

    /// <summary>The browser autocomplete hint, e.g. "username", "one-time-code".</summary>
    public string? Autocomplete { get; set; }

    public bool Autofocus { get; set; }

    /// <summary>Files: accepted types (e.g. ".pdf,image/*").</summary>
    public string? Accept { get; set; }
    public int? MaxFiles { get; set; }
    public int? MaxFileSizeMb { get; set; }

    /// <summary>Files: offer the camera ("environment" or "user").</summary>
    public string? Capture { get; set; }

    /// <summary>Phone: the countries to choose from; the country is posted as "{Name}__country".</summary>
    public IReadOnlyList<JourneyFormCountry>? Countries { get; set; }
    public string? Country { get; set; }

    /// <summary>Show the field only while another field's value meets the condition.</summary>
    public JourneyFormCondition? ShowWhen { get; set; }

    /// <summary>Show the field only while every one of these conditions is met (together with <see cref="ShowWhen"/>).</summary>
    public IReadOnlyList<JourneyFormCondition>? ShowWhenAll { get; set; }

    /// <summary>Fields with the same group are shown together.</summary>
    public string? Group { get; set; }
}

public class JourneyFormOption
{
    public string Value { get; set; } = null!;
    public string Label { get; set; } = null!;
    public string? Description { get; set; }
    public bool Disabled { get; set; }
}

public class JourneyFormCountry
{
    public string Code { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string DialCode { get; set; } = null!;
    public string? Flag { get; set; }
}

/// <summary>
/// The field's value compared with <see cref="Value"/>: eq, ne, gt, gte, lt, lte, contains, not_contains,
/// starts_with, ends_with, regex, in, not_in, exists, not_exists, empty, not_empty, true or false.
/// </summary>
public class JourneyFormCondition
{
    public string Field { get; set; } = null!;
    public string Operator { get; set; } = "eq";
    public string? Value { get; set; }
}

public class JourneyFormAction
{
    /// <summary>The posted name, e.g. "__submitted" or "action". Null for a button that posts no name of its own.</summary>
    public string? Name { get; set; }

    public string? Value { get; set; }
    public string Label { get; set; } = null!;

    /// <summary>Text shown with the button, e.g. "Didn't get the code?" before a resend link.</summary>
    public string? Description { get; set; }

    /// <summary>A question to confirm before the button posts, e.g. "Unlink this account?".</summary>
    public string? Confirm { get; set; }

    /// <summary>More values the button posts, for a hosted button whose form carries several hidden inputs.</summary>
    public IReadOnlyDictionary<string, string>? Values { get; set; }

    /// <summary>
    /// False for a button that sits in its own form on the hosted page (resend, use a recovery code, cancel): it
    /// posts only its own name, value and <see cref="Values"/>, none of the step's fields.
    /// </summary>
    public bool PostsFields { get; set; } = true;

    /// <summary>primary, secondary, link or cancel.</summary>
    public string Kind { get; set; } = "primary";

    /// <summary>Submit without checking the fields first (back, resend, skip, cancel).</summary>
    public bool SkipValidation { get; set; }
}

public class JourneyFormLink
{
    public string Label { get; set; } = null!;
    public string Url { get; set; } = null!;
}

/// <summary>
/// Browser work for the step. <see cref="Type"/> is webauthn_get, webauthn_create, captcha, qr, codes or poll;
/// <see cref="Data"/> holds what it needs, and its result is posted under <see cref="ResultField"/>.
/// </summary>
public class JourneyFormWidget
{
    public string Type { get; set; } = null!;
    public JsonElement? Data { get; set; }

    /// <summary>Where the widget's result is posted (e.g. the assertion JSON or the captcha token).</summary>
    public string? ResultField { get; set; }

    /// <summary>The action posted with the result; absent when the widget only fills a field.</summary>
    public JourneyFormAction? Submit { get; set; }
}

public class JourneyStepActionView
{
    public string Id { get; set; } = null!;
    public string Label { get; set; } = null!;
    public bool IsPrimary { get; set; }
    public bool IsCancel { get; set; }
}

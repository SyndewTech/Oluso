namespace Oluso.Core.Authentication;

/// <summary>
/// Handler that gives an <see cref="IActionTokenService"/> token a meaning.
/// Modules register one handler per <see cref="Action"/> they support
/// (e.g. <c>approval.task.decide</c>, <c>&lt;module&gt;.&lt;action&gt;</c>)
/// and the generic public links controller
/// dispatches preview / redeem calls to them.
///
/// <para>The shape is intentionally narrow: a preview view-model + a
/// per-option execute. The token plumbing (issuing, hashing, single-use)
/// stays inside <see cref="IActionTokenService"/>; handlers only see the
/// already-validated <see cref="ActionTokenClaim"/>.</para>
/// </summary>
public interface IActionTokenHandler
{
    /// <summary>
    /// Action key this handler responds to — must match
    /// <see cref="ActionTokenClaim.Action"/>. Use a dot-namespaced string
    /// keyed by module (<c>approval.task.decide</c>,
    /// <c>&lt;module&gt;.&lt;action&gt;</c>, etc.) so collisions are unlikely.
    /// </summary>
    string Action { get; }

    /// <summary>
    /// True (the default) when a link works once: redeeming consumes the token.
    /// A handler whose subject records its own outcome, such as a quote that can
    /// only be answered while it is open, returns false so the link keeps showing
    /// the document and its outcome after the response.
    /// </summary>
    bool SingleUse => true;

    /// <summary>
    /// Build the preview view-model the landing page renders before the
    /// user picks an option. Implementations resolve the subject by
    /// <paramref name="claim"/>.SubjectId and return the title, summary,
    /// and available options. Returns null when the subject is gone /
    /// already-actioned — the landing page renders a generic "this link
    /// is no longer actionable" message.
    /// </summary>
    Task<ActionTokenPreview?> PreviewAsync(
        ActionTokenClaim claim,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Apply the chosen <paramref name="option"/> after the token has
    /// already been atomically consumed. The handler is the source of
    /// truth for what each option does — workspace just passes the key
    /// the user picked. Throw to surface an error message to the page.
    /// </summary>
    Task<ActionTokenExecuteResult> ExecuteAsync(
        ActionTokenClaim claim,
        string option,
        string? comments,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Execute with the values of the option's <see cref="ActionTokenOption.Fields"/>.
    /// Handlers that declare fields override this; the rest ignore the values.
    /// </summary>
    Task<ActionTokenExecuteResult> ExecuteAsync(
        ActionTokenClaim claim,
        string option,
        string? comments,
        IReadOnlyDictionary<string, string> fields,
        CancellationToken cancellationToken = default)
        => ExecuteAsync(claim, option, comments, cancellationToken);
}

/// <summary>
/// What the landing page shows before the user clicks an action.
/// </summary>
/// <summary>
/// Optional companion to <see cref="IActionTokenHandler"/> for links that
/// carry a downloadable document (an invoice PDF, a signed agreement). The
/// public links endpoint streams the file to anyone holding a valid token.
/// </summary>
public interface IActionTokenFileProvider
{
    Task<ActionTokenFile?> GetFileAsync(ActionTokenClaim claim, CancellationToken cancellationToken = default);
}

public sealed record ActionTokenFile(byte[] Content, string ContentType, string FileName);

public sealed class ActionTokenPreview
{
    /// <summary>
    /// True when the visitor must confirm their email before the content is shown;
    /// the preview then carries only the title, description and presentation data.
    /// </summary>
    public bool Locked { get; set; }

    /// <summary>"email" or "code" when <see cref="Locked"/>.</summary>
    public string? LockMode { get; set; }

    /// <summary>
    /// Structured view-model for pages richer than the generic option list,
    /// e.g. a hosted invoice with its lines. The generic magic-link page
    /// ignores it; a dedicated page reads the shape it expects.
    /// </summary>
    public Dictionary<string, object?>? Data { get; set; }

    /// <summary>Short label — e.g. "Approval request".</summary>
    public string Title { get; set; } = "";

    /// <summary>
    /// One-line description of what the subject is — e.g. the requester's
    /// title summary. Renders under the title.
    /// </summary>
    public string? Summary { get; set; }

    /// <summary>
    /// Optional longer body shown in a panel under the summary. Useful
    /// for surfacing the requester's note / current step context.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Actions the user can pick on the landing page. Order is preserved
    /// in the rendered button row.
    /// </summary>
    public List<ActionTokenOption> Options { get; set; } = new();

    /// <summary>
    /// When true the page renders a read-only "already actioned" view
    /// instead of the option buttons (e.g. the task was decided through
    /// a different channel after the token was minted).
    /// </summary>
    public bool AlreadyActioned { get; set; }

    /// <summary>
    /// Human-readable explanation rendered when <see cref="AlreadyActioned"/>
    /// is true (e.g. "Already approved by Jane Doe").
    /// </summary>
    public string? AlreadyActionedReason { get; set; }
}

/// <summary>
/// One button on the landing page. The <see cref="Key"/> is what the
/// frontend sends back to <c>POST .../redeem</c> in the body.
/// </summary>
public sealed class ActionTokenOption
{
    /// <summary>Stable machine key — e.g. <c>approve</c>, <c>reject</c>.</summary>
    public string Key { get; set; } = "";

    /// <summary>User-facing label.</summary>
    public string Label { get; set; } = "";

    /// <summary>
    /// Visual variant the landing page uses: <c>primary</c>, <c>danger</c>,
    /// <c>neutral</c>, <c>warning</c>. Frontend picks the styling.
    /// </summary>
    public string Variant { get; set; } = "primary";

    /// <summary>
    /// When true the page collects an optional / required comment from
    /// the user before submitting (e.g. rejection reason, info-request
    /// question).
    /// </summary>
    public bool RequiresComments { get; set; }

    /// <summary>
    /// When true the page treats <see cref="RequiresComments"/> as
    /// mandatory rather than optional.
    /// </summary>
    public bool CommentsRequired { get; set; }

    /// <summary>
    /// Named inputs the page collects before submitting this option, e.g. the
    /// name of the person accepting a quote. Sent back keyed by <see cref="ActionTokenField.Key"/>.
    /// </summary>
    public List<ActionTokenField> Fields { get; set; } = new();
}

/// <summary>One labelled input the landing page asks for before an option is submitted.</summary>
public sealed class ActionTokenField
{
    public string Key { get; set; } = "";
    public string Label { get; set; } = "";
    public bool Required { get; set; }
    public string? Placeholder { get; set; }
    public int MaxLength { get; set; } = 200;
}

/// <summary>
/// Returned by <see cref="IActionTokenHandler.ExecuteAsync"/> so the page
/// can show a confirmation message after redeem.
/// </summary>
public sealed class ActionTokenExecuteResult
{
    /// <summary>Short "what just happened" headline — e.g. "Approved".</summary>
    public string Message { get; set; } = "";

    /// <summary>
    /// Optional URL the page should redirect to after a short pause
    /// (e.g. the workspace submission detail). Anonymous landing page
    /// uses this only when the user already has a session.
    /// </summary>
    public string? RedirectUrl { get; set; }
}

namespace Oluso.Core.Licensing;

/// <summary>
/// Persists seed data (email templates, etc.) for newly provisioned tenants.
/// Implemented by the Workflows module, which owns the EmailTemplate store.
/// </summary>
public interface ITenantSeedPersister
{
    Task SeedEmailTemplatesAsync(
        string tenantId,
        IEnumerable<EmailTemplateSeed> templates,
        CancellationToken cancellationToken = default);

    Task SeedWorkflowsForTemplatesAsync(
        string tenantId,
        IEnumerable<EmailTemplateSeed> templates,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Persist full workflow definitions (trigger + condition + action steps)
    /// without the caller referencing the Workflows module. Idempotent: a
    /// workflow whose <see cref="WorkflowDefinitionSeed.Name"/> already exists
    /// for the tenant is skipped unless <paramref name="overwrite"/> is set,
    /// in which case its definition is replaced.
    ///
    /// <para>IMPORTANT: seeded workflows MUST be persisted disabled (draft) so
    /// nothing fires until a tenant admin reviews and enables them — this
    /// includes overwritten workflows, which return to draft for re-review.</para>
    /// </summary>
    Task<WorkflowSeedResult> SeedWorkflowDefinitionsAsync(
        string tenantId,
        IEnumerable<WorkflowDefinitionSeed> workflows,
        bool overwrite = false,
        CancellationToken cancellationToken = default);
}

/// <summary>Outcome of <see cref="ITenantSeedPersister.SeedWorkflowDefinitionsAsync"/>.</summary>
public sealed class WorkflowSeedResult
{
    /// <summary>Names of workflows created (or replaced, when overwriting).</summary>
    public List<string> Created { get; init; } = [];

    /// <summary>Names of workflows skipped (already exist, or their trigger event is unknown).</summary>
    public List<string> Skipped { get; init; } = [];
}

/// <summary>
/// A neutral, module-agnostic description of one automation workflow to seed
/// for a tenant — name, trigger event, optional condition and ordered action
/// steps. The Workflows module translates this into its own definition shape;
/// callers (module blueprints/seeders) never reference the engine directly.
/// </summary>
public class WorkflowDefinitionSeed
{
    /// <summary>Unique workflow name for the tenant (used for duplicate detection).</summary>
    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>
    /// Webhook event type that triggers the workflow (e.g.
    /// <c>"emr.triage.recorded"</c>). Must be a registered webhook event; the
    /// persister refuses seeds with unknown triggers.
    /// </summary>
    public required string TriggerEventType { get; init; }

    /// <summary>
    /// Optional Scriban condition expression evaluated against the event data
    /// (e.g. <c>"event.acuity_level &lt;= 2"</c>). The workflow only runs when
    /// it renders true.
    /// </summary>
    public string? TriggerCondition { get; init; }

    /// <summary>Ordered action steps.</summary>
    public List<WorkflowStepSeed> Steps { get; init; } = [];

    /// <summary>The owning module's key (for display/grouping).</summary>
    public string? Module { get; init; }

    /// <summary>
    /// Authorise the seeded workflow to send the sensitive trigger properties its own steps and
    /// templates reference (and only those). Leave false when the steps need none.
    /// </summary>
    public bool AuthorizeSensitiveData { get; init; }
}

/// <summary>One action step of a <see cref="WorkflowDefinitionSeed"/>.</summary>
public class WorkflowStepSeed
{
    /// <summary>
    /// Action type string as understood by the workflow engine's registered
    /// action handlers (e.g. <c>"send_notification"</c>, <c>"send_sms"</c>,
    /// <c>"send_webhook"</c>, <c>"delay"</c>, <c>"log"</c>). Use only types
    /// the engine actually supports.
    /// </summary>
    public required string Type { get; init; }

    /// <summary>Display name of the step.</summary>
    public string? Name { get; init; }

    /// <summary>
    /// Step-specific configuration in the engine's serialized form (e.g. for
    /// <c>send_notification</c>: <c>templateName</c>/<c>templateId</c> or
    /// inline <c>subject</c>/<c>htmlBody</c>, plus <c>to</c>).
    /// </summary>
    public Dictionary<string, object> Config { get; init; } = new();

    /// <summary>Execution order (ascending).</summary>
    public int Order { get; init; }
}

/// <summary>
/// Defines a default email template to seed for new tenants.
/// Modules return these from IModuleDescriptor.GetEmailTemplateSeeds().
/// </summary>
public class EmailTemplateSeed
{
    /// <summary>
    /// Unique template name (e.g., "<module>.<entity>.<event>"). Used to check for duplicates.
    /// </summary>
    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>
    /// Email subject line. Workflow-rendered templates read event data under the
    /// <c>event</c> namespace, e.g. <c>{{ event.employee_name }}</c>.
    /// </summary>
    public required string Subject { get; init; }

    /// <summary>
    /// HTML body. Supports template variables.
    /// </summary>
    public required string HtmlBody { get; init; }

    /// <summary>
    /// Plain-text fallback body.
    /// </summary>
    public string? TextBody { get; init; }

    /// <summary>
    /// Unlayer design JSON for the visual email builder. If set, enables round-trip editing.
    /// Use <see cref="UnlayerDesignBuilder"/> to generate this.
    /// </summary>
    public string? DesignJson { get; init; }

    /// <summary>
    /// Module that owns this template (the owning module's key).
    /// </summary>
    public string? Module { get; init; }

    /// <summary>
    /// If set, a Workflow is auto-created that triggers on this event and sends this email.
    /// Must match a webhook event type (e.g., "{module}.{entity}.{action}").
    /// </summary>
    public string? TriggerEventType { get; init; }

    /// <summary>
    /// Scriban expression for the email recipient (e.g., "{{event.employee_email}}").
    /// If null, the SendEmailActionHandler falls back to event.email / user.email.
    /// </summary>
    public string? RecipientExpression { get; init; }

    /// <summary>
    /// Scriban expression for the recipient's platform user id (e.g.
    /// "{{event.assigned_to_user_id}}"). Lets the notification gate (snooze,
    /// quiet hours, muted events), per-step opt-out and the recipient's
    /// notification history work for this workflow. Without it the handler
    /// guesses from common payload names.
    /// </summary>
    public string? RecipientUserIdExpression { get; init; }

    /// <summary>
    /// Optional Scriban condition on the event data for the auto-created workflow,
    /// e.g. <c>event.responded_via == "link"</c>. The email is sent only when it renders true.
    /// </summary>
    public string? TriggerCondition { get; init; }

    /// <summary>
    /// Whether recipients may turn this notification off from their workspace
    /// preferences. Leave false for business-critical or external mail.
    /// </summary>
    public bool AllowOptOut { get; init; }

    /// <summary>
    /// Authorise the auto-created workflow for the sensitive trigger properties this template and
    /// its recipient expressions reference (and only those), recorded against the module, as
    /// <see cref="WorkflowDefinitionSeed.AuthorizeSensitiveData"/> does for workflow seeds. Without
    /// it those properties are withheld from the email. Leave false when the template needs none.
    /// </summary>
    public bool AuthorizeSensitiveData { get; init; }

    /// <summary>
    /// Send each address the recipient expression yields its own message, so
    /// recipients don't see each other. Off by default: the addresses share
    /// one message.
    /// </summary>
    public bool SendIndividually { get; init; }

    /// <summary>
    /// Scriban expressions that each resolve to a file attachment id at send
    /// time (e.g. "{{event.document_attachment_id}}"). The email action
    /// downloads them and attaches them to the message.
    /// </summary>
    public List<string> AttachmentExpressions { get; init; } = [];

    /// <summary>
    /// Template variables with descriptions and defaults.
    /// </summary>
    public List<EmailTemplateVariableSeed> Variables { get; init; } = [];

    /// <summary>
    /// Display category for the admin UI list (e.g. "Auth", "Marketing",
    /// "Campaign", "Workflow", "Transactional", "Starter"). Purely a tag
    /// for filtering / visual grouping — no code paths gate on it.
    /// </summary>
    public string? Category { get; init; }
}

public class EmailTemplateVariableSeed
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public string? DefaultValue { get; init; }
    public bool Required { get; init; }
}

/// <summary>
/// Content fingerprint of a seeded email template. Stored on the tenant's copy
/// when it is seeded so a later reconciliation can tell "still the seed we
/// wrote" (safe to refresh) from "edited by the tenant" (leave alone).
/// </summary>
public static class EmailTemplateContentHash
{
    public static string Compute(
        string subject,
        string htmlBody,
        string? textBody,
        string? designJson,
        string? description,
        string? category,
        IEnumerable<string>? variableLines)
    {
        var builder = new System.Text.StringBuilder();
        builder.Append(subject).Append('\u001f')
            .Append(htmlBody).Append('\u001f')
            .Append(textBody ?? string.Empty).Append('\u001f')
            .Append(designJson ?? string.Empty).Append('\u001f')
            .Append(description ?? string.Empty).Append('\u001f')
            .Append(category ?? string.Empty);
        foreach (var line in (variableLines ?? Array.Empty<string>()).OrderBy(l => l, StringComparer.Ordinal))
        {
            builder.Append('\u001e').Append(line);
        }

        var bytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(builder.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string VariableLine(string name, string? description, string? defaultValue, bool required) =>
        $"{name}|{description}|{defaultValue}|{(required ? 1 : 0)}";

    public static string Compute(EmailTemplateSeed seed) => Compute(
        seed.Subject, seed.HtmlBody, seed.TextBody, seed.DesignJson, seed.Description, seed.Category,
        seed.Variables.Select(v => VariableLine(v.Name, v.Description, v.DefaultValue, v.Required)));
}

/// <summary>
/// Brings tenants seeded earlier up to date with the module template seeds
/// that exist now: adds templates (and their auto-created workflows) that
/// appeared after the tenant was seeded, and refreshes seeded templates the
/// tenant never edited. Tenant-edited templates and existing workflows are
/// never touched. The platform tenant is reconciled against the platform
/// template seeds, and is included in the all-tenants pass.
/// </summary>
public interface ITenantSeedReconciler
{
    Task<TenantSeedReconciliationResult> ReconcileTenantAsync(string tenantId, CancellationToken cancellationToken = default);

    Task<TenantSeedReconciliationResult> ReconcileAllTenantsAsync(CancellationToken cancellationToken = default);
}

public sealed class TenantSeedReconciliationResult
{
    public int TenantsScanned { get; set; }
    public int TemplatesAdded { get; set; }
    public int TemplatesRefreshed { get; set; }
    public int TemplatesLeftCustomized { get; set; }
    public int WorkflowsAdded { get; set; }
    public List<string> Errors { get; } = new();

    public void Add(TenantSeedReconciliationResult other)
    {
        TenantsScanned += other.TenantsScanned;
        TemplatesAdded += other.TemplatesAdded;
        TemplatesRefreshed += other.TemplatesRefreshed;
        TemplatesLeftCustomized += other.TemplatesLeftCustomized;
        WorkflowsAdded += other.WorkflowsAdded;
        Errors.AddRange(other.Errors);
    }
}

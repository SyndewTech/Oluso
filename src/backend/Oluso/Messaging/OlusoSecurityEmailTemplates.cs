using Oluso.Core.Licensing;
using Oluso.Core.Services;

namespace Oluso.Messaging;

/// <summary>
/// Security notifications seeded into every tenant, each with an auto-created
/// workflow on its event. New tenants get them through TenantSeedEventSink;
/// existing tenants through the startup seed reconciliation, which adds
/// missing templates and creates their workflows.
/// </summary>
public class OlusoSecurityEmailTemplates : ICoreEmailTemplateSeeder
{
    /// <summary>Template name of the module-admin-assigned notification.</summary>
    public const string ModuleAdminAssigned = "security.module_admin_assigned";

    /// <summary>Template name of the module-admin-removed notification.</summary>
    public const string ModuleAdminRemoved = "security.module_admin_removed";

    private const string Who =
        "{{ if event.target_user_name != \"\" }}{{ event.target_user_name }}{{ else }}{{ event.target_user_id }}{{ end }}";

    private const string WhoEmail =
        "{{ if event.target_user_email != \"\" }} ({{ event.target_user_email }}){{ end }}";

    private const string By =
        "{{ if event.admin_user_name != \"\" }}{{ event.admin_user_name }}{{ else }}{{ event.admin_user_id }}{{ end }}";

    private const string When = "{{ event.timestamp | date.to_string \"%d %b %Y %H:%M\" }} UTC";

    private static string Html(string headline, string byLabel) =>
        "<p>Hello,</p>" +
        $"<p><strong>{Who}</strong>{WhoEmail} {headline} <strong>{{{{ event.module_name }}}}</strong>.</p>" +
        "<table role=\"presentation\" cellpadding=\"4\" cellspacing=\"0\" border=\"0\">" +
        "<tr><td><strong>Role</strong></td><td>{{ event.role_name }}</td></tr>" +
        "<tr><td><strong>Permission</strong></td><td>{{ event.module_admin_permission }}</td></tr>" +
        $"<tr><td><strong>{byLabel}</strong></td><td>{By}</td></tr>" +
        $"<tr><td><strong>When</strong></td><td>{When}</td></tr>" +
        "</table>" +
        "<p>You are receiving this because you administer {{ event.module_name }} in this organization. " +
        "If you did not expect this change, review the user's roles in the admin console.</p>";

    private static string Text(string headline, string byLabel) =>
        $"{Who}{WhoEmail} {headline} {{{{ event.module_name }}}}.\n" +
        "Role: {{ event.role_name }}\n" +
        "Permission: {{ event.module_admin_permission }}\n" +
        $"{byLabel}: {By}\n" +
        $"When: {When}\n";

    public IEnumerable<EmailTemplateSeed> GetTenantTemplates()
    {
        yield return Seed(
            name: ModuleAdminAssigned,
            description: "Sent to a module's existing administrators when someone else is made an administrator of it",
            subject: "New {{ event.module_name }} administrator: {{ if event.target_user_name != \"\" }}{{ event.target_user_name }}{{ else }}{{ event.target_user_email }}{{ end }}",
            header: "New module administrator",
            headline: "is now an administrator of",
            byLabel: "Granted by");

        yield return Seed(
            name: ModuleAdminRemoved,
            description: "Sent to a module's remaining administrators when someone stops being an administrator of it",
            subject: "{{ event.module_name }} administrator removed: {{ if event.target_user_name != \"\" }}{{ event.target_user_name }}{{ else }}{{ event.target_user_email }}{{ end }}",
            header: "Module administrator removed",
            headline: "is no longer an administrator of",
            byLabel: "Removed by");
    }

    private static EmailTemplateSeed Seed(
        string name, string description, string subject, string header, string headline, string byLabel)
    {
        var html = Html(headline, byLabel);
        return new EmailTemplateSeed
        {
            Name = name,
            Module = "security",
            Description = description,
            Category = "Security",
            // The template name is the event's webhook type.
            TriggerEventType = name,
            // The module's other administrators in the tenant, resolved when the
            // change is made and carried on the event.
            RecipientExpression = "{{ event.existing_module_admin_emails | array.join \",\" }}",
            TriggerCondition = "(event.existing_module_admin_emails | array.size) > 0",
            // Each admin gets their own message; the others' addresses stay private.
            SendIndividually = true,
            Subject = subject,
            HtmlBody = html,
            TextBody = Text(headline, byLabel),
            DesignJson = UnlayerDesignBuilder.Create().WithAccentColor("#B45309")
                .AddHeader(header)
                .AddText(html)
                .AddFooter("<p>This is an automated security notification.</p>")
                .Build(),
            Variables =
            [
                new() { Name = "event.module_name", Description = "Module display name", Required = true },
                new() { Name = "event.module_admin_permission", Description = "The administration permission", Required = true },
                new() { Name = "event.role_name", Description = "Role that carries the permission", Required = true },
                new() { Name = "event.target_user_id", Description = "User whose administration changed", Required = true },
                new() { Name = "event.target_user_name", Description = "Their display name" },
                new() { Name = "event.target_user_email", Description = "Their email" },
                new() { Name = "event.admin_user_id", Description = "Who made the change (user id or system source)", Required = true },
                new() { Name = "event.admin_user_name", Description = "Who made the change (display name)" },
                new() { Name = "event.timestamp", Description = "When it happened (UTC)" },
                new() { Name = "event.existing_module_admin_emails", Description = "The module's other administrators, who are notified" },
            ],
        };
    }
}

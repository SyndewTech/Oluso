using System.Text.Json;
using Oluso.Core;
using Oluso.Core.Licensing;
using Oluso.Core.Services;

namespace Oluso.Messaging;

/// <summary>
/// Branded "Starter" email template — seeded into <em>every</em> tenant on
/// creation (and re-seedable on host startup) as a Marketing-category
/// starter row that tenants duplicate as the base for their own campaign /
/// announcement / newsletter sends.
///
/// Implements <see cref="ICoreEmailTemplateSeeder"/> (per-tenant scope),
/// not <see cref="IPlatformTemplateSeeder"/> — the row needs to live in
/// each tenant's own templates table so tenant admins can see it without
/// any access to the <c>_platform</c> sentinel rows (which stay
/// SuperAdmin-only).
///
/// Ships with a multi-section responsive HTML body and a matching Unlayer
/// DesignJson so the visual editor opens with editable content rather than
/// a blank canvas.
///
/// Scriban variables resolved per-tenant by IPlatformEmailTemplateService:
///   {{ logo_url }} {{ primary_color }} {{ tenant_name }} {{ support_email }}
/// Recipient-driven variables (supply at send time):
///   {{ user_name }} {{ user_email }}
/// </summary>
public class OlusoStarterEmailTemplates : ICoreEmailTemplateSeeder
{
    public IEnumerable<EmailTemplateSeed> GetTenantTemplates()
    {
        yield return new EmailTemplateSeed
        {
            Name = PlatformTemplateNames.StarterBranded,
            Description = "Branded starter template — duplicate to start a campaign or announcement from a fully-styled base",
            Subject = "A message from {{ tenant_name }}",
            HtmlBody = StarterHtml,
            TextBody = StarterText,
            DesignJson = StarterDesignJson,
            Category = "Marketing",
            Variables =
            [
                new() { Name = PlatformTemplateScopeKeys.TenantName, Description = "Tenant name (auto-filled)" },
                new() { Name = PlatformTemplateScopeKeys.LogoUrl, Description = "Tenant logo URL (auto-filled)" },
                new() { Name = PlatformTemplateScopeKeys.PrimaryColor, Description = "Brand primary colour (auto-filled)" },
                new() { Name = PlatformTemplateScopeKeys.SupportEmail, Description = "Support email shown in the footer (auto-filled)" },
                new() { Name = "user_name", Description = "Recipient display name", Required = false },
                new() { Name = "headline", Description = "Hero headline shown under the logo bar", DefaultValue = "A note from our team" },
                new() { Name = "body_intro", Description = "Opening paragraph", DefaultValue = "We're excited to share what's new." },
                new() { Name = "cta_label", Description = "Call-to-action button text", DefaultValue = "Learn more" },
                new() { Name = "cta_url", Description = "Call-to-action target URL", Required = true, DefaultValue = "#" },
                new() { Name = "unsubscribe_url", Description = "Marketing unsubscribe URL (platform-served at send time)", DefaultValue = "#" },
            ],
        };
    }

    // ─────────────────────────── HTML body ──────────────────────────

    // Table-based email-safe HTML. Inline styles only. 600px max-width.
    // Falls back gracefully when {{ logo_url }} / {{ primary_color }} are
    // missing (renderer blanks unresolved tokens; defaults handled by
    // PlatformEmailTemplateService base scope).
    private const string StarterHtml = """
<!DOCTYPE html PUBLIC "-//W3C//DTD XHTML 1.0 Transitional//EN" "http://www.w3.org/TR/xhtml1/DTD/xhtml1-transitional.dtd">
<html xmlns="http://www.w3.org/1999/xhtml">
<head>
  <meta http-equiv="Content-Type" content="text/html; charset=UTF-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1.0" />
  <title>{{ tenant_name }}</title>
</head>
<body style="margin:0;padding:0;background-color:#f4f5f7;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;">
  <table role="presentation" cellspacing="0" cellpadding="0" border="0" width="100%" style="background-color:#f4f5f7;">
    <tr>
      <td align="center" style="padding:32px 12px;">
        <table role="presentation" cellspacing="0" cellpadding="0" border="0" width="600" style="max-width:600px;width:100%;background-color:#ffffff;border-radius:12px;overflow:hidden;box-shadow:0 1px 3px rgba(15,23,42,0.06);">
          <!-- Logo / header band -->
          <tr>
            <td align="center" style="padding:28px 32px 8px 32px;">
              {{ if logo_url }}<img src="{{ logo_url }}" alt="{{ tenant_name }}" style="display:block;max-height:48px;height:auto;border:0;" />{{ end }}
            </td>
          </tr>
          <!-- Coloured accent bar -->
          <tr>
            <td style="padding:0 32px;">
              <div style="height:4px;background-color:{{ primary_color }};border-radius:2px;"></div>
            </td>
          </tr>
          <!-- Hero headline -->
          <tr>
            <td style="padding:24px 32px 8px 32px;">
              <h1 style="margin:0;font-size:24px;line-height:1.3;color:#0f172a;font-weight:600;">{{ headline }}</h1>
            </td>
          </tr>
          <!-- Body content -->
          <tr>
            <td style="padding:8px 32px 24px 32px;">
              <p style="margin:0 0 16px 0;font-size:15px;line-height:1.6;color:#1e293b;">{{ if user_name }}Hi {{ user_name }},{{ else }}Hello,{{ end }}</p>
              <p style="margin:0 0 16px 0;font-size:15px;line-height:1.6;color:#334155;">{{ body_intro }}</p>
              <p style="margin:0 0 16px 0;font-size:15px;line-height:1.6;color:#334155;">Edit this paragraph in the visual editor to write the rest of your message. Use the merge tags in the right sidebar to insert dynamic values like recipient name, order numbers, or campaign details.</p>
            </td>
          </tr>
          <!-- CTA -->
{{ if cta_url && cta_url != "" && cta_url != "#" }}
          <tr>
            <td align="center" style="padding:8px 32px 24px 32px;">
              <table role="presentation" cellspacing="0" cellpadding="0" border="0">
                <tr>
                  <td style="border-radius:8px;background-color:{{ primary_color }};">
                    <a href="{{ cta_url }}" target="_blank" style="display:inline-block;padding:12px 28px;font-size:15px;font-weight:600;color:#ffffff;text-decoration:none;border-radius:8px;">{{ cta_label }}</a>
                  </td>
                </tr>
              </table>
            </td>
          </tr>
          {{ end }}
          <!-- Divider -->
          <tr>
            <td style="padding:8px 32px;">
              <div style="height:1px;background-color:#e2e8f0;"></div>
            </td>
          </tr>
          <!-- Footer -->
          <tr>
            <td style="padding:16px 32px 24px 32px;">
              <p style="margin:0 0 8px 0;font-size:12px;line-height:1.5;color:#64748b;text-align:center;">Sent by <strong>{{ tenant_name }}</strong>{{ if support_email && support_email != "" }} · <a href="mailto:{{ support_email }}" style="color:#64748b;text-decoration:underline;">{{ support_email }}</a>{{ end }}</p>
              {{ if unsubscribe_url && unsubscribe_url != "" && unsubscribe_url != "#" }}<p style="margin:0;font-size:12px;line-height:1.5;color:#94a3b8;text-align:center;">Don't want these emails? <a href="{{ unsubscribe_url }}" style="color:#94a3b8;text-decoration:underline;">Unsubscribe</a></p>{{ end }}
            </td>
          </tr>
        </table>
      </td>
    </tr>
  </table>
</body>
</html>
""";

    private const string StarterText = """
{{ if user_name }}Hi {{ user_name }},{{ else }}Hello,{{ end }}

{{ body_intro }}

Edit this paragraph in the visual editor to write the rest of your message.

{{ if cta_url && cta_url != "" && cta_url != "#" }}{{ cta_label }}: {{ cta_url }}{{ end }}

—
Sent by {{ tenant_name }}{{ if support_email && support_email != "" }} · {{ support_email }}{{ end }}
{{ if unsubscribe_url && unsubscribe_url != "" && unsubscribe_url != "#" }}Unsubscribe: {{ unsubscribe_url }}{{ end }}
""";

    // ─────────────────── Unlayer DesignJson ───────────────────
    //
    // Minimal but valid Unlayer schema. The body content is wrapped in a
    // single "html" content block so the editor opens with the full
    // template visible and editable. Tenants who want native multi-block
    // editing can rebuild from blocks in Unlayer after duplicating.
    //
    // Authored statically (no JsonSerializer) because the Unlayer schema
    // uses snake_case for some keys and camelCase for others — building
    // it as a C# anonymous object would require attribute decoration we
    // don't want to drag in for one template.
    private static readonly string StarterDesignJson = JsonSerializer.Serialize(new
    {
        counters = new Dictionary<string, int>
        {
            ["u_row"] = 1,
            ["u_column"] = 1,
            ["u_content_html"] = 1,
        },
        body = new
        {
            id = "starter-body",
            rows = new[]
            {
                new
                {
                    id = "row-1",
                    cells = new[] { 1 },
                    columns = new[]
                    {
                        new
                        {
                            id = "col-1",
                            contents = new[]
                            {
                                new
                                {
                                    id = "html-1",
                                    type = "html",
                                    values = new Dictionary<string, object?>
                                    {
                                        ["html"] = StarterHtml,
                                        ["hideDesktop"] = false,
                                        ["displayCondition"] = (object?)null,
                                        ["containerPadding"] = "10px",
                                        ["anchor"] = "",
                                        ["_meta"] = new Dictionary<string, object>
                                        {
                                            ["htmlID"] = "u_content_html_1",
                                            ["htmlClassNames"] = "u_content_html",
                                        },
                                        ["selectable"] = true,
                                        ["draggable"] = true,
                                        ["duplicatable"] = true,
                                        ["deletable"] = true,
                                        ["hideable"] = true,
                                    },
                                },
                            },
                            values = new Dictionary<string, object>
                            {
                                ["backgroundColor"] = "",
                                ["padding"] = "0px",
                                ["border"] = new { },
                                ["_meta"] = new Dictionary<string, object>
                                {
                                    ["htmlID"] = "u_column_1",
                                    ["htmlClassNames"] = "u_column",
                                },
                            },
                        },
                    },
                    values = new Dictionary<string, object>
                    {
                        ["displayCondition"] = (object?)null!,
                        ["columns"] = false,
                        ["backgroundColor"] = "",
                        ["columnsBackgroundColor"] = "",
                        ["backgroundImage"] = new Dictionary<string, object>
                        {
                            ["url"] = "",
                            ["fullWidth"] = true,
                            ["repeat"] = "no-repeat",
                            ["size"] = "custom",
                            ["position"] = "center",
                        },
                        ["padding"] = "0px",
                        ["anchor"] = "",
                        ["hideDesktop"] = false,
                        ["_meta"] = new Dictionary<string, object>
                        {
                            ["htmlID"] = "u_row_1",
                            ["htmlClassNames"] = "u_row",
                        },
                        ["selectable"] = true,
                        ["draggable"] = true,
                        ["duplicatable"] = true,
                        ["deletable"] = true,
                        ["hideable"] = true,
                    },
                },
            },
            headers = Array.Empty<object>(),
            footers = Array.Empty<object>(),
            values = new Dictionary<string, object>
            {
                ["popupPosition"] = "center",
                ["popupWidth"] = "600px",
                ["popupHeight"] = "auto",
                ["borderRadius"] = "10px",
                ["contentAlign"] = "center",
                ["contentVerticalAlign"] = "center",
                ["contentWidth"] = "600px",
                ["fontFamily"] = new Dictionary<string, object>
                {
                    ["label"] = "Arial",
                    ["value"] = "arial,helvetica,sans-serif",
                },
                ["textColor"] = "#0f172a",
                ["popupBackgroundColor"] = "#FFFFFF",
                ["popupBackgroundImage"] = new Dictionary<string, object>
                {
                    ["url"] = "",
                    ["fullWidth"] = true,
                    ["repeat"] = "no-repeat",
                    ["size"] = "cover",
                    ["position"] = "center",
                },
                ["popupOverlay_backgroundColor"] = "rgba(0, 0, 0, 0.1)",
                ["popupCloseButton_position"] = "top-right",
                ["popupCloseButton_backgroundColor"] = "#DDDDDD",
                ["popupCloseButton_iconColor"] = "#000000",
                ["popupCloseButton_borderRadius"] = "0px",
                ["popupCloseButton_margin"] = "0px",
                ["popupCloseButton_action"] = new Dictionary<string, object>
                {
                    ["name"] = "close_popup",
                    ["attrs"] = new Dictionary<string, object>
                    {
                        ["onClick"] = "document.querySelector('.u-popup-container').style.display = 'none';",
                    },
                },
                ["backgroundColor"] = "#f4f5f7",
                ["backgroundImage"] = new Dictionary<string, object>
                {
                    ["url"] = "",
                    ["fullWidth"] = true,
                    ["repeat"] = "no-repeat",
                    ["size"] = "custom",
                    ["position"] = "center",
                },
                ["preheaderText"] = "",
                ["linkStyle"] = new Dictionary<string, object>
                {
                    ["body"] = true,
                    ["linkColor"] = "#0f172a",
                    ["linkHoverColor"] = "#000000",
                    ["linkUnderline"] = true,
                    ["linkHoverUnderline"] = true,
                },
                ["_meta"] = new Dictionary<string, object>
                {
                    ["htmlID"] = "u_body",
                    ["htmlClassNames"] = "u_body",
                },
            },
        },
        schemaVersion = 17,
    });
}

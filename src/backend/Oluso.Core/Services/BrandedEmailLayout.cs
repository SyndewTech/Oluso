using System.Net;
using System.Text.RegularExpressions;

namespace Oluso.Core.Services;

/// <summary>
/// Who an email built in code is from: display name, accent colour and logo.
/// Values are raw; <see cref="BrandedEmailLayout.BrandMark"/> encodes them.
/// </summary>
public sealed record EmailBrand(string Name, string Accent, string? LogoUrl)
{
    /// <summary>
    /// A brand from tenant branding settings. The accent falls back to the product
    /// default when the configured colour is missing or isn't a plain hex colour.
    /// </summary>
    public static EmailBrand From(string? name, string? primaryColor, string? logoUrl) => new(
        name ?? string.Empty,
        BrandedEmailLayout.SafeColor(primaryColor),
        string.IsNullOrWhiteSpace(logoUrl) ? null : logoUrl);
}

/// <summary>
/// The shared look of the product's transactional emails: a centred card with an
/// accent band, a logo (or monogram), an eyebrow and heading, content rows, and a
/// note under the card. Built from email-safe tables with inline styles so it holds
/// up in Outlook and webmail.
///
/// Every argument is inserted as-is: pass HTML-encoded values, or Scriban
/// expressions when building a template seed. Content helpers return table rows,
/// so a seed can wrap any of them in <c>{{ if … }}…{{ end }}</c>.
/// </summary>
public static class BrandedEmailLayout
{
    public const string DefaultAccent = "#4F46E5";

    private const string Font = "'Segoe UI', Helvetica, Arial, sans-serif";
    private const string Ink = "#111127";
    private const string Body = "#4B4B63";
    private const string Muted = "#8A8A9E";

    /// <param name="eyebrow">Small uppercase line above the heading; null or empty to leave it out.</param>
    /// <param name="heading">The heading; null or empty to leave it (and the eyebrow) out.</param>
    public static string Document(
        string title,
        string preheader,
        string accent,
        string brand,
        string? eyebrow,
        string? heading,
        string rows,
        string footnote)
    {
        var brandRow = string.IsNullOrEmpty(brand) ? "" : $$"""
          <tr>
            <td align="center" style="padding: 40px 40px 0;">{{brand}}</td>
          </tr>
""";

        var headingRow = string.IsNullOrEmpty(heading) ? "" : $$"""
          <tr>
            <td align="center" style="padding: {{(string.IsNullOrEmpty(brand) ? "40px" : "28px")}} 40px 0; font-family: {{Font}};">
              {{(string.IsNullOrEmpty(eyebrow) ? "" : $"""<p style="margin: 0 0 10px; font-size: 12px; font-weight: 700; letter-spacing: 2px; text-transform: uppercase; color: {accent};">{eyebrow}</p>""")}}
              <h1 style="margin: 0; font-size: 26px; line-height: 1.3; font-weight: 700; color: {{Ink}};">{{heading}}</h1>
            </td>
          </tr>
""";

        return $$"""
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="utf-8" />
  <meta name="viewport" content="width=device-width, initial-scale=1" />
  <meta name="color-scheme" content="light" />
  <title>{{title}}</title>
  <style>
    .oluso-content p { margin: 0 0 14px; }
    .oluso-content a { color: {{accent}}; }
    .oluso-content ul, .oluso-content ol { margin: 0 0 14px; padding-left: 22px; }
    .oluso-content li { margin-bottom: 6px; }
    .oluso-content h2, .oluso-content h3 { color: {{Ink}}; margin: 20px 0 10px; }
  </style>
</head>
<body style="margin: 0; padding: 0; background-color: #F4F4F7; -webkit-text-size-adjust: 100%;">
  <div style="display: none; max-height: 0; overflow: hidden; opacity: 0;">{{preheader}}</div>
  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background-color: #F4F4F7;">
    <tr>
      <td align="center" style="padding: 40px 16px;">
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="max-width: 560px; background-color: #FFFFFF; border-radius: 16px; overflow: hidden; border: 1px solid #E7E7EC; box-shadow: 0 12px 32px rgba(17, 17, 39, 0.08);">
          <tr>
            <td style="height: 6px; line-height: 6px; font-size: 0; background-color: {{accent}};">&nbsp;</td>
          </tr>
{{brandRow}}
{{headingRow}}
{{rows}}
          <tr>
            <td style="height: 36px; line-height: 36px; font-size: 0;">&nbsp;</td>
          </tr>
        </table>
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="max-width: 560px;">
          <tr>
            <td align="center" style="padding: 24px 24px 0; font-family: {{Font}}; font-size: 12px; line-height: 1.6; color: #9A9AAE;">{{footnote}}</td>
          </tr>
        </table>
      </td>
    </tr>
  </table>
</body>
</html>
""";
    }

    /// <summary>
    /// Free-form HTML (paragraphs, lists, links) as one content row, styled to match.
    /// Used to wrap message bodies that were written as plain fragments.
    /// </summary>
    public static string Content(string html) =>
        $"""<tr><td class="oluso-content" style="padding: 24px 40px 0; font-family: {Font}; font-size: 15px; line-height: 1.65; color: {Body};">{html}</td></tr>""";

    /// <summary>
    /// True when <paramref name="html"/> is already a full document (an editor export or a
    /// hand-built layout) rather than a fragment that still needs the layout around it.
    /// </summary>
    public static bool IsFullDocument(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return false;
        return html.Contains("<html", StringComparison.OrdinalIgnoreCase)
            || html.Contains("<body", StringComparison.OrdinalIgnoreCase)
            || html.Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase);
    }

    private static readonly Regex HexColor = new("^#(?:[0-9a-fA-F]{3}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$", RegexOptions.Compiled);

    /// <summary><paramref name="color"/> when it is a hex colour, otherwise <see cref="DefaultAccent"/>.</summary>
    public static string SafeColor(string? color) =>
        color != null && HexColor.IsMatch(color.Trim()) ? color.Trim() : DefaultAccent;

    /// <summary>The logo, or a monogram of the name when there is no logo, or nothing.</summary>
    public static string BrandMark(EmailBrand brand)
    {
        var name = WebUtility.HtmlEncode(brand.Name);
        if (!string.IsNullOrEmpty(brand.LogoUrl))
            return Logo(WebUtility.HtmlEncode(brand.LogoUrl), name);
        return brand.Name.Length > 0
            ? Monogram(WebUtility.HtmlEncode(brand.Name[..1].ToUpperInvariant()), brand.Accent)
            : string.Empty;
    }

    /// <summary>The brand mark: an image.</summary>
    public static string Logo(string src, string alt) =>
        $"""<img src="{src}" alt="{alt}" height="44" style="display: block; max-height: 44px; width: auto; border: 0;" />""";

    /// <summary>The brand mark when there is no logo: a rounded square with one letter.</summary>
    public static string Monogram(string letter, string accent) =>
        $"""<table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" valign="middle" width="56" height="56" style="width: 56px; height: 56px; border-radius: 14px; background-color: {accent}; color: #FFFFFF; font-family: {Font}; font-size: 24px; font-weight: 700;">{letter}</td></tr></table>""";

    /// <summary>A centred lead paragraph under the heading.</summary>
    public static string Lead(string html) =>
        $"""<tr><td align="center" style="padding: 16px 48px 0; font-family: {Font}; font-size: 16px; line-height: 1.6; color: {Body};">{html}</td></tr>""";

    /// <summary>A left-aligned body paragraph.</summary>
    public static string Paragraph(string html) =>
        $"""<tr><td style="padding: 16px 40px 0; font-family: {Font}; font-size: 15px; line-height: 1.6; color: {Body};">{html}</td></tr>""";

    /// <summary>Emphasis inside <see cref="Lead"/> or <see cref="Paragraph"/>.</summary>
    public static string Strong(string html) => $"""<strong style="color: {Ink};">{html}</strong>""";

    /// <summary>A rounded "Label · value" pill.</summary>
    public static string Pill(string label, string value, string accent) =>
        $"""<tr><td align="center" style="padding: 20px 40px 0;"><table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td style="padding: 7px 16px; border-radius: 999px; background-color: #F1F1F8; border: 1px solid #E4E4EE; font-family: {Font}; font-size: 13px; color: {Body};">{label}&nbsp;&middot;&nbsp;<strong style="color: {accent};">{value}</strong></td></tr></table></td></tr>""";

    /// <summary>A large one-time code in a tinted box.</summary>
    public static string Code(string code, string accent) =>
        $"""<tr><td align="center" style="padding: 28px 40px 0;"><table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" style="padding: 18px 28px; border-radius: 12px; background-color: #F8F8FC; border: 1px dashed #D4D4E2; font-family: 'SFMono-Regular', Menlo, Consolas, monospace; font-size: 34px; font-weight: 700; letter-spacing: 10px; color: {accent};">{code}</td></tr></table></td></tr>""";

    /// <summary>A quoted note with an optional attribution line.</summary>
    public static string Quote(string html, string attribution, string accent) =>
        $"""<tr><td style="padding: 28px 40px 0;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background-color: #F8F8FC; border-radius: 12px; border-left: 4px solid {accent};"><tr><td style="padding: 18px 22px; font-family: Georgia, 'Times New Roman', serif; font-size: 16px; line-height: 1.6; font-style: italic; color: #2E2E44;">&ldquo;{html}&rdquo;{attribution}</td></tr></table></td></tr>""";

    /// <summary>The attribution line for <see cref="Quote"/>.</summary>
    public static string Attribution(string name) =>
        $"""<div style="margin-top: 10px; font-family: {Font}; font-size: 13px; font-style: normal; color: #6B6B80;">&mdash; {name}</div>""";

    /// <summary>A tinted card of label/value lines; build each line with <see cref="Detail"/>.</summary>
    public static string Details(string lines) =>
        $"""<tr><td style="padding: 24px 40px 0;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" style="background-color: #F8F8FC; border-radius: 12px; border: 1px solid #EDEDF2;"><tr><td style="padding: 8px 20px;"><table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0">{lines}</table></td></tr></table></td></tr>""";

    /// <summary>One line of a <see cref="Details"/> card.</summary>
    public static string Detail(string label, string value) =>
        $"""<tr><td valign="top" style="padding: 8px 0; width: 120px; font-family: {Font}; font-size: 13px; color: {Muted};">{label}</td><td valign="top" style="padding: 8px 0; font-family: {Font}; font-size: 14px; color: {Ink}; font-weight: 600;">{value}</td></tr>""";

    /// <summary>The main call to action, as a button that renders in every client.</summary>
    public static string Button(string href, string label, string accent) =>
        $"""<tr><td align="center" style="padding: 32px 40px 8px;"><table role="presentation" cellpadding="0" cellspacing="0" border="0"><tr><td align="center" bgcolor="{accent}" style="border-radius: 10px; background-color: {accent};"><a href="{href}" target="_blank" style="display: inline-block; padding: 15px 36px; font-family: {Font}; font-size: 16px; font-weight: 700; color: #FFFFFF; text-decoration: none; border-radius: 10px;">{label}</a></td></tr></table></td></tr>""";

    /// <summary>A small centred note, e.g. an expiry line.</summary>
    public static string Note(string html) =>
        $"""<tr><td align="center" style="padding: 12px 40px 0; font-family: {Font}; font-size: 13px; line-height: 1.6; color: {Muted};">{html}</td></tr>""";

    /// <summary>A hairline between sections.</summary>
    public static string Divider() =>
        """<tr><td style="padding: 32px 40px 0;"><div style="height: 1px; line-height: 1px; font-size: 0; background-color: #EDEDF2;">&nbsp;</div></td></tr>""";

    /// <summary>The copy-paste link for when the button doesn't work, under a <see cref="Divider"/>.</summary>
    public static string LinkFallback(string href, string accent) =>
        $"""<tr><td style="padding: 20px 40px 0; font-family: {Font}; font-size: 12px; line-height: 1.6; color: {Muted};">Button not working? Paste this link into your browser:<br /><a href="{href}" target="_blank" style="color: {accent}; word-break: break-all;">{href}</a></td></tr>""";
}

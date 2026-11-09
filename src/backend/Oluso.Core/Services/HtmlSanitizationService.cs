using Ganss.Xss;

namespace Oluso.Core.Services;

/// <summary>
/// Service for sanitizing HTML content from rich text editors or admin configuration.
/// Allows safe formatting tags while removing potentially dangerous content (XSS prevention).
/// </summary>
public interface IHtmlSanitizationService
{
    /// <summary>
    /// Sanitize HTML content, allowing only safe tags and attributes.
    /// </summary>
    /// <param name="html">The HTML content to sanitize</param>
    /// <returns>Sanitized HTML with dangerous content removed</returns>
    string Sanitize(string? html);

    /// <summary>
    /// Strip all HTML tags from content, leaving only plain text.
    /// </summary>
    /// <param name="html">The HTML content to strip</param>
    /// <returns>Plain text with all HTML tags removed</returns>
    string StripHtml(string? html);
}

/// <summary>
/// HTML sanitization implementation using HtmlSanitizer library.
/// Configured with an allowlist of safe tags suitable for rich text content.
/// </summary>
public class HtmlSanitizationService : IHtmlSanitizationService
{
    private readonly HtmlSanitizer _sanitizer;

    public HtmlSanitizationService()
    {
        _sanitizer = new HtmlSanitizer();

        // Clear defaults and set explicit allowlist
        _sanitizer.AllowedTags.Clear();
        _sanitizer.AllowedAttributes.Clear();
        _sanitizer.AllowedCssProperties.Clear();

        // Allowed tags for rich text content (terms, privacy policies, descriptions)
        var allowedTags = new[]
        {
            // Structure
            "p", "br", "hr", "div", "span", "section", "article",
            // Headings
            "h1", "h2", "h3", "h4", "h5", "h6",
            // Text formatting
            "b", "strong", "i", "em", "u", "s", "strike", "sub", "sup",
            "small", "mark", "del", "ins", "abbr", "cite", "q",
            // Lists
            "ul", "ol", "li", "dl", "dt", "dd",
            // Links (href will be validated)
            "a",
            // Tables
            "table", "thead", "tbody", "tfoot", "tr", "th", "td", "caption", "colgroup", "col",
            // Other semantic elements
            "blockquote", "pre", "code", "address", "figure", "figcaption",
        };

        foreach (var tag in allowedTags)
        {
            _sanitizer.AllowedTags.Add(tag);
        }

        // Allowed attributes
        _sanitizer.AllowedAttributes.Add("href");      // For links
        _sanitizer.AllowedAttributes.Add("target");    // For link target (_blank)
        _sanitizer.AllowedAttributes.Add("rel");       // For noopener/noreferrer
        _sanitizer.AllowedAttributes.Add("title");     // Tooltips
        _sanitizer.AllowedAttributes.Add("class");     // CSS classes (for styling)
        _sanitizer.AllowedAttributes.Add("id");        // IDs (for anchors)
        _sanitizer.AllowedAttributes.Add("name");      // Named anchors
        _sanitizer.AllowedAttributes.Add("colspan");   // Table cells
        _sanitizer.AllowedAttributes.Add("rowspan");   // Table cells
        _sanitizer.AllowedAttributes.Add("scope");     // Table header scope
        _sanitizer.AllowedAttributes.Add("abbr");      // Table header abbreviation
        _sanitizer.AllowedAttributes.Add("lang");      // Language attribute
        _sanitizer.AllowedAttributes.Add("dir");       // Text direction

        // Allow safe inline styles for basic formatting
        var allowedCss = new[]
        {
            "color", "background-color",
            "text-align", "text-decoration", "text-indent",
            "font-weight", "font-style", "font-size",
            "line-height", "letter-spacing",
            "margin", "margin-top", "margin-bottom", "margin-left", "margin-right",
            "padding", "padding-top", "padding-bottom", "padding-left", "padding-right",
            "border", "border-top", "border-bottom", "border-left", "border-right",
            "border-color", "border-width", "border-style",
            "list-style-type", "list-style-position",
            "width", "max-width", "min-width",
            "height", "max-height", "min-height",
        };

        foreach (var css in allowedCss)
        {
            _sanitizer.AllowedCssProperties.Add(css);
        }

        // Only allow safe URL schemes for href
        _sanitizer.AllowedSchemes.Clear();
        _sanitizer.AllowedSchemes.Add("http");
        _sanitizer.AllowedSchemes.Add("https");
        _sanitizer.AllowedSchemes.Add("mailto");
        _sanitizer.AllowedSchemes.Add("tel");

        // Add rel="noopener noreferrer" to external links for security
        _sanitizer.PostProcessNode += (s, e) =>
        {
            if (e.Node is AngleSharp.Html.Dom.IHtmlAnchorElement anchor)
            {
                var href = anchor.GetAttribute("href");
                if (!string.IsNullOrEmpty(href) &&
                    (href.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                     href.StartsWith("https://", StringComparison.OrdinalIgnoreCase)))
                {
                    anchor.SetAttribute("rel", "noopener noreferrer");
                    // Only set target="_blank" for external links
                    if (anchor.GetAttribute("target") == null)
                    {
                        anchor.SetAttribute("target", "_blank");
                    }
                }
            }
        };
    }

    public string Sanitize(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        return _sanitizer.Sanitize(html);
    }

    public string StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
        {
            return string.Empty;
        }

        // Use a sanitizer with no allowed tags to strip all HTML
        var stripper = new HtmlSanitizer();
        stripper.AllowedTags.Clear();
        stripper.AllowedAttributes.Clear();
        stripper.AllowedCssProperties.Clear();

        var stripped = stripper.Sanitize(html);

        // Decode HTML entities and normalize whitespace
        stripped = System.Net.WebUtility.HtmlDecode(stripped);
        stripped = System.Text.RegularExpressions.Regex.Replace(stripped, @"\s+", " ").Trim();

        return stripped;
    }
}

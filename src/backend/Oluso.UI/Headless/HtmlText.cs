using System.Net;
using System.Text.RegularExpressions;

namespace Oluso.UI.Headless;

/// <summary>Markup a hosted view renders as HTML, reduced to the plain text a form carries.</summary>
internal static partial class HtmlText
{
    public static string? ToPlainText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;

        var text = ScriptOrStyle().Replace(html, string.Empty);
        text = LineBreak().Replace(text, "\n");
        text = BlockEnd().Replace(text, "\n");
        text = Tag().Replace(text, string.Empty);
        text = WebUtility.HtmlDecode(text);

        var lines = text.Split('\n').Select(l => Spaces().Replace(l, " ").Trim());
        text = string.Join("\n", lines);
        text = BlankLines().Replace(text, "\n\n").Trim();
        return text.Length == 0 ? null : text;
    }

    [GeneratedRegex(@"<(script|style)\b[^>]*>.*?</\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
    private static partial Regex ScriptOrStyle();

    [GeneratedRegex(@"<br\s*/?>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreak();

    [GeneratedRegex(@"</(p|div|li|h[1-6]|tr|ul|ol|section|article|blockquote)\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex BlockEnd();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex Tag();

    [GeneratedRegex(@"[ \t\r\f\v]+")]
    private static partial Regex Spaces();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankLines();
}

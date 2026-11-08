using Microsoft.AspNetCore.Http;

namespace Oluso.Middleware;

/// <summary>
/// Which sites may show a page in a frame. Every HTML page the platform serves may be framed only by its own origin
/// (so a sign-in, consent or form page can't be laid under another site's clicks); a page that is meant to be embedded
/// names the other origins it trusts here, and <see cref="ActiveContentHeadersMiddleware"/> writes the policy.
/// </summary>
public static class FramePolicy
{
    private const string ItemKey = "Oluso.FramePolicy.Origins";

    /// <summary>Lets <paramref name="origins"/> frame this response too. Anything that isn't an http(s) origin is ignored.</summary>
    public static void Allow(HttpContext context, IEnumerable<string>? origins)
    {
        if (origins == null) return;
        if (context.Items[ItemKey] is not HashSet<string> allowed)
        {
            allowed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            context.Items[ItemKey] = allowed;
        }
        foreach (var origin in origins)
        {
            if (Normalize(origin) is { } normalized) allowed.Add(normalized);
        }
    }

    /// <summary>Lets the origins in a comma-separated list (a journey's allowed origins) frame this response.</summary>
    public static void Allow(HttpContext context, string? commaSeparatedOrigins) =>
        Allow(context, commaSeparatedOrigins?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

    /// <summary>Lets the origin of <paramref name="url"/> (a client's validated redirect URI) frame this response.</summary>
    public static void AllowOriginOf(HttpContext context, string? url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            Allow(context, [uri.GetLeftPart(UriPartial.Authority)]);
    }

    internal static IReadOnlyCollection<string> AllowedOrigins(HttpContext context) =>
        context.Items[ItemKey] as HashSet<string> ?? (IReadOnlyCollection<string>)Array.Empty<string>();

    /// <summary>The origin <paramref name="origin"/> names (scheme, host and port), or null when it isn't a bare http(s) origin.</summary>
    public static string? Normalize(string? origin)
    {
        if (string.IsNullOrWhiteSpace(origin)
            || !Uri.TryCreate(origin.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            return null;
        }
        return uri.GetLeftPart(UriPartial.Authority);
    }
}

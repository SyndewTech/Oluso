namespace Oluso.Core.Storage;

/// <summary>
/// Single source of truth for which storage paths are PUBLIC (anonymous,
/// CDN-frontable) versus private. Used in two places that must agree:
///
/// <list type="bullet">
///   <item>the upload path — an upload whose folder is public is written with
///   <see cref="FileAccessLevel.Public"/> and gets a stable public URL;</item>
///   <item>the anonymous serve/resize endpoint — it refuses to read any path
///   that is NOT public, so it can never expose private blobs (all modules
///   share the same underlying container) by path guessing.</item>
/// </list>
///
/// There is deliberately no per-request "make this public" flag a caller could
/// forget or abuse: public-ness is derived from the path prefix alone. Core
/// owns only its own prefixes; every other prefix is contributed by a module
/// via <see cref="IPublicAssetPathProvider"/>, so core stays decoupled from the
/// modules that store public assets.
/// </summary>
public interface IPublicAssetPathRegistry
{
    /// <summary>
    /// True if <paramref name="pathOrFolder"/> is under an allowlisted public
    /// prefix. Accepts both a bare folder (<c>profile-pictures</c>) and a full
    /// storage path (<c>profile-pictures/{tenant}/{guid}.jpg</c>); leading slashes and
    /// back-slashes are normalised first.
    /// </summary>
    bool IsPublic(string? pathOrFolder);

    /// <summary>
    /// True if uploads under this path should have responsive size variants
    /// pre-generated (the matching prefix opted in).
    /// </summary>
    bool GeneratesVariants(string? pathOrFolder);
}

/// <inheritdoc cref="IPublicAssetPathRegistry"/>
public sealed class PublicAssetPathRegistry : IPublicAssetPathRegistry
{
    // Core-owned public prefixes. Everything else is contributed by modules
    // through IPublicAssetPathProvider — keep this list to platform-level assets.
    private static readonly PublicAssetPrefix[] CorePrefixes =
    {
        new("public/"),
        new("profile-pictures/", GeneratesVariants: true),
    };

    private readonly IReadOnlyList<PublicAssetPrefix> _prefixes;

    public PublicAssetPathRegistry(IEnumerable<IPublicAssetPathProvider> providers)
    {
        var all = new List<PublicAssetPrefix>(CorePrefixes);
        foreach (var provider in providers)
            all.AddRange(provider.GetPrefixes());
        _prefixes = all;
    }

    public bool IsPublic(string? pathOrFolder) => Match(pathOrFolder, static _ => true);

    public bool GeneratesVariants(string? pathOrFolder) =>
        Match(pathOrFolder, static p => p.GeneratesVariants);

    private bool Match(string? pathOrFolder, Func<PublicAssetPrefix, bool> predicate)
    {
        if (string.IsNullOrWhiteSpace(pathOrFolder)) return false;

        var normalized = pathOrFolder.Replace('\\', '/').TrimStart('/');
        // Treat a bare folder ("profile-pictures") as if it ended in '/'.
        if (!normalized.EndsWith('/') && !normalized.Contains('.'))
            normalized += "/";

        foreach (var prefix in _prefixes)
        {
            if (normalized.StartsWith(prefix.Prefix, StringComparison.OrdinalIgnoreCase))
                return predicate(prefix);
        }
        return false;
    }
}

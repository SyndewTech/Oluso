namespace Oluso.Core.Storage;

/// <summary>
/// A module's contribution to the public-asset allowlist. Any module that
/// stores anonymously-served assets registers one of these (via DI) instead of
/// its prefixes being hardcoded in core. Collected as
/// <c>IEnumerable&lt;IPublicAssetPathProvider&gt;</c> by
/// <see cref="IPublicAssetPathRegistry"/>.
/// </summary>
public interface IPublicAssetPathProvider
{
    IEnumerable<PublicAssetPrefix> GetPrefixes();
}

/// <summary>
/// A single allowlisted public storage prefix the owning module declares.
/// <paramref name="GeneratesVariants"/> opts the prefix into responsive image
/// size-variant pre-generation on upload.
/// </summary>
public sealed record PublicAssetPrefix(string Prefix, bool GeneratesVariants = false);

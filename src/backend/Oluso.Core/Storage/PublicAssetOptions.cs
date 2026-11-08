namespace Oluso.Core.Storage;

/// <summary>
/// Provider-agnostic configuration for serving and resizing PUBLIC assets.
/// Injected by the public serve/resize endpoints and the URL helper, which
/// can't read the provider-specific <see cref="FileUploaderOptions"/>.
/// </summary>
public sealed class PublicAssetOptions
{
    public const string SectionName = "Storage:PublicAssets";

    /// <summary>
    /// Absolute origin (no trailing slash) used to build app-proxy URLs when the
    /// storage provider has no direct public URL (local/dev — the file is served
    /// by the anonymous endpoint). When null, the request origin is used, so no
    /// configuration is needed for local development.
    /// </summary>
    public string? ProxyBaseUrl { get; set; }

    /// <summary>
    /// Origin that performs on-demand image resizing using Cloudflare Image
    /// Resizing's native scheme: <c>{base}/cdn-cgi/image/{options}/{sourceUrl}</c>.
    /// In production this is the Cloudflare zone in front of the assets. When
    /// null, the asset's own origin is used and a local dev shim resizes instead
    /// — the URL shape is identical either way (never a custom format).
    /// </summary>
    public string? ImageResizeBaseUrl { get; set; }
}

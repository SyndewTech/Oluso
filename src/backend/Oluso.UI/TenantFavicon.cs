using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.UI;

/// <summary>
/// The tab icon of the server-rendered pages (sign-in, journeys, errors): the tenant's branding icon, or an explicit "no
/// icon" when it has none, so the browser doesn't go looking for a /favicon.ico the host doesn't serve.
/// </summary>
public static class TenantFavicon
{
    /// <summary>An empty icon: tells the browser there is none.</summary>
    public const string None = "data:,";

    public static async Task<string> HrefAsync(ITenantSettingsProvider settings, ILogger logger, CancellationToken ct = default)
    {
        try
        {
            var url = (await settings.GetBrandingSettingsAsync(ct))?.FaviconUrl;
            return string.IsNullOrWhiteSpace(url) ? None : url;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to load tenant branding for the page icon; the page shows none");
            return None;
        }
    }
}

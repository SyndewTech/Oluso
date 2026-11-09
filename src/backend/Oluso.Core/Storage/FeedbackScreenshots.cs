namespace Oluso.Core.Storage;

/// <summary>
/// Where beta feedback screenshots live: feedback/{tenantId}/..., whichever portal uploaded them. A submission keeps the
/// storage path and the screenshot is always read back through the API, so it never depends on a signed link that
/// expires or a storage origin the browser can't reach.
/// </summary>
public static class FeedbackScreenshots
{
    public static string Folder(string tenantId) => $"feedback/{tenantId}/";

    /// <summary>True when the path is inside the tenant's feedback folder and doesn't step out of it.</summary>
    public static bool IsInTenantFolder(string tenantId, string path) =>
        path.StartsWith(Folder(tenantId), StringComparison.Ordinal)
        && !path.Split('/').Any(segment => segment is "." or "..");

    /// <summary>
    /// The storage path a stored screenshot reference points at. Submissions saved before this held the upload's
    /// signed blob link or an /api/.../files/ address; the path is recovered from the tenant's feedback folder in it.
    /// Null when the reference is not in that folder.
    /// </summary>
    public static string? StoragePath(string tenantId, string reference)
    {
        var withoutQuery = reference.Split('?', '#')[0];
        var decoded = Uri.UnescapeDataString(withoutQuery);
        var start = decoded.IndexOf(Folder(tenantId), StringComparison.Ordinal);
        if (start < 0) return null;
        var path = decoded[start..];
        return IsInTenantFolder(tenantId, path) ? path : null;
    }
}

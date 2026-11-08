using Oluso.Core.Licensing;

namespace Oluso.Core.Services;

/// <summary>
/// Implemented by libraries that ship platform-level email templates with
/// the product (e.g. Oluso's own auth flows). A template bootstrapper, when
/// one is registered, resolves every <c>IPlatformTemplateSeeder</c>
/// at startup and persists each declared template under the
/// <see cref="PlatformConstants.TenantId"/> sentinel row, so platform
/// defaults are always available before any tenant customises them.
/// </summary>
public interface IPlatformTemplateSeeder
{
    /// <summary>
    /// Templates this library wants seeded as platform defaults. Missing
    /// names are added and rows nobody edited are refreshed when the shipped
    /// content changes; edited rows are left alone. Safe to call on every startup.
    /// </summary>
    IEnumerable<EmailTemplateSeed> GetPlatformTemplates();
}

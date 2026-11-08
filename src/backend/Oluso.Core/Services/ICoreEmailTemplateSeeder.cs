using Oluso.Core.Licensing;

namespace Oluso.Core.Services;

/// <summary>
/// Contributes email templates that should be seeded into <em>every</em>
/// new tenant on creation (and re-seedable for existing tenants via a
/// backfill pass). Distinct from <see cref="IPlatformTemplateSeeder"/>,
/// which seeds rows under the <c>_platform</c> sentinel tenant only.
///
/// Use this for tenant-discoverable starter / sample content that
/// tenants are expected to duplicate or edit — not for system-wide auth
/// templates that should live only in <c>_platform</c>.
///
/// Resolved as <c>IEnumerable&lt;ICoreEmailTemplateSeeder&gt;</c> by
/// <c>TenantSeedEventSink</c> on the <c>AdminTenantCreatedEvent</c> path
/// and merged with module-contributed templates before persistence.
/// </summary>
public interface ICoreEmailTemplateSeeder
{
    IEnumerable<EmailTemplateSeed> GetTenantTemplates();
}

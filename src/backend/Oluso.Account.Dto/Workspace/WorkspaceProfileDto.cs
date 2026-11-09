namespace Oluso.Account.Dto.Workspace;

public class WorkspaceProfileDto
{
    public string Id { get; set; } = null!;
    public string UserName { get; set; } = null!;
    public string? Email { get; set; }
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string DisplayName { get; set; } = null!;
    public string? ProfilePictureUrl { get; set; }
    public string TenantId { get; set; } = null!;
    public string? TenantName { get; set; }
    public string? OrganizationName { get; set; }
    public List<string> Roles { get; set; } = new();
    public List<string> Permissions { get; set; } = new();
    public List<string> Features { get; set; } = new();
    /// <summary>
    /// Per-user module seats granted in this tenant. Drives the workspace
    /// app switcher's <c>requiresSeat</c> filter — without a seat the user
    /// can't see the module's app even if the tenant has the feature
    /// licensed (Features list).
    /// </summary>
    public List<string> ModuleSeats { get; set; } = new();

    /// <summary>
    /// Additional data populated by workspace profile enrichers (e.g., a module).
    /// Keys are enricher IDs, values are enricher-specific data objects.
    /// </summary>
    public IDictionary<string, object> Extensions { get; set; } = new Dictionary<string, object>();
}

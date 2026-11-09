namespace Oluso.Admin.Dto.Scopes;

/// <summary>
/// The set of Service API permissions a client can be granted, served to the
/// admin UI so scope names never have to be typed by hand.
/// </summary>
public class ServiceApiCatalogDto
{
    /// <summary>
    /// The scope that turns Service API access on. A client holding this scope
    /// passes the ServiceApi policy; the granular scopes below decide what it
    /// can actually call.
    /// </summary>
    public string BaseScope { get; set; } = null!;

    /// <summary>
    /// Grant types the client must allow for these scopes to be usable.
    /// </summary>
    public List<string> RequiredGrantTypes { get; set; } = new();

    /// <summary>
    /// Granular permissions, in display order.
    /// </summary>
    public List<ServiceApiScopeDto> Scopes { get; set; } = new();
}

/// <summary>
/// One grantable Service API permission.
/// </summary>
public class ServiceApiScopeDto
{
    public string Name { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string Description { get; set; } = null!;

    /// <summary>
    /// Grouping key for the UI (e.g. "Users", "Roles").
    /// </summary>
    public string Group { get; set; } = null!;

    /// <summary>
    /// A sensitive scope: only an admin who holds <see cref="RequiredAdminPermission"/>
    /// (granted by name, never by wildcard) can give it to a client.
    /// </summary>
    public bool RequiresExplicitGrant { get; set; }

    /// <summary>
    /// The admin permission a sensitive scope requires the saving admin to hold.
    /// </summary>
    public string? RequiredAdminPermission { get; set; }
}

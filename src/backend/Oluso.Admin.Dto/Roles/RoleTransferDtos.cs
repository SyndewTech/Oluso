namespace Oluso.Admin.Dto.Roles;

/// <summary>
/// An exported set of roles: plain JSON, so a file can be kept in a repo and compared between environments. Carries
/// the definitions of the entitlements its roles grant, so an environment that lacks them can create them on import.
/// </summary>
public class RoleExportDocument
{
    public const string CurrentFormat = "oluso-roles";
    public const int CurrentVersion = 1;

    public string Format { get; set; } = CurrentFormat;
    public int Version { get; set; } = CurrentVersion;
    public DateTime ExportedAt { get; set; }
    public List<RoleExportItem> Roles { get; set; } = new();
    public List<EntitlementExportItem> Entitlements { get; set; } = new();
}

public class RoleExportItem
{
    public string Name { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public List<string> Permissions { get; set; } = new();
    public List<string> Entitlements { get; set; } = new();

    /// <summary>Claims other than permissions and entitlements, which have their own lists.</summary>
    public List<RoleClaimDto> Claims { get; set; } = new();
}

public class EntitlementExportItem
{
    public string Name { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public string? Group { get; set; }
}

public class ExportRolesRequest
{
    public List<string> RoleIds { get; set; } = new();
}

public class ImportRolesRequest
{
    /// <summary>The exported file's text.</summary>
    public string Content { get; set; } = string.Empty;

    /// <summary>Update roles that already exist by name instead of skipping them.</summary>
    public bool ReplaceExisting { get; set; }
}

/// <summary>What an import does, or did, for each role in the file.</summary>
public class RoleImportResultDto
{
    /// <summary>False when nothing can be imported; <see cref="Error"/> says why.</summary>
    public bool CanImport { get; set; }
    public string? Error { get; set; }

    /// <summary>True once the import has been applied; false for a preview.</summary>
    public bool Applied { get; set; }

    public List<RoleImportItemDto> Roles { get; set; } = new();

    /// <summary>Entitlements the file uses that this tenant lacks; the import creates them.</summary>
    public List<string> EntitlementsToCreate { get; set; } = new();
}

public class RoleImportItemDto
{
    public string Name { get; set; } = null!;

    /// <summary>create, update, skip (exists and not replacing) or invalid.</summary>
    public string Action { get; set; } = null!;
    public string? Reason { get; set; }

    /// <summary>Permissions left out: unknown here, or not ones the importer may grant.</summary>
    public List<string> DroppedPermissions { get; set; } = new();

    /// <summary>Claim types left out because they are reserved.</summary>
    public List<string> DroppedClaims { get; set; } = new();

    /// <summary>Entitlement names left out because they are not valid names.</summary>
    public List<string> DroppedEntitlements { get; set; } = new();

    /// <summary>Set after applying when this role could not be written.</summary>
    public string? Error { get; set; }
}

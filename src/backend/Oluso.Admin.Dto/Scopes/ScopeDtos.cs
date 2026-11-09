namespace Oluso.Admin.Dto.Scopes;

public class ApiScopeDto
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public bool Required { get; set; }
    public bool Emphasize { get; set; }
    public bool ShowInDiscoveryDocument { get; set; }
    public bool Enabled { get; set; }
    public List<string> UserClaims { get; set; } = new();

    /// <summary>
    /// True for platform-managed scopes (e.g. the Service API scopes). These
    /// are provisioned when a client is granted service access and are rejected
    /// by the update and delete endpoints.
    /// </summary>
    public bool NonEditable { get; set; }

    /// <summary>
    /// A prefix scope matches every scope value that starts with its name followed by a client-chosen suffix
    /// (e.g. <c>device:&lt;id&gt;</c> for the prefix scope <c>device:</c>). Null on update leaves it, and the two suffix
    /// fields, as they are.
    /// </summary>
    public bool? IsPrefix { get; set; }

    /// <summary>
    /// Prefix scopes only: a regular expression the whole suffix must match (property <c>suffix_pattern</c>). Null or
    /// empty: any characters a scope token can contain. Applied on update when <see cref="IsPrefix"/> is set; empty
    /// then removes it.
    /// </summary>
    public string? SuffixPattern { get; set; }

    /// <summary>
    /// Prefix scopes only: the longest suffix accepted (property <c>suffix_max_length</c>). Null: the server default.
    /// Applied on update when <see cref="IsPrefix"/> is set; null then removes it.
    /// </summary>
    public int? SuffixMaxLength { get; set; }
}

public class CreateApiScopeRequest
{
    public string Name { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public bool? Required { get; set; }
    public bool? Emphasize { get; set; }
    public bool? ShowInDiscoveryDocument { get; set; }
    public bool? Enabled { get; set; }
    public List<string>? UserClaims { get; set; }

    /// <summary>Create a prefix scope (see <see cref="ApiScopeDto.IsPrefix"/>).</summary>
    public bool? IsPrefix { get; set; }

    /// <summary>See <see cref="ApiScopeDto.SuffixPattern"/>; ignored unless <see cref="IsPrefix"/>.</summary>
    public string? SuffixPattern { get; set; }

    /// <summary>See <see cref="ApiScopeDto.SuffixMaxLength"/>; ignored unless <see cref="IsPrefix"/>.</summary>
    public int? SuffixMaxLength { get; set; }
}

public class IdentityResourceDto
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public bool Required { get; set; }
    public bool Emphasize { get; set; }
    public bool ShowInDiscoveryDocument { get; set; }
    public bool Enabled { get; set; }
    public List<string> UserClaims { get; set; } = new();
    public DateTime Created { get; set; }
    public DateTime? Updated { get; set; }

    /// <summary>
    /// A standard OIDC scope the server provides itself, not a row the organisation registered
    /// (Id is 0). Registering one with the same name overrides its claims.
    /// </summary>
    public bool BuiltIn { get; set; }
}

public class CreateIdentityResourceRequest
{
    public string Name { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public bool? Required { get; set; }
    public bool? Emphasize { get; set; }
    public bool? ShowInDiscoveryDocument { get; set; }
    public bool? Enabled { get; set; }
    public List<string>? UserClaims { get; set; }
}

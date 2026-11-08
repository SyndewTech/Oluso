namespace Oluso.Admin.Dto.Resources;

public class ResourceDto
{
    public int Id { get; set; }
    public string Uri { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public bool Enabled { get; set; }
    public bool ShowInDiscoveryDocument { get; set; }
    public List<string> AllowedScopes { get; set; } = new();
    public List<string> UserClaims { get; set; } = new();
    public DateTime Created { get; set; }
    public DateTime? Updated { get; set; }
}

public class CreateResourceRequest
{
    public string Uri { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
    public bool? Enabled { get; set; }
    public bool? ShowInDiscoveryDocument { get; set; }
    public List<string>? AllowedScopes { get; set; }
    public List<string>? UserClaims { get; set; }
}

public class AddScopeRequest
{
    public string ScopeName { get; set; } = null!;
}

public class ApiScopeSummaryDto
{
    public string Name { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? Description { get; set; }
}

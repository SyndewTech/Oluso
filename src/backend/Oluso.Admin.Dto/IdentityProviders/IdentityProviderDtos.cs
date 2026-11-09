using Oluso.Core.Domain.Entities;

namespace Oluso.Admin.Dto.IdentityProviders;

public class IdentityProviderDto
{
    public int Id { get; set; }
    public string Scheme { get; set; } = null!;
    public string? DisplayName { get; set; }
    public bool Enabled { get; set; }
    public ExternalProviderType ProviderType { get; set; }
    public string? IconUrl { get; set; }
    public int DisplayOrder { get; set; }
    public List<string> AllowedClientIds { get; set; } = new();
    public Dictionary<string, object>? Configuration { get; set; }
    public DateTime Created { get; set; }
    public DateTime? Updated { get; set; }
    public DateTime? LastAccessed { get; set; }
}

public class IdentityProviderEditDto
{
    public int Id { get; set; }
    public string Scheme { get; set; } = null!;
    public string? DisplayName { get; set; }
    public bool Enabled { get; set; }
    public ExternalProviderType ProviderType { get; set; }
    public string? IconUrl { get; set; }
    public int DisplayOrder { get; set; }
    public List<string> AllowedClientIds { get; set; } = new();
    public Dictionary<string, object>? Configuration { get; set; }
    public bool NonEditable { get; set; }
    public DateTime Created { get; set; }
    public DateTime? Updated { get; set; }
    public DateTime? LastAccessed { get; set; }
}

public class CreateIdentityProviderRequest
{
    public string Scheme { get; set; } = null!;
    public string? DisplayName { get; set; }
    public bool? Enabled { get; set; }
    public ExternalProviderType ProviderType { get; set; }
    public string? IconUrl { get; set; }
    public int? DisplayOrder { get; set; }
    public List<string>? AllowedClientIds { get; set; }
    public Dictionary<string, object>? Configuration { get; set; }
}

public class UpdateIdentityProviderRequest
{
    public string? DisplayName { get; set; }
    public bool? Enabled { get; set; }
    public ExternalProviderType? ProviderType { get; set; }
    public string? IconUrl { get; set; }
    public int? DisplayOrder { get; set; }
    public List<string>? AllowedClientIds { get; set; }
    public Dictionary<string, object>? Configuration { get; set; }
}

public class ProviderTypeInfo
{
    public ExternalProviderType Type { get; set; }
    public string Name { get; set; } = null!;
    public string Description { get; set; } = null!;
    public string? IconUrl { get; set; }
    public string[] RequiredFields { get; set; } = Array.Empty<string>();
    public string[] OptionalFields { get; set; } = Array.Empty<string>();
}

public class TestResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = null!;
}

public class ServerInfoResponse
{
    public string IssuerUri { get; set; } = null!;
    public string? AutoSubdomain { get; set; }
    public string CallbackPathTemplate { get; set; } = null!;
}

public class SchemeAvailabilityResponse
{
    public bool Available { get; set; }
    public string? Message { get; set; }
}

public class SecretRevealResponse
{
    public string Value { get; set; } = null!;
}

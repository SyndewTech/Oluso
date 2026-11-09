namespace Oluso.Admin.Dto.Clients;

public class ClientConnectionDto
{
    public string ClientId { get; set; } = null!;
    public string? ClientName { get; set; }
    public bool RequireClientSecret { get; set; }
    public bool RequirePkce { get; set; }
    public List<string> AllowedGrantTypes { get; set; } = new();
    public List<string> AllowedScopes { get; set; } = new();
    public List<string> RedirectUris { get; set; } = new();
    public List<string> PostLogoutRedirectUris { get; set; } = new();
    public List<string> AllowedCorsOrigins { get; set; } = new();
    public string IssuerUri { get; set; } = null!;
    public string DiscoveryUrl { get; set; } = null!;
    public string JwksUri { get; set; } = null!;
    public string AuthorizationEndpoint { get; set; } = null!;
    public string TokenEndpoint { get; set; } = null!;
    public string UserInfoEndpoint { get; set; } = null!;
    public string EndSessionEndpoint { get; set; } = null!;
    public string IntrospectionEndpoint { get; set; } = null!;
    public string RevocationEndpoint { get; set; } = null!;
}

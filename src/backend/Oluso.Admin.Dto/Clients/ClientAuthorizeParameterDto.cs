namespace Oluso.Admin.Dto.Clients;

/// <summary>
/// Allowlists a custom /authorize request parameter this client may pass and
/// have surfaced as a token claim. <see cref="ClaimType"/> must not be a
/// reserved/system claim.
/// </summary>
public class ClientAuthorizeParameterDto
{
    public string Parameter { get; set; } = null!;
    public string ClaimType { get; set; } = null!;
    public bool Required { get; set; }
}

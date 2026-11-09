namespace Oluso.Core.Domain.Entities;

/// <summary>
/// Allowlists a single custom request parameter that this client may pass and
/// have surfaced as a token claim. Applies to both the front-channel /authorize
/// call (auth-code / implicit) and the back-channel token request of the
/// client_credentials grant (where the value rides the client-authenticated
/// token body instead of a browser redirect).
///
/// SECURITY MODEL:
/// - This is an ALLOWLIST. A parameter the client sends is only carried forward
///   if a row here names it. Anything not listed is ignored — the server never
///   reflects unknown parameters back to anyone.
/// - <see cref="ClaimType"/> is the claim the value is emitted under at token
///   issuance. It is validated against <c>ReservedClaimKeys</c> on write (admin
///   /DCR), again when the claim is emitted, and a final time by the
///   <c>ReservedAuthInterceptor</c> at SaveChanges — so a client can never map
///   a custom parameter onto a system-managed claim (sub, role, tenant_id,
///   permissions, module_seats, …).
/// - Setting these via DCR is privileged: only a registration authenticated
///   with the tenant's required initial-access-token scope/claim may declare
///   them (see OidcDynamicRegistrationController). Open/self-service DCR cannot.
/// </summary>
public class ClientAuthorizeParameter : TenantEntity
{
    /// <summary>
    /// Prefix under which a captured authorize-parameter value is transported
    /// through the authorization code's claim bag from the /authorize request
    /// to the token endpoint. Stripped before token emission — only the mapped
    /// <see cref="ClaimType"/> reaches the token.
    /// </summary>
    public const string TransportClaimPrefix = "authz_param:";

    public int Id { get; set; }
    public int ClientId { get; set; }
    public Client Client { get; set; } = default!;

    /// <summary>
    /// The authorize-request parameter name the client is permitted to send
    /// (e.g. "branch_id"). Case-sensitive, matched against the raw query/form.
    /// </summary>
    public string Parameter { get; set; } = default!;

    /// <summary>
    /// The claim type the parameter's value is emitted under in issued tokens
    /// (e.g. "branch"). Must not be a reserved/system claim key.
    /// </summary>
    public string ClaimType { get; set; } = default!;

    /// <summary>
    /// When true, the /authorize request is rejected if the client does not
    /// supply this parameter. When false, a missing parameter is simply skipped.
    /// </summary>
    public bool Required { get; set; }
}

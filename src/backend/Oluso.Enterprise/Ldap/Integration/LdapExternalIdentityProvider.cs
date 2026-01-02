using System.Security.Claims;
using Oluso.Core.Services;
using Oluso.Enterprise.Ldap.Authentication;
using Oluso.Enterprise.Ldap.GroupMapping;

namespace Oluso.Enterprise.Ldap.Integration;

/// <summary>
/// LDAP implementation of IDirectIdentityProvider.
/// Authenticates users against LDAP/Active Directory servers.
/// </summary>
public class LdapExternalIdentityProvider : IDirectIdentityProvider
{
    private readonly ILdapAuthenticator _authenticator;
    private readonly ILdapGroupMapper _groupMapper;

    public LdapExternalIdentityProvider(
        ILdapAuthenticator authenticator,
        ILdapGroupMapper groupMapper)
    {
        _authenticator = authenticator;
        _groupMapper = groupMapper;
    }

    public string ProviderType => "ldap";

    public async Task<DirectIdentityResult> AuthenticateAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        var result = await _authenticator.AuthenticateAsync(username, password, cancellationToken);

        if (!result.Success || result.User == null)
        {
            return DirectIdentityResult.Failed(result.ErrorMessage ?? "Authentication failed");
        }

        // Get user groups and map to roles
        var groups = await _groupMapper.GetUserGroupsAsync(result.User.DistinguishedName, cancellationToken);
        var roles = _groupMapper.MapGroupsToRoles(groups);

        // Build claims with roles
        var claims = new List<Claim>(result.Claims);
        foreach (var role in roles)
        {
            if (!claims.Any(c => c.Type == ClaimTypes.Role && c.Value == role))
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }
        }

        // Add groups as claims too
        foreach (var group in groups)
        {
            claims.Add(new Claim("groups", group));
        }

        var identity = new ClaimsIdentity(claims, ProviderType);
        var principal = new ClaimsPrincipal(identity);

        return DirectIdentityResult.Success(
            result.User.DistinguishedName,
            result.User.Username,
            principal);
    }
}

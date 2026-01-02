using System.DirectoryServices.Protocols;
using System.Net;
using System.Security.Claims;
using Oluso.Enterprise.Ldap.Configuration;
using Oluso.Enterprise.Ldap.Connection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Oluso.Enterprise.Ldap.Authentication;

public class LdapAuthenticator : ILdapAuthenticator
{
    private readonly LdapOptions _options;
    private readonly ILdapConnectionPool _connectionPool;
    private readonly ILogger<LdapAuthenticator> _logger;

    public LdapAuthenticator(
        IOptions<LdapOptions> options,
        ILdapConnectionPool connectionPool,
        ILogger<LdapAuthenticator> logger)
    {
        _options = options.Value;
        _connectionPool = connectionPool;
        _logger = logger;
    }

    public async Task<LdapAuthenticationResult> AuthenticateAsync(
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return LdapAuthenticationResult.Failed("Username and password are required");
        }

        try
        {
            // First, find the user to get their DN
            var user = await FindUserAsync(username, cancellationToken);
            if (user == null)
            {
                _logger.LogWarning("LDAP authentication failed: user {Username} not found", username);
                return LdapAuthenticationResult.Failed("Invalid username or password");
            }

            // Attempt to bind as the user
            using var bindConnection = _connectionPool.CreateBindConnection();
            var credential = new NetworkCredential(user.DistinguishedName, password);

            try
            {
                bindConnection.Bind(credential);
                _logger.LogInformation("LDAP authentication successful for {Username}", username);

                var claims = BuildClaims(user);
                return LdapAuthenticationResult.Succeeded(user, claims);
            }
            catch (LdapException ex) when (ex.ErrorCode == 49) // Invalid credentials
            {
                _logger.LogWarning("LDAP authentication failed for {Username}: invalid credentials", username);
                return LdapAuthenticationResult.Failed("Invalid username or password");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LDAP authentication error for {Username}", username);
            return LdapAuthenticationResult.Failed("Authentication service unavailable");
        }
    }

    public async Task<LdapUser?> FindUserAsync(string username, CancellationToken cancellationToken = default)
    {
        var connection = await _connectionPool.GetConnectionAsync(cancellationToken);
        try
        {
            var baseDn = _options.UserSearch.BaseDn ?? _options.BaseDn;
            var filter = string.Format(_options.UserSearch.AuthenticationFilter, EscapeLdapFilter(username));
            var scope = ParseSearchScope(_options.UserSearch.Scope);

            var request = new SearchRequest(baseDn, filter, scope, GetUserAttributes());
            request.TimeLimit = TimeSpan.FromSeconds(_options.SearchTimeoutSeconds);

            var response = (SearchResponse)connection.SendRequest(request);

            if (response.Entries.Count == 0)
            {
                return null;
            }

            if (response.Entries.Count > 1)
            {
                _logger.LogWarning("Multiple users found for username {Username}", username);
            }

            var entry = response.Entries[0];
            return MapToLdapUser(entry);
        }
        finally
        {
            _connectionPool.ReturnConnection(connection);
        }
    }

    public async Task<LdapUser?> FindUserByDnAsync(string distinguishedName, CancellationToken cancellationToken = default)
    {
        var connection = await _connectionPool.GetConnectionAsync(cancellationToken);
        try
        {
            var request = new SearchRequest(
                distinguishedName,
                "(objectClass=*)",
                SearchScope.Base,
                GetUserAttributes());
            request.TimeLimit = TimeSpan.FromSeconds(_options.SearchTimeoutSeconds);

            var response = (SearchResponse)connection.SendRequest(request);

            if (response.Entries.Count == 0)
            {
                return null;
            }

            return MapToLdapUser(response.Entries[0]);
        }
        catch (DirectoryOperationException)
        {
            return null;
        }
        finally
        {
            _connectionPool.ReturnConnection(connection);
        }
    }

    public async Task<bool> ValidateUserAsync(string username, CancellationToken cancellationToken = default)
    {
        var user = await FindUserAsync(username, cancellationToken);
        return user != null;
    }

    private LdapUser MapToLdapUser(SearchResultEntry entry)
    {
        var mappings = _options.AttributeMappings;

        var user = new LdapUser
        {
            DistinguishedName = entry.DistinguishedName,
            UniqueId = GetAttributeValue(entry, mappings.UniqueId) ?? entry.DistinguishedName,
            Username = GetAttributeValue(entry, mappings.Username) ?? "",
            Email = GetAttributeValue(entry, mappings.Email),
            FirstName = GetAttributeValue(entry, mappings.FirstName),
            LastName = GetAttributeValue(entry, mappings.LastName),
            DisplayName = GetAttributeValue(entry, mappings.DisplayName),
            Phone = GetAttributeValue(entry, mappings.Phone),
            Attributes = GetAllAttributes(entry)
        };

        return user;
    }

    private string[] GetUserAttributes()
    {
        var mappings = _options.AttributeMappings;
        var attributes = new List<string>
        {
            mappings.UniqueId,
            mappings.Username,
            mappings.Email,
            mappings.FirstName,
            mappings.LastName,
            mappings.DisplayName,
            mappings.Phone
        };

        attributes.AddRange(mappings.CustomMappings.Keys);

        return attributes.Where(a => !string.IsNullOrEmpty(a)).Distinct().ToArray();
    }

    private static string? GetAttributeValue(SearchResultEntry entry, string attributeName)
    {
        if (string.IsNullOrEmpty(attributeName))
            return null;

        var attribute = entry.Attributes[attributeName];
        if (attribute == null || attribute.Count == 0)
            return null;

        return attribute[0]?.ToString();
    }

    private static Dictionary<string, string[]> GetAllAttributes(SearchResultEntry entry)
    {
        var result = new Dictionary<string, string[]>();

        foreach (DirectoryAttribute attr in entry.Attributes.Values)
        {
            var values = new string[attr.Count];
            for (int i = 0; i < attr.Count; i++)
            {
                values[i] = attr[i]?.ToString() ?? "";
            }
            result[attr.Name] = values;
        }

        return result;
    }

    private List<Claim> BuildClaims(LdapUser user)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UniqueId),
            new(ClaimTypes.Name, user.Username),
            new("ldap_dn", user.DistinguishedName)
        };

        if (!string.IsNullOrEmpty(user.Email))
            claims.Add(new Claim(ClaimTypes.Email, user.Email));

        if (!string.IsNullOrEmpty(user.FirstName))
            claims.Add(new Claim(ClaimTypes.GivenName, user.FirstName));

        if (!string.IsNullOrEmpty(user.LastName))
            claims.Add(new Claim(ClaimTypes.Surname, user.LastName));

        if (!string.IsNullOrEmpty(user.DisplayName))
            claims.Add(new Claim("name", user.DisplayName));

        if (!string.IsNullOrEmpty(user.Phone))
            claims.Add(new Claim(ClaimTypes.MobilePhone, user.Phone));

        foreach (var group in user.Groups)
        {
            claims.Add(new Claim(ClaimTypes.Role, group));
        }

        // Add custom mapped claims
        foreach (var (ldapAttr, claimType) in _options.AttributeMappings.CustomMappings)
        {
            if (user.Attributes.TryGetValue(ldapAttr, out var values))
            {
                foreach (var value in values)
                {
                    claims.Add(new Claim(claimType, value));
                }
            }
        }

        return claims;
    }

    private static SearchScope ParseSearchScope(string scope)
    {
        return scope.ToLowerInvariant() switch
        {
            "base" => SearchScope.Base,
            "onelevel" => SearchScope.OneLevel,
            "subtree" => SearchScope.Subtree,
            _ => SearchScope.Subtree
        };
    }

    private static string EscapeLdapFilter(string value)
    {
        return value
            .Replace("\\", "\\5c")
            .Replace("*", "\\2a")
            .Replace("(", "\\28")
            .Replace(")", "\\29")
            .Replace("\0", "\\00");
    }
}

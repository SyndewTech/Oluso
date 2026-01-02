using System.DirectoryServices.Protocols;
using Oluso.Enterprise.Ldap.Configuration;
using Oluso.Enterprise.Ldap.Connection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Oluso.Enterprise.Ldap.GroupMapping;

/// <summary>
/// Service for mapping LDAP groups to roles/claims
/// </summary>
public interface ILdapGroupMapper
{
    /// <summary>
    /// Gets all groups a user belongs to
    /// </summary>
    Task<IReadOnlyList<string>> GetUserGroupsAsync(
        string userDn,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all groups from LDAP
    /// </summary>
    Task<IReadOnlyList<LdapGroup>> GetAllGroupsAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Maps LDAP group names to application roles
    /// </summary>
    IReadOnlyList<string> MapGroupsToRoles(IEnumerable<string> ldapGroups);
}

/// <summary>
/// Represents an LDAP group
/// </summary>
public class LdapGroup
{
    public string DistinguishedName { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public IReadOnlyList<string> Members { get; set; } = Array.Empty<string>();
}

public class LdapGroupMapper : ILdapGroupMapper
{
    private readonly LdapOptions _options;
    private readonly ILdapConnectionPool _connectionPool;
    private readonly ILogger<LdapGroupMapper> _logger;
    private readonly Dictionary<string, string> _groupToRoleMappings;

    public LdapGroupMapper(
        IOptions<LdapOptions> options,
        ILdapConnectionPool connectionPool,
        ILogger<LdapGroupMapper> logger)
    {
        _options = options.Value;
        _connectionPool = connectionPool;
        _logger = logger;
        _groupToRoleMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Configures group to role mappings
    /// </summary>
    public void ConfigureMapping(string ldapGroupName, string roleName)
    {
        _groupToRoleMappings[ldapGroupName] = roleName;
    }

    public async Task<IReadOnlyList<string>> GetUserGroupsAsync(
        string userDn,
        CancellationToken cancellationToken = default)
    {
        var connection = await _connectionPool.GetConnectionAsync(cancellationToken);
        try
        {
            var groups = new List<string>();
            var baseDn = _options.GroupSearch.BaseDn ?? _options.BaseDn;
            var scope = ParseSearchScope(_options.GroupSearch.Scope);

            // Build filter to find groups containing this user
            string memberFilter;
            if (_options.GroupSearch.MemberAttributeIsDn)
            {
                memberFilter = $"({_options.GroupSearch.MemberAttribute}={EscapeLdapFilter(userDn)})";
            }
            else
            {
                // Extract username from DN for memberUid style
                var username = ExtractUsernameFromDn(userDn);
                memberFilter = $"({_options.GroupSearch.MemberAttribute}={EscapeLdapFilter(username)})";
            }

            var filter = $"(&{_options.GroupSearch.Filter}{memberFilter})";

            var request = new SearchRequest(
                baseDn,
                filter,
                scope,
                _options.AttributeMappings.GroupName);
            request.TimeLimit = TimeSpan.FromSeconds(_options.SearchTimeoutSeconds);

            var response = (SearchResponse)connection.SendRequest(request);

            foreach (SearchResultEntry entry in response.Entries)
            {
                var groupName = GetAttributeValue(entry, _options.AttributeMappings.GroupName);
                if (!string.IsNullOrEmpty(groupName))
                {
                    groups.Add(groupName);
                }
            }

            _logger.LogDebug("Found {Count} groups for user {UserDn}", groups.Count, userDn);
            return groups;
        }
        finally
        {
            _connectionPool.ReturnConnection(connection);
        }
    }

    public async Task<IReadOnlyList<LdapGroup>> GetAllGroupsAsync(CancellationToken cancellationToken = default)
    {
        var connection = await _connectionPool.GetConnectionAsync(cancellationToken);
        try
        {
            var groups = new List<LdapGroup>();
            var baseDn = _options.GroupSearch.BaseDn ?? _options.BaseDn;
            var scope = ParseSearchScope(_options.GroupSearch.Scope);

            var request = new SearchRequest(
                baseDn,
                _options.GroupSearch.Filter,
                scope,
                _options.AttributeMappings.GroupName,
                "description",
                _options.GroupSearch.MemberAttribute);
            request.TimeLimit = TimeSpan.FromSeconds(_options.SearchTimeoutSeconds);

            var response = (SearchResponse)connection.SendRequest(request);

            foreach (SearchResultEntry entry in response.Entries)
            {
                var group = new LdapGroup
                {
                    DistinguishedName = entry.DistinguishedName,
                    Name = GetAttributeValue(entry, _options.AttributeMappings.GroupName) ?? "",
                    Description = GetAttributeValue(entry, "description"),
                    Members = GetAttributeValues(entry, _options.GroupSearch.MemberAttribute)
                };
                groups.Add(group);
            }

            return groups;
        }
        finally
        {
            _connectionPool.ReturnConnection(connection);
        }
    }

    public IReadOnlyList<string> MapGroupsToRoles(IEnumerable<string> ldapGroups)
    {
        var roles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in ldapGroups)
        {
            if (_groupToRoleMappings.TryGetValue(group, out var role))
            {
                roles.Add(role);
            }
            else
            {
                // If no explicit mapping, use group name as role
                roles.Add(group);
            }
        }

        return roles.ToList();
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

    private static IReadOnlyList<string> GetAttributeValues(SearchResultEntry entry, string attributeName)
    {
        var values = new List<string>();

        if (string.IsNullOrEmpty(attributeName))
            return values;

        var attribute = entry.Attributes[attributeName];
        if (attribute == null)
            return values;

        for (int i = 0; i < attribute.Count; i++)
        {
            var value = attribute[i]?.ToString();
            if (!string.IsNullOrEmpty(value))
            {
                values.Add(value);
            }
        }

        return values;
    }

    private static string ExtractUsernameFromDn(string dn)
    {
        // Extract uid from "uid=john,ou=users,dc=example,dc=com"
        var parts = dn.Split(',');
        if (parts.Length > 0)
        {
            var firstPart = parts[0];
            var equalIndex = firstPart.IndexOf('=');
            if (equalIndex > 0)
            {
                return firstPart[(equalIndex + 1)..];
            }
        }
        return dn;
    }

    private static SearchScope ParseSearchScope(string scope)
    {
        return scope.ToLowerInvariant() switch
        {
            "base" => SearchScope.Base,
            "onelevel" => SearchScope.OneLevel,
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

using System.DirectoryServices.Protocols;
using System.Diagnostics;
using Oluso.Enterprise.Ldap.Authentication;
using Oluso.Enterprise.Ldap.Configuration;
using Oluso.Enterprise.Ldap.Connection;
using Oluso.Enterprise.Ldap.GroupMapping;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Oluso.Enterprise.Ldap.UserSync;

public class LdapUserSync : ILdapUserSync
{
    private readonly LdapOptions _options;
    private readonly ILdapConnectionPool _connectionPool;
    private readonly ILdapGroupMapper _groupMapper;
    private readonly ILogger<LdapUserSync> _logger;

    public LdapUserSync(
        IOptions<LdapOptions> options,
        ILdapConnectionPool connectionPool,
        ILdapGroupMapper groupMapper,
        ILogger<LdapUserSync> logger)
    {
        _options = options.Value;
        _connectionPool = connectionPool;
        _groupMapper = groupMapper;
        _logger = logger;
    }

    public async Task<LdapSyncResult> SyncAllUsersAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new LdapSyncResult();

        try
        {
            var users = await GetAllLdapUsersAsync(cancellationToken);
            result.TotalUsers = users.Count;

            _logger.LogInformation("Starting sync of {Count} LDAP users", users.Count);

            foreach (var user in users)
            {
                try
                {
                    // Here you would integrate with your user store
                    // This is a placeholder for the actual sync logic
                    result.Updated++;
                }
                catch (Exception ex)
                {
                    result.Errors++;
                    result.ErrorMessages.Add($"Error syncing {user.Username}: {ex.Message}");
                    _logger.LogError(ex, "Error syncing user {Username}", user.Username);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LDAP sync failed");
            result.ErrorMessages.Add($"Sync failed: {ex.Message}");
        }

        stopwatch.Stop();
        result.Duration = stopwatch.Elapsed;

        _logger.LogInformation(
            "LDAP sync completed in {Duration}ms. Total: {Total}, Created: {Created}, Updated: {Updated}, Errors: {Errors}",
            result.Duration.TotalMilliseconds,
            result.TotalUsers,
            result.Created,
            result.Updated,
            result.Errors);

        return result;
    }

    public async Task<LdapUser?> SyncUserAsync(string username, CancellationToken cancellationToken = default)
    {
        var connection = await _connectionPool.GetConnectionAsync(cancellationToken);
        try
        {
            var user = await FindUserInternalAsync(connection, username, cancellationToken);
            if (user == null)
            {
                return null;
            }

            // Get user's groups
            user = new LdapUser
            {
                Groups = await _groupMapper.GetUserGroupsAsync(user.DistinguishedName, cancellationToken)
            };

            return user;
        }
        finally
        {
            _connectionPool.ReturnConnection(connection);
        }
    }

    public async Task<IReadOnlyList<LdapUser>> GetAllLdapUsersAsync(CancellationToken cancellationToken = default)
    {
        var connection = await _connectionPool.GetConnectionAsync(cancellationToken);
        try
        {
            var users = new List<LdapUser>();
            var baseDn = _options.UserSearch.BaseDn ?? _options.BaseDn;
            var scope = ParseSearchScope(_options.UserSearch.Scope);

            var request = new SearchRequest(
                baseDn,
                _options.UserSearch.Filter,
                scope,
                GetUserAttributes());
            request.TimeLimit = TimeSpan.FromSeconds(_options.SearchTimeoutSeconds);

            // Use paging for large directories
            var pageControl = new PageResultRequestControl(500);
            request.Controls.Add(pageControl);

            while (true)
            {
                var response = (SearchResponse)connection.SendRequest(request);

                foreach (SearchResultEntry entry in response.Entries)
                {
                    var user = MapToLdapUser(entry);
                    users.Add(user);
                }

                // Check for more pages
                var pageResponse = response.Controls
                    .OfType<PageResultResponseControl>()
                    .FirstOrDefault();

                if (pageResponse == null || pageResponse.Cookie.Length == 0)
                {
                    break;
                }

                pageControl.Cookie = pageResponse.Cookie;
            }

            return users;
        }
        finally
        {
            _connectionPool.ReturnConnection(connection);
        }
    }

    public async Task<IReadOnlyList<LdapUser>> GetModifiedUsersAsync(
        DateTime since,
        CancellationToken cancellationToken = default)
    {
        var connection = await _connectionPool.GetConnectionAsync(cancellationToken);
        try
        {
            var users = new List<LdapUser>();
            var baseDn = _options.UserSearch.BaseDn ?? _options.BaseDn;
            var scope = ParseSearchScope(_options.UserSearch.Scope);

            // Format for LDAP generalized time
            var sinceStr = since.ToUniversalTime().ToString("yyyyMMddHHmmss") + "Z";
            var filter = $"(&{_options.UserSearch.Filter}(modifyTimestamp>={sinceStr}))";

            var request = new SearchRequest(baseDn, filter, scope, GetUserAttributes());
            request.TimeLimit = TimeSpan.FromSeconds(_options.SearchTimeoutSeconds);

            var response = (SearchResponse)connection.SendRequest(request);

            foreach (SearchResultEntry entry in response.Entries)
            {
                users.Add(MapToLdapUser(entry));
            }

            return users;
        }
        finally
        {
            _connectionPool.ReturnConnection(connection);
        }
    }

    private async Task<LdapUser?> FindUserInternalAsync(
        LdapConnection connection,
        string username,
        CancellationToken cancellationToken)
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

        return MapToLdapUser(response.Entries[0]);
    }

    private LdapUser MapToLdapUser(SearchResultEntry entry)
    {
        var mappings = _options.AttributeMappings;

        return new LdapUser
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
            mappings.Phone,
            "modifyTimestamp"
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

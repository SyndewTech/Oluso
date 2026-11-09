using System.DirectoryServices.Protocols;
using System.Formats.Asn1;
using System.Text;
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

    /// <summary>
    /// Authenticates using global LDAP configuration
    /// </summary>
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

    /// <summary>
    /// Authenticates using per-tenant LDAP settings from IdentityProvider configuration
    /// </summary>
    public async Task<LdapAuthenticationResult> AuthenticateAsync(
        string username,
        string password,
        LdapProviderSettings settings,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            return LdapAuthenticationResult.Failed("Username and password are required");
        }

        if (!settings.IsValid(out var validationError))
        {
            _logger.LogError("Invalid LDAP settings: {Error}", validationError);
            return LdapAuthenticationResult.Failed(validationError ?? "Invalid LDAP configuration");
        }

        try
        {
            // First, find the user to get their DN
            var user = await FindUserAsync(username, settings, cancellationToken);
            if (user == null)
            {
                _logger.LogWarning("LDAP authentication failed: user {Username} not found in {Server}",
                    username, settings.Server);
                return LdapAuthenticationResult.Failed("Invalid username or password");
            }

            // Attempt to bind as the user to verify password
            using var bindConnection = CreateConnection(settings, bindAsServiceAccount: false);
            var credential = new NetworkCredential(user.DistinguishedName, password);

            try
            {
                bindConnection.Bind(credential);
                _logger.LogInformation("LDAP authentication successful for {Username} via {Server}",
                    username, settings.Server);

                var claims = BuildClaims(user, settings);
                return LdapAuthenticationResult.Succeeded(user, claims);
            }
            catch (LdapException ex) when (ex.ErrorCode == 49) // Invalid credentials
            {
                _logger.LogWarning("LDAP authentication failed for {Username}: invalid credentials", username);
                return LdapAuthenticationResult.Failed("Invalid username or password");
            }
        }
        catch (LdapException ex)
        {
            _logger.LogError(ex, "LDAP error during authentication for {Username}: {ErrorCode}",
                username, ex.ErrorCode);
            return LdapAuthenticationResult.Failed("Authentication service unavailable");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "LDAP authentication error for {Username}", username);
            return LdapAuthenticationResult.Failed("Authentication service unavailable");
        }
    }

    /// <summary>
    /// Find user using global LDAP configuration
    /// </summary>
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

    /// <summary>
    /// Find user using per-tenant LDAP settings
    /// </summary>
    public async Task<LdapUser?> FindUserAsync(
        string username,
        LdapProviderSettings settings,
        CancellationToken cancellationToken = default)
    {
        using var connection = CreateConnection(settings, bindAsServiceAccount: true);

        var baseDn = settings.GetUserSearchBaseDn();
        var filter = string.Format(settings.UserSearchFilter, EscapeLdapFilter(username));

        var request = new SearchRequest(baseDn, filter, SearchScope.Subtree, GetUserAttributes(settings));
        request.TimeLimit = TimeSpan.FromSeconds(settings.SearchTimeout);

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
        return MapToLdapUser(entry, settings);
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

    public Task<LdapConnectionTestResult> TestConnectionAsync(LdapProviderSettings? settings, CancellationToken cancellationToken = default)
    {
        if (settings != null && !settings.IsValid(out var invalid))
        {
            return Task.FromResult(new LdapConnectionTestResult(false, invalid ?? "Invalid LDAP configuration", Array.Empty<string>()));
        }

        var server = settings?.Server ?? _options.Server;
        var port = settings?.Port ?? _options.Port;
        var baseDn = settings?.BaseDn ?? _options.BaseDn;
        var bindDn = settings?.BindDn ?? _options.BindDn;
        var secure = settings == null ? _options.UseSsl || _options.UseStartTls : settings.UseSsl || settings.UseStartTls;
        var details = new List<string>
        {
            $"Server: {server}:{port}{(secure ? " (secure)" : "")}",
            $"Base DN: {baseDn}",
            string.IsNullOrEmpty(bindDn) ? "Bind: anonymous" : $"Bind DN: {bindDn}"
        };

        try
        {
            using var connection = settings == null
                ? _connectionPool.CreateBindConnection()
                : CreateConnection(settings, bindAsServiceAccount: false);
            if (string.IsNullOrEmpty(bindDn))
            {
                connection.AuthType = AuthType.Anonymous;
                connection.Bind();
            }
            else
            {
                connection.Bind(new NetworkCredential(bindDn, settings?.BindPassword ?? _options.BindPassword));
            }
            details.Add("Bind succeeded");

            var response = (SearchResponse)connection.SendRequest(new SearchRequest(baseDn, "(objectClass=*)", SearchScope.Base, "objectClass"));
            if (response.Entries.Count == 0)
            {
                return Task.FromResult(new LdapConnectionTestResult(false, $"Base DN {baseDn} wasn't found", details));
            }
            details.Add("Base DN found");
            return Task.FromResult(new LdapConnectionTestResult(true, $"Connected to {server}", details));
        }
        catch (LdapException ex) when (ex.ErrorCode == 49)
        {
            return Task.FromResult(new LdapConnectionTestResult(false, "The directory rejected the bind DN or password", details));
        }
        catch (LdapException ex)
        {
            return Task.FromResult(new LdapConnectionTestResult(false, $"Couldn't connect to {server}:{port}: {ex.Message}", details));
        }
        catch (DirectoryOperationException ex)
        {
            return Task.FromResult(new LdapConnectionTestResult(false, $"The directory refused the request: {ex.Response?.ErrorMessage ?? ex.Message}", details));
        }
    }

    /// <summary>RFC 3062 LDAP Password Modify extended operation.</summary>
    internal const string PasswordModifyOid = "1.3.6.1.4.1.4203.1.11.1";

    /// <summary>Active Directory's LDAP_CAP_ACTIVE_DIRECTORY_OID capability.</summary>
    internal const string ActiveDirectoryCapabilityOid = "1.2.840.113556.1.4.800";

    public async Task<LdapPasswordChangeResult> ChangePasswordAsync(
        string username,
        string currentPassword,
        string newPassword,
        LdapProviderSettings? settings,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(currentPassword) || string.IsNullOrEmpty(newPassword))
        {
            return LdapPasswordChangeResult.Failed(LdapPasswordChangeStatus.InvalidCredentials, "Username, current password and new password are required");
        }

        if (settings != null && !settings.IsValid(out var validationError))
        {
            _logger.LogError("Invalid LDAP settings: {Error}", validationError);
            return LdapPasswordChangeResult.Failed(LdapPasswordChangeStatus.Unavailable, validationError ?? "Invalid LDAP configuration");
        }

        try
        {
            var user = settings == null
                ? await FindUserAsync(username, cancellationToken)
                : await FindUserAsync(username, settings, cancellationToken);
            if (user == null)
            {
                return LdapPasswordChangeResult.Failed(LdapPasswordChangeStatus.InvalidCredentials, "Invalid username or password");
            }

            using var connection = settings == null
                ? _connectionPool.CreateBindConnection()
                : CreateConnection(settings, bindAsServiceAccount: false);
            var secure = settings == null ? _options.UseSsl || _options.UseStartTls : settings.UseSsl || settings.UseStartTls;

            try
            {
                connection.Bind(new NetworkCredential(user.DistinguishedName, currentPassword));
            }
            catch (LdapException ex) when (ex.ErrorCode == 49)
            {
                return LdapPasswordChangeResult.Failed(LdapPasswordChangeStatus.InvalidCredentials, "Invalid username or password");
            }

            var (extensions, capabilities) = ReadRootDse(connection);
            DirectoryRequest request;
            if (extensions.Contains(PasswordModifyOid))
            {
                request = new ExtendedRequest(PasswordModifyOid, EncodePasswordModify(user.DistinguishedName, currentPassword, newPassword));
            }
            else if (capabilities.Contains(ActiveDirectoryCapabilityOid))
            {
                if (!secure)
                {
                    return LdapPasswordChangeResult.Failed(LdapPasswordChangeStatus.SecureConnectionRequired,
                        "Active Directory only accepts password changes over SSL or StartTLS");
                }
                request = new ModifyRequest(user.DistinguishedName, new[]
                {
                    new DirectoryAttributeModification { Name = "unicodePwd", Operation = DirectoryAttributeOperation.Delete },
                    new DirectoryAttributeModification { Name = "unicodePwd", Operation = DirectoryAttributeOperation.Add },
                });
                ((ModifyRequest)request).Modifications[0].Add(ActiveDirectoryPassword(currentPassword));
                ((ModifyRequest)request).Modifications[1].Add(ActiveDirectoryPassword(newPassword));
            }
            else
            {
                return LdapPasswordChangeResult.Failed(LdapPasswordChangeStatus.NotSupported,
                    "This directory doesn't support changing passwords over LDAP");
            }

            try
            {
                connection.SendRequest(request);
            }
            catch (DirectoryOperationException ex)
            {
                _logger.LogWarning("LDAP password change for {Username} rejected: {ResultCode} {Message}",
                    username, ex.Response?.ResultCode, ex.Response?.ErrorMessage);
                return ex.Response?.ResultCode switch
                {
                    ResultCode.ConstraintViolation => LdapPasswordChangeResult.Failed(LdapPasswordChangeStatus.RejectedByPolicy,
                        "The new password doesn't meet the directory's password policy"),
                    ResultCode.UnwillingToPerform when !secure => LdapPasswordChangeResult.Failed(LdapPasswordChangeStatus.SecureConnectionRequired,
                        "The directory only accepts password changes over a secure connection"),
                    ResultCode.UnwillingToPerform => LdapPasswordChangeResult.Failed(LdapPasswordChangeStatus.RejectedByPolicy,
                        "The directory refused the new password"),
                    _ => LdapPasswordChangeResult.Failed(LdapPasswordChangeStatus.Unavailable, "The password couldn't be changed"),
                };
            }

            _logger.LogInformation("LDAP password changed for {Username}", username);
            return LdapPasswordChangeResult.Changed();
        }
        catch (Exception ex) when (ex is LdapException or DirectoryException)
        {
            _logger.LogError(ex, "LDAP error changing password for {Username}", username);
            return LdapPasswordChangeResult.Failed(LdapPasswordChangeStatus.Unavailable, "Directory service unavailable");
        }
    }

    private static (HashSet<string> Extensions, HashSet<string> Capabilities) ReadRootDse(LdapConnection connection)
    {
        var response = (SearchResponse)connection.SendRequest(
            new SearchRequest(string.Empty, "(objectClass=*)", SearchScope.Base, "supportedExtension", "supportedCapabilities"));
        var entry = response.Entries.Count > 0 ? response.Entries[0] : null;
        return (Values(entry, "supportedExtension"), Values(entry, "supportedCapabilities"));

        static HashSet<string> Values(SearchResultEntry? entry, string attribute) =>
            entry?.Attributes[attribute] is { } values
                ? values.GetValues(typeof(string)).Cast<string>().ToHashSet(StringComparer.Ordinal)
                : new HashSet<string>();
    }

    /// <summary>PasswdModifyRequestValue ::= SEQUENCE { userIdentity [0], oldPasswd [1], newPasswd [2] }.</summary>
    internal static byte[] EncodePasswordModify(string userDn, string oldPassword, string newPassword)
    {
        var writer = new AsnWriter(AsnEncodingRules.BER);
        using (writer.PushSequence())
        {
            writer.WriteOctetString(Encoding.UTF8.GetBytes(userDn), new Asn1Tag(TagClass.ContextSpecific, 0));
            writer.WriteOctetString(Encoding.UTF8.GetBytes(oldPassword), new Asn1Tag(TagClass.ContextSpecific, 1));
            writer.WriteOctetString(Encoding.UTF8.GetBytes(newPassword), new Asn1Tag(TagClass.ContextSpecific, 2));
        }
        return writer.Encode();
    }

    /// <summary>Active Directory takes the password quoted and UTF-16LE encoded.</summary>
    internal static byte[] ActiveDirectoryPassword(string password) => Encoding.Unicode.GetBytes($"\"{password}\"");

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

    #region Per-Tenant Settings Helpers

    /// <summary>
    /// Create LDAP connection using per-tenant settings
    /// </summary>
    private LdapConnection CreateConnection(LdapProviderSettings settings, bool bindAsServiceAccount)
    {
        var identifier = new LdapDirectoryIdentifier(settings.Server, settings.Port);

        var connection = new LdapConnection(identifier)
        {
            AuthType = AuthType.Basic,
            AutoBind = false
        };

        connection.SessionOptions.ProtocolVersion = 3;
        connection.Timeout = TimeSpan.FromSeconds(settings.ConnectionTimeout);

        if (settings.UseSsl)
        {
            connection.SessionOptions.SecureSocketLayer = true;
        }

        if (settings.UseStartTls)
        {
            connection.SessionOptions.StartTransportLayerSecurity(null);
        }

        if (bindAsServiceAccount && !string.IsNullOrEmpty(settings.BindDn))
        {
            var credential = new NetworkCredential(settings.BindDn, settings.BindPassword);
            connection.Bind(credential);
            _logger.LogDebug("Bound to LDAP server {Server} as {BindDn}", settings.Server, settings.BindDn);
        }

        return connection;
    }

    /// <summary>
    /// Get user attributes to retrieve using per-tenant settings
    /// </summary>
    private static string[] GetUserAttributes(LdapProviderSettings settings)
    {
        return new[]
        {
            "entryUUID",
            "objectGUID",
            settings.UidAttribute,
            settings.EmailAttribute,
            settings.FirstNameAttribute,
            settings.LastNameAttribute,
            settings.DisplayNameAttribute,
            settings.PhoneAttribute,
            settings.MemberOfAttribute
        }.Where(a => !string.IsNullOrEmpty(a)).Distinct().ToArray();
    }

    /// <summary>
    /// Map LDAP entry to user using per-tenant attribute mappings
    /// </summary>
    private LdapUser MapToLdapUser(SearchResultEntry entry, LdapProviderSettings settings)
    {
        return new LdapUser
        {
            DistinguishedName = entry.DistinguishedName,
            UniqueId = GetAttributeValue(entry, "entryUUID")
                ?? GetAttributeValue(entry, "objectGUID")
                ?? entry.DistinguishedName,
            Username = GetAttributeValue(entry, settings.UidAttribute) ?? "",
            Email = GetAttributeValue(entry, settings.EmailAttribute),
            FirstName = GetAttributeValue(entry, settings.FirstNameAttribute),
            LastName = GetAttributeValue(entry, settings.LastNameAttribute),
            DisplayName = GetAttributeValue(entry, settings.DisplayNameAttribute),
            Phone = GetAttributeValue(entry, settings.PhoneAttribute),
            Groups = GetAttributeValues(entry, settings.MemberOfAttribute),
            Attributes = GetAllAttributes(entry)
        };
    }

    /// <summary>
    /// Get multiple values for an LDAP attribute
    /// </summary>
    private static string[] GetAttributeValues(SearchResultEntry entry, string attributeName)
    {
        if (string.IsNullOrEmpty(attributeName))
            return Array.Empty<string>();

        var attribute = entry.Attributes[attributeName];
        if (attribute == null || attribute.Count == 0)
            return Array.Empty<string>();

        var values = new string[attribute.Count];
        for (int i = 0; i < attribute.Count; i++)
        {
            values[i] = attribute[i]?.ToString() ?? "";
        }
        return values;
    }

    /// <summary>
    /// Build claims from LDAP user using per-tenant settings
    /// </summary>
    private List<Claim> BuildClaims(LdapUser user, LdapProviderSettings settings)
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

        // Add groups as claims
        foreach (var group in user.Groups)
        {
            claims.Add(new Claim("groups", group));

            // If configured, map groups to roles
            if (settings.SyncGroupsToRoles)
            {
                var roleName = ExtractCnFromDn(group) ?? group;
                claims.Add(new Claim(ClaimTypes.Role, roleName));
            }
        }

        return claims;
    }

    /// <summary>
    /// Extract CN from a distinguished name
    /// </summary>
    private static string? ExtractCnFromDn(string dn)
    {
        if (string.IsNullOrEmpty(dn))
            return null;

        var parts = dn.Split(',');
        var cnPart = parts.FirstOrDefault(p => p.Trim().StartsWith("cn=", StringComparison.OrdinalIgnoreCase));
        return cnPart?.Substring(3);
    }

    #endregion
}

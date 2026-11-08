using System.Text.Json;
using Oluso.Core.Services;

namespace Oluso.Enterprise.Ldap.Configuration;

/// <summary>
/// LDAP connection settings parsed from IdentityProvider configuration.
/// Each tenant can have their own LDAP/AD server configuration.
/// </summary>
public class LdapProviderSettings
{
    /// <summary>
    /// LDAP server hostname or IP address
    /// </summary>
    public string Server { get; set; } = string.Empty;

    /// <summary>
    /// LDAP server port (389 for LDAP, 636 for LDAPS)
    /// </summary>
    public int Port { get; set; } = 389;

    /// <summary>
    /// Use SSL/TLS connection (LDAPS)
    /// </summary>
    public bool UseSsl { get; set; } = false;

    /// <summary>
    /// Use StartTLS for connection upgrade
    /// </summary>
    public bool UseStartTls { get; set; } = false;

    /// <summary>
    /// Base DN for searches (e.g., "dc=example,dc=com")
    /// </summary>
    public string BaseDn { get; set; } = string.Empty;

    /// <summary>
    /// Bind DN for service account (e.g., "cn=admin,dc=example,dc=com")
    /// </summary>
    public string? BindDn { get; set; }

    /// <summary>
    /// Bind password for service account
    /// </summary>
    public string? BindPassword { get; set; }

    /// <summary>
    /// Base DN for user searches (defaults to BaseDn if not set)
    /// </summary>
    public string? UserSearchBase { get; set; }

    /// <summary>
    /// LDAP filter for authenticating users. Use {0} for username placeholder.
    /// Example: "(&amp;(objectClass=inetOrgPerson)(uid={0}))"
    /// </summary>
    public string UserSearchFilter { get; set; } = "(&(objectClass=inetOrgPerson)(uid={0}))";

    /// <summary>
    /// Base DN for group searches
    /// </summary>
    public string? GroupSearchBase { get; set; }

    /// <summary>
    /// LDAP filter for finding groups
    /// </summary>
    public string GroupSearchFilter { get; set; } = "(objectClass=groupOfNames)";

    /// <summary>
    /// Attribute for unique user identifier
    /// </summary>
    public string UidAttribute { get; set; } = "uid";

    /// <summary>
    /// Attribute for email address
    /// </summary>
    public string EmailAttribute { get; set; } = "mail";

    /// <summary>
    /// Attribute for first name
    /// </summary>
    public string FirstNameAttribute { get; set; } = "givenName";

    /// <summary>
    /// Attribute for last name
    /// </summary>
    public string LastNameAttribute { get; set; } = "sn";

    /// <summary>
    /// Attribute for display name
    /// </summary>
    public string DisplayNameAttribute { get; set; } = "cn";

    /// <summary>
    /// Attribute for phone number
    /// </summary>
    public string PhoneAttribute { get; set; } = "telephoneNumber";

    /// <summary>
    /// Attribute for group membership
    /// </summary>
    public string MemberOfAttribute { get; set; } = "memberOf";

    /// <summary>
    /// Whether to auto-provision users on first login
    /// </summary>
    public bool AutoProvisionUsers { get; set; } = true;

    /// <summary>
    /// Whether to sync LDAP groups to roles
    /// </summary>
    public bool SyncGroupsToRoles { get; set; } = false;

    /// <summary>
    /// Connection timeout in seconds
    /// </summary>
    public int ConnectionTimeout { get; set; } = 30;

    /// <summary>
    /// Search timeout in seconds
    /// </summary>
    public int SearchTimeout { get; set; } = 30;

    /// <summary>
    /// Parse LDAP settings from ExternalProviderDefinition
    /// </summary>
    public static LdapProviderSettings FromProviderDefinition(ExternalProviderDefinition provider)
    {
        var settings = new LdapProviderSettings();

        if (provider.Properties == null || provider.Properties.Count == 0)
        {
            return settings;
        }

        var props = provider.Properties;

        // Required fields
        if (props.TryGetValue("server", out var server))
            settings.Server = server;
        if (props.TryGetValue("baseDn", out var baseDn))
            settings.BaseDn = baseDn;

        // Connection settings
        if (props.TryGetValue("port", out var portStr) && int.TryParse(portStr, out var port))
            settings.Port = port;
        if (props.TryGetValue("useSsl", out var useSslStr) && bool.TryParse(useSslStr, out var useSsl))
            settings.UseSsl = useSsl;
        if (props.TryGetValue("useStartTls", out var useStartTlsStr) && bool.TryParse(useStartTlsStr, out var useStartTls))
            settings.UseStartTls = useStartTls;

        // Bind credentials
        if (props.TryGetValue("bindDn", out var bindDn))
            settings.BindDn = bindDn;
        if (props.TryGetValue("bindPassword", out var bindPassword))
            settings.BindPassword = bindPassword;

        // Search settings
        if (props.TryGetValue("userSearchBase", out var userSearchBase))
            settings.UserSearchBase = userSearchBase;
        if (props.TryGetValue("userSearchFilter", out var userSearchFilter))
            settings.UserSearchFilter = userSearchFilter;
        if (props.TryGetValue("groupSearchBase", out var groupSearchBase))
            settings.GroupSearchBase = groupSearchBase;
        if (props.TryGetValue("groupSearchFilter", out var groupSearchFilter))
            settings.GroupSearchFilter = groupSearchFilter;

        // Attribute mappings
        if (props.TryGetValue("uidAttribute", out var uidAttr))
            settings.UidAttribute = uidAttr;
        if (props.TryGetValue("emailAttribute", out var emailAttr))
            settings.EmailAttribute = emailAttr;
        if (props.TryGetValue("firstNameAttribute", out var firstNameAttr))
            settings.FirstNameAttribute = firstNameAttr;
        if (props.TryGetValue("lastNameAttribute", out var lastNameAttr))
            settings.LastNameAttribute = lastNameAttr;
        if (props.TryGetValue("displayNameAttribute", out var displayNameAttr))
            settings.DisplayNameAttribute = displayNameAttr;
        if (props.TryGetValue("phoneAttribute", out var phoneAttr))
            settings.PhoneAttribute = phoneAttr;
        if (props.TryGetValue("memberOfAttribute", out var memberOfAttr))
            settings.MemberOfAttribute = memberOfAttr;

        // Behavior settings
        if (props.TryGetValue("autoProvisionUsers", out var autoProvisionStr) && bool.TryParse(autoProvisionStr, out var autoProv))
            settings.AutoProvisionUsers = autoProv;
        if (props.TryGetValue("syncGroupsToRoles", out var syncGroupsStr) && bool.TryParse(syncGroupsStr, out var syncGroups))
            settings.SyncGroupsToRoles = syncGroups;

        // Timeouts
        if (props.TryGetValue("connectionTimeout", out var connTimeoutStr) && int.TryParse(connTimeoutStr, out var connTimeout))
            settings.ConnectionTimeout = connTimeout;
        if (props.TryGetValue("searchTimeout", out var searchTimeoutStr) && int.TryParse(searchTimeoutStr, out var searchTimeout))
            settings.SearchTimeout = searchTimeout;

        return settings;
    }

    /// <summary>
    /// Get the effective user search base DN
    /// </summary>
    public string GetUserSearchBaseDn() => UserSearchBase ?? BaseDn;

    /// <summary>
    /// Get the effective group search base DN
    /// </summary>
    public string GetGroupSearchBaseDn() => GroupSearchBase ?? BaseDn;

    /// <summary>
    /// Validate that required settings are present
    /// </summary>
    public bool IsValid(out string? error)
    {
        if (string.IsNullOrWhiteSpace(Server))
        {
            error = "LDAP server is required";
            return false;
        }

        if (string.IsNullOrWhiteSpace(BaseDn))
        {
            error = "Base DN is required";
            return false;
        }

        error = null;
        return true;
    }
}

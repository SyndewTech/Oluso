namespace Oluso.Enterprise.Ldap.Configuration;

/// <summary>
/// Configuration options for LDAP connectivity
/// </summary>
public class LdapOptions
{
    public const string SectionName = "Oluso:Ldap";

    /// <summary>
    /// Whether LDAP authentication is enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Display name shown in the UI (e.g., "Corporate Directory", "Active Directory")
    /// </summary>
    public string DisplayName { get; set; } = "Corporate Directory";

    /// <summary>
    /// LDAP server hostname or IP address
    /// </summary>
    public string Server { get; set; } = "localhost";

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
    /// Connection timeout in seconds
    /// </summary>
    public int ConnectionTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Search timeout in seconds
    /// </summary>
    public int SearchTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Maximum connections in the pool
    /// </summary>
    public int MaxPoolSize { get; set; } = 10;

    /// <summary>
    /// User search settings
    /// </summary>
    public LdapUserSearchOptions UserSearch { get; set; } = new();

    /// <summary>
    /// Group search settings
    /// </summary>
    public LdapGroupSearchOptions GroupSearch { get; set; } = new();

    /// <summary>
    /// Attribute mappings
    /// </summary>
    public LdapAttributeMappings AttributeMappings { get; set; } = new();
}

/// <summary>
/// User search configuration
/// </summary>
public class LdapUserSearchOptions
{
    /// <summary>
    /// Base DN for user searches (defaults to main BaseDn if not set)
    /// </summary>
    public string? BaseDn { get; set; }

    /// <summary>
    /// LDAP filter for finding users (e.g., "(objectClass=inetOrgPerson)")
    /// </summary>
    public string Filter { get; set; } = "(objectClass=inetOrgPerson)";

    /// <summary>
    /// Filter template for authenticating users. Use {0} for username placeholder.
    /// Example: "(&(objectClass=inetOrgPerson)(uid={0}))"
    /// </summary>
    public string AuthenticationFilter { get; set; } = "(&(objectClass=inetOrgPerson)(uid={0}))";

    /// <summary>
    /// Search scope (Base, OneLevel, Subtree)
    /// </summary>
    public string Scope { get; set; } = "Subtree";
}

/// <summary>
/// Group search configuration
/// </summary>
public class LdapGroupSearchOptions
{
    /// <summary>
    /// Base DN for group searches
    /// </summary>
    public string? BaseDn { get; set; }

    /// <summary>
    /// LDAP filter for finding groups
    /// </summary>
    public string Filter { get; set; } = "(objectClass=groupOfNames)";

    /// <summary>
    /// Attribute containing group members
    /// </summary>
    public string MemberAttribute { get; set; } = "member";

    /// <summary>
    /// Whether member attribute contains DNs (true) or usernames (false)
    /// </summary>
    public bool MemberAttributeIsDn { get; set; } = true;

    /// <summary>
    /// Search scope
    /// </summary>
    public string Scope { get; set; } = "Subtree";
}

/// <summary>
/// Attribute mappings from LDAP to claims
/// </summary>
public class LdapAttributeMappings
{
    /// <summary>
    /// Attribute for unique user identifier
    /// </summary>
    public string UniqueId { get; set; } = "entryUUID";

    /// <summary>
    /// Attribute for username/login
    /// </summary>
    public string Username { get; set; } = "uid";

    /// <summary>
    /// Attribute for email address
    /// </summary>
    public string Email { get; set; } = "mail";

    /// <summary>
    /// Attribute for first name
    /// </summary>
    public string FirstName { get; set; } = "givenName";

    /// <summary>
    /// Attribute for last name
    /// </summary>
    public string LastName { get; set; } = "sn";

    /// <summary>
    /// Attribute for display name
    /// </summary>
    public string DisplayName { get; set; } = "cn";

    /// <summary>
    /// Attribute for phone number
    /// </summary>
    public string Phone { get; set; } = "telephoneNumber";

    /// <summary>
    /// Attribute for group name
    /// </summary>
    public string GroupName { get; set; } = "cn";

    /// <summary>
    /// Custom attribute to claim mappings
    /// </summary>
    public Dictionary<string, string> CustomMappings { get; set; } = new();
}

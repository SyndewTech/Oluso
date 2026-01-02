namespace Oluso.Enterprise.Ldap.Server;

/// <summary>
/// Configuration options for the LDAP server
/// </summary>
public class LdapServerOptions
{
    public const string SectionName = "Oluso:LdapServer";

    /// <summary>
    /// Whether the LDAP server is enabled
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Port to listen on (default: 389 for LDAP, 636 for LDAPS)
    /// </summary>
    public int Port { get; set; } = 389;

    /// <summary>
    /// SSL/TLS port for secure LDAP
    /// </summary>
    public int SslPort { get; set; } = 636;

    /// <summary>
    /// Whether to enable SSL/TLS on the SSL port
    /// </summary>
    public bool EnableSsl { get; set; } = false;

    /// <summary>
    /// Whether to support STARTTLS on the regular port
    /// </summary>
    public bool EnableStartTls { get; set; } = false;

    /// <summary>
    /// Path to SSL certificate for LDAPS (file-based option)
    /// </summary>
    public string? SslCertificatePath { get; set; }

    /// <summary>
    /// Password for SSL certificate (file-based option)
    /// </summary>
    public string? SslCertificatePassword { get; set; }

    /// <summary>
    /// Use ICertificateService for managed certificates instead of file-based.
    /// When true, looks up certificate by CertificatePurpose.LdapTls.
    /// Supports per-tenant certificates when TenantIsolation is enabled.
    /// </summary>
    public bool UseManagedCertificates { get; set; } = false;

    /// <summary>
    /// Require client certificates for mutual TLS (mTLS).
    /// </summary>
    public bool RequireClientCertificate { get; set; } = false;

    /// <summary>
    /// TLS protocols to allow. Defaults to TLS 1.2 and 1.3.
    /// </summary>
    public string[] AllowedTlsProtocols { get; set; } = { "Tls12", "Tls13" };

    /// <summary>
    /// Base DN for the directory (e.g., dc=example,dc=com)
    /// </summary>
    public string BaseDn { get; set; } = "dc=oluso,dc=local";

    /// <summary>
    /// Organization name in the directory
    /// </summary>
    public string Organization { get; set; } = "Oluso";

    /// <summary>
    /// OU for users (default: ou=users)
    /// </summary>
    public string UserOu { get; set; } = "users";

    /// <summary>
    /// OU for groups (default: ou=groups)
    /// </summary>
    public string GroupOu { get; set; } = "groups";

    /// <summary>
    /// OU for tenants (default: ou=tenants)
    /// </summary>
    public string TenantOu { get; set; } = "tenants";

    /// <summary>
    /// Maximum number of concurrent connections
    /// </summary>
    public int MaxConnections { get; set; } = 100;

    /// <summary>
    /// Connection timeout in seconds
    /// </summary>
    public int ConnectionTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// Maximum search results to return
    /// </summary>
    public int MaxSearchResults { get; set; } = 1000;

    /// <summary>
    /// Whether to allow anonymous binds (read-only)
    /// </summary>
    public bool AllowAnonymousBind { get; set; } = false;

    /// <summary>
    /// Service account DN for administrative operations
    /// Format: cn=admin,{BaseDn}
    /// </summary>
    public string? AdminDn { get; set; }

    /// <summary>
    /// Service account password
    /// </summary>
    public string? AdminPassword { get; set; }

    /// <summary>
    /// Attribute mappings from LDAP to user properties
    /// </summary>
    public LdapServerAttributeMappings AttributeMappings { get; set; } = new();

    /// <summary>
    /// Whether to include tenant in user DN (for multi-tenant isolation)
    /// When true: uid=user,ou=users,o=tenantId,{BaseDn}
    /// When false: uid=user,ou=users,{BaseDn}
    /// </summary>
    public bool TenantIsolation { get; set; } = true;
}

/// <summary>
/// LDAP Server attribute mappings (for outbound LDAP server)
/// </summary>
public class LdapServerAttributeMappings
{
    public string UserId { get; set; } = "uid";
    public string CommonName { get; set; } = "cn";
    public string Surname { get; set; } = "sn";
    public string GivenName { get; set; } = "givenName";
    public string DisplayName { get; set; } = "displayName";
    public string Email { get; set; } = "mail";
    public string Phone { get; set; } = "telephoneNumber";
    public string MemberOf { get; set; } = "memberOf";
    public string ObjectClass { get; set; } = "objectClass";
    public string UniqueId { get; set; } = "entryUUID";
}

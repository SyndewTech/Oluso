namespace Oluso.Enterprise.Ldap.Server;

/// <summary>
/// LDAP server that exposes ASP.NET Core Identity users via LDAP protocol
/// </summary>
public interface ILdapServer : IAsyncDisposable
{
    /// <summary>
    /// Whether the server is currently running
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Start the LDAP server
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stop the LDAP server
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Current number of active connections
    /// </summary>
    int ActiveConnections { get; }
}

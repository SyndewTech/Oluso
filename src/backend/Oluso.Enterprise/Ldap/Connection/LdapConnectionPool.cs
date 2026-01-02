using System.Collections.Concurrent;
using System.DirectoryServices.Protocols;
using System.Net;
using Oluso.Enterprise.Ldap.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Oluso.Enterprise.Ldap.Connection;

/// <summary>
/// Manages a pool of LDAP connections for efficient reuse
/// </summary>
public interface ILdapConnectionPool : IDisposable
{
    /// <summary>
    /// Gets a connection from the pool
    /// </summary>
    Task<LdapConnection> GetConnectionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a connection to the pool
    /// </summary>
    void ReturnConnection(LdapConnection connection);

    /// <summary>
    /// Creates a new connection for bind operations (not pooled)
    /// </summary>
    LdapConnection CreateBindConnection();
}

public class LdapConnectionPool : ILdapConnectionPool
{
    private readonly LdapOptions _options;
    private readonly ILogger<LdapConnectionPool> _logger;
    private readonly ConcurrentBag<LdapConnection> _pool = new();
    private readonly SemaphoreSlim _semaphore;
    private bool _disposed;

    public LdapConnectionPool(
        IOptions<LdapOptions> options,
        ILogger<LdapConnectionPool> logger)
    {
        _options = options.Value;
        _logger = logger;
        _semaphore = new SemaphoreSlim(_options.MaxPoolSize, _options.MaxPoolSize);
    }

    public async Task<LdapConnection> GetConnectionAsync(CancellationToken cancellationToken = default)
    {
        await _semaphore.WaitAsync(cancellationToken);

        if (_pool.TryTake(out var connection))
        {
            if (IsConnectionValid(connection))
            {
                return connection;
            }

            // Connection is stale, dispose and create new
            try { connection.Dispose(); } catch { }
        }

        return CreateConnection(bindAsServiceAccount: true);
    }

    public void ReturnConnection(LdapConnection connection)
    {
        if (_disposed)
        {
            connection.Dispose();
            return;
        }

        _pool.Add(connection);
        _semaphore.Release();
    }

    public LdapConnection CreateBindConnection()
    {
        return CreateConnection(bindAsServiceAccount: false);
    }

    private LdapConnection CreateConnection(bool bindAsServiceAccount)
    {
        var identifier = new LdapDirectoryIdentifier(_options.Server, _options.Port);

        var connection = new LdapConnection(identifier)
        {
            AuthType = AuthType.Basic,
            AutoBind = false
        };

        connection.SessionOptions.ProtocolVersion = 3;
        connection.Timeout = TimeSpan.FromSeconds(_options.ConnectionTimeoutSeconds);

        if (_options.UseSsl)
        {
            connection.SessionOptions.SecureSocketLayer = true;
        }

        if (_options.UseStartTls)
        {
            connection.SessionOptions.StartTransportLayerSecurity(null);
        }

        if (bindAsServiceAccount && !string.IsNullOrEmpty(_options.BindDn))
        {
            var credential = new NetworkCredential(_options.BindDn, _options.BindPassword);
            connection.Bind(credential);
            _logger.LogDebug("Bound to LDAP server as {BindDn}", _options.BindDn);
        }

        return connection;
    }

    private bool IsConnectionValid(LdapConnection connection)
    {
        try
        {
            // Simple operation to test connection
            var request = new SearchRequest(
                _options.BaseDn,
                "(objectClass=*)",
                SearchScope.Base,
                "1.1" // No attributes, just test connectivity
            );
            request.TimeLimit = TimeSpan.FromSeconds(5);

            connection.SendRequest(request);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        while (_pool.TryTake(out var connection))
        {
            try { connection.Dispose(); } catch { }
        }

        _semaphore.Dispose();
    }
}

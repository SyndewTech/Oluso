using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Oluso.Enterprise.Ldap.Server;

/// <summary>
/// Hosted service that manages the LDAP server lifecycle
/// </summary>
public class LdapServerHostedService : IHostedService
{
    private readonly ILdapServer _ldapServer;
    private readonly LdapServerOptions _options;
    private readonly ILogger<LdapServerHostedService> _logger;

    public LdapServerHostedService(
        ILdapServer ldapServer,
        IOptions<LdapServerOptions> options,
        ILogger<LdapServerHostedService> logger)
    {
        _ldapServer = ldapServer;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("LDAP server is disabled in configuration");
            return;
        }

        _logger.LogInformation("Starting LDAP server on port {Port}...", _options.Port);

        try 
        {
            await _ldapServer.StartAsync(cancellationToken);
            _logger.LogInformation("LDAP server started successfully. Base DN: {BaseDn}", _options.BaseDn);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start LDAP server");
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (!_options.Enabled)
            return;

        _logger.LogInformation("Stopping LDAP server...");

        try
        {
            await _ldapServer.StopAsync(cancellationToken);
            _logger.LogInformation("LDAP server stopped");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error stopping LDAP server");
        }
    }
}

using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Enterprise.Ldap.Configuration;
using Oluso.Enterprise.Ldap.Entities;
using Oluso.Enterprise.Ldap.Server.Protocol;
using Oluso.Enterprise.Ldap.Stores;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Oluso.Enterprise.Ldap.Server;

/// <summary>
/// LDAP server implementation that exposes ASP.NET Core Identity users.
/// Supports plain LDAP, LDAPS (SSL/TLS), and STARTTLS.
/// </summary>
public class LdapServer : ILdapServer
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly LdapServerOptions _options;
    private readonly ILogger<LdapServer> _logger;

    private TcpListener? _listener;
    private TcpListener? _sslListener;
    private CancellationTokenSource? _cts;
    private Task? _acceptTask;
    private Task? _sslAcceptTask;
    private readonly ConcurrentDictionary<string, LdapConnection> _connections = new();

    // Cached global certificate for SSL
    private X509Certificate2? _globalCertificate;

    public bool IsRunning => _listener != null || _sslListener != null;
    public int ActiveConnections => _connections.Count;

    public LdapServer(
        IServiceScopeFactory scopeFactory,
        IOptions<LdapServerOptions> options,
        ILogger<LdapServer> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("LDAP server is disabled");
            return;
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        // Load global SSL certificate if SSL is enabled
        if (_options.EnableSsl || _options.EnableStartTls)
        {
            _globalCertificate = await LoadGlobalCertificateAsync(cancellationToken);
            if (_globalCertificate == null)
            {
                _logger.LogWarning("SSL/TLS is enabled but no certificate is configured. SSL features will be unavailable.");
            }
        }

        // Start plain LDAP listener
        _listener = new TcpListener(IPAddress.Any, _options.Port);
        _listener.Start();
        _logger.LogInformation("LDAP server started on port {Port}", _options.Port);
        _acceptTask = AcceptConnectionsAsync(_listener, isSsl: false, _cts.Token);

        // Start LDAPS listener if enabled
        if (_options.EnableSsl && _globalCertificate != null)
        {
            _sslListener = new TcpListener(IPAddress.Any, _options.SslPort);
            _sslListener.Start();
            _logger.LogInformation("LDAPS server started on port {SslPort}", _options.SslPort);
            _sslAcceptTask = AcceptConnectionsAsync(_sslListener, isSsl: true, _cts.Token);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        _cts?.Cancel();
        _listener?.Stop();
        _sslListener?.Stop();

        foreach (var connection in _connections.Values)
        {
            await connection.CloseAsync();
        }
        _connections.Clear();

        var tasks = new List<Task>();
        if (_acceptTask != null) tasks.Add(_acceptTask);
        if (_sslAcceptTask != null) tasks.Add(_sslAcceptTask);

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        _globalCertificate?.Dispose();
        _globalCertificate = null;

        _logger.LogInformation("LDAP server stopped");
    }

    private async Task<X509Certificate2?> LoadGlobalCertificateAsync(CancellationToken cancellationToken)
    {
        // Try managed certificates first
        if (_options.UseManagedCertificates)
        {
            using var scope = _scopeFactory.CreateScope();
            var certService = scope.ServiceProvider.GetService<ICertificateService>();
            if (certService != null)
            {
                var cert = await certService.GetCertificateAsync(
                    CertificatePurpose.LdapTls,
                    cancellationToken: cancellationToken);

                if (cert != null)
                {
                    _logger.LogInformation("Loaded LDAP TLS certificate from ICertificateService: {Subject}",
                        cert.Subject);
                    return cert;
                }
            }
        }

        // Fall back to file-based certificate
        if (!string.IsNullOrEmpty(_options.SslCertificatePath))
        {
            try
            {
                var cert = new X509Certificate2(
                    _options.SslCertificatePath,
                    _options.SslCertificatePassword,
                    X509KeyStorageFlags.MachineKeySet | X509KeyStorageFlags.PersistKeySet);

                _logger.LogInformation("Loaded LDAP TLS certificate from file: {Path}, Subject: {Subject}",
                    _options.SslCertificatePath, cert.Subject);
                return cert;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load SSL certificate from {Path}", _options.SslCertificatePath);
            }
        }

        return null;
    }

    private async Task<X509Certificate2?> GetCertificateForTenantAsync(string? tenantId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(tenantId) || !_options.UseManagedCertificates)
        {
            return _globalCertificate;
        }

        using var scope = _scopeFactory.CreateScope();

        // Check tenant-specific settings
        var ldapSettings = scope.ServiceProvider.GetService<ILdapTenantSettingsService>();
        if (ldapSettings != null)
        {
            var tenantLdapSettings = await ldapSettings.GetSettingsAsync(tenantId, cancellationToken);
            if (tenantLdapSettings.TlsCertificate?.Source != LdapCertificateSource.Global)
            {
                var certService = scope.ServiceProvider.GetService<ICertificateService>();
                if (certService != null)
                {
                    var tenantCert = await certService.GetCertificateAsync(
                        CertificatePurpose.LdapTls,
                        tenantId,
                        cancellationToken: cancellationToken);

                    if (tenantCert != null)
                    {
                        _logger.LogDebug("Using tenant-specific TLS certificate for {TenantId}", tenantId);
                        return tenantCert;
                    }
                }
            }
        }

        return _globalCertificate;
    }

    private SslProtocols GetAllowedProtocols()
    {
        var protocols = SslProtocols.None;
        foreach (var proto in _options.AllowedTlsProtocols)
        {
            if (Enum.TryParse<SslProtocols>(proto, ignoreCase: true, out var parsed))
            {
                protocols |= parsed;
            }
        }
        return protocols == SslProtocols.None ? SslProtocols.Tls12 | SslProtocols.Tls13 : protocols;
    }

    private async Task AcceptConnectionsAsync(TcpListener listener, bool isSsl, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var tcpClient = await listener.AcceptTcpClientAsync(cancellationToken);

                if (_connections.Count >= _options.MaxConnections)
                {
                    _logger.LogWarning("Max connections reached, rejecting new connection");
                    tcpClient.Close();
                    continue;
                }

                var connectionId = Guid.NewGuid().ToString();
                Stream stream = tcpClient.GetStream();

                // For LDAPS connections, wrap in SSL immediately
                if (isSsl && _globalCertificate != null)
                {
                    var sslStream = new SslStream(stream, leaveInnerStreamOpen: false);
                    try
                    {
                        await sslStream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
                        {
                            ServerCertificate = _globalCertificate,
                            ClientCertificateRequired = _options.RequireClientCertificate,
                            EnabledSslProtocols = GetAllowedProtocols(),
                            CertificateRevocationCheckMode = X509RevocationMode.NoCheck
                        }, cancellationToken);

                        stream = sslStream;
                        _logger.LogDebug("LDAPS connection {ConnectionId} established with {Protocol}",
                            connectionId, sslStream.SslProtocol);
                    }
                    catch (AuthenticationException ex)
                    {
                        _logger.LogWarning(ex, "SSL/TLS handshake failed for connection {ConnectionId}", connectionId);
                        sslStream.Dispose();
                        tcpClient.Close();
                        continue;
                    }
                }

                var connection = new LdapConnection(
                    connectionId,
                    tcpClient,
                    stream,
                    _scopeFactory,
                    _options,
                    _logger,
                    _options.EnableStartTls ? _globalCertificate : null,
                    GetAllowedProtocols(),
                    () => _connections.TryRemove(connectionId, out _));

                _connections[connectionId] = connection;
                _ = connection.HandleAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error accepting connection");
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _cts?.Dispose();
    }
}

/// <summary>
/// Represents a single LDAP client connection
/// </summary>
internal class LdapConnection
{
    private readonly string _id;
    private readonly TcpClient _client;
    private Stream _stream;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly LdapServerOptions _options;
    private readonly ILogger _logger;
    private readonly X509Certificate2? _startTlsCertificate;
    private readonly SslProtocols _allowedProtocols;
    private readonly Action _onClose;

    private string? _boundDn;
    private bool _isAuthenticated;
    private string? _boundTenantId;
    private bool _isTlsUpgraded;
    private LdapServiceAccount? _boundServiceAccount;

    public LdapConnection(
        string id,
        TcpClient client,
        Stream stream,
        IServiceScopeFactory scopeFactory,
        LdapServerOptions options,
        ILogger logger,
        X509Certificate2? startTlsCertificate,
        SslProtocols allowedProtocols,
        Action onClose)
    {
        _id = id;
        _client = client;
        _stream = stream;
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;
        _startTlsCertificate = startTlsCertificate;
        _allowedProtocols = allowedProtocols;
        _onClose = onClose;
    }

    public async Task HandleAsync(CancellationToken cancellationToken)
    {
        var buffer = new byte[65536];

        try
        {
            while (!cancellationToken.IsCancellationRequested && _client.Connected)
            {
                // Read length-prefixed message
                var bytesRead = await _stream.ReadAsync(buffer, cancellationToken);
                if (bytesRead == 0)
                    break;

                var messageData = buffer[..bytesRead];
                var message = LdapMessage.Decode(messageData);

                if (message == null)
                {
                    _logger.LogWarning("Failed to decode LDAP message");
                    continue;
                }

                var response = await ProcessMessageAsync(message, cancellationToken);

                if (response != null)
                {
                    if (response is IEnumerable<LdapMessage> responses)
                    {
                        foreach (var resp in responses)
                        {
                            var encoded = resp.Encode();
                            await _stream.WriteAsync(encoded, cancellationToken);
                        }
                    }
                    else if (response is LdapMessage ldapMessage)
                    {
                        var encoded = ldapMessage.Encode();
                        await _stream.WriteAsync(encoded, cancellationToken);
                    }
                }

                if (message is UnbindRequest)
                    break;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Error handling LDAP connection {ConnectionId}", _id);
        }
        finally
        {
            await CloseAsync();
        }
    }

    public Task CloseAsync()
    {
        _stream.Dispose();
        _client.Close();
        _onClose();
        return Task.CompletedTask;
    }

    private async Task<object?> ProcessMessageAsync(LdapMessage message, CancellationToken cancellationToken)
    {
        return message switch
        {
            BindRequest bind => await HandleBindAsync(bind, cancellationToken),
            SearchRequest search => await HandleSearchAsync(search, cancellationToken),
            ExtendedRequest extended => await HandleExtendedAsync(extended, cancellationToken),
            UnbindRequest => null,
            _ => new BindResponse
            {
                MessageId = message.MessageId,
                ResultCode = LdapResultCode.UnwillingToPerform,
                DiagnosticMessage = "Operation not supported"
            }
        };
    }

    private async Task<ExtendedResponse> HandleExtendedAsync(ExtendedRequest request, CancellationToken cancellationToken)
    {
        // STARTTLS OID: 1.3.6.1.4.1.1466.20037
        if (request.RequestName == "1.3.6.1.4.1.1466.20037")
        {
            return await HandleStartTlsAsync(request, cancellationToken);
        }

        return new ExtendedResponse
        {
            MessageId = request.MessageId,
            ResultCode = LdapResultCode.UnwillingToPerform,
            DiagnosticMessage = $"Extended operation {request.RequestName} not supported"
        };
    }

    private async Task<ExtendedResponse> HandleStartTlsAsync(ExtendedRequest request, CancellationToken cancellationToken)
    {
        if (_isTlsUpgraded)
        {
            return new ExtendedResponse
            {
                MessageId = request.MessageId,
                ResultCode = LdapResultCode.OperationsError,
                ResponseName = "1.3.6.1.4.1.1466.20037",
                DiagnosticMessage = "TLS already established"
            };
        }

        if (_startTlsCertificate == null)
        {
            return new ExtendedResponse
            {
                MessageId = request.MessageId,
                ResultCode = LdapResultCode.UnwillingToPerform,
                ResponseName = "1.3.6.1.4.1.1466.20037",
                DiagnosticMessage = "STARTTLS not supported"
            };
        }

        // Send success response before upgrading
        var successResponse = new ExtendedResponse
        {
            MessageId = request.MessageId,
            ResultCode = LdapResultCode.Success,
            ResponseName = "1.3.6.1.4.1.1466.20037"
        };

        var encoded = successResponse.Encode();
        await _stream.WriteAsync(encoded, cancellationToken);
        await _stream.FlushAsync(cancellationToken);

        // Now upgrade to TLS
        try
        {
            var sslStream = new SslStream(_stream, leaveInnerStreamOpen: false);
            await sslStream.AuthenticateAsServerAsync(new SslServerAuthenticationOptions
            {
                ServerCertificate = _startTlsCertificate,
                ClientCertificateRequired = _options.RequireClientCertificate,
                EnabledSslProtocols = _allowedProtocols,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck
            }, cancellationToken);

            _stream = sslStream;
            _isTlsUpgraded = true;

            _logger.LogInformation("STARTTLS upgrade successful for connection {ConnectionId}, protocol: {Protocol}",
                _id, sslStream.SslProtocol);

            // Don't return a response - we already sent it before the upgrade
            return null!;
        }
        catch (AuthenticationException ex)
        {
            _logger.LogError(ex, "STARTTLS handshake failed for connection {ConnectionId}", _id);
            throw; // This will close the connection
        }
    }

    private async Task<BindResponse> HandleBindAsync(BindRequest request, CancellationToken cancellationToken)
    {
        // Anonymous bind
        if (string.IsNullOrEmpty(request.Name))
        {
            if (_options.AllowAnonymousBind)
            {
                _isAuthenticated = false;
                _boundDn = null;
                return new BindResponse
                {
                    MessageId = request.MessageId,
                    ResultCode = LdapResultCode.Success
                };
            }

            return new BindResponse
            {
                MessageId = request.MessageId,
                ResultCode = LdapResultCode.InappropriateAuthentication,
                DiagnosticMessage = "Anonymous bind not allowed"
            };
        }

        // Check for global admin bind
        if (!string.IsNullOrEmpty(_options.AdminDn) &&
            request.Name.Equals(_options.AdminDn, StringComparison.OrdinalIgnoreCase))
        {
            var adminPassword = request.GetSimplePassword();
            if (adminPassword == _options.AdminPassword)
            {
                _isAuthenticated = true;
                _boundDn = request.Name;
                _boundTenantId = null; // Admin has access to all tenants
                return new BindResponse
                {
                    MessageId = request.MessageId,
                    ResultCode = LdapResultCode.Success
                };
            }

            return new BindResponse
            {
                MessageId = request.MessageId,
                ResultCode = LdapResultCode.InvalidCredentials,
                DiagnosticMessage = "Invalid admin credentials"
            };
        }

        // Check for service account bind (DN contains ou=services)
        if (request.Name.Contains("ou=services", StringComparison.OrdinalIgnoreCase))
        {
            return await HandleServiceAccountBindAsync(request, cancellationToken);
        }

        // Parse user DN to extract username and tenant
        var (username, tenantId) = ParseUserDn(request.Name);
        if (string.IsNullOrEmpty(username))
        {
            return new BindResponse
            {
                MessageId = request.MessageId,
                ResultCode = LdapResultCode.InvalidDnSyntax,
                DiagnosticMessage = "Invalid DN format"
            };
        }

        // Authenticate against Identity
        using var scope = _scopeFactory.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<OlusoUser>>();
        var signInManager = scope.ServiceProvider.GetRequiredService<SignInManager<OlusoUser>>();

        // Find user
        var usersQuery = userManager.Users.Where(u =>
            u.UserName == username || u.Email == username);

        if (!string.IsNullOrEmpty(tenantId))
        {
            usersQuery = usersQuery.Where(u => u.TenantId == tenantId);
        }

        var user = await usersQuery.FirstOrDefaultAsync(cancellationToken);

        if (user == null)
        {
            return new BindResponse
            {
                MessageId = request.MessageId,
                ResultCode = LdapResultCode.InvalidCredentials,
                DiagnosticMessage = "Invalid credentials"
            };
        }

        if (!user.IsActive)
        {
            return new BindResponse
            {
                MessageId = request.MessageId,
                ResultCode = LdapResultCode.InvalidCredentials,
                DiagnosticMessage = "Account is disabled"
            };
        }

        // Check password
        var password = request.GetSimplePassword();
        var result = await signInManager.CheckPasswordSignInAsync(user, password ?? "", lockoutOnFailure: true);

        if (!result.Succeeded)
        {
            var diagnostic = result.IsLockedOut ? "Account is locked" :
                             result.IsNotAllowed ? "Sign in not allowed" :
                             "Invalid credentials";

            return new BindResponse
            {
                MessageId = request.MessageId,
                ResultCode = LdapResultCode.InvalidCredentials,
                DiagnosticMessage = diagnostic
            };
        }

        _isAuthenticated = true;
        _boundDn = BuildUserDn(user);
        _boundTenantId = user.TenantId;

        _logger.LogInformation("LDAP bind successful for user {UserId}", user.Id);

        return new BindResponse
        {
            MessageId = request.MessageId,
            ResultCode = LdapResultCode.Success
        };
    }

    private async Task<BindResponse> HandleServiceAccountBindAsync(BindRequest request, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var serviceAccountStore = scope.ServiceProvider.GetService<ILdapServiceAccountStore>();

        if (serviceAccountStore == null)
        {
            return new BindResponse
            {
                MessageId = request.MessageId,
                ResultCode = LdapResultCode.UnwillingToPerform,
                DiagnosticMessage = "Service accounts not configured"
            };
        }

        var password = request.GetSimplePassword();
        var account = await serviceAccountStore.ValidateCredentialsAsync(request.Name, password ?? "", cancellationToken);

        if (account == null)
        {
            _logger.LogWarning("LDAP service account bind failed for DN: {BindDn}", request.Name);
            return new BindResponse
            {
                MessageId = request.MessageId,
                ResultCode = LdapResultCode.InvalidCredentials,
                DiagnosticMessage = "Invalid credentials"
            };
        }

        _isAuthenticated = true;
        _boundDn = account.BindDn;
        _boundTenantId = account.TenantId;
        _boundServiceAccount = account;

        _logger.LogInformation("LDAP bind successful for service account {AccountId} ({AccountName})",
            account.Id, account.Name);

        return new BindResponse
        {
            MessageId = request.MessageId,
            ResultCode = LdapResultCode.Success
        };
    }

    private async Task<IEnumerable<LdapMessage>> HandleSearchAsync(SearchRequest request, CancellationToken cancellationToken)
    {
        var results = new List<LdapMessage>();

        if (!_isAuthenticated && !_options.AllowAnonymousBind)
        {
            results.Add(new SearchResultDone
            {
                MessageId = request.MessageId,
                ResultCode = LdapResultCode.InsufficientAccessRights,
                DiagnosticMessage = "Bind required"
            });
            return results;
        }

        using var scope = _scopeFactory.CreateScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<OlusoUser>>();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<OlusoRole>>();

        // Determine what we're searching for based on base DN
        var baseDnLower = request.BaseDn.ToLowerInvariant();
        var isUserSearch = baseDnLower.Contains($"ou={_options.UserOu.ToLowerInvariant()}");
        var isGroupSearch = baseDnLower.Contains($"ou={_options.GroupOu.ToLowerInvariant()}");

        // Check if memberOf or member attributes are requested
        var needsMemberOf = !request.Attributes.Any() ||
            request.Attributes.Any(a => a.Equals("*") ||
                a.Equals(_options.AttributeMappings.MemberOf, StringComparison.OrdinalIgnoreCase));
        var needsMembers = !request.Attributes.Any() ||
            request.Attributes.Any(a => a.Equals("*") ||
                a.Equals("member", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("memberuid", StringComparison.OrdinalIgnoreCase));

        // Extract tenant from base DN if tenant isolation is enabled
        var searchTenantId = _boundTenantId;
        if (_options.TenantIsolation && string.IsNullOrEmpty(_boundTenantId))
        {
            searchTenantId = ExtractTenantFromDn(request.BaseDn);
        }

        if (isUserSearch || (!isGroupSearch && request.Scope != SearchScope.BaseObject))
        {
            var users = await SearchUsersAsync(userManager, searchTenantId, request, cancellationToken);

            foreach (var user in users.Take(_options.MaxSearchResults))
            {
                // Get user's roles if memberOf is requested
                List<string>? userRoles = null;
                if (needsMemberOf)
                {
                    userRoles = await GetUserRolesAsync(userManager, user);
                }

                var entry = BuildUserEntry(user, request, userRoles);
                entry.MessageId = request.MessageId;
                results.Add(entry);
            }
        }

        if (isGroupSearch || (!isUserSearch && request.Scope != SearchScope.BaseObject))
        {
            var roles = await SearchRolesAsync(roleManager, searchTenantId, request, cancellationToken);
            var rolesToReturn = roles.Take(_options.MaxSearchResults - results.Count).ToList();

            // Get members for each role if member attribute is requested
            Dictionary<string, List<OlusoUser>>? roleMembers = null;
            if (needsMembers && rolesToReturn.Any())
            {
                roleMembers = await GetRoleMembersAsync(userManager, rolesToReturn, searchTenantId, cancellationToken);
            }

            foreach (var role in rolesToReturn)
            {
                var members = roleMembers?.GetValueOrDefault(role.Id) ?? new List<OlusoUser>();
                var entry = BuildGroupEntry(role, request, members);
                entry.MessageId = request.MessageId;
                results.Add(entry);
            }
        }

        // Add search result done
        results.Add(new SearchResultDone
        {
            MessageId = request.MessageId,
            ResultCode = LdapResultCode.Success
        });

        return results;
    }

    private async Task<List<OlusoUser>> SearchUsersAsync(
        UserManager<OlusoUser> userManager,
        string? tenantId,
        SearchRequest request,
        CancellationToken cancellationToken)
    {
        var query = userManager.Users.Where(u => u.IsActive);

        if (!string.IsNullOrEmpty(tenantId))
        {
            query = query.Where(u => u.TenantId == tenantId);
        }

        var users = await query.ToListAsync(cancellationToken);

        // Apply LDAP filter
        if (request.Filter != null)
        {
            users = users.Where(u =>
            {
                var attrs = BuildUserAttributes(u);
                return request.Filter.Matches(attrs);
            }).ToList();
        }

        return users;
    }

    private async Task<List<OlusoRole>> SearchRolesAsync(
        RoleManager<OlusoRole> roleManager,
        string? tenantId,
        SearchRequest request,
        CancellationToken cancellationToken)
    {
        var query = roleManager.Roles.AsQueryable();

        if (!string.IsNullOrEmpty(tenantId))
        {
            query = query.Where(r => r.TenantId == tenantId || r.TenantId == null);
        }

        var roles = await query.ToListAsync(cancellationToken);

        // Apply LDAP filter
        if (request.Filter != null)
        {
            roles = roles.Where(r =>
            {
                var attrs = BuildGroupAttributes(r, new List<OlusoUser>());
                return request.Filter.Matches(attrs);
            }).ToList();
        }

        return roles;
    }

    private async Task<Dictionary<string, List<OlusoUser>>> GetRoleMembersAsync(
        UserManager<OlusoUser> userManager,
        IEnumerable<OlusoRole> roles,
        string? tenantId,
        CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, List<OlusoUser>>();

        // Get all active users for this tenant
        var usersQuery = userManager.Users.Where(u => u.IsActive);
        if (!string.IsNullOrEmpty(tenantId))
        {
            usersQuery = usersQuery.Where(u => u.TenantId == tenantId);
        }
        var users = await usersQuery.ToListAsync(cancellationToken);

        // For each role, find users who have that role
        foreach (var role in roles)
        {
            var roleUsers = new List<OlusoUser>();
            foreach (var user in users)
            {
                if (await userManager.IsInRoleAsync(user, role.Name ?? role.Id))
                {
                    roleUsers.Add(user);
                }
            }
            result[role.Id] = roleUsers;
        }

        return result;
    }

    private async Task<List<string>> GetUserRolesAsync(
        UserManager<OlusoUser> userManager,
        OlusoUser user)
    {
        var roles = await userManager.GetRolesAsync(user);
        return roles.ToList();
    }

    private Dictionary<string, List<string>> BuildUserAttributes(OlusoUser user, List<string>? roleNames = null)
    {
        var attrs = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["objectclass"] = new() { "inetOrgPerson", "organizationalPerson", "person", "top" },
            [_options.AttributeMappings.UserId.ToLowerInvariant()] = new() { user.UserName ?? user.Id },
            [_options.AttributeMappings.CommonName.ToLowerInvariant()] = new() { user.DisplayName ?? user.UserName ?? "" },
            [_options.AttributeMappings.UniqueId.ToLowerInvariant()] = new() { user.Id }
        };

        if (!string.IsNullOrEmpty(user.Email))
            attrs[_options.AttributeMappings.Email.ToLowerInvariant()] = new() { user.Email };

        if (!string.IsNullOrEmpty(user.FirstName))
            attrs[_options.AttributeMappings.GivenName.ToLowerInvariant()] = new() { user.FirstName };

        if (!string.IsNullOrEmpty(user.LastName))
            attrs[_options.AttributeMappings.Surname.ToLowerInvariant()] = new() { user.LastName };

        if (!string.IsNullOrEmpty(user.DisplayName))
            attrs[_options.AttributeMappings.DisplayName.ToLowerInvariant()] = new() { user.DisplayName };

        if (!string.IsNullOrEmpty(user.PhoneNumber))
            attrs[_options.AttributeMappings.Phone.ToLowerInvariant()] = new() { user.PhoneNumber };

        // Add memberOf attribute with group DNs
        if (roleNames != null && roleNames.Count > 0)
        {
            var memberOfDns = roleNames.Select(roleName =>
            {
                if (_options.TenantIsolation && !string.IsNullOrEmpty(user.TenantId))
                {
                    return $"cn={roleName},ou={_options.GroupOu},o={user.TenantId},{_options.BaseDn}";
                }
                return $"cn={roleName},ou={_options.GroupOu},{_options.BaseDn}";
            }).ToList();

            attrs[_options.AttributeMappings.MemberOf.ToLowerInvariant()] = memberOfDns;
        }

        return attrs;
    }

    private Dictionary<string, List<string>> BuildGroupAttributes(OlusoRole role, List<OlusoUser> members)
    {
        var attrs = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["objectclass"] = new() { "groupOfNames", "top" },
            ["cn"] = new() { role.Name ?? role.Id },
            ["description"] = new() { role.Description ?? "" },
            [_options.AttributeMappings.UniqueId.ToLowerInvariant()] = new() { role.Id }
        };

        // Add member attribute with user DNs
        if (members.Count > 0)
        {
            var memberDns = members.Select(user => BuildUserDn(user)).ToList();
            attrs["member"] = memberDns;

            // Also add memberUid for compatibility with some LDAP clients
            var memberUids = members.Select(user => user.UserName ?? user.Id).ToList();
            attrs["memberuid"] = memberUids;
        }

        return attrs;
    }

    private SearchResultEntry BuildUserEntry(OlusoUser user, SearchRequest request, List<string>? roleNames = null)
    {
        var dn = BuildUserDn(user);
        var allAttrs = BuildUserAttributes(user, roleNames);

        var attrs = request.Attributes.Any()
            ? allAttrs.Where(a => request.Attributes.Any(r =>
                r.Equals("*") || r.Equals(a.Key, StringComparison.OrdinalIgnoreCase)))
                .ToDictionary(a => a.Key, a => a.Value)
            : allAttrs;

        return new SearchResultEntry
        {
            ObjectName = dn,
            Attributes = attrs
        };
    }

    private SearchResultEntry BuildGroupEntry(OlusoRole role, SearchRequest request, List<OlusoUser>? members = null)
    {
        var dn = BuildGroupDn(role);
        var allAttrs = BuildGroupAttributes(role, members ?? new List<OlusoUser>());

        var attrs = request.Attributes.Any()
            ? allAttrs.Where(a => request.Attributes.Any(r =>
                r.Equals("*") || r.Equals(a.Key, StringComparison.OrdinalIgnoreCase)))
                .ToDictionary(a => a.Key, a => a.Value)
            : allAttrs;

        return new SearchResultEntry
        {
            ObjectName = dn,
            Attributes = attrs
        };
    }

    private string BuildUserDn(OlusoUser user)
    {
        var uid = user.UserName ?? user.Id;
        if (_options.TenantIsolation && !string.IsNullOrEmpty(user.TenantId))
        {
            return $"uid={uid},ou={_options.UserOu},o={user.TenantId},{_options.BaseDn}";
        }
        return $"uid={uid},ou={_options.UserOu},{_options.BaseDn}";
    }

    private string BuildGroupDn(OlusoRole role)
    {
        var cn = role.Name ?? role.Id;
        if (_options.TenantIsolation && !string.IsNullOrEmpty(role.TenantId))
        {
            return $"cn={cn},ou={_options.GroupOu},o={role.TenantId},{_options.BaseDn}";
        }
        return $"cn={cn},ou={_options.GroupOu},{_options.BaseDn}";
    }

    private (string? Username, string? TenantId) ParseUserDn(string dn)
    {
        // Parse DN like: uid=username,ou=users,o=tenantId,dc=example,dc=com
        // or: uid=username,ou=users,dc=example,dc=com
        string? username = null;
        string? tenantId = null;

        var parts = dn.Split(',');
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("uid=", StringComparison.OrdinalIgnoreCase))
            {
                username = trimmed[4..];
            }
            else if (trimmed.StartsWith("cn=", StringComparison.OrdinalIgnoreCase))
            {
                username ??= trimmed[3..];
            }
            else if (trimmed.StartsWith("o=", StringComparison.OrdinalIgnoreCase) &&
                     !trimmed.StartsWith("ou=", StringComparison.OrdinalIgnoreCase))
            {
                tenantId = trimmed[2..];
            }
        }

        return (username, tenantId);
    }

    private string? ExtractTenantFromDn(string dn)
    {
        var parts = dn.Split(',');
        foreach (var part in parts)
        {
            var trimmed = part.Trim();
            if (trimmed.StartsWith("o=", StringComparison.OrdinalIgnoreCase) &&
                !trimmed.StartsWith("ou=", StringComparison.OrdinalIgnoreCase))
            {
                return trimmed[2..];
            }
        }
        return null;
    }
}

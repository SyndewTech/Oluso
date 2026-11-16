using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Security;
using Oluso.Core.Services;

namespace Oluso.Services;

/// <summary>
/// Implementation of backchannel logout per OpenID Connect Back-Channel Logout 1.0.
/// Sends logout tokens to clients' backchannel logout URIs when a user logs out, and to the resource-server
/// watchers (<see cref="IResourceServerLogoutNotifier"/>) for the ended sessions that hold their scopes.
/// </summary>
public class BackchannelLogoutService : IBackchannelLogoutService, IResourceServerLogoutNotifier
{
    /// <summary>
    /// The named HttpClient logout tokens go to clients through (configured with
    /// <see cref="PublicHttpTargets.CreateHandler"/>): a client's backchannel_logout_uri is set by a tenant
    /// administrator, so delivery is held to the server-side request forgery guard.
    /// </summary>
    public const string HttpClientName = "Oluso.BackchannelLogout";

    /// <summary>
    /// Admits backchannel_logout_uri targets on private, loopback and link-local addresses, for a deployment whose
    /// relying parties live on its own network and whose tenant administrators are all trusted. Off by default.
    /// </summary>
    public const string AllowPrivateNetworkSetting = "Oluso:BackchannelLogout:AllowPrivateNetworkTargets";

    /// <summary>How long one client gets to accept its logout token.</summary>
    public static readonly TimeSpan SendTimeout = TimeSpan.FromSeconds(5);

    private readonly IClientStore _clientStore;
    private readonly IPersistedGrantStore _grantStore;
    private readonly ILogoutTokenGenerator _logoutTokenGenerator;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BackchannelLogoutService> _logger;
    private readonly ResourceServerLogoutOptions _resourceServers;

    public BackchannelLogoutService(
        IClientStore clientStore,
        IPersistedGrantStore grantStore,
        ILogoutTokenGenerator logoutTokenGenerator,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<BackchannelLogoutService> logger,
        IOptions<ResourceServerLogoutOptions>? resourceServers = null)
    {
        _configuration = configuration;
        _clientStore = clientStore;
        _grantStore = grantStore;
        _logoutTokenGenerator = logoutTokenGenerator;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _resourceServers = resourceServers?.Value ?? new ResourceServerLogoutOptions();
    }

    public async Task NotifyAsync(IEnumerable<PersistedGrant> endedGrants, CancellationToken cancellationToken = default)
    {
        if (_resourceServers.Watchers.Count == 0)
            return;

        // One logout token per watcher and user session: grants of a session share its sid.
        var sessions = endedGrants
            .Where(g => !string.IsNullOrEmpty(g.SubjectId) && !string.IsNullOrEmpty(g.SessionId))
            .Select(g => (Grant: g, Scopes: ScopesOf(g)))
            .ToList();

        foreach (var watcher in _resourceServers.Watchers)
        {
            var targets = sessions
                .Where(s => s.Scopes.Any(watcher.Scopes.Contains))
                .Select(s => (Subject: s.Grant.SubjectId!, Session: s.Grant.SessionId!))
                .Distinct()
                .ToList();
            foreach (var (subject, session) in targets)
            {
                try
                {
                    await SendLogoutNotificationAsync(watcher.ClientId, subject, session, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogWarning(ex, "Back-channel logout to resource server client {ClientId} for session {SessionId} failed",
                        watcher.ClientId, session);
                }
            }
        }
    }

    /// <summary>The scopes a stored grant (refresh or reference token) was issued for.</summary>
    internal static IReadOnlyCollection<string> ScopesOf(PersistedGrant grant)
    {
        if (string.IsNullOrEmpty(grant.Data))
            return Array.Empty<string>();
        try
        {
            using var document = JsonDocument.Parse(grant.Data);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && (document.RootElement.TryGetProperty("Scopes", out var scopes) || document.RootElement.TryGetProperty("scopes", out scopes))
                && scopes.ValueKind == JsonValueKind.Array)
            {
                return scopes.EnumerateArray().Where(s => s.ValueKind == JsonValueKind.String).Select(s => s.GetString()!).ToList();
            }
        }
        catch (JsonException)
        {
        }
        return Array.Empty<string>();
    }

    /// <summary>
    /// How many logout tokens are posted at once: enough that one slow relying party does not hold up the rest,
    /// few enough that a logout never floods the outbound connection pool.
    /// </summary>
    public const int MaxParallelDeliveries = 4;

    /// <summary>
    /// Tells the relying parties of the ended session(s) (Back-Channel Logout 1.0). With a session id, the clients
    /// that took part in that session (<see cref="Protocols.SessionClients"/> and the session's grants) each get a
    /// logout token carrying its sid; another session of the same user, on another device or browser, is not
    /// touched. Without one (the user signed out everywhere), every session gets its own token, so each client
    /// ends exactly the sessions it holds. Call it before the session's grants are removed: they name its clients.
    /// </summary>
    public async Task<BackchannelLogoutResult> SendLogoutNotificationsAsync(
        string subjectId,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        var result = new BackchannelLogoutResult();

        var grants = (await _grantStore.GetAllAsync(
            new PersistedGrantFilter { SubjectId = subjectId, SessionId = sessionId },
            cancellationToken)).ToList();

        // Resource servers watching the ended session(s).
        await NotifyAsync(grants, cancellationToken);

        // One delivery per client and session. Grants from before session ids were recorded name no session: their
        // clients get a token without sid, which ends all of the user's sessions there (section 2.4).
        var targets = grants
            .Select(g => (ClientId: g.ClientId, SessionId: string.IsNullOrEmpty(g.SessionId) ? null : g.SessionId))
            .Where(t => !string.IsNullOrEmpty(t.ClientId))
            .Distinct()
            .ToList();

        // Clients and tokens one after another (they come from the scoped stores); then the posts in parallel.
        var deliveries = new List<(string ClientId, Uri Target, string LogoutToken)>();
        foreach (var (clientId, targetSession) in targets)
        {
            var prepared = await PrepareAsync(clientId, subjectId, targetSession, cancellationToken);
            if (prepared != null)
            {
                deliveries.Add((clientId, prepared.Value.Target, prepared.Value.LogoutToken));
            }
        }

        using var gate = new SemaphoreSlim(MaxParallelDeliveries);
        var outcomes = await Task.WhenAll(deliveries.Select(async d =>
        {
            await gate.WaitAsync(cancellationToken);
            try
            {
                return (d.ClientId, Delivered: await PostAsync(d.ClientId, d.Target, d.LogoutToken, cancellationToken), Error: (string?)null);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return (d.ClientId, Delivered: false, Error: ex.Message);
            }
            finally
            {
                gate.Release();
            }
        }));

        foreach (var outcome in outcomes)
        {
            if (outcome.Delivered)
            {
                result.SuccessCount++;
            }
            else
            {
                result.FailedClients.Add(new BackchannelLogoutFailure
                {
                    ClientId = outcome.ClientId,
                    Error = outcome.Error ?? "The client did not accept the logout token"
                });
            }
        }

        _logger.LogInformation(
            "Backchannel logout completed for subject {SubjectId}: {SuccessCount} succeeded, {FailedCount} failed",
            subjectId, result.SuccessCount, result.FailedClients.Count);

        return result;
    }

    public async Task<bool> SendLogoutNotificationAsync(
        string clientId,
        string subjectId,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        var prepared = await PrepareAsync(clientId, subjectId, sessionId, cancellationToken);
        if (prepared == null)
        {
            return true; // Nothing to send: not configured, or not deliverable (logged)
        }

        return await PostAsync(clientId, prepared.Value.Target, prepared.Value.LogoutToken, cancellationToken);
    }

    /// <summary>
    /// The client's backchannel logout URI and the logout token for it, or null when the client has none, cannot
    /// receive one here, or requires a sid that is not known.
    /// </summary>
    private async Task<(Uri Target, string LogoutToken)?> PrepareAsync(
        string clientId,
        string subjectId,
        string? sessionId,
        CancellationToken cancellationToken)
    {
        var client = await _clientStore.FindClientByIdAsync(clientId, cancellationToken);
        if (client == null || string.IsNullOrEmpty(client.BackChannelLogoutUri))
        {
            return null;
        }

        // backchannel_logout_session_required: the client can only act on a token with a sid (section 2.2).
        if (client.BackChannelLogoutSessionRequired && string.IsNullOrEmpty(sessionId))
        {
            _logger.LogWarning(
                "Client {ClientId} requires a sid in its logout token and none is known; not sent", clientId);
            return null;
        }

        var allowPrivateNetwork = _configuration.GetValue<bool>(AllowPrivateNetworkSetting);
        var rejection = Uri.TryCreate(client.BackChannelLogoutUri, UriKind.Absolute, out var target)
            ? PublicHttpTargets.ValidateTarget(target, allowPrivateNetwork)
            : "not an absolute URL";
        if (rejection != null)
        {
            _logger.LogWarning("Backchannel logout URI of client {ClientId} refused: {Reason}", clientId, rejection);
            return null;
        }

        // The sub the client was issued, pairwise for a pairwise client, so the token names the user it knows.
        var clientSubject = Oluso.Core.Protocols.SubjectIdentifiers.ForClient(subjectId, client.ClientId, client.PairWiseSubjectSalt);
        var logoutToken = await _logoutTokenGenerator.GenerateLogoutTokenAsync(
            clientId, clientSubject, sessionId, cancellationToken);
        return (target!, logoutToken);
    }

    /// <summary>
    /// Posts a logout token through the request forgery guard: every resolved address public (unless the deployment
    /// allows its private network), the connection pinned to the checked address, no redirects, a short timeout.
    /// </summary>
    private async Task<bool> PostAsync(string clientId, Uri target, string logoutToken, CancellationToken cancellationToken)
    {
        try
        {
            var allowPrivateNetwork = _configuration.GetValue<bool>(AllowPrivateNetworkSetting);
            var address = await PublicHttpTargets.ResolvePublicAddressAsync(target, cancellationToken, allowPrivateNetwork);
            if (address.Error != null)
            {
                _logger.LogWarning("Backchannel logout URI of client {ClientId} refused: {Reason}", clientId, address.Error);
                return false;
            }

            var httpClient = _httpClientFactory.CreateClient(HttpClientName);
            httpClient.Timeout = SendTimeout;
            using var message = new HttpRequestMessage(HttpMethod.Post, target)
            {
                Content = new FormUrlEncodedContent(new[]
                {
                    new KeyValuePair<string, string>("logout_token", logoutToken)
                })
            };
            PublicHttpTargets.Pin(message, address.Address!, allowPrivateNetwork);
            message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            using var response = await httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogDebug("Successfully sent backchannel logout to client {ClientId} at {Uri}", clientId, target);
                return true;
            }

            // The response body is the target's, not ours: never read into the logs.
            _logger.LogWarning(
                "Backchannel logout to client {ClientId} failed with status {StatusCode}",
                clientId, (int)response.StatusCode);
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException ||
                                   (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            _logger.LogWarning("Backchannel logout to client {ClientId} failed: {Reason}", clientId, ex.Message);
            throw;
        }
    }
}

/// <summary>
/// Generates logout tokens for backchannel logout per OpenID Connect Back-Channel Logout 1.0.
/// </summary>
public class LogoutTokenGenerator : ILogoutTokenGenerator
{
    private readonly ISigningCredentialStore _signingCredentialStore;
    private readonly ITenantSettingsProvider _tenantSettingsProvider;
    private readonly IIssuerResolver _issuerResolver;
    private readonly ILogger<LogoutTokenGenerator> _logger;

    public LogoutTokenGenerator(
        ISigningCredentialStore signingCredentialStore,
        ITenantSettingsProvider tenantSettingsProvider,
        IIssuerResolver issuerResolver,
        ILogger<LogoutTokenGenerator> logger)
    {
        _signingCredentialStore = signingCredentialStore;
        _tenantSettingsProvider = tenantSettingsProvider;
        _issuerResolver = issuerResolver;
        _logger = logger;
    }

    public async Task<string> GenerateLogoutTokenAsync(
        string clientId,
        string subjectId,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        var signingCredentials = await _signingCredentialStore.GetSigningCredentialsAsync(cancellationToken);
        if (signingCredentials == null)
        {
            throw new InvalidOperationException("No signing credentials available");
        }

        var issuer = await _issuerResolver.GetIssuerAsync(cancellationToken);
        var now = DateTime.UtcNow;

        // Build claims for the logout token
        var claims = new List<Claim>
        {
            new Claim(JwtRegisteredClaimNames.Sub, subjectId),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            new Claim(JwtRegisteredClaimNames.Iat, new DateTimeOffset(now).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            // The events claim - required for logout tokens
            new Claim("events", CreateEventsClaimValue(), JsonClaimValueTypes.Json)
        };

        // Add session ID if provided
        if (!string.IsNullOrEmpty(sessionId))
        {
            claims.Add(new Claim("sid", sessionId));
        }

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: clientId,
            claims: claims,
            notBefore: now,
            expires: now.AddMinutes(5), // Logout tokens should be short-lived
            signingCredentials: signingCredentials);

        // Back-Channel Logout 1.0 §2.4: explicitly typed, so a logout token can't be mistaken for an ID token.
        token.Header["typ"] = "logout+jwt";

        var tokenHandler = new JwtSecurityTokenHandler();
        var tokenString = tokenHandler.WriteToken(token);

        _logger.LogDebug(
            "Generated logout token for client {ClientId}, subject {SubjectId}, session {SessionId}",
            clientId, subjectId, sessionId ?? "(none)");

        return tokenString;
    }

    /// <summary>
    /// Creates the events claim value for the logout token.
    /// Per spec: {"http://schemas.openid.net/event/backchannel-logout": {}}
    /// </summary>
    private static string CreateEventsClaimValue()
    {
        var events = new Dictionary<string, object>
        {
            ["http://schemas.openid.net/event/backchannel-logout"] = new Dictionary<string, object>()
        };
        return JsonSerializer.Serialize(events);
    }
}

using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;

namespace Oluso.Services;

/// <summary>
/// Implementation of backchannel logout per OpenID Connect Back-Channel Logout 1.0.
/// Sends logout tokens to clients' backchannel logout URIs when a user logs out.
/// </summary>
public class BackchannelLogoutService : IBackchannelLogoutService
{
    private readonly IClientStore _clientStore;
    private readonly IPersistedGrantStore _grantStore;
    private readonly ILogoutTokenGenerator _logoutTokenGenerator;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<BackchannelLogoutService> _logger;

    public BackchannelLogoutService(
        IClientStore clientStore,
        IPersistedGrantStore grantStore,
        ILogoutTokenGenerator logoutTokenGenerator,
        IHttpClientFactory httpClientFactory,
        ILogger<BackchannelLogoutService> logger)
    {
        _clientStore = clientStore;
        _grantStore = grantStore;
        _logoutTokenGenerator = logoutTokenGenerator;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<BackchannelLogoutResult> SendLogoutNotificationsAsync(
        string subjectId,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        var result = new BackchannelLogoutResult();

        // Find all clients that have grants for this user
        var grants = await _grantStore.GetAllAsync(
            new PersistedGrantFilter { SubjectId = subjectId },
            cancellationToken);

        var clientIds = grants
            .Select(g => g.ClientId)
            .Distinct()
            .ToList();

        _logger.LogDebug("Found {Count} clients with active grants for subject {SubjectId}",
            clientIds.Count, subjectId);

        foreach (var clientId in clientIds)
        {
            try
            {
                var success = await SendLogoutNotificationAsync(
                    clientId, subjectId, sessionId, cancellationToken);

                if (success)
                {
                    result.SuccessCount++;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to send backchannel logout to client {ClientId}", clientId);
                result.FailedClients.Add(new BackchannelLogoutFailure
                {
                    ClientId = clientId,
                    Error = ex.Message
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
        var client = await _clientStore.FindClientByIdAsync(clientId, cancellationToken);
        if (client == null)
        {
            _logger.LogDebug("Client {ClientId} not found, skipping backchannel logout", clientId);
            return false;
        }

        // Check if client has backchannel logout configured
        if (string.IsNullOrEmpty(client.BackChannelLogoutUri))
        {
            _logger.LogDebug("Client {ClientId} has no backchannel logout URI configured", clientId);
            return true; // Not a failure, just not configured
        }

        // Check if session ID is required but not provided
        if (client.BackChannelLogoutSessionRequired && string.IsNullOrEmpty(sessionId))
        {
            _logger.LogWarning(
                "Client {ClientId} requires session in logout token but no session ID provided",
                clientId);
            // Still try to send without session - some clients may accept it
        }

        try
        {
            // Generate the logout token
            var logoutToken = await _logoutTokenGenerator.GenerateLogoutTokenAsync(
                clientId, subjectId, sessionId, cancellationToken);

            // Send the logout token to the client's backchannel logout URI
            using var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));

            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("logout_token", logoutToken)
            });

            var response = await httpClient.PostAsync(
                client.BackChannelLogoutUri, content, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                _logger.LogDebug(
                    "Successfully sent backchannel logout to client {ClientId} at {Uri}",
                    clientId, client.BackChannelLogoutUri);
                return true;
            }

            var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogWarning(
                "Backchannel logout to client {ClientId} failed with status {StatusCode}: {Response}",
                clientId, (int)response.StatusCode, responseBody);

            return false;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex,
                "HTTP error sending backchannel logout to client {ClientId} at {Uri}",
                clientId, client.BackChannelLogoutUri);
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

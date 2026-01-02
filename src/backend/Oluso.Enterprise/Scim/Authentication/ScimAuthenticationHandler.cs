using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oluso.Enterprise.Scim.Services;
using Oluso.Enterprise.Scim.Stores;

namespace Oluso.Enterprise.Scim.Authentication;

/// <summary>
/// Authentication handler for SCIM bearer token authentication
/// </summary>
public class ScimAuthenticationHandler : AuthenticationHandler<ScimAuthenticationOptions>
{
    private readonly IScimClientStore _clientStore;
    private readonly IScimContextAccessor _contextAccessor;

    public ScimAuthenticationHandler(
        IOptionsMonitor<ScimAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IScimClientStore clientStore,
        IScimContextAccessor contextAccessor)
        : base(options, logger, encoder)
    {
        _clientStore = clientStore;
        _contextAccessor = contextAccessor;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        // Check if this is a SCIM endpoint
        var path = Request.Path.Value ?? "";
        if (!path.StartsWith("/scim/", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.NoResult();
        }

        // Discovery endpoints are public
        if (IsDiscoveryEndpoint(path))
        {
            return AuthenticateResult.NoResult();
        }

        // Get Authorization header
        if (!Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            return AuthenticateResult.Fail("Missing Authorization header");
        }

        var authValue = authHeader.ToString();
        if (!authValue.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return AuthenticateResult.Fail("Invalid Authorization scheme");
        }

        var token = authValue["Bearer ".Length..].Trim();
        if (string.IsNullOrEmpty(token))
        {
            return AuthenticateResult.Fail("Missing token");
        }

        // Hash the token and look up the client
        var tokenHash = HashToken(token);
        var client = await _clientStore.GetByTokenHashAsync(tokenHash);

        if (client == null)
        {
            Logger.LogWarning("SCIM authentication failed: Invalid token");
            return AuthenticateResult.Fail("Invalid token");
        }

        if (!client.IsEnabled)
        {
            Logger.LogWarning("SCIM authentication failed: Client {ClientId} is disabled", client.Id);
            return AuthenticateResult.Fail("Client is disabled");
        }

        // Check token expiration
        if (client.TokenExpiresAt.HasValue && client.TokenExpiresAt < DateTime.UtcNow)
        {
            Logger.LogWarning("SCIM authentication failed: Token expired for client {ClientId}", client.Id);
            return AuthenticateResult.Fail("Token expired");
        }

        // Check IP restrictions
        if (!string.IsNullOrEmpty(client.AllowedIpRanges))
        {
            var clientIp = Context.Connection.RemoteIpAddress?.ToString();
            if (!IsIpAllowed(clientIp, client.AllowedIpRanges))
            {
                Logger.LogWarning("SCIM authentication failed: IP {ClientIp} not allowed for client {ClientId}", clientIp, client.Id);
                return AuthenticateResult.Fail("IP address not allowed");
            }
        }

        // Set the SCIM context
        _contextAccessor.Client = client;

        // Create claims principal
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, client.Id),
            new Claim(ClaimTypes.Name, client.Name),
            new Claim("tenant_id", client.TenantId),
            new Claim("scim_client_id", client.Id)
        };

        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, Scheme.Name);

        // Update last activity
        _ = _clientStore.UpdateActivityAsync(client.Id, true);

        return AuthenticateResult.Success(ticket);
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = 401;
        Response.Headers["WWW-Authenticate"] = "Bearer";
        return Task.CompletedTask;
    }

    private static bool IsDiscoveryEndpoint(string path)
    {
        var lowerPath = path.ToLowerInvariant();
        return lowerPath.Contains("/serviceproviderconfig") ||
               lowerPath.Contains("/resourcetypes") ||
               lowerPath.Contains("/schemas");
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToBase64String(bytes);
    }

    private static bool IsIpAllowed(string? clientIp, string allowedRanges)
    {
        if (string.IsNullOrEmpty(clientIp))
            return false;

        var ranges = allowedRanges.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var range in ranges)
        {
            // Simple IP matching - could be extended for CIDR
            if (range == clientIp)
                return true;

            // Wildcard support (e.g., "192.168.1.*")
            if (range.EndsWith(".*"))
            {
                var prefix = range[..^2];
                if (clientIp.StartsWith(prefix))
                    return true;
            }
        }

        return false;
    }
}

/// <summary>
/// Options for SCIM authentication
/// </summary>
public class ScimAuthenticationOptions : AuthenticationSchemeOptions
{
}

/// <summary>
/// Extension methods for SCIM authentication
/// </summary>
public static class ScimAuthenticationExtensions
{
    public const string SchemeName = "ScimBearer";

    /// <summary>
    /// Add SCIM bearer token authentication
    /// </summary>
    public static AuthenticationBuilder AddScimAuthentication(
        this AuthenticationBuilder builder,
        Action<ScimAuthenticationOptions>? configure = null)
    {
        return builder.AddScheme<ScimAuthenticationOptions, ScimAuthenticationHandler>(
            SchemeName,
            configure ?? (_ => { }));
    }
}

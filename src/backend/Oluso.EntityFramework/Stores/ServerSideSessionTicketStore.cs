using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using System.Security.Claims;
using System.Text.Json;

namespace Oluso.EntityFramework.Stores;

/// <summary>
/// Cookie ticket store that persists authentication tickets to the database.
/// This enables server-side sessions for:
/// - Session management and revocation
/// - Backchannel logout support
/// - Session introspection
/// - Concurrent session limits
/// </summary>
public class ServerSideSessionTicketStore : ITicketStore
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<ServerSideSessionTicketStore> _logger;

    public ServerSideSessionTicketStore(
        IHttpContextAccessor httpContextAccessor,
        ILogger<ServerSideSessionTicketStore> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _logger = logger;
    }

    /// <summary>
    /// Stores a new authentication ticket and returns its key
    /// </summary>
    public async Task<string> StoreAsync(AuthenticationTicket ticket)
    {
        var sessionStore = GetSessionStore();
        if (sessionStore == null)
        {
            _logger.LogWarning("IServerSideSessionStore not available, falling back to cookie-only storage");
            return string.Empty;
        }

        var key = GenerateKey();
        var subjectId = ticket.Principal.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? ticket.Principal.FindFirstValue("sub")
            ?? "unknown";
        var sessionId = ticket.Principal.FindFirstValue("sid")
            ?? ticket.Properties.GetString("session_id")
            ?? key;
        var displayName = ticket.Principal.FindFirstValue(ClaimTypes.Name)
            ?? ticket.Principal.FindFirstValue("name");
        var tenantId = ticket.Principal.FindFirstValue("tenant_id") ?? string.Empty;

        var session = new ServerSideSession
        {
            Key = key,
            Scheme = ticket.AuthenticationScheme,
            SubjectId = subjectId,
            SessionId = sessionId,
            DisplayName = displayName,
            Created = DateTime.UtcNow,
            Renewed = DateTime.UtcNow,
            Expires = ticket.Properties.ExpiresUtc?.UtcDateTime,
            Data = SerializeTicket(ticket),
            TenantId = tenantId
        };

        await sessionStore.CreateSessionAsync(session);

        _logger.LogInformation(
            "Created server-side session {Key} for subject {SubjectId}",
            key, subjectId);

        return key;
    }

    /// <summary>
    /// Renews the authentication ticket with the given key
    /// </summary>
    public async Task RenewAsync(string key, AuthenticationTicket ticket)
    {
        var sessionStore = GetSessionStore();
        if (sessionStore == null || string.IsNullOrEmpty(key))
            return;

        var session = await sessionStore.GetSessionAsync(key);
        if (session == null)
        {
            _logger.LogWarning("Cannot renew session {Key} - not found", key);
            return;
        }

        session.Renewed = DateTime.UtcNow;
        session.Expires = ticket.Properties.ExpiresUtc?.UtcDateTime;
        session.Data = SerializeTicket(ticket);

        await sessionStore.UpdateSessionAsync(session);

        _logger.LogDebug("Renewed server-side session {Key}", key);
    }

    /// <summary>
    /// Retrieves the authentication ticket with the given key
    /// </summary>
    public async Task<AuthenticationTicket?> RetrieveAsync(string key)
    {
        var sessionStore = GetSessionStore();
        if (sessionStore == null || string.IsNullOrEmpty(key))
            return null;

        var session = await sessionStore.GetSessionAsync(key);
        if (session == null)
        {
            _logger.LogDebug("Session {Key} not found", key);
            return null;
        }

        // Check if expired
        if (session.Expires.HasValue && session.Expires.Value < DateTime.UtcNow)
        {
            _logger.LogInformation("Session {Key} has expired", key);
            await sessionStore.DeleteSessionAsync(key);
            return null;
        }

        var ticket = DeserializeTicket(session.Data, session.Scheme);
        if (ticket == null)
        {
            _logger.LogWarning("Failed to deserialize session {Key}", key);
            return null;
        }

        return ticket;
    }

    /// <summary>
    /// Removes the authentication ticket with the given key
    /// </summary>
    public async Task RemoveAsync(string key)
    {
        var sessionStore = GetSessionStore();
        if (sessionStore == null || string.IsNullOrEmpty(key))
            return;

        await sessionStore.DeleteSessionAsync(key);

        _logger.LogInformation("Removed server-side session {Key}", key);
    }

    private IServerSideSessionStore? GetSessionStore()
    {
        return _httpContextAccessor.HttpContext?.RequestServices
            .GetService<IServerSideSessionStore>();
    }

    private static string GenerateKey()
    {
        return Guid.NewGuid().ToString("N");
    }

    private static string SerializeTicket(AuthenticationTicket ticket)
    {
        var data = new SessionTicketData
        {
            AuthenticationScheme = ticket.AuthenticationScheme,
            Claims = ticket.Principal.Claims.Select(c => new SessionClaim
            {
                Type = c.Type,
                Value = c.Value,
                ValueType = c.ValueType,
                Issuer = c.Issuer
            }).ToList(),
            AuthenticationType = ticket.Principal.Identity?.AuthenticationType,
            Properties = ticket.Properties.Items.ToDictionary(x => x.Key, x => x.Value)
        };

        return JsonSerializer.Serialize(data);
    }

    private static AuthenticationTicket? DeserializeTicket(string data, string scheme)
    {
        try
        {
            var ticketData = JsonSerializer.Deserialize<SessionTicketData>(data);
            if (ticketData == null)
                return null;

            var claims = ticketData.Claims.Select(c =>
                new Claim(c.Type, c.Value, c.ValueType, c.Issuer)).ToList();

            var identity = new ClaimsIdentity(
                claims,
                ticketData.AuthenticationType ?? scheme);

            var principal = new ClaimsPrincipal(identity);

            var properties = new AuthenticationProperties(
                ticketData.Properties.ToDictionary(x => x.Key, x => x.Value));

            return new AuthenticationTicket(principal, properties, scheme);
        }
        catch
        {
            return null;
        }
    }

    private class SessionTicketData
    {
        public string AuthenticationScheme { get; set; } = string.Empty;
        public List<SessionClaim> Claims { get; set; } = new();
        public string? AuthenticationType { get; set; }
        public Dictionary<string, string?> Properties { get; set; } = new();
    }

    private class SessionClaim
    {
        public string Type { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string ValueType { get; set; } = ClaimValueTypes.String;
        public string Issuer { get; set; } = ClaimsIdentity.DefaultIssuer;
    }
}

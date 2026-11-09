using System.Net;
using System.Net.Sockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Oluso.Enterprise.Scim.Entities;
using Oluso.Enterprise.Scim.Models;
using Oluso.Enterprise.Scim.Services;
using Oluso.Enterprise.Scim.Stores;

namespace Oluso.Enterprise.Scim.Authentication;

/// <summary>
/// Authenticates SCIM provisioning requests. Supports two credentials:
/// a static hashed bearer token issued per SCIM client, and an OAuth 2.0
/// client-credentials JWT issued by this platform's token endpoint (validated
/// via the host's existing access-token scheme and mapped to a SCIM client by
/// its linked <c>client_id</c>). Discovery endpoints remain anonymous.
/// </summary>
public sealed class ScimAuthenticationMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ScimOptions _options;
    private readonly ILogger<ScimAuthenticationMiddleware> _logger;

    public ScimAuthenticationMiddleware(
        RequestDelegate next,
        ScimOptions options,
        ILogger<ScimAuthenticationMiddleware> logger)
    {
        _next = next;
        _options = options;
        _logger = logger;
    }

    public async Task InvokeAsync(
        HttpContext context,
        IScimClientStore clientStore,
        IScimContextAccessor contextAccessor,
        IAuthenticationSchemeProvider schemeProvider)
    {
        var path = context.Request.Path.Value ?? "";
        if (!path.StartsWith("/scim/", StringComparison.OrdinalIgnoreCase) || IsDiscoveryEndpoint(path))
        {
            await _next(context);
            return;
        }

        var token = ExtractBearerToken(context.Request);
        if (token == null)
        {
            await WriteUnauthorizedAsync(context, "Missing or invalid Authorization header");
            return;
        }

        var viaStaticToken = !LooksLikeJwt(token);
        var client = viaStaticToken
            ? await clientStore.GetByTokenHashAsync(HashToken(token))
            : await ResolveViaJwtAsync(context, clientStore, schemeProvider);

        if (client == null)
        {
            _logger.LogWarning("SCIM authentication failed: no client resolved for the presented credential");
            await WriteUnauthorizedAsync(context, "Invalid token");
            return;
        }

        // IP restriction first: a valid credential from a disallowed network is
        // the strongest token-theft signal, so gate on it before the
        // operational enabled/expired checks.
        if (!string.IsNullOrEmpty(client.AllowedIpRanges))
        {
            var clientIp = context.Connection.RemoteIpAddress?.ToString();
            if (!IsIpAllowed(clientIp, client.AllowedIpRanges))
            {
                _logger.LogWarning("SCIM authentication failed: IP {ClientIp} not allowed for client {ClientId}", clientIp, client.Id);
                await WriteUnauthorizedAsync(context, "IP address not allowed");
                return;
            }
        }

        if (!client.IsEnabled)
        {
            _logger.LogWarning("SCIM authentication failed: client {ClientId} is disabled", client.Id);
            await WriteUnauthorizedAsync(context, "Client is disabled");
            return;
        }

        // The static token carries its own expiry; a JWT's lifetime is validated
        // by the access-token scheme, so only check this on the static path.
        if (viaStaticToken && client.TokenExpiresAt.HasValue && client.TokenExpiresAt < DateTime.UtcNow)
        {
            _logger.LogWarning("SCIM authentication failed: static token expired for client {ClientId}", client.Id);
            await WriteUnauthorizedAsync(context, "Token expired");
            return;
        }

        contextAccessor.Client = client;
        await clientStore.UpdateActivityAsync(client.Id, true);

        await _next(context);
    }

    private async Task<ScimClient?> ResolveViaJwtAsync(
        HttpContext context, IScimClientStore clientStore, IAuthenticationSchemeProvider schemeProvider)
    {
        var schemeName = _options.AccessTokenScheme;

        // Only attempt JWT validation if the host actually registered the
        // platform access-token scheme (a SCIM-only host may not have one).
        if (await schemeProvider.GetSchemeAsync(schemeName) == null)
        {
            _logger.LogWarning("SCIM client-credentials auth attempted but scheme '{Scheme}' is not registered", schemeName);
            return null;
        }

        ClaimsPrincipal? principal;
        if (context.User.Identity?.IsAuthenticated == true && context.User.HasClaim(c => c.Type == "client_id"))
        {
            // Already validated by UseAuthentication for the default scheme.
            principal = context.User;
        }
        else
        {
            var result = await context.AuthenticateAsync(schemeName);
            if (!result.Succeeded)
            {
                _logger.LogWarning("SCIM client-credentials auth failed: {Error}", result.Failure?.Message);
                return null;
            }
            principal = result.Principal;
        }

        var clientId = principal?.FindFirst("client_id")?.Value;
        var tenantId = principal?.FindFirst("tenant_id")?.Value;
        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(tenantId))
        {
            _logger.LogWarning("SCIM client-credentials token is missing client_id or tenant_id claim");
            return null;
        }

        return await clientStore.GetByOAuthClientIdAsync(tenantId, clientId);
    }

    private static string? ExtractBearerToken(HttpRequest request)
    {
        if (!request.Headers.TryGetValue("Authorization", out var authHeader))
            return null;

        var value = authHeader.ToString();
        if (!value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return null;

        var token = value["Bearer ".Length..].Trim();
        return string.IsNullOrEmpty(token) ? null : token;
    }

    // A platform JWT has three non-empty dot-delimited segments; the static
    // SCIM token is url-safe base64 with no dots.
    private static bool LooksLikeJwt(string token)
    {
        var parts = token.Split('.');
        return parts.Length == 3 && parts[0].Length > 0 && parts[1].Length > 0 && parts[2].Length > 0;
    }

    private static async Task WriteUnauthorizedAsync(HttpContext context, string detail)
    {
        context.Response.StatusCode = 401;
        context.Response.Headers["WWW-Authenticate"] = "Bearer";
        context.Response.ContentType = ScimConstants.ContentTypes.Scim;
        await context.Response.WriteAsync(JsonSerializer.Serialize(new ScimError { Status = 401, Detail = detail }));
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
        if (string.IsNullOrEmpty(clientIp) || !IPAddress.TryParse(clientIp, out var client))
            return false;

        var ranges = allowedRanges.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var range in ranges)
        {
            if (range.Contains('/'))
            {
                if (IsInCidr(client, range))
                    return true;
            }
            else if (range.EndsWith(".*"))
            {
                if (MatchesOctetWildcard(clientIp, range))
                    return true;
            }
            else if (IPAddress.TryParse(range, out var exact) && exact.Equals(client))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsInCidr(IPAddress address, string cidr)
    {
        var parts = cidr.Split('/', 2);
        if (!IPAddress.TryParse(parts[0], out var network) || !int.TryParse(parts[1], out var prefixLength))
            return false;

        if (network.AddressFamily != address.AddressFamily)
            return false;

        var networkBytes = network.GetAddressBytes();
        var addressBytes = address.GetAddressBytes();
        if (networkBytes.Length != addressBytes.Length)
            return false;

        var totalBits = networkBytes.Length * 8;
        if (prefixLength < 0 || prefixLength > totalBits)
            return false;

        for (var bit = 0; bit < prefixLength; bit++)
        {
            var byteIndex = bit / 8;
            var bitMask = (byte)(1 << (7 - (bit % 8)));
            if ((networkBytes[byteIndex] & bitMask) != (addressBytes[byteIndex] & bitMask))
                return false;
        }

        return true;
    }

    // Matches an IPv4 wildcard such as "192.168.1.*" on full octet boundaries
    // (so "192.168.1.*" does not match "192.168.10.5").
    private static bool MatchesOctetWildcard(string clientIp, string range)
    {
        var rangeOctets = range.Split('.');
        var clientOctets = clientIp.Split('.');
        if (clientOctets.Length != rangeOctets.Length)
            return false;

        for (var i = 0; i < rangeOctets.Length; i++)
        {
            if (rangeOctets[i] == "*")
                continue;
            if (rangeOctets[i] != clientOctets[i])
                return false;
        }

        return true;
    }
}

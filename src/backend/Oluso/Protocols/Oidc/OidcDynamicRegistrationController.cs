using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Core.Protocols.Models;

namespace Oluso.Protocols.Oidc;

/// <summary>
/// OAuth 2.0 Dynamic Client Registration endpoint (RFC 7591).
/// Route is configured via OidcEndpointRouteConvention.
/// </summary>
public class OidcDynamicRegistrationController : ControllerBase
{
    private readonly IClientStore _clientStore;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantSettingsProvider _tenantSettings;
    private readonly IIssuerResolver _issuerResolver;
    private readonly ISigningCredentialStore _signingCredentialStore;
    private readonly OidcEndpointConfiguration _endpointConfig;
    private readonly ILogger<OidcDynamicRegistrationController> _logger;

    private static readonly string[] DefaultScopes = ["openid", "profile", "email"];
    private static readonly string[] DefaultGrantTypes = ["authorization_code", "refresh_token"];

    public OidcDynamicRegistrationController(
        IClientStore clientStore,
        ITenantContext tenantContext,
        ITenantSettingsProvider tenantSettings,
        IIssuerResolver issuerResolver,
        ISigningCredentialStore signingCredentialStore,
        IOptions<OidcEndpointConfiguration> endpointConfig,
        ILogger<OidcDynamicRegistrationController> logger)
    {
        _clientStore = clientStore;
        _tenantContext = tenantContext;
        _tenantSettings = tenantSettings;
        _issuerResolver = issuerResolver;
        _signingCredentialStore = signingCredentialStore;
        _endpointConfig = endpointConfig.Value;
        _logger = logger;
    }

    /// <summary>
    /// Register a new client dynamically (RFC 7591)
    /// </summary>
    [HttpPost]
    [Consumes("application/json")]
    public async Task<IActionResult> Register(
        [FromBody] DynamicRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        var protocolSettings = await _tenantSettings.GetProtocolSettingsAsync(cancellationToken);

        // Check if DCR is enabled for this tenant
        if (!protocolSettings.EnableDynamicClientRegistration)
        {
            return BadRequest(new DynamicRegistrationError
            {
                Error = "invalid_request",
                ErrorDescription = "Dynamic client registration is not enabled for this tenant"
            });
        }

        // Check authentication for protected registration
        if (!protocolSettings.AllowOpenDynamicRegistration)
        {
            var (tokenResult, tokenError) = await ValidateInitialAccessTokenAsync(cancellationToken);
            if (tokenError != null)
            {
                return tokenError;
            }

            _logger.LogInformation(
                "Protected DCR request authorized by token from client {ClientId}",
                tokenResult!.ClientId ?? "unknown");
        }

        // Validate redirect URIs
        if (request.RedirectUris == null || request.RedirectUris.Count == 0)
        {
            return BadRequest(new DynamicRegistrationError
            {
                Error = "invalid_redirect_uri",
                ErrorDescription = "At least one redirect_uri is required"
            });
        }

        if (request.RedirectUris.Count > protocolSettings.DynamicRegistrationMaxRedirectUris)
        {
            return BadRequest(new DynamicRegistrationError
            {
                Error = "invalid_redirect_uri",
                ErrorDescription = $"Maximum {protocolSettings.DynamicRegistrationMaxRedirectUris} redirect URIs allowed"
            });
        }

        // Validate redirect URI format
        foreach (var uri in request.RedirectUris)
        {
            if (!IsValidRedirectUri(uri))
            {
                return BadRequest(new DynamicRegistrationError
                {
                    Error = "invalid_redirect_uri",
                    ErrorDescription = $"Invalid redirect URI: {uri}"
                });
            }
        }

        // Validate and filter grant types
        var allowedGrantTypes = protocolSettings.DynamicRegistrationAllowedGrantTypes?.Count > 0
            ? protocolSettings.DynamicRegistrationAllowedGrantTypes
            : DefaultGrantTypes.ToList();

        var requestedGrantTypes = request.GrantTypes?.Count > 0
            ? request.GrantTypes
            : ["authorization_code"];

        var invalidGrantTypes = requestedGrantTypes.Except(allowedGrantTypes).ToList();
        if (invalidGrantTypes.Count > 0)
        {
            return BadRequest(new DynamicRegistrationError
            {
                Error = "invalid_client_metadata",
                ErrorDescription = $"Grant types not allowed: {string.Join(", ", invalidGrantTypes)}"
            });
        }

        // Validate and filter scopes
        var allowedScopes = protocolSettings.DynamicRegistrationAllowedScopes?.Count > 0
            ? protocolSettings.DynamicRegistrationAllowedScopes
            : DefaultScopes.ToList();

        var requestedScopes = ParseScopes(request.Scope);
        var invalidScopes = requestedScopes.Except(allowedScopes).ToList();
        if (invalidScopes.Count > 0)
        {
            return BadRequest(new DynamicRegistrationError
            {
                Error = "invalid_client_metadata",
                ErrorDescription = $"Scopes not allowed: {string.Join(", ", invalidScopes)}"
            });
        }

        // Use allowed scopes if none requested
        if (requestedScopes.Count == 0)
        {
            requestedScopes = allowedScopes;
        }

        // Determine token endpoint auth method
        var authMethod = request.TokenEndpointAuthMethod ?? "none";
        var requireSecret = authMethod != "none";

        // Generate client credentials
        var clientId = GenerateClientId();
        string? clientSecret = null;
        string? hashedSecret = null;

        if (requireSecret)
        {
            clientSecret = GenerateClientSecret();
            hashedSecret = HashSecret(clientSecret);
        }

        var now = DateTime.UtcNow;
        var issuedAt = new DateTimeOffset(now).ToUnixTimeSeconds();

        // Create the client
        var client = new Client
        {
            ClientId = clientId,
            ClientName = request.ClientName,
            ClientUri = request.ClientUri,
            LogoUri = request.LogoUri,
            Enabled = true,
            IsDynamicallyRegistered = true,
            RequireClientSecret = requireSecret,
            RequirePkce = protocolSettings.DynamicRegistrationRequirePkce,
            AllowPlainTextPkce = false,
            AllowOfflineAccess = requestedGrantTypes.Contains("refresh_token"),
            Created = now,
            AllowedGrantTypes = requestedGrantTypes
                .Select(g => new ClientGrantType { GrantType = g })
                .ToList(),
            RedirectUris = request.RedirectUris
                .Select(u => new ClientRedirectUri { RedirectUri = u })
                .ToList(),
            AllowedScopes = requestedScopes
                .Select(s => new ClientScope { Scope = s })
                .ToList(),
            PostLogoutRedirectUris = (request.PostLogoutRedirectUris ?? [])
                .Select(u => new ClientPostLogoutRedirectUri { PostLogoutRedirectUri = u })
                .ToList()
        };

        // Store additional RFC 7591 metadata in properties
        if (!string.IsNullOrEmpty(request.TosUri))
            client.Properties.Add(new ClientProperty { Key = "tos_uri", Value = request.TosUri });
        if (!string.IsNullOrEmpty(request.PolicyUri))
            client.Properties.Add(new ClientProperty { Key = "policy_uri", Value = request.PolicyUri });
        if (!string.IsNullOrEmpty(request.SoftwareId))
            client.Properties.Add(new ClientProperty { Key = "software_id", Value = request.SoftwareId });
        if (!string.IsNullOrEmpty(request.SoftwareVersion))
            client.Properties.Add(new ClientProperty { Key = "software_version", Value = request.SoftwareVersion });
        if (request.Contacts?.Count > 0)
            client.Properties.Add(new ClientProperty { Key = "contacts", Value = JsonSerializer.Serialize(request.Contacts) });

        // Add secret if required
        if (hashedSecret != null)
        {
            client.ClientSecrets.Add(new ClientSecret { Value = hashedSecret });
        }

        // Generate registration access token for client management
        var registrationAccessToken = GenerateRegistrationAccessToken();
        client.RegistrationAccessTokenHash = HashSecret(registrationAccessToken);

        // Save the client
        await _clientStore.AddClientAsync(client, cancellationToken);

        _logger.LogInformation(
            "Dynamically registered client {ClientId} for tenant {TenantId}",
            clientId, _tenantContext.TenantId);

        // Build response
        var issuer = await _issuerResolver.GetIssuerAsync(cancellationToken);
        var baseUrl = issuer.TrimEnd('/');

        var response = new DynamicRegistrationResponse
        {
            ClientId = clientId,
            ClientSecret = clientSecret,
            ClientIdIssuedAt = issuedAt,
            ClientSecretExpiresAt = 0, // Never expires
            RedirectUris = request.RedirectUris,
            GrantTypes = requestedGrantTypes.ToList(),
            ResponseTypes = DeriveResponseTypes(requestedGrantTypes),
            TokenEndpointAuthMethod = authMethod,
            ClientName = request.ClientName,
            ClientUri = request.ClientUri,
            LogoUri = request.LogoUri,
            Scope = string.Join(" ", requestedScopes),
            Contacts = request.Contacts,
            TosUri = request.TosUri,
            PolicyUri = request.PolicyUri,
            SoftwareId = request.SoftwareId,
            SoftwareVersion = request.SoftwareVersion,
            RegistrationClientUri = $"{baseUrl}{_endpointConfig.RegistrationEndpoint}/{clientId}",
            RegistrationAccessToken = registrationAccessToken
        };

        return StatusCode(201, response);
    }

    /// <summary>
    /// Get client configuration (RFC 7592)
    /// </summary>
    [HttpGet("{clientId}")]
    public async Task<IActionResult> GetClient(string clientId, CancellationToken cancellationToken)
    {
        var (client, error) = await ValidateRegistrationAccessToken(clientId, cancellationToken);
        if (error != null) return error;

        var issuer = await _issuerResolver.GetIssuerAsync(cancellationToken);
        var baseUrl = issuer.TrimEnd('/');

        return Ok(BuildClientResponse(client!, baseUrl));
    }

    /// <summary>
    /// Update client configuration (RFC 7592)
    /// </summary>
    [HttpPut("{clientId}")]
    [Consumes("application/json")]
    public async Task<IActionResult> UpdateClient(
        string clientId,
        [FromBody] DynamicRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        var (client, error) = await ValidateRegistrationAccessToken(clientId, cancellationToken);
        if (error != null) return error;

        var protocolSettings = await _tenantSettings.GetProtocolSettingsAsync(cancellationToken);

        // Validate redirect URIs if provided
        if (request.RedirectUris != null)
        {
            if (request.RedirectUris.Count == 0)
            {
                return BadRequest(new DynamicRegistrationError
                {
                    Error = "invalid_redirect_uri",
                    ErrorDescription = "At least one redirect_uri is required"
                });
            }

            if (request.RedirectUris.Count > protocolSettings.DynamicRegistrationMaxRedirectUris)
            {
                return BadRequest(new DynamicRegistrationError
                {
                    Error = "invalid_redirect_uri",
                    ErrorDescription = $"Maximum {protocolSettings.DynamicRegistrationMaxRedirectUris} redirect URIs allowed"
                });
            }

            foreach (var uri in request.RedirectUris)
            {
                if (!IsValidRedirectUri(uri))
                {
                    return BadRequest(new DynamicRegistrationError
                    {
                        Error = "invalid_redirect_uri",
                        ErrorDescription = $"Invalid redirect URI: {uri}"
                    });
                }
            }

            client!.RedirectUris = request.RedirectUris
                .Select(u => new ClientRedirectUri { RedirectUri = u })
                .ToList();
        }

        // Update other fields if provided
        if (request.ClientName != null) client!.ClientName = request.ClientName;
        if (request.ClientUri != null) client!.ClientUri = request.ClientUri;
        if (request.LogoUri != null) client!.LogoUri = request.LogoUri;

        if (request.PostLogoutRedirectUris != null)
        {
            client!.PostLogoutRedirectUris = request.PostLogoutRedirectUris
                .Select(u => new ClientPostLogoutRedirectUri { PostLogoutRedirectUri = u })
                .ToList();
        }

        // Update metadata properties
        UpdateClientProperty(client!, "tos_uri", request.TosUri);
        UpdateClientProperty(client!, "policy_uri", request.PolicyUri);
        UpdateClientProperty(client!, "contacts", request.Contacts != null ? JsonSerializer.Serialize(request.Contacts) : null);

        client!.Updated = DateTime.UtcNow;

        await _clientStore.UpdateClientAsync(client, cancellationToken);

        _logger.LogInformation("Updated DCR client {ClientId}", clientId);

        var issuer = await _issuerResolver.GetIssuerAsync(cancellationToken);
        var baseUrl = issuer.TrimEnd('/');

        return Ok(BuildClientResponse(client, baseUrl));
    }

    /// <summary>
    /// Delete client (RFC 7592)
    /// </summary>
    [HttpDelete("{clientId}")]
    public async Task<IActionResult> DeleteClient(string clientId, CancellationToken cancellationToken)
    {
        var (client, error) = await ValidateRegistrationAccessToken(clientId, cancellationToken);
        if (error != null) return error;

        await _clientStore.DeleteClientAsync(client!.ClientId, cancellationToken);

        _logger.LogInformation("Deleted DCR client {ClientId}", clientId);

        return NoContent();
    }

    private async Task<(InitialAccessTokenResult? result, IActionResult? error)> ValidateInitialAccessTokenAsync(
        CancellationToken cancellationToken)
    {
        // Get token from Authorization header
        var authHeader = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            var issuer = await _issuerResolver.GetIssuerAsync(cancellationToken);
            Response.Headers["WWW-Authenticate"] = $"Bearer realm=\"{issuer}\"";
            return (null, Unauthorized(new DynamicRegistrationError
            {
                Error = "invalid_token",
                ErrorDescription = "Initial access token required"
            }));
        }

        var token = authHeader["Bearer ".Length..];

        // Validate JWT against tenant's signing keys
        var handler = new JwtSecurityTokenHandler();
        if (!handler.CanReadToken(token))
        {
            return (null, Unauthorized(new DynamicRegistrationError
            {
                Error = "invalid_token",
                ErrorDescription = "Invalid token format"
            }));
        }

        // Get tenant's validation keys
        var validationKeys = await _signingCredentialStore.GetValidationKeysAsync(cancellationToken);
        if (validationKeys == null || !validationKeys.Any())
        {
            _logger.LogError("No validation keys available for DCR token validation");
            return (null, StatusCode(500, new DynamicRegistrationError
            {
                Error = "server_error",
                ErrorDescription = "Token validation unavailable"
            }));
        }

        var issuerUri = await _issuerResolver.GetIssuerAsync(cancellationToken);

        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = validationKeys.Select(k => k.Key),
            ValidateIssuer = true,
            ValidIssuer = issuerUri,
            ValidateAudience = false, // DCR tokens don't have a specific audience
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };

        try
        {
            var principal = handler.ValidateToken(token, validationParameters, out var validatedToken);
            var jwt = validatedToken as JwtSecurityToken;

            if (jwt == null)
            {
                return (null, Unauthorized(new DynamicRegistrationError
                {
                    Error = "invalid_token",
                    ErrorDescription = "Token validation failed"
                }));
            }

            // Check for required scope
            var scopes = jwt.Claims
                .Where(c => c.Type == "scope")
                .SelectMany(c => c.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                .ToList();

            if (!scopes.Contains(OidcConstants.Scopes.ClientRegistration))
            {
                return (null, StatusCode(403, new DynamicRegistrationError
                {
                    Error = "insufficient_scope",
                    ErrorDescription = $"Token must have '{OidcConstants.Scopes.ClientRegistration}' scope"
                }));
            }

            var clientId = jwt.Claims.FirstOrDefault(c => c.Type == "client_id")?.Value;

            return (new InitialAccessTokenResult { ClientId = clientId, Scopes = scopes }, null);
        }
        catch (SecurityTokenExpiredException)
        {
            return (null, Unauthorized(new DynamicRegistrationError
            {
                Error = "invalid_token",
                ErrorDescription = "Token has expired"
            }));
        }
        catch (SecurityTokenInvalidSignatureException)
        {
            return (null, Unauthorized(new DynamicRegistrationError
            {
                Error = "invalid_token",
                ErrorDescription = "Token signature validation failed"
            }));
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogWarning(ex, "DCR token validation failed");
            return (null, Unauthorized(new DynamicRegistrationError
            {
                Error = "invalid_token",
                ErrorDescription = "Token validation failed"
            }));
        }
    }

    private class InitialAccessTokenResult
    {
        public string? ClientId { get; set; }
        public List<string> Scopes { get; set; } = [];
    }

    private async Task<(Client? client, IActionResult? error)> ValidateRegistrationAccessToken(
        string clientId,
        CancellationToken cancellationToken)
    {
        // Get the registration access token from Authorization header
        var authHeader = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return (null, Unauthorized(new DynamicRegistrationError
            {
                Error = "invalid_token",
                ErrorDescription = "Registration access token required"
            }));
        }

        var token = authHeader["Bearer ".Length..];

        // Find the client
        var client = await _clientStore.FindClientByIdAsync(clientId, cancellationToken);
        if (client == null || !client.IsDynamicallyRegistered)
        {
            return (null, NotFound(new DynamicRegistrationError
            {
                Error = "invalid_client",
                ErrorDescription = "Client not found or not dynamically registered"
            }));
        }

        // Validate the token
        if (string.IsNullOrEmpty(client.RegistrationAccessTokenHash))
        {
            return (null, StatusCode(403, new DynamicRegistrationError
            {
                Error = "invalid_token",
                ErrorDescription = "Client does not have a registration access token"
            }));
        }

        var tokenHash = HashSecret(token);
        if (!CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(tokenHash),
            System.Text.Encoding.UTF8.GetBytes(client.RegistrationAccessTokenHash)))
        {
            return (null, Unauthorized(new DynamicRegistrationError
            {
                Error = "invalid_token",
                ErrorDescription = "Invalid registration access token"
            }));
        }

        return (client, null);
    }

    private DynamicRegistrationResponse BuildClientResponse(Client client, string baseUrl)
    {
        var issuedAt = new DateTimeOffset(client.Created).ToUnixTimeSeconds();

        return new DynamicRegistrationResponse
        {
            ClientId = client.ClientId,
            ClientIdIssuedAt = issuedAt,
            ClientSecretExpiresAt = 0,
            RedirectUris = client.RedirectUris.Select(r => r.RedirectUri).ToList(),
            GrantTypes = client.AllowedGrantTypes.Select(g => g.GrantType).ToList(),
            ResponseTypes = DeriveResponseTypes(client.AllowedGrantTypes.Select(g => g.GrantType)),
            TokenEndpointAuthMethod = client.RequireClientSecret ? "client_secret_basic" : "none",
            ClientName = client.ClientName,
            ClientUri = client.ClientUri,
            LogoUri = client.LogoUri,
            Scope = string.Join(" ", client.AllowedScopes.Select(s => s.Scope)),
            Contacts = GetClientProperty<List<string>>(client, "contacts"),
            TosUri = GetClientProperty<string>(client, "tos_uri"),
            PolicyUri = GetClientProperty<string>(client, "policy_uri"),
            SoftwareId = GetClientProperty<string>(client, "software_id"),
            SoftwareVersion = GetClientProperty<string>(client, "software_version"),
            RegistrationClientUri = $"{baseUrl}{_endpointConfig.RegistrationEndpoint}/{client.ClientId}"
            // Note: registration_access_token is not returned on GET/PUT per RFC 7592
        };
    }

    private static void UpdateClientProperty(Client client, string key, string? value)
    {
        var existing = client.Properties.FirstOrDefault(p => p.Key == key);
        if (value != null)
        {
            if (existing != null)
                existing.Value = value;
            else
                client.Properties.Add(new ClientProperty { Key = key, Value = value });
        }
        else if (existing != null)
        {
            client.Properties.Remove(existing);
        }
    }

    private static T? GetClientProperty<T>(Client client, string key)
    {
        var prop = client.Properties.FirstOrDefault(p => p.Key == key);
        if (prop == null) return default;

        try
        {
            if (typeof(T) == typeof(string))
                return (T)(object)prop.Value;
            return JsonSerializer.Deserialize<T>(prop.Value);
        }
        catch
        {
            return default;
        }
    }

    private static string GenerateClientId()
    {
        var bytes = new byte[16];
        RandomNumberGenerator.Fill(bytes);
        return $"dcr_{Convert.ToHexString(bytes).ToLowerInvariant()}";
    }

    private static string GenerateRegistrationAccessToken()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return $"rat_{Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=')}";
    }

    private static string GenerateClientSecret()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    private static string HashSecret(string secret)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(secret);
        var hash = SHA256.HashData(bytes);
        return Convert.ToBase64String(hash);
    }

    private static bool IsValidRedirectUri(string uri)
    {
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
            return false;

        // Allow localhost HTTP for development
        if (parsed.Host == "localhost" || parsed.Host == "127.0.0.1")
            return true;

        // Require HTTPS for non-localhost
        if (parsed.Scheme != "https")
        {
            // Allow custom schemes for native apps
            return !parsed.Scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    private static List<string> ParseScopes(string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
            return [];

        return scope.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    private static List<string> DeriveResponseTypes(IEnumerable<string> grantTypes)
    {
        var responseTypes = new List<string>();

        if (grantTypes.Contains("authorization_code"))
            responseTypes.Add("code");
        if (grantTypes.Contains("implicit"))
        {
            responseTypes.Add("token");
            responseTypes.Add("id_token");
            responseTypes.Add("id_token token");
        }

        return responseTypes.Count > 0 ? responseTypes : ["code"];
    }
}

#region Request/Response Models

/// <summary>
/// RFC 7591 Dynamic Client Registration Request
/// </summary>
public class DynamicRegistrationRequest
{
    [JsonPropertyName("redirect_uris")]
    public List<string>? RedirectUris { get; set; }

    [JsonPropertyName("token_endpoint_auth_method")]
    public string? TokenEndpointAuthMethod { get; set; }

    [JsonPropertyName("grant_types")]
    public List<string>? GrantTypes { get; set; }

    [JsonPropertyName("response_types")]
    public List<string>? ResponseTypes { get; set; }

    [JsonPropertyName("client_name")]
    public string? ClientName { get; set; }

    [JsonPropertyName("client_uri")]
    public string? ClientUri { get; set; }

    [JsonPropertyName("logo_uri")]
    public string? LogoUri { get; set; }

    [JsonPropertyName("scope")]
    public string? Scope { get; set; }

    [JsonPropertyName("contacts")]
    public List<string>? Contacts { get; set; }

    [JsonPropertyName("tos_uri")]
    public string? TosUri { get; set; }

    [JsonPropertyName("policy_uri")]
    public string? PolicyUri { get; set; }

    [JsonPropertyName("jwks_uri")]
    public string? JwksUri { get; set; }

    [JsonPropertyName("jwks")]
    public JsonElement? Jwks { get; set; }

    [JsonPropertyName("software_id")]
    public string? SoftwareId { get; set; }

    [JsonPropertyName("software_version")]
    public string? SoftwareVersion { get; set; }

    [JsonPropertyName("software_statement")]
    public string? SoftwareStatement { get; set; }

    [JsonPropertyName("post_logout_redirect_uris")]
    public List<string>? PostLogoutRedirectUris { get; set; }
}

/// <summary>
/// RFC 7591 Dynamic Client Registration Response
/// </summary>
public class DynamicRegistrationResponse
{
    [JsonPropertyName("client_id")]
    public string ClientId { get; set; } = null!;

    [JsonPropertyName("client_secret")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ClientSecret { get; set; }

    [JsonPropertyName("client_id_issued_at")]
    public long ClientIdIssuedAt { get; set; }

    [JsonPropertyName("client_secret_expires_at")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public long ClientSecretExpiresAt { get; set; }

    [JsonPropertyName("redirect_uris")]
    public List<string>? RedirectUris { get; set; }

    [JsonPropertyName("grant_types")]
    public List<string>? GrantTypes { get; set; }

    [JsonPropertyName("response_types")]
    public List<string>? ResponseTypes { get; set; }

    [JsonPropertyName("token_endpoint_auth_method")]
    public string? TokenEndpointAuthMethod { get; set; }

    [JsonPropertyName("client_name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ClientName { get; set; }

    [JsonPropertyName("client_uri")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ClientUri { get; set; }

    [JsonPropertyName("logo_uri")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LogoUri { get; set; }

    [JsonPropertyName("scope")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Scope { get; set; }

    [JsonPropertyName("contacts")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? Contacts { get; set; }

    [JsonPropertyName("tos_uri")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TosUri { get; set; }

    [JsonPropertyName("policy_uri")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PolicyUri { get; set; }

    [JsonPropertyName("software_id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SoftwareId { get; set; }

    [JsonPropertyName("software_version")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SoftwareVersion { get; set; }

    [JsonPropertyName("registration_client_uri")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RegistrationClientUri { get; set; }

    [JsonPropertyName("registration_access_token")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RegistrationAccessToken { get; set; }
}

/// <summary>
/// RFC 7591 Error Response
/// </summary>
public class DynamicRegistrationError
{
    [JsonPropertyName("error")]
    public string Error { get; set; } = null!;

    [JsonPropertyName("error_description")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorDescription { get; set; }
}

#endregion

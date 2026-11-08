using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oluso.Core.Authorization;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Core.Protocols;
using Oluso.Core.Protocols.DPoP;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Protocols.Validation;
using Oluso.Protocols.Validation;

namespace Oluso.Protocols.Oidc;

/// <summary>
/// OAuth 2.0 Dynamic Client Registration endpoint (RFC 7591/7592).
/// Route is configured via OidcEndpointRouteConvention.
///
/// This implementation properly integrates with the existing token validation infrastructure:
/// - Uses IBearerTokenValidator for initial access token validation (supports JWT and reference tokens)
/// - Stores registration access tokens in IPersistedGrantStore for proper lifecycle management
/// - Supports DPoP-bound tokens for sender-constrained access
/// - Uses IScopeValidator for consistent scope validation
/// </summary>
public class OidcDynamicRegistrationController : OidcControllerBase
{
    private readonly IClientStore _clientStore;
    private readonly IResourceStore? _resourceStore;
    private readonly IPersistedGrantStore _grantStore;
    private readonly ITenantContext _tenantContext;
    private readonly ITenantSettingsProvider _tenantSettings;
    private readonly IIssuerResolver _issuerResolver;
    private readonly IBearerTokenValidator _tokenValidator;
    private readonly IDPoPProofValidator _dpopValidator;
    private readonly OidcEndpointConfiguration _endpointConfig;
    private readonly ILogger<OidcDynamicRegistrationController> _logger;

    private static readonly string[] DefaultScopes = ["openid", "profile", "email"];
    private static readonly string[] DefaultGrantTypes = ["authorization_code", "refresh_token"];

    // Registration access token lifetime (default 24 hours, configurable)
    private const int RegistrationAccessTokenLifetimeSeconds = 86400;

    public OidcDynamicRegistrationController(
        IClientStore clientStore,
        IPersistedGrantStore grantStore,
        ITenantContext tenantContext,
        ITenantSettingsProvider tenantSettings,
        IIssuerResolver issuerResolver,
        IBearerTokenValidator tokenValidator,
        IDPoPProofValidator dpopValidator,
        IOptions<OidcEndpointConfiguration> endpointConfig,
        ILogger<OidcDynamicRegistrationController> logger,
        IResourceStore? resourceStore = null)
    {
        _resourceStore = resourceStore;
        _clientStore = clientStore;
        _grantStore = grantStore;
        _tenantContext = tenantContext;
        _tenantSettings = tenantSettings;
        _issuerResolver = issuerResolver;
        _tokenValidator = tokenValidator;
        _dpopValidator = dpopValidator;
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

        // Validate initial access token for protected registration. A protected
        // registration (initial access token validated) is "privileged" — only
        // it may declare security-sensitive metadata like custom claim mappings.
        var privilegedRegistration = !protocolSettings.AllowOpenDynamicRegistration;
        string? dpopJkt = null;
        if (!protocolSettings.AllowOpenDynamicRegistration)
        {
            var (tokenResult, tokenError) = await ValidateInitialAccessTokenAsync(protocolSettings, cancellationToken);
            if (tokenError != null)
            {
                return tokenError;
            }

            dpopJkt = tokenResult!.DPoPKeyThumbprint;

            _logger.LogInformation(
                "Protected DCR request authorized by token from client {ClientId}",
                tokenResult.ClientId ?? "unknown");
        }

        // Validate redirect URIs: required for the redirect-based grants (RFC 7591 §2), not for a client that
        // only uses, say, the device authorization grant.
        var usesRedirects = request.GrantTypes is not { Count: > 0 }
            || request.GrantTypes.Any(g => g is "authorization_code" or "implicit");
        request.RedirectUris ??= new List<string>();
        if (usesRedirects && request.RedirectUris.Count == 0)
        {
            return BadRequest(new DynamicRegistrationError
            {
                Error = "invalid_redirect_uri",
                ErrorDescription = "At least one redirect_uri is required"
            });
        }

        // application_type (OpenID Connect DCR §2): web (the default) or native.
        var applicationType = string.IsNullOrEmpty(request.ApplicationType) ? MatrixClientMetadata.Web : request.ApplicationType;
        if (!MatrixClientMetadata.IsKnownApplicationType(applicationType))
        {
            return BadRequest(new DynamicRegistrationError
            {
                Error = "invalid_client_metadata",
                ErrorDescription = "application_type must be web or native"
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
        var invalidScopes = await NotAllowedScopesAsync(requestedScopes, allowedScopes, cancellationToken);
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

        // A Matrix client (it will hold the Matrix API or device scope) follows the Matrix spec's registration rules.
        var matrixClient = MatrixScopes.AnyMatrix(requestedScopes);
        if (matrixClient && MatrixClientMetadata.Validate(applicationType, request.ClientUri, request.LogoUri,
                request.PolicyUri, request.TosUri, request.RedirectUris) is { } matrixError)
        {
            return BadRequest(new DynamicRegistrationError
            {
                Error = matrixError.Error,
                ErrorDescription = matrixError.Description
            });
        }

        // Determine token endpoint auth method
        var authMethod = request.TokenEndpointAuthMethod ?? "none";
        var requireSecret = authMethod != "none";

        // mTLS auth method handling (RFC 8705)
        if (authMethod == "tls_client_auth")
        {
            // Exactly one identity field is required
            var identityFieldCount = new[]
            {
                request.TlsClientAuthSubjectDn,
                request.TlsClientAuthSanDns,
                request.TlsClientAuthSanUri,
                request.TlsClientAuthSanEmail,
                request.TlsClientAuthSanIp
            }.Count(f => !string.IsNullOrEmpty(f));

            if (identityFieldCount != 1)
            {
                return BadRequest(new DynamicRegistrationError
                {
                    Error = "invalid_client_metadata",
                    ErrorDescription = "Exactly one tls_client_auth identity field (subject_dn, san_dns, san_uri, san_email, san_ip) is required for tls_client_auth"
                });
            }

            // PKI mTLS: cert identity replaces secret entirely
            requireSecret = false;
        }
        else if (authMethod == "self_signed_tls_client_auth")
        {
            // Self-signed mTLS: no generated secret, but RequireClientSecret = true
            // because the self-signed cert acts as the secret
            requireSecret = true;
        }

        // Generate client credentials
        var clientId = GenerateClientId();
        string? clientSecret = null;
        string? hashedSecret = null;

        // Generate a shared secret only for methods that use one
        if (requireSecret && authMethod != "self_signed_tls_client_auth")
        {
            clientSecret = GenerateClientSecret();
            hashedSecret = HashSecret(clientSecret);
        }

        var now = DateTime.UtcNow;
        var issuedAt = new DateTimeOffset(now).ToUnixTimeSeconds();

        // Create the client with tenant protocol settings applied
        // DCR clients inherit tenant-level security requirements
        var client = new Client
        {
            ClientId = clientId,
            ClientName = request.ClientName,
            ClientUri = request.ClientUri,
            LogoUri = request.LogoUri,
            Enabled = true,
            IsDynamicallyRegistered = true,
            RequireClientSecret = requireSecret,
            // PKCE settings from tenant DCR configuration
            RequirePkce = protocolSettings.DynamicRegistrationRequirePkce || matrixClient,
            // Only allow plain PKCE if tenant allows it (secure default: false)
            AllowPlainTextPkce = protocolSettings.AllowPlainPkce,
            // Inherit tenant-level DPoP requirement
            RequireDPoP = protocolSettings.RequireDPoP,
            // Inherit tenant-level PAR requirement
            RequirePushedAuthorization = protocolSettings.RequirePushedAuthorizationRequests,
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
                .ToList(),
            // RFC 8705 mTLS + general auth method tracking
            TokenEndpointAuthMethod = authMethod,
            TlsClientAuthSubjectDn = request.TlsClientAuthSubjectDn,
            TlsClientAuthSanDns = request.TlsClientAuthSanDns,
            TlsClientAuthSanUri = request.TlsClientAuthSanUri,
            TlsClientAuthSanEmail = request.TlsClientAuthSanEmail,
            TlsClientAuthSanIp = request.TlsClientAuthSanIp
        };

        // Store additional RFC 7591 metadata in properties
        client.Properties.Add(new ClientProperty { Key = "application_type", Value = applicationType });
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

        // Custom authorize-parameter → claim mappings (Oluso vendor extension to
        // RFC 7591). Declaring where a token claim comes from is a security
        // decision, so this is privileged-only: open/self-service registration
        // may not set it, and a mapping onto a reserved/system claim is rejected.
        if (request.AuthorizeParameterClaims?.Count > 0)
        {
            if (!privilegedRegistration)
            {
                return BadRequest(new DynamicRegistrationError
                {
                    Error = "invalid_client_metadata",
                    ErrorDescription = "oluso_authorize_parameter_claims requires protected registration (an initial access token)"
                });
            }

            var mappingError = MapAuthorizeParameterClaims(request.AuthorizeParameterClaims, client);
            if (mappingError != null)
            {
                return mappingError;
            }
        }

        // Add secret if required
        if (hashedSecret != null)
        {
            client.ClientSecrets.Add(new ClientSecret { Value = hashedSecret });
        }

        // Save the client
        await _clientStore.AddClientAsync(client, cancellationToken);

        // Generate and store registration access token in the grant store
        var registrationAccessToken = await CreateRegistrationAccessTokenAsync(
            clientId, dpopJkt, cancellationToken);

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
            ApplicationType = applicationType,
            Scope = string.Join(" ", requestedScopes),
            Contacts = request.Contacts,
            TosUri = request.TosUri,
            PolicyUri = request.PolicyUri,
            SoftwareId = request.SoftwareId,
            SoftwareVersion = request.SoftwareVersion,
            AuthorizeParameterClaims = ToAuthorizeParameterDtos(client.AllowedAuthorizeParameters),
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
        var (client, error) = await ValidateRegistrationAccessTokenAsync(clientId, cancellationToken);
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
        var (client, error) = await ValidateRegistrationAccessTokenAsync(clientId, cancellationToken);
        if (error != null) return error;

        var protocolSettings = await _tenantSettings.GetProtocolSettingsAsync(cancellationToken);

        // RFC 7592 self-management authenticates with the registration access
        // token (the client managing itself), NOT the privileged initial access
        // token. A client must never be able to grant itself custom claim
        // mappings, so reject the field here — existing mappings are preserved.
        if (request.AuthorizeParameterClaims != null)
        {
            return BadRequest(new DynamicRegistrationError
            {
                Error = "invalid_client_metadata",
                ErrorDescription = "oluso_authorize_parameter_claims cannot be modified via client self-management"
            });
        }

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

        if (request.ApplicationType != null && !MatrixClientMetadata.IsKnownApplicationType(request.ApplicationType))
        {
            return BadRequest(new DynamicRegistrationError
            {
                Error = "invalid_client_metadata",
                ErrorDescription = "application_type must be web or native"
            });
        }

        // A Matrix client's metadata, as it will be after this update, follows the Matrix registration rules.
        if (MatrixScopes.AnyMatrix(client!.AllowedScopes.Select(s => s.Scope)))
        {
            var after = MatrixClientMetadata.Validate(
                request.ApplicationType ?? GetClientProperty<string>(client, "application_type"),
                request.ClientUri ?? client.ClientUri,
                request.LogoUri ?? client.LogoUri,
                request.PolicyUri ?? GetClientProperty<string>(client, "policy_uri"),
                request.TosUri ?? GetClientProperty<string>(client, "tos_uri"),
                client.RedirectUris.Select(r => r.RedirectUri));
            if (after is { } updateError)
            {
                return BadRequest(new DynamicRegistrationError
                {
                    Error = updateError.Error,
                    ErrorDescription = updateError.Description
                });
            }
        }

        // Update other fields if provided
        if (request.ApplicationType != null) UpdateClientProperty(client, "application_type", request.ApplicationType);
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
        var (client, error) = await ValidateRegistrationAccessTokenAsync(clientId, cancellationToken);
        if (error != null) return error;

        // Remove all registration access tokens for this client
        await _grantStore.RemoveAllAsync(new PersistedGrantFilter
        {
            ClientId = clientId,
            Type = OidcConstants.PersistedGrantTypes.RegistrationAccessToken
        }, cancellationToken);

        await _clientStore.DeleteByIdAsync(client!.Id, cancellationToken);

        _logger.LogInformation("Deleted DCR client {ClientId}", clientId);

        return NoContent();
    }

    /// <summary>
    /// Validates the initial access token for protected DCR registration.
    /// Uses the shared IBearerTokenValidator for consistent validation of both JWT and reference tokens.
    /// Scope/claim requirements are configurable per tenant via TenantProtocolSettings.
    /// </summary>
    private async Task<(BearerTokenValidationResult? result, IActionResult? error)> ValidateInitialAccessTokenAsync(
        TenantProtocolSettings protocolSettings,
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
                Error = OidcConstants.Errors.InvalidToken,
                ErrorDescription = "Initial access token required"
            }));
        }

        var token = authHeader["Bearer ".Length..];

        // Check for DPoP header
        var dpopProof = Request.Headers["DPoP"].FirstOrDefault();
        var httpUri = $"{Request.Scheme}://{Request.Host}{Request.Path}";

        // Build validation context with configurable scope requirement
        var requiredScopes = !string.IsNullOrEmpty(protocolSettings.DynamicRegistrationRequiredScope)
            ? new List<string> { protocolSettings.DynamicRegistrationRequiredScope }
            : null;

        var validationContext = new BearerTokenValidationContext
        {
            Token = token,
            RequiredScopes = requiredScopes,
            RequireAllScopes = true,
            DPoPProof = dpopProof,
            HttpMethod = Request.Method,
            HttpUri = httpUri
        };

        var result = await _tokenValidator.ValidateAsync(validationContext, cancellationToken);

        if (!result.IsValid)
        {
            var statusCode = result.Error == OidcConstants.Errors.InsufficientScope ? 403 : 401;

            if (statusCode == 401)
            {
                var issuer = await _issuerResolver.GetIssuerAsync(cancellationToken);
                Response.Headers["WWW-Authenticate"] = $"Bearer realm=\"{issuer}\", error=\"{result.Error}\"";
            }

            return (null, StatusCode(statusCode, new DynamicRegistrationError
            {
                Error = result.Error!,
                ErrorDescription = result.ErrorDescription
            }));
        }

        // Check for required claim if configured
        if (!string.IsNullOrEmpty(protocolSettings.DynamicRegistrationRequiredClaim))
        {
            var claimName = protocolSettings.DynamicRegistrationRequiredClaim;
            var requiredValue = protocolSettings.DynamicRegistrationRequiredClaimValue;

            if (!result.Claims.TryGetValue(claimName, out var claimValue))
            {
                _logger.LogWarning(
                    "Initial access token missing required claim '{ClaimName}' for DCR",
                    claimName);
                return (null, StatusCode(403, new DynamicRegistrationError
                {
                    Error = OidcConstants.Errors.AccessDenied,
                    ErrorDescription = $"Token missing required claim: {claimName}"
                }));
            }

            // If a specific value is required, check it
            if (!string.IsNullOrEmpty(requiredValue))
            {
                var hasMatchingValue = claimValue switch
                {
                    string s => s == requiredValue,
                    string[] arr => arr.Contains(requiredValue),
                    IEnumerable<object> enumerable => enumerable.Any(v => v?.ToString() == requiredValue),
                    _ => claimValue?.ToString() == requiredValue
                };

                if (!hasMatchingValue)
                {
                    _logger.LogWarning(
                        "Initial access token claim '{ClaimName}' does not have required value '{RequiredValue}' for DCR",
                        claimName, requiredValue);
                    return (null, StatusCode(403, new DynamicRegistrationError
                    {
                        Error = OidcConstants.Errors.AccessDenied,
                        ErrorDescription = $"Token claim '{claimName}' does not have required value"
                    }));
                }
            }
        }

        return (result, null);
    }

    /// <summary>
    /// Validates the registration access token for client management operations.
    /// Registration access tokens are stored in the persisted grant store.
    /// </summary>
    private async Task<(Client? client, IActionResult? error)> ValidateRegistrationAccessTokenAsync(
        string clientId,
        CancellationToken cancellationToken)
    {
        // Get the registration access token from Authorization header
        var authHeader = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return (null, Unauthorized(new DynamicRegistrationError
            {
                Error = OidcConstants.Errors.InvalidToken,
                ErrorDescription = "Registration access token required"
            }));
        }

        var token = authHeader["Bearer ".Length..];

        // Find the client — return 401 (not 404) when missing to avoid
        // leaking whether a clientId exists (RFC 7592 §2.1).
        var client = await _clientStore.FindClientByIdAsync(clientId, cancellationToken);
        if (client == null || !client.IsDynamicallyRegistered)
        {
            return (null, Unauthorized(new DynamicRegistrationError
            {
                Error = OidcConstants.Errors.InvalidToken,
                ErrorDescription = "Invalid registration access token"
            }));
        }

        // Look up the registration access token in the grant store
        var tokenKey = ComputeRegistrationTokenKey(token);
        var grant = await _grantStore.GetAsync(tokenKey, cancellationToken);

        if (grant == null)
        {
            _logger.LogWarning("Registration access token not found for client {ClientId}", clientId);
            return (null, Unauthorized(new DynamicRegistrationError
            {
                Error = OidcConstants.Errors.InvalidToken,
                ErrorDescription = "Invalid registration access token"
            }));
        }

        // Verify it's for the right client
        if (grant.ClientId != clientId)
        {
            _logger.LogWarning(
                "Registration access token client mismatch: expected {Expected}, got {Actual}",
                clientId, grant.ClientId);
            return (null, Unauthorized(new DynamicRegistrationError
            {
                Error = OidcConstants.Errors.InvalidToken,
                ErrorDescription = "Invalid registration access token"
            }));
        }

        // Check if consumed
        if (grant.ConsumedTime.HasValue)
        {
            _logger.LogWarning("Registration access token has been revoked for client {ClientId}", clientId);
            return (null, Unauthorized(new DynamicRegistrationError
            {
                Error = OidcConstants.Errors.InvalidToken,
                ErrorDescription = "Registration access token has been revoked"
            }));
        }

        // Check expiration
        if (grant.Expiration.HasValue && grant.Expiration.Value < DateTime.UtcNow)
        {
            _logger.LogWarning("Registration access token has expired for client {ClientId}", clientId);
            return (null, Unauthorized(new DynamicRegistrationError
            {
                Error = OidcConstants.Errors.InvalidToken,
                ErrorDescription = "Registration access token has expired"
            }));
        }

        // Validate DPoP if the token was bound
        var dpopProof = Request.Headers["DPoP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(grant.Data))
        {
            try
            {
                var data = JsonSerializer.Deserialize<RegistrationTokenData>(grant.Data);
                if (!string.IsNullOrEmpty(data?.DPoPJkt))
                {
                    if (string.IsNullOrEmpty(dpopProof))
                    {
                        return (null, Unauthorized(new DynamicRegistrationError
                        {
                            Error = OidcConstants.Errors.InvalidToken,
                            ErrorDescription = "DPoP proof required for this registration access token"
                        }));
                    }

                    var httpUri = $"{Request.Scheme}://{Request.Host}{Request.Path}";
                    var dpopContext = new DPoPValidationContext
                    {
                        Proof = dpopProof,
                        HttpMethod = Request.Method,
                        HttpUri = httpUri,
                        ExpectedJwkThumbprint = data.DPoPJkt
                    };

                    var dpopResult = await _dpopValidator.ValidateAsync(dpopContext, cancellationToken);
                    if (!dpopResult.IsValid)
                    {
                        return (null, Unauthorized(new DynamicRegistrationError
                        {
                            Error = dpopResult.Error ?? OidcConstants.Errors.InvalidDPoPProof,
                            ErrorDescription = dpopResult.ErrorDescription
                        }));
                    }
                }
            }
            catch (JsonException)
            {
                // Ignore JSON parse errors - treat as no DPoP binding
            }
        }

        return (client, null);
    }

    /// <summary>
    /// Creates a registration access token and stores it in the grant store.
    /// </summary>
    private async Task<string> CreateRegistrationAccessTokenAsync(
        string clientId,
        string? dpopJkt,
        CancellationToken cancellationToken)
    {
        var token = GenerateRegistrationAccessToken();
        var tokenKey = ComputeRegistrationTokenKey(token);
        var now = DateTime.UtcNow;

        var data = new RegistrationTokenData
        {
            DPoPJkt = dpopJkt,
            CreatedAt = now
        };

        var grant = new PersistedGrant
        {
            Key = tokenKey,
            Type = OidcConstants.PersistedGrantTypes.RegistrationAccessToken,
            ClientId = clientId,
            CreationTime = now,
            Expiration = now.AddSeconds(RegistrationAccessTokenLifetimeSeconds),
            Data = JsonSerializer.Serialize(data)
        };

        if (_tenantContext.HasTenant)
        {
            grant.TenantId = _tenantContext.TenantId;
        }

        await _grantStore.StoreAsync(grant, cancellationToken);

        return token;
    }

    /// <summary>
    /// Computes a key for storing/looking up registration access tokens.
    /// Uses SHA-256 hash to avoid storing the raw token.
    /// </summary>
    private static string ComputeRegistrationTokenKey(string token)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(token);
        var hash = SHA256.HashData(bytes);
        return $"rat:{Convert.ToBase64String(hash)}";
    }

    private DynamicRegistrationResponse BuildClientResponse(Client client, string baseUrl)
    {
        var issuedAt = new DateTimeOffset(client.Created).ToUnixTimeSeconds();

        // Use the persisted auth method; fall back to legacy derivation for pre-existing clients
        var authMethod = client.TokenEndpointAuthMethod
            ?? (client.RequireClientSecret ? "client_secret_basic" : "none");

        return new DynamicRegistrationResponse
        {
            ClientId = client.ClientId,
            ClientIdIssuedAt = issuedAt,
            ClientSecretExpiresAt = 0,
            RedirectUris = client.RedirectUris.Select(r => r.RedirectUri).ToList(),
            GrantTypes = client.AllowedGrantTypes.Select(g => g.GrantType).ToList(),
            ResponseTypes = DeriveResponseTypes(client.AllowedGrantTypes.Select(g => g.GrantType)),
            TokenEndpointAuthMethod = authMethod,
            ClientName = client.ClientName,
            ClientUri = client.ClientUri,
            LogoUri = client.LogoUri,
            ApplicationType = GetClientProperty<string>(client, "application_type") ?? MatrixClientMetadata.Web,
            Scope = string.Join(" ", client.AllowedScopes.Select(s => s.Scope)),
            Contacts = GetClientProperty<List<string>>(client, "contacts"),
            TosUri = GetClientProperty<string>(client, "tos_uri"),
            PolicyUri = GetClientProperty<string>(client, "policy_uri"),
            SoftwareId = GetClientProperty<string>(client, "software_id"),
            SoftwareVersion = GetClientProperty<string>(client, "software_version"),
            AuthorizeParameterClaims = ToAuthorizeParameterDtos(client.AllowedAuthorizeParameters),
            RegistrationClientUri = $"{baseUrl}{_endpointConfig.RegistrationEndpoint}/{client.ClientId}",
            // RFC 8705 mTLS identity fields
            TlsClientAuthSubjectDn = client.TlsClientAuthSubjectDn,
            TlsClientAuthSanDns = client.TlsClientAuthSanDns,
            TlsClientAuthSanUri = client.TlsClientAuthSanUri,
            TlsClientAuthSanEmail = client.TlsClientAuthSanEmail,
            TlsClientAuthSanIp = client.TlsClientAuthSanIp
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

        // Loopback http (RFC 8252 §7.3): IPv4, IPv6 or localhost
        if (parsed.Host is "localhost" or "127.0.0.1" or "[::1]")
            return true;

        // Require HTTPS for non-localhost
        if (parsed.Scheme != "https")
        {
            // Allow custom schemes for native apps
            return !parsed.Scheme.StartsWith("http", StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }

    /// <summary>
    /// The requested scopes registration doesn't allow. A value of an allowed prefix scope (<see cref="ApiScopePrefixes"/>)
    /// is allowed, the same rule a client's allowed scopes follow at authorization, and is registered as requested: the
    /// client gets that value, not the whole prefix scope (it registers the prefix scope's name to choose suffixes later).
    /// </summary>
    private async Task<List<string>> NotAllowedScopesAsync(List<string> requested, IList<string> allowed, CancellationToken cancellationToken)
    {
        var notListed = requested.Where(s => !allowed.Contains(s)).ToList();
        if (_resourceStore == null || notListed.Count == 0)
            return notListed;

        var prefixScopes = (await _resourceStore.FindPrefixApiScopesAsync(cancellationToken))
            .Where(s => allowed.Contains(s.Name))
            .ToList();
        if (prefixScopes.Count == 0)
            return notListed;
        var defaultSuffixMaxLength = await ApiScopePrefixes.GetDefaultSuffixMaxLengthAsync(_tenantSettings, cancellationToken);
        return notListed.Where(s => ApiScopePrefixes.Match(prefixScopes, s, defaultSuffixMaxLength) == null).ToList();
    }

    private static List<string> ParseScopes(string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
            return [];

        return scope.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    /// <summary>
    /// Validates and maps the vendor authorize-parameter claim entries onto the
    /// client. Returns a BadRequest result on the first invalid entry, or null
    /// when all entries are valid and have been added to the client.
    /// </summary>
    private IActionResult? MapAuthorizeParameterClaims(
        List<DynamicRegistrationAuthorizeParameter> entries, Client client)
    {
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Parameter) || string.IsNullOrWhiteSpace(entry.ClaimType))
            {
                return BadRequest(new DynamicRegistrationError
                {
                    Error = "invalid_client_metadata",
                    ErrorDescription = "Each oluso_authorize_parameter_claims entry requires 'parameter' and 'claim_type'"
                });
            }

            if (ReservedClaimKeys.IsReserved(entry.ClaimType) || ReservedClaimKeys.IsPermissionEntry(entry.ClaimType))
            {
                return BadRequest(new DynamicRegistrationError
                {
                    Error = "invalid_client_metadata",
                    ErrorDescription = $"claim_type '{entry.ClaimType}' is reserved and cannot be sourced from an authorize parameter"
                });
            }

            client.AllowedAuthorizeParameters.Add(new ClientAuthorizeParameter
            {
                Parameter = entry.Parameter.Trim(),
                ClaimType = entry.ClaimType.Trim(),
                Required = entry.Required
            });
        }

        return null;
    }

    private static List<DynamicRegistrationAuthorizeParameter>? ToAuthorizeParameterDtos(
        IEnumerable<ClientAuthorizeParameter>? parameters)
    {
        var list = parameters?
            .Select(p => new DynamicRegistrationAuthorizeParameter
            {
                Parameter = p.Parameter,
                ClaimType = p.ClaimType,
                Required = p.Required
            })
            .ToList();

        return list is { Count: > 0 } ? list : null;
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

    /// <summary>
    /// Data stored with registration access tokens
    /// </summary>
    private class RegistrationTokenData
    {
        public string? DPoPJkt { get; set; }
        public DateTime CreatedAt { get; set; }
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

    /// <summary>OpenID Connect DCR application type: web (default) or native.</summary>
    [JsonPropertyName("application_type")]
    public string? ApplicationType { get; set; }

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

    [JsonPropertyName("tls_client_auth_subject_dn")]
    public string? TlsClientAuthSubjectDn { get; set; }

    [JsonPropertyName("tls_client_auth_san_dns")]
    public string? TlsClientAuthSanDns { get; set; }

    [JsonPropertyName("tls_client_auth_san_uri")]
    public string? TlsClientAuthSanUri { get; set; }

    [JsonPropertyName("tls_client_auth_san_email")]
    public string? TlsClientAuthSanEmail { get; set; }

    [JsonPropertyName("tls_client_auth_san_ip")]
    public string? TlsClientAuthSanIp { get; set; }

    /// <summary>
    /// Oluso vendor extension: custom /authorize parameters this client may pass
    /// and have surfaced as token claims. Privileged registration only.
    /// </summary>
    [JsonPropertyName("oluso_authorize_parameter_claims")]
    public List<DynamicRegistrationAuthorizeParameter>? AuthorizeParameterClaims { get; set; }
}

/// <summary>
/// Oluso vendor extension entry mapping a custom /authorize parameter to the
/// token claim its value should be emitted under. See
/// <see cref="ClientAuthorizeParameter"/>.
/// </summary>
public class DynamicRegistrationAuthorizeParameter
{
    [JsonPropertyName("parameter")]
    public string? Parameter { get; set; }

    [JsonPropertyName("claim_type")]
    public string? ClaimType { get; set; }

    [JsonPropertyName("required")]
    public bool Required { get; set; }
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

    [JsonPropertyName("application_type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ApplicationType { get; set; }

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

    [JsonPropertyName("oluso_authorize_parameter_claims")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<DynamicRegistrationAuthorizeParameter>? AuthorizeParameterClaims { get; set; }

    [JsonPropertyName("registration_client_uri")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RegistrationClientUri { get; set; }

    [JsonPropertyName("registration_access_token")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RegistrationAccessToken { get; set; }

    [JsonPropertyName("tls_client_auth_subject_dn")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TlsClientAuthSubjectDn { get; set; }

    [JsonPropertyName("tls_client_auth_san_dns")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TlsClientAuthSanDns { get; set; }

    [JsonPropertyName("tls_client_auth_san_uri")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TlsClientAuthSanUri { get; set; }

    [JsonPropertyName("tls_client_auth_san_email")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TlsClientAuthSanEmail { get; set; }

    [JsonPropertyName("tls_client_auth_san_ip")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TlsClientAuthSanIp { get; set; }
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

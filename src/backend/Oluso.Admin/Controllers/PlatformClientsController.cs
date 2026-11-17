using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oluso.Admin.Authorization;
using Oluso.Core.Authorization;
using Oluso.Core.Api;
using Oluso.Core.Domain.Entities;
using Oluso.Admin.Dto.Clients;
using Oluso.Admin.Services;
using Oluso.Core.Domain.Interfaces;

using Oluso.Core.Services;

namespace Oluso.Admin.Controllers;

/// <summary>
/// Admin API for managing platform-level OAuth clients.
/// Platform clients are accessible across all tenants but invisible
/// in normal tenant admin views.
/// SuperAdmin access only.
///
/// Uses the same DTOs and request models as ClientsController (ClientDetailDto,
/// CreateClientRequest, UpdateClientRequest) to allow the frontend to reuse
/// the same client detail editing component.
/// </summary>
[Route("api/admin/platform/clients")]
[Authorize(Policy = "SuperAdmin")]
public class PlatformClientsController : AdminBaseController
{
    private readonly IClientStore _clientStore;
    private readonly ICorsOriginsCacheInvalidator? _corsCache;
    private readonly IIssuerResolver _issuerResolver;
    private readonly IServiceApiScopeProvisioner _serviceApiScopeProvisioner;
    private readonly IClientSecretProtector _secrets;
    private readonly ILogger<PlatformClientsController> _logger;

    public PlatformClientsController(
        ITenantContext tenantContext,
        IClientStore clientStore,
        IIssuerResolver issuerResolver,
        IServiceApiScopeProvisioner serviceApiScopeProvisioner,
        IClientSecretProtector secrets,
        ILogger<PlatformClientsController> logger,
        ICorsOriginsCacheInvalidator? corsCache = null)
        : base(tenantContext)
    {
        _secrets = secrets;
        _corsCache = corsCache;
        _clientStore = clientStore;
        _issuerResolver = issuerResolver;
        _serviceApiScopeProvisioner = serviceApiScopeProvisioner;
        _logger = logger;
    }

    /// <summary>
    /// Get all platform clients
    /// </summary>
    [HttpGet]
    [RequirePermission(AdminPermissions.PlatformClientsRead)]
    public async Task<ActionResult<IEnumerable<ClientListDto>>> GetAll(CancellationToken cancellationToken)
    {
        var clients = await _clientStore.GetPlatformClientsAsync(cancellationToken);
        return Ok(clients.Select(c => new ClientListDto
        {
            Id = c.Id,
            ClientId = c.ClientId,
            ClientName = c.ClientName,
            Description = c.Description,
            Enabled = c.Enabled,
            AllowedGrantTypes = c.AllowedGrantTypes.Select(g => g.GrantType).ToList(),
            RequireClientSecret = c.RequireClientSecret,
            RequirePkce = c.RequirePkce,
            Created = c.Created,
            Updated = c.Updated
        }));
    }

    /// <summary>
    /// Get a platform client by ID
    /// </summary>
    [HttpGet("{id:int}")]
    [RequirePermission(AdminPermissions.PlatformClientsRead)]
    public async Task<ActionResult<ClientDetailDto>> GetById(int id, CancellationToken cancellationToken)
    {
        var client = await _clientStore.GetPlatformClientByIdAsync(id, cancellationToken);
        if (client == null) return NotFound();
        return Ok(ClientsController.MapToDetailDto(client));
    }

    /// <summary>
    /// Integration / connection details for a platform client.
    /// Mirrors ClientsController.GetClientConnection — see that for the full
    /// shape — but scoped to platform clients (any tenant can sign in
    /// against them, so the issuer here is the server-config / platform issuer).
    /// </summary>
    [HttpGet("{id:int}/connection")]
    [RequirePermission(AdminPermissions.PlatformClientsRead)]
    public async Task<ActionResult<ClientConnectionDto>> GetConnection(int id, CancellationToken cancellationToken)
    {
        var client = await _clientStore.GetPlatformClientByIdAsync(id, cancellationToken);
        if (client == null) return NotFound();

        var issuer = (await _issuerResolver.GetIssuerAsync(cancellationToken)).TrimEnd('/');

        return Ok(new ClientConnectionDto
        {
            ClientId = client.ClientId,
            ClientName = client.ClientName,
            RequireClientSecret = client.RequireClientSecret,
            RequirePkce = client.RequirePkce,
            AllowedGrantTypes = client.AllowedGrantTypes.Select(g => g.GrantType).ToList(),
            AllowedScopes = client.AllowedScopes.Select(s => s.Scope).ToList(),
            RedirectUris = client.RedirectUris.Select(r => r.RedirectUri).ToList(),
            PostLogoutRedirectUris = client.PostLogoutRedirectUris.Select(r => r.PostLogoutRedirectUri).ToList(),
            AllowedCorsOrigins = client.AllowedCorsOrigins.Select(c => c.Origin).ToList(),
            IssuerUri = issuer,
            DiscoveryUrl = $"{issuer}/.well-known/openid-configuration",
            JwksUri = $"{issuer}/.well-known/jwks",
            AuthorizationEndpoint = $"{issuer}/connect/authorize",
            TokenEndpoint = $"{issuer}/connect/token",
            UserInfoEndpoint = $"{issuer}/connect/userinfo",
            EndSessionEndpoint = $"{issuer}/connect/endsession",
            IntrospectionEndpoint = $"{issuer}/connect/introspect",
            RevocationEndpoint = $"{issuer}/connect/revocation",
        });
    }

    /// <summary>
    /// Create a new platform client
    /// </summary>
    [HttpPost]
    [RequirePermission(AdminPermissions.PlatformClientsWrite)]
    public async Task<ActionResult<ClientDetailDto>> Create(
        [FromBody] CreateClientRequest request,
        CancellationToken cancellationToken)
    {
        // Check if client ID already exists in platform clients
        var existing = await _clientStore.GetPlatformClientsAsync(cancellationToken);
        if (existing.Any(c => c.ClientId == request.ClientId))
        {
            return BadRequest(new { error = "A platform client with this ClientId already exists" });
        }

        // Validate mTLS settings
        var mtlsError = ClientsController.ValidateMtlsSettings(
            request.TokenEndpointAuthMethod,
            request.TlsClientAuthSubjectDn,
            request.TlsClientAuthSanDns,
            request.TlsClientAuthSanUri,
            request.TlsClientAuthSanEmail,
            request.TlsClientAuthSanIp);
        if (mtlsError != null)
        {
            return BadRequest(new { error = mtlsError });
        }

        var serviceScopeError = ClientsController.ValidateServiceApiScopes(
            request.AllowedScopes, request.AllowedGrantTypes);
        if (serviceScopeError != null)
        {
            return BadRequest(new { error = serviceScopeError });
        }

        if (ClientsController.InvalidAllowedResource(request.AllowedResources) is { } resourceError)
        {
            return BadRequest(new { error = resourceError });
        }
        if (ClientsController.InvalidJwksUri(request.JwksUri) is { } jwksUriError)
        {
            return BadRequest(new { error = jwksUriError });
        }

        if (ClientsController.InvalidAllowedResource(request.IntrospectionResources, "Introspection resource") is { } introspectionResourceError)
        {
            return BadRequest(new { error = introspectionResourceError });
        }

        await _serviceApiScopeProvisioner.EnsureAsync(
            request.AllowedScopes ?? Array.Empty<string>(), cancellationToken);

        var client = ClientsController.MapFromCreateRequest(request);

        // Force platform client settings
        client.NonEditable = true;

        // Map collections
        ClientsController.MapCollectionsToClient(client, request);

        // Generate client secret if needed
        if (request.RequireClientSecret != false && !string.IsNullOrEmpty(request.ClientSecret))
        {
            if (ClientsController.HmacSecretTooShort(client, request.ClientSecret) is { } tooShort)
            {
                return BadRequest(new { error = tooShort });
            }

            client.ClientSecrets = new List<ClientSecret> { ClientsController.NewSecret(_secrets, client, request.ClientSecret) };
        }

        var created = await _clientStore.AddPlatformClientAsync(client, cancellationToken);
        if (_corsCache != null) await _corsCache.InvalidateCacheAsync(cancellationToken);

        _logger.LogInformation("SuperAdmin {AdminUser} created platform client {ClientId}",
            AdminUserName, client.ClientId);

        return CreatedAtAction(nameof(GetById), new { id = created.Id }, ClientsController.MapToDetailDto(created));
    }

    /// <summary>
    /// Update a platform client
    /// </summary>
    [HttpPut("{id:int}")]
    [RequirePermission(AdminPermissions.PlatformClientsWrite)]
    public async Task<ActionResult<ClientDetailDto>> Update(
        int id,
        [FromBody] UpdateClientRequest request,
        CancellationToken cancellationToken)
    {
        var client = await _clientStore.GetPlatformClientByIdAsync(id, cancellationToken);
        if (client == null) return NotFound();

        // Validate mTLS settings — use the effective value after applying the update
        var effectiveAuthMethod = request.TokenEndpointAuthMethod ?? client.TokenEndpointAuthMethod;
        var effectiveSubjectDn = request.TlsClientAuthSubjectDn ?? client.TlsClientAuthSubjectDn;
        var effectiveSanDns = request.TlsClientAuthSanDns ?? client.TlsClientAuthSanDns;
        var effectiveSanUri = request.TlsClientAuthSanUri ?? client.TlsClientAuthSanUri;
        var effectiveSanEmail = request.TlsClientAuthSanEmail ?? client.TlsClientAuthSanEmail;
        var effectiveSanIp = request.TlsClientAuthSanIp ?? client.TlsClientAuthSanIp;
        var mtlsError = ClientsController.ValidateMtlsSettings(
            effectiveAuthMethod, effectiveSubjectDn, effectiveSanDns,
            effectiveSanUri, effectiveSanEmail, effectiveSanIp);
        if (mtlsError != null)
        {
            return BadRequest(new { error = mtlsError });
        }

        var effectiveScopes = request.AllowedScopes
            ?? client.AllowedScopes.Select(s => s.Scope).ToList();
        var effectiveGrantTypes = request.AllowedGrantTypes
            ?? client.AllowedGrantTypes.Select(g => g.GrantType).ToList();

        var serviceScopeError = ClientsController.ValidateServiceApiScopes(
            effectiveScopes, effectiveGrantTypes);
        if (serviceScopeError != null)
        {
            return BadRequest(new { error = serviceScopeError });
        }

        if (ClientsController.InvalidAllowedResource(request.AllowedResources) is { } resourceError)
        {
            return BadRequest(new { error = resourceError });
        }
        if (ClientsController.InvalidJwksUri(request.JwksUri) is { } jwksUriError)
        {
            return BadRequest(new { error = jwksUriError });
        }

        if (ClientsController.InvalidAllowedResource(request.IntrospectionResources, "Introspection resource") is { } introspectionResourceError)
        {
            return BadRequest(new { error = introspectionResourceError });
        }

        await _serviceApiScopeProvisioner.EnsureAsync(effectiveScopes, cancellationToken);

        // Apply all field updates and collection mappings
        ClientsController.ApplyUpdateRequest(client, request);

        // Ensure NonEditable stays true for platform clients
        client.NonEditable = true;

        client.Updated = DateTime.UtcNow;
        var updated = await _clientStore.UpdatePlatformClientAsync(client, cancellationToken);
        if (_corsCache != null) await _corsCache.InvalidateCacheAsync(cancellationToken);

        _logger.LogInformation("SuperAdmin {AdminUser} updated platform client {ClientId}",
            AdminUserName, client.ClientId);

        return Ok(ClientsController.MapToDetailDto(updated));
    }

    /// <summary>
    /// Delete a platform client
    /// </summary>
    [HttpDelete("{id:int}")]
    [RequirePermission(AdminPermissions.PlatformClientsDelete)]
    public async Task<ActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var client = await _clientStore.GetPlatformClientByIdAsync(id, cancellationToken);
        if (client == null) return NotFound();

        await _clientStore.DeletePlatformClientByIdAsync(id, cancellationToken);
        if (_corsCache != null) await _corsCache.InvalidateCacheAsync(cancellationToken);

        _logger.LogInformation("SuperAdmin {AdminUser} deleted platform client {ClientId}",
            AdminUserName, client.ClientId);

        return NoContent();
    }

    /// <summary>
    /// Regenerate client secret for a platform client
    /// </summary>
    [HttpPost("{id:int}/regenerate-secret")]
    [RequirePermission(AdminPermissions.PlatformClientsWrite)]
    public async Task<ActionResult<RegenerateSecretResponse>> RegenerateSecret(int id, CancellationToken cancellationToken)
    {
        var client = await _clientStore.GetPlatformClientByIdAsync(id, cancellationToken);
        if (client == null) return NotFound();

        if (!client.RequireClientSecret)
        {
            return BadRequest(new { error = "Client does not require a secret" });
        }

        var newSecret = ClientSecretProtector.GenerateSecret(ClientSecretProtector.UsesHmac(client));
        client.ClientSecrets.Add(ClientsController.NewSecret(_secrets, client, newSecret));

        await _clientStore.UpdatePlatformClientAsync(client, cancellationToken);

        _logger.LogInformation("SuperAdmin {AdminUser} regenerated secret for platform client {ClientId}",
            AdminUserName, client.ClientId);

        return Ok(new RegenerateSecretResponse
        {
            ClientId = client.ClientId,
            ClientSecret = newSecret,
            SecretId = client.ClientSecrets.Last().Id
        });
    }

    /// <summary>
    /// Adds a public key or certificate to a platform client (see <see cref="ClientsController.AddKey"/>).
    /// </summary>
    [HttpPost("{id:int}/keys")]
    [RequirePermission(AdminPermissions.PlatformClientsWrite)]
    public async Task<ActionResult<ClientSecretDto>> AddKey(int id, [FromBody] AddClientKeyRequest request, CancellationToken cancellationToken)
    {
        var client = await _clientStore.GetPlatformClientByIdAsync(id, cancellationToken);
        if (client == null) return NotFound();

        var (secret, error) = ClientsController.NewPublicKey(client, request);
        if (error != null)
        {
            return BadRequest(new { error });
        }

        client.ClientSecrets.Add(secret!);
        await _clientStore.UpdatePlatformClientAsync(client, cancellationToken);

        _logger.LogInformation("SuperAdmin {AdminUser} added a {Type} to platform client {ClientId}",
            AdminUserName, secret!.Type, client.ClientId);

        return Ok(ClientsController.ToSecretDto(secret));
    }

    /// <summary>
    /// Delete a specific secret from a platform client
    /// </summary>
    [HttpDelete("{id:int}/secrets/{secretId:int}")]
    [RequirePermission(AdminPermissions.PlatformClientsWrite)]
    public async Task<ActionResult> DeleteSecret(int id, int secretId, CancellationToken cancellationToken)
    {
        var client = await _clientStore.GetPlatformClientByIdAsync(id, cancellationToken);
        if (client == null) return NotFound();

        var secret = client.ClientSecrets.FirstOrDefault(s => s.Id == secretId);
        if (secret == null)
        {
            return NotFound(new { error = "Secret not found" });
        }

        if (ClientsController.IsLastRequiredCredential(client, secret))
        {
            return BadRequest(new { error = ClientsController.LastCredentialError(secret) });
        }

        client.ClientSecrets.Remove(secret);
        await _clientStore.UpdatePlatformClientAsync(client, cancellationToken);

        _logger.LogInformation("SuperAdmin {AdminUser} deleted secret {SecretId} from platform client {ClientId}",
            AdminUserName, secretId, client.ClientId);

        return NoContent();
    }
}

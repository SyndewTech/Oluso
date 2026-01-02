using System.Collections.Concurrent;
using System.Security.Claims;
using System.Security.Cryptography.X509Certificates;
using ITfoxtec.Identity.Saml2;
using ITfoxtec.Identity.Saml2.Claims;
using ITfoxtec.Identity.Saml2.MvcCore;
using ITfoxtec.Identity.Saml2.Schemas;
using ITfoxtec.Identity.Saml2.Schemas.Metadata;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens.Saml2;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Enterprise.Saml.Configuration;

namespace Oluso.Enterprise.Saml.ServiceProvider;

/// <summary>
/// SAML Service Provider with dynamic IdP loading from database.
/// Combines static configuration from options with dynamic IdPs from IIdentityProviderStore.
/// Supports multi-tenancy with tenant-aware IdP configurations.
/// SP EntityId and BaseUrl are derived dynamically from IIssuerResolver when not explicitly configured.
/// </summary>
public class SamlServiceProvider : ISamlServiceProvider
{
    private readonly SamlSpOptions _options;
    private readonly IIdentityProviderStore _identityProviderStore;
    private readonly ITenantContext _tenantContext;
    private readonly IIssuerResolver _issuerResolver;
    private readonly ILogger<SamlServiceProvider> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ConcurrentDictionary<string, (Saml2Configuration Config, DateTime LoadedAt)> _idpConfigs = new();
    private readonly TimeSpan _cacheExpiry = TimeSpan.FromMinutes(5);

    public SamlServiceProvider(
        IOptions<SamlSpOptions> options,
        IIdentityProviderStore identityProviderStore,
        ITenantContext tenantContext,
        IIssuerResolver issuerResolver,
        ILogger<SamlServiceProvider> logger,
        IHttpClientFactory httpClientFactory)
    {
        _options = options.Value;
        _identityProviderStore = identityProviderStore;
        _tenantContext = tenantContext;
        _issuerResolver = issuerResolver;
        _logger = logger;
        _httpClientFactory = httpClientFactory;

        // Initialize static IdP configurations (global, not tenant-specific)
        InitializeStaticIdpConfigurations();
    }

    /// <summary>
    /// Gets the SP Entity ID, deriving from IIssuerResolver if not explicitly configured.
    /// </summary>
    private async Task<string> GetSpEntityIdAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(_options.EntityId))
        {
            return _options.EntityId;
        }

        // Derive from issuer resolver (tenant-aware)
        return await _issuerResolver.GetIssuerAsync(cancellationToken);
    }

    /// <summary>
    /// Gets the SP Base URL, deriving from IIssuerResolver if not explicitly configured.
    /// </summary>
    private async Task<string> GetSpBaseUrlAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrEmpty(_options.BaseUrl))
        {
            return _options.BaseUrl;
        }

        // Derive from issuer resolver (tenant-aware)
        return await _issuerResolver.GetIssuerAsync(cancellationToken);
    }

    /// <summary>
    /// Gets the current tenant ID for cache key generation
    /// </summary>
    private string CurrentTenantId => _tenantContext.TenantId ?? "global";

    private void InitializeStaticIdpConfigurations()
    {
        foreach (var idp in _options.IdentityProviders.Where(i => i.Enabled))
        {
            try
            {
                var config = CreateIdpConfiguration(idp);
                _idpConfigs[$"static:{idp.Name}"] = (config, DateTime.UtcNow);
                _logger.LogInformation("Loaded static SAML IdP configuration: {Name}", idp.Name);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load static SAML IdP configuration: {Name}", idp.Name);
            }
        }
    }

    public async Task<IReadOnlyList<SamlIdpInfo>> GetConfiguredIdpsAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<SamlIdpInfo>();

        // Add static IdPs
        foreach (var idp in _options.IdentityProviders.Where(i => i.Enabled))
        {
            result.Add(new SamlIdpInfo
            {
                Name = idp.Name,
                DisplayName = idp.DisplayName,
                EntityId = idp.EntityId,
                Enabled = idp.Enabled,
                ProxyMode = idp.ProxyMode,
                StoreUserLocally = idp.StoreUserLocally
            });
        }

        // Add database IdPs
        var dbIdps = await _identityProviderStore.GetByProviderTypeAsync(ExternalProviderType.Saml2, cancellationToken);
        foreach (var dbIdp in dbIdps.Where(p => p.Enabled))
        {
            // Skip if already in static config
            if (result.Any(r => r.Name.Equals(dbIdp.Scheme, StringComparison.OrdinalIgnoreCase)))
                continue;

            var samlConfig = dbIdp.GetConfiguration<Saml2ProviderConfiguration>();
            result.Add(new SamlIdpInfo
            {
                Name = dbIdp.Scheme,
                DisplayName = dbIdp.DisplayName ?? dbIdp.Scheme,
                EntityId = samlConfig?.EntityId ?? "",
                Enabled = dbIdp.Enabled,
                ProxyMode = samlConfig?.ProxyMode ?? false,
                StoreUserLocally = samlConfig?.StoreUserLocally ?? true
            });
        }

        return result;
    }

    public async Task<SamlIdpInfo?> GetIdpByNameAsync(string name, CancellationToken cancellationToken = default)
    {
        // Check static config first
        var staticIdp = _options.IdentityProviders.FirstOrDefault(i => i.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (staticIdp != null)
        {
            return new SamlIdpInfo
            {
                Name = staticIdp.Name,
                DisplayName = staticIdp.DisplayName,
                EntityId = staticIdp.EntityId,
                Enabled = staticIdp.Enabled,
                ProxyMode = staticIdp.ProxyMode,
                StoreUserLocally = staticIdp.StoreUserLocally
            };
        }

        // Check database - scheme is already unique, no prefix needed
        var dbIdp = await _identityProviderStore.GetBySchemeAsync(name, cancellationToken);
        if (dbIdp != null)
        {
            var samlConfig = dbIdp.GetConfiguration<Saml2ProviderConfiguration>();
            return new SamlIdpInfo
            {
                Name = dbIdp.Scheme,
                DisplayName = dbIdp.DisplayName ?? dbIdp.Scheme,
                EntityId = samlConfig?.EntityId ?? "",
                Enabled = dbIdp.Enabled,
                ProxyMode = samlConfig?.ProxyMode ?? false,
                StoreUserLocally = samlConfig?.StoreUserLocally ?? true
            };
        }

        return null;
    }

    public async Task RefreshIdpConfigurationAsync(string name, CancellationToken cancellationToken = default)
    {
        // Remove tenant-specific cache entry
        var cacheKey = GetDbCacheKey(name);
        _idpConfigs.TryRemove(cacheKey, out _);
        await EnsureIdpConfigurationLoadedAsync(name, cancellationToken);
        _logger.LogInformation("Refreshed SAML IdP configuration: {Name} for tenant {TenantId}", name, CurrentTenantId);
    }

    /// <summary>
    /// Generates a tenant-aware cache key for database-loaded IdP configurations
    /// </summary>
    private string GetDbCacheKey(string idpName) => $"db:{CurrentTenantId}:{idpName}";

    public async Task<SamlAuthnRequest> CreateAuthnRequestAsync(
        SamlAuthnRequestParams parameters,
        CancellationToken cancellationToken = default)
    {
        var config = await EnsureIdpConfigurationLoadedAsync(parameters.IdpName, cancellationToken);
        if (config == null)
        {
            throw new InvalidOperationException($"IdP not configured: {parameters.IdpName}");
        }

        // Validate required configuration
        if (config.SingleSignOnDestination == null)
        {
            throw new InvalidOperationException(
                $"IdP '{parameters.IdpName}' has no SingleSignOnDestination configured. " +
                "Check that MetadataUrl is accessible or SingleSignOnServiceUrl is set.");
        }

        if (string.IsNullOrEmpty(config.Issuer))
        {
            throw new InvalidOperationException(
                $"SP Issuer (EntityId) is not configured. Check SAML SP configuration.");
        }

        var binding = new Saml2RedirectBinding();
        var spBaseUrl = await GetSpBaseUrlAsync(cancellationToken);

        var authnRequest = new Saml2AuthnRequest(config)
        {
            AssertionConsumerServiceUrl = new Uri($"{spBaseUrl}{_options.AssertionConsumerServicePath}"),
            ForceAuthn = parameters.ForceAuthn,
            IsPassive = parameters.IsPassive
        };

        if (!string.IsNullOrEmpty(_options.NameIdFormat))
        {
            authnRequest.NameIdPolicy = new NameIdPolicy
            {
                Format = _options.NameIdFormat,
                AllowCreate = true
            };
        }

        if (parameters.RequestedAuthnContextClasses?.Any() == true)
        {
            authnRequest.RequestedAuthnContext = new RequestedAuthnContext
            {
                Comparison = AuthnContextComparisonTypes.Exact,
                AuthnContextClassRef = parameters.RequestedAuthnContextClasses
            };
        }

        var relayState = parameters.RelayState ?? parameters.ReturnUrl ?? Guid.NewGuid().ToString();
        // Set RelayState directly instead of using SetRelayStateQuery to avoid encoding it as a query string
        binding.RelayState = relayState;

        var result = binding.Bind(authnRequest);

        return new SamlAuthnRequest
        {
            Url = result.RedirectLocation.ToString(),
            RelayState = relayState,
            Binding = "Redirect"
        };
    }

    public async Task<SamlAuthenticationResult> ProcessResponseAsync(
        HttpContext httpContext,
        string samlResponse,
        string? relayState = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Determine which IdP sent the response
            var responseXml = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(samlResponse));
            var idpName = await DetermineIdpFromResponseAsync(responseXml, cancellationToken);

            if (string.IsNullOrEmpty(idpName))
            {
                return SamlAuthenticationResult.Failure("Unknown IdP");
            }

            var config = await EnsureIdpConfigurationLoadedAsync(idpName, cancellationToken);
            if (config == null)
            {
                return SamlAuthenticationResult.Failure($"IdP configuration not found: {idpName}");
            }

            var binding = new Saml2PostBinding();
            var saml2AuthnResponse = new Saml2AuthnResponse(config);

            binding.ReadSamlResponse(new FakeHttpRequest(samlResponse, relayState), saml2AuthnResponse);

            if (saml2AuthnResponse.Status != Saml2StatusCodes.Success)
            {
                _logger.LogWarning("SAML response status: {Status}", saml2AuthnResponse.Status);
                return SamlAuthenticationResult.Failure(
                    $"SAML authentication failed: {saml2AuthnResponse.Status}");
            }

            // Note: We don't call saml2AuthnResponse.CreateSession() here because
            // the SamlSpController handles signing into the external scheme after processing
            // the response. CreateSession would try to sign in with a "saml2" scheme
            // that isn't registered.

            // Get claim mappings from config
            var claimMappings = await GetClaimMappingsAsync(idpName, cancellationToken);
            var claims = MapClaims(saml2AuthnResponse.ClaimsIdentity, claimMappings);

            var identity = new ClaimsIdentity(claims, "SAML2", ClaimTypes.NameIdentifier, ClaimTypes.Role);
            var principal = new ClaimsPrincipal(identity);

            var result = SamlAuthenticationResult.Success(
                saml2AuthnResponse.NameId?.Value ?? identity.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "",
                principal,
                saml2AuthnResponse.SessionIndex,
                relayState);

            var authStatement = saml2AuthnResponse.Saml2SecurityToken?.Assertion?.Statements
                .OfType<Saml2AuthenticationStatement>()
                .FirstOrDefault();

            if (authStatement != null)
            {
                result.AuthnInstant = authStatement.AuthenticationInstant;
                result.SessionNotOnOrAfter = authStatement.SessionNotOnOrAfter;
            }
            else
            {
                result.AuthnInstant = saml2AuthnResponse.IssueInstant.DateTime;
                result.SessionNotOnOrAfter = saml2AuthnResponse.SecurityTokenValidTo.DateTime;
            }

            _logger.LogInformation("SAML authentication successful for {NameId} via IdP {IdpName}",
                result.SubjectId, idpName);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing SAML response");
            return SamlAuthenticationResult.Failure($"Error processing response: {ex.Message}");
        }
    }

    public async Task<SamlLogoutRequest> CreateLogoutRequestAsync(
        string idpName,
        string nameId,
        string? sessionIndex = null,
        CancellationToken cancellationToken = default)
    {
        var config = await EnsureIdpConfigurationLoadedAsync(idpName, cancellationToken);
        if (config == null)
        {
            throw new InvalidOperationException($"IdP not configured: {idpName}");
        }

        var binding = new Saml2RedirectBinding();
        var logoutRequest = new Saml2LogoutRequest(config, new FakeUser(nameId, sessionIndex));
        var result = binding.Bind(logoutRequest);

        return new SamlLogoutRequest
        {
            Url = result.RedirectLocation.ToString(),
            Binding = "Redirect"
        };
    }

    public async Task<bool> ProcessLogoutResponseAsync(
        string samlResponse,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Try each configured IdP
            var idps = await GetConfiguredIdpsAsync(cancellationToken);
            foreach (var idp in idps)
            {
                try
                {
                    var config = await EnsureIdpConfigurationLoadedAsync(idp.Name, cancellationToken);
                    if (config == null) continue;

                    var binding = new Saml2PostBinding();
                    var logoutResponse = new Saml2LogoutResponse(config);

                    binding.ReadSamlResponse(new FakeHttpRequest(samlResponse, null), logoutResponse);

                    if (logoutResponse.Status == Saml2StatusCodes.Success)
                    {
                        _logger.LogInformation("SAML logout successful from {IdP}", idp.Name);
                        return true;
                    }
                }
                catch
                {
                    // Try next IdP
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing SAML logout response");
            return false;
        }
    }

    public async Task<string> GenerateMetadataAsync(CancellationToken cancellationToken = default)
    {
        var spEntityId = await GetSpEntityIdAsync(cancellationToken);
        var spBaseUrl = await GetSpBaseUrlAsync(cancellationToken);

        var config = new Saml2Configuration
        {
            Issuer = spEntityId
        };

        if (_options.SigningCertificate != null)
        {
            config.SigningCertificate = _options.SigningCertificate.LoadCertificate();
        }

        var entityDescriptor = new EntityDescriptor(config);
        entityDescriptor.ValidUntil = 365;

        var spDescriptor = new SPSsoDescriptor
        {
            AuthnRequestsSigned = _options.SignAuthnRequests,
            WantAssertionsSigned = _options.RequireSignedAssertions
        };

        if (config.SigningCertificate != null)
        {
            spDescriptor.SigningCertificates = new[] { config.SigningCertificate };
        }

        spDescriptor.AssertionConsumerServices = (spDescriptor.AssertionConsumerServices ?? []).Append(new AssertionConsumerService
        {
            Binding = ProtocolBindings.HttpPost,
            Location = new Uri($"{spBaseUrl}{_options.AssertionConsumerServicePath}"),
            IsDefault = true,
            Index = 0
        });

        spDescriptor.SingleLogoutServices = (spDescriptor.SingleLogoutServices ?? []).Append(new SingleLogoutService
        {
            Binding = ProtocolBindings.HttpPost,
            Location = new Uri($"{spBaseUrl}{_options.SingleLogoutServicePath}")
        });

        spDescriptor.NameIDFormats = (spDescriptor.NameIDFormats ?? []).Append(new Uri(_options.NameIdFormat));

        entityDescriptor.SPSsoDescriptor = spDescriptor;

        var metadata = new Saml2Metadata(entityDescriptor);
        return metadata.CreateMetadata().ToXml();
    }

    private async Task<Saml2Configuration?> EnsureIdpConfigurationLoadedAsync(string idpName, CancellationToken cancellationToken)
    {
        // Check static config (global, not tenant-specific)
        if (_idpConfigs.TryGetValue($"static:{idpName}", out var staticConfig))
        {
            return staticConfig.Config;
        }

        // Check cached database config (tenant-aware)
        var cacheKey = GetDbCacheKey(idpName);
        if (_idpConfigs.TryGetValue(cacheKey, out var cachedConfig))
        {
            if (DateTime.UtcNow - cachedConfig.LoadedAt < _cacheExpiry)
            {
                return cachedConfig.Config;
            }
            // Expired, remove and reload
            _idpConfigs.TryRemove(cacheKey, out _);
        }

        // Load from database (IIdentityProviderStore is already tenant-aware via query filters)
        // Scheme is already unique, no prefix needed
        var dbIdp = await _identityProviderStore.GetBySchemeAsync(idpName, cancellationToken);

        if (dbIdp?.ProviderType != ExternalProviderType.Saml2)
        {
            return null;
        }

        var samlConfig = dbIdp.GetConfiguration<Saml2ProviderConfiguration>();
        if (samlConfig == null)
        {
            _logger.LogWarning("SAML IdP {Name} has no configuration for tenant {TenantId}", idpName, CurrentTenantId);
            return null;
        }

        var config = await CreateDbIdpConfigurationAsync(samlConfig, cancellationToken);
        _idpConfigs[cacheKey] = (config, DateTime.UtcNow);
        _logger.LogInformation("Loaded SAML IdP configuration from database: {Name} for tenant {TenantId}", idpName, CurrentTenantId);

        return config;
    }

    private async Task<Saml2Configuration> CreateDbIdpConfigurationAsync(
        Saml2ProviderConfiguration samlConfig,
        CancellationToken cancellationToken)
    {
        var spEntityId = await GetSpEntityIdAsync(cancellationToken);
        var config = new Saml2Configuration
        {
            Issuer = spEntityId,
            AllowedAudienceUris = [spEntityId]
        };

        // Load SP signing certificate if configured
        if (_options.SigningCertificate != null)
        {
            var cert = _options.SigningCertificate.LoadCertificate();
            if (cert != null)
            {
                config.SigningCertificate = cert;
            }
        }

        // Load SP decryption certificate if configured
        if (_options.DecryptionCertificate != null)
        {
            var cert = _options.DecryptionCertificate.LoadCertificate();
            if (cert != null)
            {
                config.DecryptionCertificates.Add(cert);
            }
        }

        // Set IdP details from database config
        _logger.LogInformation("Creating IdP configuration. MetadataUrl={MetadataUrl}, EntityId={EntityId}, SSO={SsoUrl}",
            samlConfig.MetadataUrl, samlConfig.EntityId, samlConfig.SingleSignOnServiceUrl);

        if (!string.IsNullOrEmpty(samlConfig.MetadataUrl))
        {
            try
            {
                _logger.LogDebug("Loading IdP metadata from {Url}", samlConfig.MetadataUrl);
                var entityDescriptor = new EntityDescriptor();
                await entityDescriptor.ReadIdPSsoDescriptorFromUrlAsync(
                    _httpClientFactory,
                    new Uri(samlConfig.MetadataUrl));

                if (entityDescriptor.IdPSsoDescriptor != null)
                {
                    config.AllowedIssuer = entityDescriptor.EntityId;
                    config.SingleSignOnDestination = entityDescriptor.IdPSsoDescriptor.SingleSignOnServices
                        .FirstOrDefault(s => s.Binding == ProtocolBindings.HttpRedirect)?.Location;

                    // If no HTTP-Redirect binding, try HTTP-POST
                    if (config.SingleSignOnDestination == null)
                    {
                        config.SingleSignOnDestination = entityDescriptor.IdPSsoDescriptor.SingleSignOnServices
                            .FirstOrDefault(s => s.Binding == ProtocolBindings.HttpPost)?.Location;
                        _logger.LogDebug("No HTTP-Redirect SSO binding found, using HTTP-POST: {Url}", config.SingleSignOnDestination);
                    }

                    config.SingleLogoutDestination = entityDescriptor.IdPSsoDescriptor.SingleLogoutServices
                        .FirstOrDefault()?.Location;

                    config.SignatureValidationCertificates.AddRange(
                        entityDescriptor.IdPSsoDescriptor.SigningCertificates);

                    _logger.LogInformation(
                        "Loaded IdP metadata: EntityId={EntityId}, SSO={SsoUrl}, SLO={SloUrl}, Certs={CertCount}",
                        entityDescriptor.EntityId,
                        config.SingleSignOnDestination,
                        config.SingleLogoutDestination,
                        config.SignatureValidationCertificates.Count);
                }
                else
                {
                    _logger.LogWarning("IdP metadata from {Url} has no IdPSsoDescriptor", samlConfig.MetadataUrl);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load IdP metadata from {Url}", samlConfig.MetadataUrl);
            }
        }
        else
        {
            // Manual configuration
            config.AllowedIssuer = samlConfig.EntityId;

            if (!string.IsNullOrEmpty(samlConfig.SingleSignOnServiceUrl))
            {
                config.SingleSignOnDestination = new Uri(samlConfig.SingleSignOnServiceUrl);
            }

            if (!string.IsNullOrEmpty(samlConfig.SingleLogoutServiceUrl))
            {
                config.SingleLogoutDestination = new Uri(samlConfig.SingleLogoutServiceUrl);
            }

            if (!string.IsNullOrEmpty(samlConfig.SigningCertificate))
            {
                try
                {
                    var certBytes = Convert.FromBase64String(samlConfig.SigningCertificate);
                    var cert = new X509Certificate2(certBytes);
                    config.SignatureValidationCertificates.Add(cert);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load IdP signing certificate");
                }
            }
        }

        return config;
    }

    private Saml2Configuration CreateIdpConfiguration(SamlIdpConfig idp)
    {
        var config = new Saml2Configuration
        {
            Issuer = _options.EntityId,
            AllowedAudienceUris = [_options.EntityId]
        };

        // Load SP signing certificate if configured
        if (_options.SigningCertificate != null)
        {
            var cert = _options.SigningCertificate.LoadCertificate();
            if (cert != null)
            {
                config.SigningCertificate = cert;
            }
        }

        // Load SP decryption certificate if configured
        if (_options.DecryptionCertificate != null)
        {
            var cert = _options.DecryptionCertificate.LoadCertificate();
            if (cert != null)
            {
                config.DecryptionCertificates.Add(cert);
            }
        }

        // Set IdP details
        if (!string.IsNullOrEmpty(idp.MetadataUrl))
        {
            var entityDescriptor = new EntityDescriptor();
            entityDescriptor.ReadIdPSsoDescriptorFromUrlAsync(
                _httpClientFactory,
                new Uri(idp.MetadataUrl)).GetAwaiter().GetResult();

            if (entityDescriptor.IdPSsoDescriptor != null)
            {
                config.AllowedIssuer = entityDescriptor.EntityId;
                config.SingleSignOnDestination = entityDescriptor.IdPSsoDescriptor.SingleSignOnServices
                    .FirstOrDefault(s => s.Binding == ProtocolBindings.HttpRedirect)?.Location;

                config.SingleLogoutDestination = entityDescriptor.IdPSsoDescriptor.SingleLogoutServices
                    .FirstOrDefault()?.Location;

                config.SignatureValidationCertificates.AddRange(
                    entityDescriptor.IdPSsoDescriptor.SigningCertificates);
            }
        }
        else
        {
            // Manual configuration
            config.AllowedIssuer = idp.EntityId;

            if (!string.IsNullOrEmpty(idp.SingleSignOnServiceUrl))
            {
                config.SingleSignOnDestination = new Uri(idp.SingleSignOnServiceUrl);
            }

            if (!string.IsNullOrEmpty(idp.SingleLogoutServiceUrl))
            {
                config.SingleLogoutDestination = new Uri(idp.SingleLogoutServiceUrl);
            }

            if (idp.SigningCertificate != null)
            {
                var cert = idp.SigningCertificate.LoadCertificate();
                if (cert != null)
                {
                    config.SignatureValidationCertificates.Add(cert);
                }
            }
        }

        return config;
    }

    private async Task<string?> DetermineIdpFromResponseAsync(string responseXml, CancellationToken cancellationToken)
    {
        // Check static IdPs first
        foreach (var idp in _options.IdentityProviders)
        {
            if (responseXml.Contains(idp.EntityId))
            {
                return idp.Name;
            }
        }

        // Check database IdPs
        var dbIdps = await _identityProviderStore.GetByProviderTypeAsync(ExternalProviderType.Saml2, cancellationToken);
        foreach (var dbIdp in dbIdps)
        {
            var samlConfig = dbIdp.GetConfiguration<Saml2ProviderConfiguration>();
            if (samlConfig != null && responseXml.Contains(samlConfig.EntityId))
            {
                return dbIdp.Scheme;
            }
        }

        return _options.IdentityProviders.FirstOrDefault()?.Name;
    }

    private async Task<Dictionary<string, string>> GetClaimMappingsAsync(string idpName, CancellationToken cancellationToken)
    {
        // Check static config
        var staticIdp = _options.IdentityProviders.FirstOrDefault(i => i.Name.Equals(idpName, StringComparison.OrdinalIgnoreCase));
        if (staticIdp != null)
        {
            return staticIdp.ClaimMappings;
        }

        // Check database - scheme is already unique
        var dbIdp = await _identityProviderStore.GetBySchemeAsync(idpName, cancellationToken);
        if (dbIdp != null)
        {
            var samlConfig = dbIdp.GetConfiguration<Saml2ProviderConfiguration>();
            return samlConfig?.ClaimMappings ?? new Dictionary<string, string>();
        }

        return new Dictionary<string, string>();
    }

    private List<Claim> MapClaims(ClaimsIdentity? identity, Dictionary<string, string> mappings)
    {
        var claims = new List<Claim>();

        if (identity == null) return claims;

        foreach (var claim in identity.Claims)
        {
            if (mappings.TryGetValue(claim.Type, out var mappedType))
            {
                claims.Add(new Claim(mappedType, claim.Value));
            }
            else
            {
                claims.Add(claim);
            }
        }

        return claims;
    }

    // Helper classes for ITfoxtec compatibility
    private class FakeHttpRequest : ITfoxtec.Identity.Saml2.Http.HttpRequest
    {
        public FakeHttpRequest(string samlResponse, string? relayState)
        {
            // Set the Method property on the base class (not hide it with 'new')
            Method = "POST";
            Form = new System.Collections.Specialized.NameValueCollection
            {
                ["SAMLResponse"] = samlResponse
            };
            if (relayState != null)
            {
                Form["RelayState"] = relayState;
            }
        }
    }

    private class FakeUser : ClaimsPrincipal
    {
        public string NameId { get; }
        public string? SessionIndex { get; }

        public FakeUser(string nameId, string? sessionIndex)
            : base(new ClaimsIdentity(CreateClaims(nameId, sessionIndex), "SAML2"))
        {
            NameId = nameId;
            SessionIndex = sessionIndex;
        }

        private static IEnumerable<Claim> CreateClaims(string nameId, string? sessionIndex)
        {
            yield return new Claim(ClaimTypes.NameIdentifier, nameId);
            if (sessionIndex != null)
                yield return new Claim(Saml2ClaimTypes.SessionIndex, sessionIndex);
        }
    }
}

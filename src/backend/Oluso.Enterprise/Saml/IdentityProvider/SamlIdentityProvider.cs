using System.Security.Claims;
using System.Text.Json;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Enterprise.Saml.Configuration;
using Oluso.Enterprise.Saml.Entities;
using Oluso.Enterprise.Saml.Services;
using Oluso.Enterprise.Saml.Stores;
using ITfoxtec.Identity.Saml2;
using ITfoxtec.Identity.Saml2.Schemas;
using ITfoxtec.Identity.Saml2.Schemas.Metadata;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens.Saml2;
using Oluso.Core.Services;

namespace Oluso.Enterprise.Saml.IdentityProvider;

public class SamlIdentityProvider : ISamlIdentityProvider
{
    private readonly SamlIdpOptions _options;
    private readonly ILogger<SamlIdentityProvider> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICertificateService? _certificateService;
    private readonly ISamlServiceProviderStore? _spStore;
    private readonly ISamlTenantSettingsService? _tenantSettingsService;
    private readonly IIssuerResolver? _issuerResolver;
    private readonly Dictionary<string, Saml2Configuration> _spConfigs = new();
    private readonly Dictionary<string, SamlSpConfig> _spConfigsRaw = new();

    private Saml2Configuration? _idpConfig;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public SamlIdentityProvider(
        IOptions<SamlIdpOptions> options,
        ILogger<SamlIdentityProvider> logger,
        IHttpClientFactory httpClientFactory,
        ICertificateService? certificateService = null,
        ISamlServiceProviderStore? spStore = null,
        ISamlTenantSettingsService? tenantSettingsService = null,
        IIssuerResolver? issuerResolver = null)
    {
        _options = options.Value;
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _certificateService = certificateService;
        _spStore = spStore;
        _tenantSettingsService = tenantSettingsService;
        _issuerResolver = issuerResolver;
    }

    public bool IsEnabled => _options.Enabled;

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken = default)
    {
        if (_initialized) return;

        await _initLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;

            _idpConfig = await CreateIdpConfigurationAsync(cancellationToken);
            await InitializeSpConfigurationsAsync(cancellationToken);
            _initialized = true;
        }
        finally
        {
            _initLock.Release();
        }
    }

    private async Task<Saml2Configuration> CreateIdpConfigurationAsync(CancellationToken cancellationToken)
    {
        var config = new Saml2Configuration
        {
            Issuer = _options.EntityId
        };

        // Load signing certificate (supports managed certificates)
        var signingCert = await _options.SigningCertificate.LoadCertificateAsync(
            _certificateService, cancellationToken: cancellationToken);
        if (signingCert != null)
        {
            config.SigningCertificate = signingCert;
            _logger.LogInformation("Loaded SAML IdP signing certificate: {Subject}", signingCert.Subject);
        }
        else
        {
            _logger.LogWarning("No SAML IdP signing certificate configured - assertions cannot be signed");
        }

        // Load encryption certificate (optional)
        if (_options.EncryptionCertificate != null)
        {
            var encCert = await _options.EncryptionCertificate.LoadCertificateAsync(
                _certificateService, cancellationToken: cancellationToken);
            if (encCert != null)
            {
                config.DecryptionCertificate = encCert;
                _logger.LogInformation("Loaded SAML IdP encryption certificate: {Subject}", encCert.Subject);
            }
        }

        return config;
    }

    private async Task InitializeSpConfigurationsAsync(CancellationToken cancellationToken = default)
    {
        // Load from config (appsettings.json)
        foreach (var sp in _options.ServiceProviders.Where(s => s.Enabled))
        {
            try
            {
                var config = await CreateSpConfigurationAsync(sp, cancellationToken);
                _spConfigs[sp.EntityId] = config;
                _spConfigsRaw[sp.EntityId] = sp;
                _logger.LogInformation("Loaded SAML SP from config: {EntityId}", sp.EntityId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load SAML SP from config: {EntityId}", sp.EntityId);
            }
        }

        // Load from database store (if available)
        if (_spStore != null)
        {
            try
            {
                var dbServiceProviders = await _spStore.GetAllAsync(cancellationToken: cancellationToken);
                foreach (var dbSp in dbServiceProviders)
                {
                    if (_spConfigs.ContainsKey(dbSp.EntityId))
                    {
                        _logger.LogDebug("Skipping database SAML SP (already loaded from config): {EntityId}", dbSp.EntityId);
                        continue;
                    }

                    try
                    {
                        var spConfig = ConvertToSpConfig(dbSp);
                        var config = await CreateSpConfigurationAsync(spConfig, cancellationToken);
                        _spConfigs[dbSp.EntityId] = config;
                        _spConfigsRaw[dbSp.EntityId] = spConfig;
                        _logger.LogInformation("Loaded SAML SP from database: {EntityId} ({DisplayName})", dbSp.EntityId, dbSp.DisplayName);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to load SAML SP from database: {EntityId}", dbSp.EntityId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load SAML SPs from database store");
            }
        }

        _logger.LogInformation("Loaded {Count} SAML Service Provider configurations", _spConfigs.Count);
    }

    /// <summary>
    /// Converts a database entity to the SamlSpConfig used internally
    /// </summary>
    private SamlSpConfig ConvertToSpConfig(SamlServiceProvider entity)
    {
        var config = new SamlSpConfig
        {
            EntityId = entity.EntityId,
            DisplayName = entity.DisplayName,
            Enabled = entity.Enabled,
            MetadataUrl = entity.MetadataUrl,
            AssertionConsumerServiceUrl = entity.AssertionConsumerServiceUrl,
            SingleLogoutServiceUrl = entity.SingleLogoutServiceUrl,
            EncryptAssertions = entity.EncryptAssertions,
            NameIdFormat = entity.NameIdFormat,
            SsoBinding = entity.SsoBinding,
            SignResponses = entity.SignResponses,
            SignAssertions = entity.SignAssertions,
            RequireSignedAuthnRequests = entity.RequireSignedAuthnRequests
        };

        // Parse allowed claims from JSON
        if (!string.IsNullOrEmpty(entity.AllowedClaimsJson))
        {
            try
            {
                var claims = JsonSerializer.Deserialize<List<string>>(entity.AllowedClaimsJson);
                if (claims != null)
                {
                    config.AllowedClaims = claims;
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse AllowedClaimsJson for SP {EntityId}", entity.EntityId);
            }
        }

        // Parse claim mappings from JSON
        if (!string.IsNullOrEmpty(entity.ClaimMappingsJson))
        {
            try
            {
                var mappings = JsonSerializer.Deserialize<Dictionary<string, string>>(entity.ClaimMappingsJson);
                if (mappings != null)
                {
                    config.ClaimMappings = mappings;
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse ClaimMappingsJson for SP {EntityId}", entity.EntityId);
            }
        }

        // Handle signing certificate (stored as base64)
        if (!string.IsNullOrEmpty(entity.SigningCertificate))
        {
            config.SigningCertificate = new CertificateOptions { Base64 = entity.SigningCertificate };
        }

        // Handle encryption certificate (stored as base64)
        if (!string.IsNullOrEmpty(entity.EncryptionCertificate))
        {
            config.EncryptionCertificate = new CertificateOptions { Base64 = entity.EncryptionCertificate };
        }

        return config;
    }

    private async Task<Saml2Configuration> CreateSpConfigurationAsync(SamlSpConfig sp, CancellationToken cancellationToken = default)
    {
        var config = new Saml2Configuration
        {
            Issuer = _options.EntityId,
            SigningCertificate = _idpConfig!.SigningCertificate,
            AllowedAudienceUris =  [ sp.EntityId ]
        };

        if (!string.IsNullOrEmpty(sp.MetadataUrl))
        {
            var entityDescriptor = new EntityDescriptor();
            await entityDescriptor.ReadSPSsoDescriptorFromUrlAsync(
                _httpClientFactory,
                new Uri(sp.MetadataUrl));

            if (entityDescriptor.SPSsoDescriptor != null)
            {
                var acs = entityDescriptor.SPSsoDescriptor.AssertionConsumerServices
                    .FirstOrDefault(a => a.IsDefault) ??
                    entityDescriptor.SPSsoDescriptor.AssertionConsumerServices.FirstOrDefault();

                if (acs != null)
                {
                    config.SingleSignOnDestination = acs.Location;
                }

                if (sp.EncryptAssertions && entityDescriptor.SPSsoDescriptor.EncryptionCertificates.Any())
                {
                    config.EncryptionCertificate = entityDescriptor.SPSsoDescriptor.EncryptionCertificates.First();
                }
            }
        }
        else if (!string.IsNullOrEmpty(sp.AssertionConsumerServiceUrl))
        {
            config.SingleSignOnDestination = new Uri(sp.AssertionConsumerServiceUrl);

            if (sp.EncryptAssertions && sp.EncryptionCertificate != null)
            {
                // Load encryption certificate (supports managed certificates)
                config.EncryptionCertificate = await sp.EncryptionCertificate.LoadCertificateAsync(
                    _certificateService, cancellationToken: cancellationToken);
            }
        }

        return config;
    }

    public async Task<SamlAuthnRequestResult> ParseAuthnRequestAsync(
        string samlRequest,
        string? relayState = null,
        CancellationToken cancellationToken = default)
    {
        return await ParseAuthnRequestAsync(samlRequest, relayState, isPostBinding: false, cancellationToken);
    }

    public async Task<SamlAuthnRequestResult> ParseAuthnRequestAsync(
        string samlRequest,
        string? relayState,
        bool isPostBinding,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        try
        {
            if (isPostBinding)
            {
                // HTTP-POST binding - base64 encoded but not DEFLATE compressed
                // Parse manually since ITfoxtec has issues with FakeHttpRequest
                return ParseAuthnRequestFromBase64(samlRequest, relayState);
            }
            else
            {
                // HTTP-Redirect binding - use ITfoxtec library (handles DEFLATE decompression)
                var authnRequest = new Saml2AuthnRequest(_idpConfig!);
                var binding = new Saml2RedirectBinding();
                var fakeQuery = new Dictionary<string, string> { ["SAMLRequest"] = samlRequest };
                if (relayState != null) fakeQuery["RelayState"] = relayState;

                binding.ReadSamlRequest(new FakeHttpRequest(fakeQuery, isPost: false), authnRequest);

                // Validate SP is registered
                var issuer = authnRequest.Issuer;
                if (!_spConfigs.ContainsKey(issuer))
                {
                    return new SamlAuthnRequestResult
                    {
                        Valid = false,
                        Error = $"Unknown Service Provider: {issuer}"
                    };
                }

                return new SamlAuthnRequestResult
                {
                    Valid = true,
                    Id = authnRequest.Id?.Value,
                    Issuer = issuer,
                    AssertionConsumerServiceUrl = authnRequest.AssertionConsumerServiceUrl?.ToString(),
                    RelayState = relayState,
                    ForceAuthn = authnRequest.ForceAuthn ?? false,
                    IsPassive = authnRequest.IsPassive ?? false,
                    NameIdFormat = authnRequest.NameIdPolicy?.Format,
                    RequestedAuthnContextClasses = authnRequest.RequestedAuthnContext?.AuthnContextClassRef?.ToList()
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing AuthnRequest (POST binding: {IsPost})", isPostBinding);
            return new SamlAuthnRequestResult
            {
                Valid = false,
                Error = ex.Message
            };
        }
    }

    private SamlAuthnRequestResult ParseAuthnRequestFromBase64(string base64Request, string? relayState)
    {
        // Decode base64
        var xmlBytes = Convert.FromBase64String(base64Request);
        var xml = System.Text.Encoding.UTF8.GetString(xmlBytes);

        // Parse XML
        var doc = new System.Xml.XmlDocument();
        doc.LoadXml(xml);

        var nsManager = new System.Xml.XmlNamespaceManager(doc.NameTable);
        nsManager.AddNamespace("samlp", "urn:oasis:names:tc:SAML:2.0:protocol");
        nsManager.AddNamespace("saml", "urn:oasis:names:tc:SAML:2.0:assertion");

        var authnRequestNode = doc.DocumentElement;
        if (authnRequestNode == null || authnRequestNode.LocalName != "AuthnRequest")
        {
            return new SamlAuthnRequestResult
            {
                Valid = false,
                Error = "Invalid AuthnRequest XML"
            };
        }

        var id = authnRequestNode.GetAttribute("ID");
        var issuerNode = authnRequestNode.SelectSingleNode("saml:Issuer", nsManager);
        var issuer = issuerNode?.InnerText;
        var acsUrl = authnRequestNode.GetAttribute("AssertionConsumerServiceURL");
        var forceAuthn = authnRequestNode.GetAttribute("ForceAuthn")?.ToLowerInvariant() == "true";
        var isPassive = authnRequestNode.GetAttribute("IsPassive")?.ToLowerInvariant() == "true";

        // Get NameIDPolicy format if present
        var nameIdPolicyNode = authnRequestNode.SelectSingleNode("samlp:NameIDPolicy", nsManager);
        var nameIdFormat = nameIdPolicyNode?.Attributes?["Format"]?.Value;

        if (string.IsNullOrEmpty(issuer))
        {
            return new SamlAuthnRequestResult
            {
                Valid = false,
                Error = "AuthnRequest missing Issuer"
            };
        }

        // Validate SP is registered
        if (!_spConfigs.ContainsKey(issuer))
        {
            return new SamlAuthnRequestResult
            {
                Valid = false,
                Error = $"Unknown Service Provider: {issuer}"
            };
        }

        return new SamlAuthnRequestResult
        {
            Valid = true,
            Id = id,
            Issuer = issuer,
            AssertionConsumerServiceUrl = string.IsNullOrEmpty(acsUrl) ? null : acsUrl,
            RelayState = relayState,
            ForceAuthn = forceAuthn,
            IsPassive = isPassive,
            NameIdFormat = nameIdFormat
        };
    }

    public async Task<SamlResponseResult> CreateResponseAsync(
        SamlAssertionParams parameters,
        string? relayState = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        if (!_spConfigs.TryGetValue(parameters.SpEntityId, out var spConfig))
        {
            throw new InvalidOperationException($"SP not registered: {parameters.SpEntityId}");
        }

        if (!_spConfigsRaw.TryGetValue(parameters.SpEntityId, out var sp))
        {
            throw new InvalidOperationException($"SP config not found: {parameters.SpEntityId}");
        }

        // Create identity from claims
        var filteredClaims = parameters.Claims;
        if (sp.AllowedClaims.Any())
        {
            filteredClaims = parameters.Claims.Where(c => sp.AllowedClaims.Contains(c.Type));
        }

        var claimsIdentity = new ClaimsIdentity(filteredClaims, "SAML2");

        // Configure encryption if needed
        var responseConfig = new Saml2Configuration
        {
            Issuer = _options.EntityId,
            SigningCertificate = _idpConfig!.SigningCertificate,
            AllowedAudienceUris = [parameters.SpEntityId]
        };

        if (!string.IsNullOrEmpty(parameters.Destination))
        {
            responseConfig.SingleSignOnDestination = new Uri(parameters.Destination);
        }
        else if (spConfig.SingleSignOnDestination != null)
        {
            responseConfig.SingleSignOnDestination = spConfig.SingleSignOnDestination;
        }

        // Set encryption certificate if SP requires encrypted assertions
        if (sp.EncryptAssertions && spConfig.EncryptionCertificate != null)
        {
            responseConfig.EncryptionCertificate = spConfig.EncryptionCertificate;
        }

        var authnResponse = new Saml2AuthnResponse(responseConfig)
        {
            Status = Saml2StatusCodes.Success,
            ClaimsIdentity = claimsIdentity
        };

        if (!string.IsNullOrEmpty(parameters.InResponseTo))
        {
            authnResponse.InResponseTo = new Saml2Id(parameters.InResponseTo);
        }

        var destination = parameters.Destination ?? spConfig.SingleSignOnDestination?.ToString();
        if (!string.IsNullOrEmpty(destination))
        {
            authnResponse.Destination = new Uri(destination);
        }

        // Determine the authn context
        var authnContextUri = !string.IsNullOrEmpty(parameters.AuthnContextClassRef)
            ? new Uri(parameters.AuthnContextClassRef)
            : AuthnContextClassTypes.PasswordProtectedTransport;

        // Create assertion using the library's expected signature
        authnResponse.CreateSecurityToken(
            parameters.SpEntityId,
            authnContext: authnContextUri,
            subjectConfirmationLifetime: 5,
            issuedTokenLifetime: _options.AssertionLifetimeMinutes);

        var binding = new Saml2PostBinding();
        binding.Bind(authnResponse);

        // Get the Base64-encoded SAML response XML
        var samlResponseXml = authnResponse.ToXml();
        var samlResponseBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(samlResponseXml.OuterXml));

        return new SamlResponseResult
        {
            Destination = destination ?? "",
            SamlResponse = samlResponseBase64,
            RelayState = relayState
        };
    }

    public async Task<SamlResponseResult> CreateErrorResponseAsync(
        string spEntityId,
        string? inResponseTo,
        string statusCode,
        string? message = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        if (!_spConfigs.TryGetValue(spEntityId, out var spConfig))
        {
            throw new InvalidOperationException($"SP not registered: {spEntityId}");
        }

        var response = new Saml2AuthnResponse(spConfig)
        {
            Status = ParseStatusCode(statusCode),
            StatusMessage = message
        };

        if (!string.IsNullOrEmpty(inResponseTo))
        {
            response.InResponseTo = new Saml2Id(inResponseTo);
        }

        if (spConfig.SingleSignOnDestination != null)
        {
            response.Destination = spConfig.SingleSignOnDestination;
        }

        var binding = new Saml2PostBinding();
        var result = binding.Bind(response);

        return new SamlResponseResult
        {
            Destination = spConfig.SingleSignOnDestination?.ToString() ?? "",
            SamlResponse = result.PostContent
        };
    }

    public async Task<SamlLogoutRequestResult> ParseLogoutRequestAsync(
        string samlRequest,
        string? relayState = null,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        try
        {
            var binding = new Saml2RedirectBinding();
            var logoutRequest = new Saml2LogoutRequest(_idpConfig!);

            var fakeQuery = new Dictionary<string, string> { ["SAMLRequest"] = samlRequest };
            if (relayState != null) fakeQuery["RelayState"] = relayState;

            binding.ReadSamlRequest(new FakeHttpRequest(fakeQuery), logoutRequest);

            return new SamlLogoutRequestResult
            {
                Valid = true,
                Id = logoutRequest.Id?.Value,
                Issuer = logoutRequest.Issuer,
                NameId = logoutRequest.NameId?.Value,
                SessionIndex = logoutRequest.SessionIndex,
                RelayState = relayState
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing logout request");
            return new SamlLogoutRequestResult
            {
                Valid = false,
                Error = ex.Message
            };
        }
    }

    public async Task<SamlResponseResult> CreateLogoutResponseAsync(
        string spEntityId,
        string? inResponseTo,
        bool success,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        if (!_spConfigs.TryGetValue(spEntityId, out var spConfig))
        {
            throw new InvalidOperationException($"SP not registered: {spEntityId}");
        }

        var response = new Saml2LogoutResponse(spConfig)
        {
            Status = success ? Saml2StatusCodes.Success : Saml2StatusCodes.Responder
        };

        if (!string.IsNullOrEmpty(inResponseTo))
        {
            response.InResponseTo = new Saml2Id(inResponseTo);
        }

        var binding = new Saml2PostBinding();
        var result = binding.Bind(response);

        // Get SLO destination for SP
        var destination = spConfig.SingleLogoutDestination?.ToString() ?? "";

        return new SamlResponseResult
        {
            Destination = destination,
            SamlResponse = result.PostContent
        };
    }

    public IReadOnlyList<SamlSpInfo> GetRegisteredServiceProviders()
    {
        // Return both config and database SPs (using _spConfigsRaw which contains both)
        return _spConfigsRaw
            .Select(kvp => new SamlSpInfo
            {
                EntityId = kvp.Value.EntityId,
                DisplayName = kvp.Value.DisplayName,
                Enabled = kvp.Value.Enabled
            })
            .ToList();
    }

    public async Task<string> GenerateMetadataAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);

        var entityDescriptor = new EntityDescriptor(_idpConfig!)
        {
            ValidUntil = 365
        };

        var idpDescriptor = new IdPSsoDescriptor
        {
            WantAuthnRequestsSigned = false
        };

        if (_idpConfig!.SigningCertificate != null)
        {
            idpDescriptor.SigningCertificates = new[] { _idpConfig.SigningCertificate };
        }

        // Initialize collections directly instead of using Append on null
        idpDescriptor.SingleSignOnServices = new[]
        {
            new SingleSignOnService
            {
                Binding = ProtocolBindings.HttpRedirect,
                Location = new Uri($"{_options.BaseUrl}{_options.SingleSignOnServicePath}")
            },
            new SingleSignOnService
            {
                Binding = ProtocolBindings.HttpPost,
                Location = new Uri($"{_options.BaseUrl}{_options.SingleSignOnServicePath}")
            }
        };

        idpDescriptor.SingleLogoutServices = new[]
        {
            new SingleLogoutService
            {
                Binding = ProtocolBindings.HttpRedirect,
                Location = new Uri($"{_options.BaseUrl}{_options.SingleLogoutServicePath}")
            }
        };

        idpDescriptor.NameIDFormats = _options.NameIdFormats.Select(f => new Uri(f)).ToArray();

        entityDescriptor.IdPSsoDescriptor = idpDescriptor;

        var metadata = new Saml2Metadata(entityDescriptor);
        return metadata.CreateMetadata().ToXml();
    }

    public async Task<string> GenerateMetadataForTenantAsync(string tenantId, CancellationToken cancellationToken = default)
    {
        if (_tenantSettingsService == null)
        {
            throw new InvalidOperationException("Tenant settings service is not available");
        }

        var settings = await _tenantSettingsService.GetSettingsAsync(tenantId, cancellationToken);
        if (!settings.Enabled)
        {
            throw new InvalidOperationException($"SAML IdP is not enabled for tenant {tenantId}");
        }

        // Get tenant-specific issuer URL
        string baseUrl;
        if (_issuerResolver != null)
        {
            baseUrl = await _issuerResolver.GetIssuerAsync(cancellationToken);
        }
        else
        {
            baseUrl = _options.BaseUrl;
        }

        // Determine entity ID (tenant override > issuer URL > global options)
        var entityId = settings.EntityId ?? baseUrl ?? _options.EntityId;

        // Get tenant-specific signing certificate
        var signingCert = await _tenantSettingsService.GetSigningCertificateAsync(tenantId, cancellationToken);

        var tenantConfig = new Saml2Configuration
        {
            Issuer = entityId
        };

        var entityDescriptor = new EntityDescriptor(tenantConfig)
        {
            ValidUntil = 365
        };

        var idpDescriptor = new IdPSsoDescriptor
        {
            WantAuthnRequestsSigned = false
        };

        if (signingCert != null)
        {
            idpDescriptor.SigningCertificates = new[] { signingCert };
        }

        // Use tenant-specific base URL for endpoints
        idpDescriptor.SingleSignOnServices = new[]
        {
            new SingleSignOnService
            {
                Binding = ProtocolBindings.HttpRedirect,
                Location = new Uri($"{baseUrl}{_options.SingleSignOnServicePath}")
            },
            new SingleSignOnService
            {
                Binding = ProtocolBindings.HttpPost,
                Location = new Uri($"{baseUrl}{_options.SingleSignOnServicePath}")
            }
        };

        idpDescriptor.SingleLogoutServices = new[]
        {
            new SingleLogoutService
            {
                Binding = ProtocolBindings.HttpRedirect,
                Location = new Uri($"{baseUrl}{_options.SingleLogoutServicePath}")
            }
        };

        idpDescriptor.NameIDFormats = _options.NameIdFormats.Select(f => new Uri(f)).ToArray();

        entityDescriptor.IdPSsoDescriptor = idpDescriptor;

        var metadata = new Saml2Metadata(entityDescriptor);
        return metadata.CreateMetadata().ToXml();
    }

    private static Saml2StatusCodes ParseStatusCode(string code)
    {
        return code.ToLowerInvariant() switch
        {
            "success" => Saml2StatusCodes.Success,
            "requester" => Saml2StatusCodes.Requester,
            "responder" => Saml2StatusCodes.Responder,
            "versionmismatch" => Saml2StatusCodes.VersionMismatch,
            "authnfailed" => Saml2StatusCodes.AuthnFailed,
            "noauthncontext" => Saml2StatusCodes.NoAuthnContext,
            _ => Saml2StatusCodes.Responder
        };
    }

    private class FakeHttpRequest : ITfoxtec.Identity.Saml2.Http.HttpRequest
    {
        private readonly bool _isPost;

        public FakeHttpRequest(IDictionary<string, string> data, bool isPost = false)
        {
            _isPost = isPost;
            if (isPost)
            {
                Form = data;
                Query = new Dictionary<string, string>();
            }
            else
            {
                Query = data;
                Form = new Dictionary<string, string>();
            }
        }

        public new string Method => _isPost ? "POST" : "GET";
        public new string? Binding => _isPost ? "urn:oasis:names:tc:SAML:2.0:bindings:HTTP-POST" : "urn:oasis:names:tc:SAML:2.0:bindings:HTTP-Redirect";
        public new IDictionary<string, string> Query { get; }
        public new IDictionary<string, string> Form { get; }
    }
}

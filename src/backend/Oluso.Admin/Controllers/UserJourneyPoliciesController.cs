using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Oluso.Core.Api;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.UserJourneys;

namespace Oluso.Admin.Controllers;

/// <summary>
/// Admin API for managing user journey policies per tenant
/// </summary>
[Route("api/admin/journeys")]
public class UserJourneyPoliciesController : AdminBaseController
{
    private readonly IJourneyPolicyStore _policyStore;
    private readonly IExtendedStepHandlerRegistry? _stepRegistry;
    private readonly IPluginStore? _pluginStore;
    private readonly ITenantContext _tenantContext;
    private readonly ILogger<UserJourneyPoliciesController> _logger;

    public UserJourneyPoliciesController(
        IJourneyPolicyStore policyStore,
        ITenantContext tenantContext,
        ILogger<UserJourneyPoliciesController> logger,
        IExtendedStepHandlerRegistry? stepRegistry = null,
        IPluginStore? pluginStore = null) : base(tenantContext)
    {
        _policyStore = policyStore;
        _stepRegistry = stepRegistry;
        _pluginStore = pluginStore;
        _tenantContext = tenantContext;
        _logger = logger;
    }

    /// <summary>
    /// Get all policies for the current tenant
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<PolicyListItemDto>>> GetPolicies(
        [FromQuery] bool includeGlobal = true,
        CancellationToken cancellationToken = default)
    {
        var policies = await _policyStore.GetByTenantAsync(_tenantContext.TenantId, cancellationToken);

        if (!includeGlobal)
        {
            policies = policies.Where(p => p.TenantId == _tenantContext.TenantId);
        }

        var result = policies.Select(p => new PolicyListItemDto
        {
            Id = p.Id,
            Name = p.Name,
            Description = p.Description,
            Type = p.Type.ToString(),
            Enabled = p.Enabled,
            Priority = p.Priority,
            IsGlobal = p.TenantId == null,
            StepCount = p.Steps.Count,
            Version = p.Version,
            CreatedAt = p.CreatedAt,
            UpdatedAt = p.UpdatedAt
        });

        return Ok(result);
    }

    /// <summary>
    /// Get a specific policy
    /// </summary>
    [HttpGet("{policyId}")]
    public async Task<ActionResult<PolicyDetailDto>> GetPolicy(
        string policyId,
        CancellationToken cancellationToken = default)
    {
        var policy = await _policyStore.GetAsync(policyId, cancellationToken);

        if (policy == null)
        {
            return NotFound();
        }

        // Check tenant access
        if (policy.TenantId != null && policy.TenantId != _tenantContext.TenantId)
        {
            return Forbid();
        }

        return Ok(MapToDetailDto(policy));
    }

    /// <summary>
    /// Create a new policy
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<PolicyDetailDto>> CreatePolicy(
        [FromBody] CreatePolicyRequest request,
        CancellationToken cancellationToken = default)
    {
        // Validate request
        var validation = await ValidatePolicyAsync(request.Steps, cancellationToken);
        if (!validation.IsValid)
        {
            return BadRequest(new { errors = validation.Errors });
        }

        var journeyType = Enum.Parse<JourneyType>(request.Type);

        // Determine defaults based on journey type
        var isDataCollectionType = journeyType is JourneyType.Waitlist or JourneyType.ContactForm
            or JourneyType.Survey or JourneyType.Feedback or JourneyType.DataCollection;

        var policy = new JourneyPolicy
        {
            Id = request.Id ?? $"policy_{Guid.NewGuid():N}",
            Name = request.Name,
            Description = request.Description,
            Type = journeyType,
            TenantId = _tenantContext.TenantId, // Always scoped to current tenant
            Enabled = request.Enabled,
            Priority = request.Priority,
            Steps = request.Steps.Select(s => new JourneyPolicyStep
            {
                Id = s.Id,
                Type = s.Type,
                DisplayName = s.DisplayName,
                Configuration = s.Configuration,
                Optional = s.Optional,
                Order = s.Order,
                Branches = s.Branches
            }).ToList(),
            Conditions = request.Conditions,
            OutputClaims = request.OutputClaims,
            Session = request.Session,
            Ui = request.Ui,
            // Data collection properties - use smart defaults for data collection types
            RequiresAuthentication = request.RequiresAuthentication ?? !isDataCollectionType,
            PersistSubmissions = request.PersistSubmissions ?? isDataCollectionType,
            SubmissionCollection = request.SubmissionCollection,
            MaxSubmissions = request.MaxSubmissions ?? 0,
            AllowDuplicates = request.AllowDuplicates ?? false,
            DuplicateCheckFields = request.DuplicateCheckFields,
            SuccessRedirectUrl = request.SuccessRedirectUrl,
            SuccessMessage = request.SuccessMessage
        };

        await _policyStore.SaveAsync(policy, cancellationToken);

        _logger.LogInformation("Created policy {PolicyId} for tenant {TenantId}",
            policy.Id, _tenantContext.TenantId);

        return CreatedAtAction(nameof(GetPolicy), new { policyId = policy.Id }, MapToDetailDto(policy));
    }

    /// <summary>
    /// Update an existing policy
    /// </summary>
    [HttpPut("{policyId}")]
    public async Task<ActionResult<PolicyDetailDto>> UpdatePolicy(
        string policyId,
        [FromBody] UpdatePolicyRequest request,
        CancellationToken cancellationToken = default)
    {
        var existing = await _policyStore.GetAsync(policyId, cancellationToken);

        if (existing == null)
        {
            return NotFound();
        }

        // Check tenant access - can only update own policies
        if (existing.TenantId != _tenantContext.TenantId)
        {
            return Forbid();
        }

        // Validate steps
        if (request.Steps != null)
        {
            var validation = await ValidatePolicyAsync(request.Steps, cancellationToken);
            if (!validation.IsValid)
            {
                return BadRequest(new { errors = validation.Errors });
            }
        }

        // Update fields
        existing.Name = request.Name ?? existing.Name;
        existing.Description = request.Description ?? existing.Description;
        existing.Enabled = request.Enabled ?? existing.Enabled;
        existing.Priority = request.Priority ?? existing.Priority;
        if (request.Steps != null)
        {
            existing.Steps = request.Steps.Select(s => new JourneyPolicyStep
            {
                Id = s.Id,
                Type = s.Type,
                DisplayName = s.DisplayName,
                Configuration = s.Configuration,
                Optional = s.Optional,
                Order = s.Order,
                Branches = s.Branches
            }).ToList();
        }
        existing.Conditions = request.Conditions ?? existing.Conditions;
        existing.OutputClaims = request.OutputClaims ?? existing.OutputClaims;
        existing.Session = request.Session ?? existing.Session;
        existing.Ui = request.Ui ?? existing.Ui;

        // Data collection properties
        existing.RequiresAuthentication = request.RequiresAuthentication ?? existing.RequiresAuthentication;
        existing.PersistSubmissions = request.PersistSubmissions ?? existing.PersistSubmissions;
        existing.SubmissionCollection = request.SubmissionCollection ?? existing.SubmissionCollection;
        existing.MaxSubmissions = request.MaxSubmissions ?? existing.MaxSubmissions;
        existing.AllowDuplicates = request.AllowDuplicates ?? existing.AllowDuplicates;
        existing.DuplicateCheckFields = request.DuplicateCheckFields ?? existing.DuplicateCheckFields;
        existing.SuccessRedirectUrl = request.SuccessRedirectUrl ?? existing.SuccessRedirectUrl;
        existing.SuccessMessage = request.SuccessMessage ?? existing.SuccessMessage;

        existing.UpdatedAt = DateTime.UtcNow;
        existing.Version++;

        await _policyStore.SaveAsync(existing, cancellationToken);

        _logger.LogInformation("Updated policy {PolicyId}", policyId);

        return Ok(MapToDetailDto(existing));
    }

    /// <summary>
    /// Delete a policy
    /// </summary>
    [HttpDelete("{policyId}")]
    public async Task<IActionResult> DeletePolicy(
        string policyId,
        CancellationToken cancellationToken = default)
    {
        var existing = await _policyStore.GetAsync(policyId, cancellationToken);

        if (existing == null)
        {
            return NotFound();
        }

        // Check tenant access - can only delete own policies
        if (existing.TenantId != _tenantContext.TenantId)
        {
            return Forbid();
        }

        await _policyStore.DeleteAsync(policyId, cancellationToken);

        _logger.LogInformation("Deleted policy {PolicyId}", policyId);

        return NoContent();
    }

    /// <summary>
    /// Clone a policy (including global policies for customization)
    /// </summary>
    [HttpPost("{policyId}/clone")]
    public async Task<ActionResult<PolicyDetailDto>> ClonePolicy(
        string policyId,
        [FromBody] ClonePolicyRequest request,
        CancellationToken cancellationToken = default)
    {
        var source = await _policyStore.GetAsync(policyId, cancellationToken);

        if (source == null)
        {
            return NotFound();
        }

        var cloned = new JourneyPolicy
        {
            Id = request.NewId ?? $"policy_{Guid.NewGuid():N}",
            Name = request.NewName ?? $"{source.Name} (Copy)",
            Description = source.Description,
            Type = source.Type,
            TenantId = _tenantContext.TenantId, // Clone to current tenant
            Enabled = false, // Start disabled
            Priority = source.Priority,
            Steps = source.Steps.Select(s => new JourneyPolicyStep
            {
                Id = s.Id,
                Type = s.Type,
                DisplayName = s.DisplayName,
                Configuration = s.Configuration != null
                    ? new Dictionary<string, object>(s.Configuration)
                    : null,
                Branches = s.Branches,
                Optional = s.Optional,
                Order = s.Order
            }).ToList(),
            Conditions = source.Conditions?.ToList(),
            OutputClaims = source.OutputClaims?.ToList(),
            Session = source.Session,
            Ui = source.Ui,
            // Data collection properties
            RequiresAuthentication = source.RequiresAuthentication,
            PersistSubmissions = source.PersistSubmissions,
            SubmissionCollection = source.SubmissionCollection,
            MaxSubmissions = source.MaxSubmissions,
            AllowDuplicates = source.AllowDuplicates,
            DuplicateCheckFields = source.DuplicateCheckFields?.ToList(),
            SuccessRedirectUrl = source.SuccessRedirectUrl,
            SuccessMessage = source.SuccessMessage
        };

        await _policyStore.SaveAsync(cloned, cancellationToken);

        _logger.LogInformation("Cloned policy {SourceId} to {NewId}", policyId, cloned.Id);

        return CreatedAtAction(nameof(GetPolicy), new { policyId = cloned.Id }, MapToDetailDto(cloned));
    }

    /// <summary>
    /// Enable or disable a policy
    /// </summary>
    [HttpPatch("{policyId}/status")]
    public async Task<IActionResult> SetPolicyStatus(
        string policyId,
        [FromBody] SetStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        var policy = await _policyStore.GetAsync(policyId, cancellationToken);

        if (policy == null)
        {
            return NotFound();
        }

        if (policy.TenantId != _tenantContext.TenantId)
        {
            return Forbid();
        }

        policy.Enabled = request.Enabled;
        await _policyStore.SaveAsync(policy, cancellationToken);

        return Ok(new { enabled = policy.Enabled });
    }

    /// <summary>
    /// Get available step types
    /// </summary>
    [HttpGet("step-types")]
    public ActionResult<IEnumerable<StepTypeInfoDto>> GetStepTypes()
    {
        // If registry is available, use its types; otherwise return built-in enum types
        if (_stepRegistry != null)
        {
            var registeredTypes = _stepRegistry.GetRegisteredTypes()
                .Select(s => new StepTypeInfoDto
                {
                    Type = s.Type,
                    TypeId = s.Type,
                    Category = s.Category ?? "Other",
                    DisplayName = s.DisplayName ?? s.Type,
                    Description = s.Description ?? "",
                    IsAvailable = true,
                    ConfigurationSchema = s.ConfigurationSchema
                })
                .OrderBy(s => GetCategoryOrder(s.Category))
                .ThenBy(s => s.DisplayName);
            return Ok(registeredTypes);
        }

        var stepTypes = Enum.GetValues<StepType>()
            .Select(type => new StepTypeInfoDto
            {
                Type = type.ToString(),
                TypeId = type.ToString(),
                Category = GetStepCategory(type),
                DisplayName = GetStepDisplayName(type),
                Description = GetStepDescription(type),
                IsAvailable = true,
                ConfigurationSchema = GetStepConfigSchema(type)
            })
            .OrderBy(s => GetCategoryOrder(s.Category))
            .ThenBy(s => s.DisplayName);

        return Ok(stepTypes);
    }

    /// <summary>
    /// Get available plugins for use in CustomPlugin steps
    /// </summary>
    [HttpGet("available-plugins")]
    public async Task<ActionResult<IEnumerable<AvailablePluginDto>>> GetAvailablePlugins(
        CancellationToken cancellationToken = default)
    {
        if (_pluginStore == null)
        {
            return Ok(Array.Empty<AvailablePluginDto>());
        }

        var plugins = await _pluginStore.GetAvailablePluginsAsync(
            _tenantContext.TenantId,
            cancellationToken);

        var result = plugins.Select(p => new AvailablePluginDto
        {
            Name = p.Name,
            DisplayName = p.DisplayName ?? p.Name,
            Description = p.Description,
            Version = p.Version,
            Author = p.Author,
            Scope = p.Scope.ToString(),
            IsGlobal = p.Scope == PluginScope.Global,
            RequiredClaims = p.GetRequiredClaimsList(),
            OutputClaims = p.GetOutputClaimsList(),
            ConfigSchema = p.GetConfigSchemaObject()
        });

        return Ok(result);
    }

    /// <summary>
    /// Validate a policy configuration
    /// </summary>
    [HttpPost("validate")]
    public async Task<ActionResult<ValidationResultDto>> ValidatePolicy(
        [FromBody] ValidatePolicyRequest request,
        CancellationToken cancellationToken = default)
    {
        var validation = await ValidatePolicyAsync(request.Steps, cancellationToken);
        return Ok(new ValidationResultDto
        {
            IsValid = validation.IsValid,
            Errors = validation.Errors
        });
    }

    /// <summary>
    /// Validate a single step's configuration. Used for real-time validation in the UI.
    /// </summary>
    [HttpPost("validate-step")]
    public async Task<ActionResult<StepValidationResultDto>> ValidateStepConfiguration(
        [FromBody] ValidateStepRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(request.Type))
        {
            return BadRequest(new { error = "Step type is required" });
        }

        var errors = new List<string>();
        var warnings = new List<string>();

        // Try to get the handler for this step type
        if (_stepRegistry != null)
        {
            var handler = _stepRegistry.GetHandler(request.Type);
            if (handler != null)
            {
                var result = await handler.ValidateConfigurationAsync(request.Configuration);
                if (!result.IsValid)
                {
                    errors.AddRange(result.Errors);
                }
            }
            else if (!Enum.TryParse<StepType>(request.Type, out _))
            {
                errors.Add($"Unknown step type: {request.Type}");
            }
        }

        // Perform additional common validations based on step type
        var stepTypeValidation = ValidateStepTypeConfiguration(request.Type, request.Configuration);
        errors.AddRange(stepTypeValidation.Errors);
        warnings.AddRange(stepTypeValidation.Warnings);

        return Ok(new StepValidationResultDto
        {
            IsValid = errors.Count == 0,
            Errors = errors,
            Warnings = warnings
        });
    }

    /// <summary>
    /// Test connectivity/configuration for steps that require external resources.
    /// Currently supports: ExternalIdp, Ldap, ApiCall, Webhook
    /// </summary>
    [HttpPost("test-step")]
    public async Task<ActionResult<StepTestResultDto>> TestStepConfiguration(
        [FromBody] TestStepRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(request.Type))
        {
            return BadRequest(new { error = "Step type is required" });
        }

        var result = request.Type switch
        {
            "ExternalIdp" => await TestExternalIdpAsync(request.Configuration, cancellationToken),
            "Ldap" => await TestLdapConnectionAsync(request.Configuration, cancellationToken),
            "ApiCall" => await TestApiCallAsync(request.Configuration, cancellationToken),
            "Webhook" => await TestWebhookAsync(request.Configuration, cancellationToken),
            "CaptchaVerification" => await TestCaptchaAsync(request.Configuration, cancellationToken),
            _ => new StepTestResultDto
            {
                Success = false,
                Message = $"Testing is not supported for step type: {request.Type}"
            }
        };

        return Ok(result);
    }

    private (List<string> Errors, List<string> Warnings) ValidateStepTypeConfiguration(
        string stepType,
        IDictionary<string, object>? config)
    {
        var errors = new List<string>();
        var warnings = new List<string>();

        if (config == null)
        {
            // Some steps don't require configuration
            return (errors, warnings);
        }

        switch (stepType)
        {
            case "ExternalIdp":
                // Check that at least one provider is specified, or providers will be auto-loaded
                if (config.TryGetValue("providers", out var providers) && providers is IList<object> providerList)
                {
                    if (providerList.Count == 0)
                    {
                        warnings.Add("No specific providers configured. All enabled identity providers will be shown.");
                    }
                }
                break;

            case "ApiCall":
                if (!config.ContainsKey("url") || string.IsNullOrEmpty(config["url"]?.ToString()))
                {
                    errors.Add("API endpoint URL is required");
                }
                else
                {
                    var url = config["url"]?.ToString() ?? "";
                    if (!url.StartsWith("http://") && !url.StartsWith("https://") && !url.StartsWith("{"))
                    {
                        errors.Add("URL must start with http:// or https:// (or be a template like {claim:endpoint})");
                    }
                }
                break;

            case "Webhook":
                if (!config.ContainsKey("url") || string.IsNullOrEmpty(config["url"]?.ToString()))
                {
                    errors.Add("Webhook URL is required");
                }
                break;

            case "Ldap":
                if (!config.ContainsKey("serverUrl") || string.IsNullOrEmpty(config["serverUrl"]?.ToString()))
                {
                    errors.Add("LDAP server URL is required");
                }
                if (!config.ContainsKey("baseDn") || string.IsNullOrEmpty(config["baseDn"]?.ToString()))
                {
                    errors.Add("LDAP base DN is required");
                }
                break;

            case "Saml":
                if (!config.ContainsKey("idpEntityId") || string.IsNullOrEmpty(config["idpEntityId"]?.ToString()))
                {
                    errors.Add("SAML IdP Entity ID is required");
                }
                if (!config.ContainsKey("idpSsoUrl") || string.IsNullOrEmpty(config["idpSsoUrl"]?.ToString()))
                {
                    errors.Add("SAML IdP SSO URL is required");
                }
                break;

            case "CaptchaVerification":
                if (!config.ContainsKey("siteKey") || string.IsNullOrEmpty(config["siteKey"]?.ToString()))
                {
                    errors.Add("CAPTCHA site key is required");
                }
                if (!config.ContainsKey("secretKey") || string.IsNullOrEmpty(config["secretKey"]?.ToString()))
                {
                    errors.Add("CAPTCHA secret key is required");
                }
                break;

            case "CustomPlugin":
                if (!config.ContainsKey("pluginName") || string.IsNullOrEmpty(config["pluginName"]?.ToString()))
                {
                    errors.Add("Plugin name is required");
                }
                break;

            case "CustomPage":
                if (!config.ContainsKey("templatePath") || string.IsNullOrEmpty(config["templatePath"]?.ToString()))
                {
                    errors.Add("Template path is required");
                }
                break;

            case "TermsAcceptance":
                if (!config.ContainsKey("termsUrl") || string.IsNullOrEmpty(config["termsUrl"]?.ToString()))
                {
                    errors.Add("Terms URL is required");
                }
                break;

            case "ClaimsCollection":
                if (!config.ContainsKey("fields") || config["fields"] is not IList<object> fields || fields.Count == 0)
                {
                    errors.Add("At least one form field is required");
                }
                break;

            case "Condition":
                if (!config.ContainsKey("conditions") || config["conditions"] is not IList<object> conditions || conditions.Count == 0)
                {
                    errors.Add("At least one condition is required");
                }
                break;

            case "Transform":
                if (!config.ContainsKey("mappings") || config["mappings"] is not IList<object> mappings || mappings.Count == 0)
                {
                    errors.Add("At least one mapping is required");
                }
                break;
        }

        return (errors, warnings);
    }

    private static string? GetConfigValue(IDictionary<string, object>? config, string key)
    {
        if (config == null) return null;
        return config.TryGetValue(key, out var value) ? value?.ToString() : null;
    }

    private async Task<StepTestResultDto> TestExternalIdpAsync(
        IDictionary<string, object>? config,
        CancellationToken cancellationToken)
    {
        // Test that the specified providers exist and are enabled
        if (config == null || !config.TryGetValue("providers", out var providersObj))
        {
            return new StepTestResultDto
            {
                Success = true,
                Message = "No specific providers configured. All enabled identity providers will be available."
            };
        }

        var providerStore = HttpContext.RequestServices.GetService(typeof(IIdentityProviderStore)) as IIdentityProviderStore;
        if (providerStore == null)
        {
            return new StepTestResultDto
            {
                Success = false,
                Message = "Identity provider store not available"
            };
        }

        var providers = await providerStore.GetAllAsync(cancellationToken);
        var requestedProviders = new List<string>();

        if (providersObj is IList<object> providerList)
        {
            requestedProviders = providerList.Select(p => p?.ToString() ?? "").Where(p => !string.IsNullOrEmpty(p)).ToList();
        }

        var results = new List<string>();
        var hasErrors = false;

        foreach (var scheme in requestedProviders)
        {
            var provider = providers.FirstOrDefault(p => p.Scheme.Equals(scheme, StringComparison.OrdinalIgnoreCase));
            if (provider == null)
            {
                results.Add($"Provider '{scheme}' not found");
                hasErrors = true;
            }
            else if (!provider.Enabled)
            {
                results.Add($"Provider '{scheme}' is disabled");
                hasErrors = true;
            }
            else
            {
                results.Add($"Provider '{scheme}' is configured and enabled");
            }
        }

        return new StepTestResultDto
        {
            Success = !hasErrors,
            Message = hasErrors ? "Some providers have issues" : "All providers are configured correctly",
            Details = results
        };
    }

    private async Task<StepTestResultDto> TestLdapConnectionAsync(
        IDictionary<string, object>? config,
        CancellationToken cancellationToken)
    {
        if (config == null)
        {
            return new StepTestResultDto
            {
                Success = false,
                Message = "No LDAP configuration provided"
            };
        }

        var serverUrl = GetConfigValue(config, "serverUrl") ?? "";
        var baseDn = GetConfigValue(config, "baseDn") ?? "";

        if (string.IsNullOrEmpty(serverUrl) || string.IsNullOrEmpty(baseDn))
        {
            return new StepTestResultDto
            {
                Success = false,
                Message = "Server URL and Base DN are required"
            };
        }

        // TODO: Implement actual LDAP connection test
        // For now, just validate the URL format
        try
        {
            var uri = new Uri(serverUrl);
            if (uri.Scheme != "ldap" && uri.Scheme != "ldaps")
            {
                return new StepTestResultDto
                {
                    Success = false,
                    Message = "Server URL must use ldap:// or ldaps:// scheme"
                };
            }

            await Task.CompletedTask;
            return new StepTestResultDto
            {
                Success = true,
                Message = $"LDAP configuration appears valid. Server: {uri.Host}:{uri.Port}",
                Details = new List<string>
                {
                    $"Server: {uri.Host}",
                    $"Port: {uri.Port}",
                    $"Base DN: {baseDn}",
                    "Note: Full connection test requires LDAP service integration"
                }
            };
        }
        catch (UriFormatException)
        {
            return new StepTestResultDto
            {
                Success = false,
                Message = "Invalid server URL format"
            };
        }
    }

    private async Task<StepTestResultDto> TestApiCallAsync(
        IDictionary<string, object>? config,
        CancellationToken cancellationToken)
    {
        if (config == null)
        {
            return new StepTestResultDto
            {
                Success = false,
                Message = "No API configuration provided"
            };
        }

        var url = GetConfigValue(config, "url") ?? "";

        if (string.IsNullOrEmpty(url))
        {
            return new StepTestResultDto
            {
                Success = false,
                Message = "API URL is required"
            };
        }

        // Check if URL contains placeholders
        if (url.Contains("{"))
        {
            return new StepTestResultDto
            {
                Success = true,
                Message = "URL contains placeholders that will be resolved at runtime",
                Details = new List<string>
                {
                    $"URL template: {url}",
                    "Note: Cannot test URLs with placeholders"
                }
            };
        }

        try
        {
            var httpClientFactory = HttpContext.RequestServices.GetService(typeof(IHttpClientFactory)) as IHttpClientFactory;
            if (httpClientFactory == null)
            {
                return new StepTestResultDto
                {
                    Success = false,
                    Message = "HTTP client factory not available"
                };
            }
            var httpClient = httpClientFactory.CreateClient();

            var method = (GetConfigValue(config, "method") ?? "GET").ToUpperInvariant();
            var request = new HttpRequestMessage(new HttpMethod(method), url);

            // Just do a HEAD or OPTIONS request to verify connectivity
            request.Method = HttpMethod.Head;

            var response = await httpClient.SendAsync(request, cancellationToken);

            return new StepTestResultDto
            {
                Success = response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed,
                Message = response.IsSuccessStatusCode
                    ? $"Successfully connected to API. Status: {(int)response.StatusCode}"
                    : $"API responded with status: {(int)response.StatusCode} {response.StatusCode}",
                Details = new List<string>
                {
                    $"URL: {url}",
                    $"Status: {(int)response.StatusCode} {response.StatusCode}"
                }
            };
        }
        catch (HttpRequestException ex)
        {
            return new StepTestResultDto
            {
                Success = false,
                Message = $"Failed to connect to API: {ex.Message}"
            };
        }
        catch (Exception ex)
        {
            return new StepTestResultDto
            {
                Success = false,
                Message = $"Error testing API: {ex.Message}"
            };
        }
    }

    private async Task<StepTestResultDto> TestWebhookAsync(
        IDictionary<string, object>? config,
        CancellationToken cancellationToken)
    {
        // Similar to API test but for webhooks
        return await TestApiCallAsync(config, cancellationToken);
    }

    private async Task<StepTestResultDto> TestCaptchaAsync(
        IDictionary<string, object>? config,
        CancellationToken cancellationToken)
    {
        if (config == null)
        {
            return new StepTestResultDto
            {
                Success = false,
                Message = "No CAPTCHA configuration provided"
            };
        }

        var provider = GetConfigValue(config, "provider") ?? "recaptcha";
        var siteKey = GetConfigValue(config, "siteKey") ?? "";

        if (string.IsNullOrEmpty(siteKey))
        {
            return new StepTestResultDto
            {
                Success = false,
                Message = "CAPTCHA site key is required"
            };
        }

        await Task.CompletedTask;

        return new StepTestResultDto
        {
            Success = true,
            Message = $"{provider} CAPTCHA configuration appears valid",
            Details = new List<string>
            {
                $"Provider: {provider}",
                $"Site Key: {siteKey[..Math.Min(10, siteKey.Length)]}...",
                "Note: Full validation requires a browser test"
            }
        };
    }

    private async Task<(bool IsValid, List<string> Errors)> ValidatePolicyAsync(
        List<JourneyPolicyStepDto> steps,
        CancellationToken cancellationToken)
    {
        var errors = new List<string>();

        if (steps == null || steps.Count == 0)
        {
            errors.Add("At least one step is required");
            return (false, errors);
        }

        var stepIds = new HashSet<string>();

        foreach (var step in steps)
        {
            if (string.IsNullOrEmpty(step.Id))
            {
                errors.Add("Each step must have an ID");
                continue;
            }

            if (!stepIds.Add(step.Id))
            {
                errors.Add($"Duplicate step ID: {step.Id}");
            }

            // Check if step type is valid and supported
            // Use ignoreCase since registry uses snake_case but enum uses PascalCase
            if (!Enum.TryParse<StepType>(step.Type, ignoreCase: true, out _))
            {
                // Not a built-in enum type - check registry (handles custom step handlers)
                if (_stepRegistry != null)
                {
                    var knownTypes = _stepRegistry.GetRegisteredTypes().Select(t => t.Type);
                    if (!knownTypes.Any(t => t.Equals(step.Type, StringComparison.OrdinalIgnoreCase)))
                    {
                        errors.Add($"Unknown step type: {step.Type} in step {step.Id}");
                    }
                }
            }

            // Validate branch references
            if (step.Branches != null)
            {
                foreach (var (key, targetId) in step.Branches)
                {
                    if (!steps.Any(s => s.Id == targetId))
                    {
                        errors.Add($"Step {step.Id} branch '{key}' references non-existent step: {targetId}");
                    }
                }
            }
        }

        await Task.CompletedTask; // Placeholder for async validation
        return (errors.Count == 0, errors);
    }

    private static PolicyDetailDto MapToDetailDto(JourneyPolicy policy)
    {
        return new PolicyDetailDto
        {
            Id = policy.Id,
            Name = policy.Name,
            Description = policy.Description,
            Type = policy.Type.ToString(),
            TenantId = policy.TenantId,
            Enabled = policy.Enabled,
            Priority = policy.Priority,
            Steps = policy.Steps.ToList(),
            Conditions = policy.Conditions?.ToList(),
            OutputClaims = policy.OutputClaims?.ToList(),
            Session = policy.Session,
            Ui = policy.Ui,
            Version = policy.Version,
            CreatedAt = policy.CreatedAt,
            UpdatedAt = policy.UpdatedAt,
            // Data collection properties
            RequiresAuthentication = policy.RequiresAuthentication,
            PersistSubmissions = policy.PersistSubmissions,
            SubmissionCollection = policy.SubmissionCollection,
            MaxSubmissions = policy.MaxSubmissions,
            AllowDuplicates = policy.AllowDuplicates,
            DuplicateCheckFields = policy.DuplicateCheckFields?.ToList(),
            SuccessRedirectUrl = policy.SuccessRedirectUrl,
            SuccessMessage = policy.SuccessMessage
        };
    }

    private static int GetCategoryOrder(string category) => category switch
    {
        "Authentication" => 0,
        "User Interaction" => 1,
        "Logic" => 2,
        "Account Management" => 3,
        "Billing" => 4,
        "Custom" => 5,
        _ => 6
    };

    private static string GetStepCategory(StepType type) => type switch
    {
        StepType.LocalLogin or StepType.ExternalIdp or StepType.Mfa or
        StepType.PasswordlessEmail or StepType.PasswordlessSms or StepType.WebAuthn or
        StepType.Ldap or StepType.Saml
            => "Authentication",
        StepType.Consent or StepType.ClaimsCollection or StepType.TermsAcceptance or
        StepType.CaptchaVerification
            => "User Interaction",
        StepType.Condition or StepType.Branch or StepType.Transform or StepType.ApiCall or
        StepType.Webhook
            => "Logic",
        StepType.CreateUser or StepType.UpdateUser or StepType.LinkAccount or
        StepType.PasswordChange or StepType.PasswordReset
            => "Account Management",
        StepType.CustomPlugin or StepType.CustomPage
            => "Custom",
        _ => "Other"
    };

    private static string GetStepDisplayName(StepType type) => type switch
    {
        StepType.LocalLogin => "Local Login",
        StepType.ExternalIdp => "External Identity Provider",
        StepType.Mfa => "Multi-Factor Authentication",
        StepType.PasswordlessEmail => "Passwordless (Email)",
        StepType.PasswordlessSms => "Passwordless (SMS)",
        StepType.WebAuthn => "WebAuthn/FIDO2",
        StepType.Ldap => "LDAP / Active Directory",
        StepType.Saml => "SAML 2.0",
        StepType.Consent => "OAuth Consent",
        StepType.ClaimsCollection => "Collect User Info",
        StepType.TermsAcceptance => "Terms & Conditions",
        StepType.CaptchaVerification => "CAPTCHA",
        StepType.Condition => "Condition",
        StepType.Branch => "Branch",
        StepType.Transform => "Transform Claims",
        StepType.ApiCall => "API Call",
        StepType.Webhook => "Webhook",
        StepType.CreateUser => "Create User",
        StepType.UpdateUser => "Update User",
        StepType.LinkAccount => "Link Account",
        StepType.PasswordChange => "Change Password",
        StepType.PasswordReset => "Reset Password",
        StepType.CustomPlugin => "Custom Plugin",
        StepType.CustomPage => "Custom Page",
        _ => type.ToString()
    };

    private static string GetStepDescription(StepType type) => type switch
    {
        StepType.LocalLogin => "Authenticate user with username and password",
        StepType.ExternalIdp => "Authenticate via external provider (Google, Microsoft, etc.)",
        StepType.Mfa => "Require multi-factor authentication (TOTP, SMS, etc.)",
        StepType.PasswordlessEmail => "Passwordless authentication via email magic link",
        StepType.PasswordlessSms => "Passwordless authentication via SMS code",
        StepType.WebAuthn => "Biometric/hardware key authentication using WebAuthn/FIDO2",
        StepType.Ldap => "Authenticate against LDAP or Active Directory server",
        StepType.Saml => "Authenticate via SAML 2.0 identity provider (Okta, ADFS, etc.)",
        StepType.Consent => "Show OAuth consent screen for scope approval",
        StepType.ClaimsCollection => "Collect custom user information with configurable form fields",
        StepType.TermsAcceptance => "Require user to accept terms and conditions",
        StepType.CaptchaVerification => "Verify user is human via CAPTCHA",
        StepType.Condition => "Evaluate conditions and branch flow",
        StepType.Branch => "Branch to different steps based on input",
        StepType.Transform => "Transform or map claims between formats",
        StepType.ApiCall => "Call external API for validation or enrichment",
        StepType.Webhook => "Send webhook notification to external service",
        StepType.CreateUser => "Create a new user account in the system",
        StepType.UpdateUser => "Update existing user profile information",
        StepType.LinkAccount => "Link external identity to existing account",
        StepType.PasswordChange => "Allow user to change their password",
        StepType.PasswordReset => "Reset user password via email verification",
        StepType.CustomPlugin => "Execute custom WASM or managed plugin",
        StepType.CustomPage => "Display custom HTML page or template",
        _ => ""
    };

    private static Dictionary<string, object>? GetStepConfigSchema(StepType type)
    {
        return type switch
        {
            StepType.LocalLogin => new Dictionary<string, object>
            {
                ["allowRememberMe"] = new { type = "boolean", @default = true, description = "Show 'Remember me' checkbox" },
                ["allowSelfRegistration"] = new { type = "boolean", @default = false, description = "Show link to registration" },
                ["loginHintClaim"] = new { type = "string", description = "Claim to use as login hint" }
            },
            StepType.Mfa => new Dictionary<string, object>
            {
                ["required"] = new { type = "boolean", @default = false, description = "Always require MFA" },
                ["methods"] = new { type = "array", items = "string", @default = new[] { "totp", "phone", "email" }, description = "Allowed MFA methods" },
                ["allowSetup"] = new { type = "boolean", @default = true, description = "Allow users to set up MFA during flow" },
                ["rememberDevice"] = new { type = "boolean", @default = true, description = "Remember trusted devices" },
                ["rememberDeviceDays"] = new { type = "number", @default = 30, description = "Days to remember device" }
            },
            StepType.ExternalIdp => new Dictionary<string, object>
            {
                ["providers"] = new Dictionary<string, object>
                {
                    ["type"] = "array",
                    ["items"] = "string",
                    ["description"] = "Allowed identity provider schemes",
                    ["x-enumSource"] = new Dictionary<string, object>
                    {
                        ["endpoint"] = "/identity-providers",
                        ["valueField"] = "scheme",
                        ["labelField"] = "displayName",
                        ["descriptionField"] = "type"
                    }
                },
                ["autoProvision"] = new { type = "boolean", @default = true, description = "Auto-create user on first login" },
                ["autoRedirect"] = new { type = "boolean", @default = false, description = "Auto-redirect if only one provider" },
                ["claimMappings"] = new { type = "object", description = "Map external claims to internal claims" }
            },
            StepType.Consent => new Dictionary<string, object>
            {
                ["allowRemember"] = new { type = "boolean", @default = true, description = "Allow user to remember consent" },
                ["rememberDays"] = new { type = "number", @default = 365, description = "Days to remember consent" },
                ["showResourceScopes"] = new { type = "boolean", @default = true, description = "Show API resource scopes" },
                ["showIdentityScopes"] = new { type = "boolean", @default = true, description = "Show identity scopes" }
            },
            StepType.ClaimsCollection => new Dictionary<string, object>
            {
                ["title"] = new { type = "string", description = "Form title displayed to user" },
                ["description"] = new { type = "string", description = "Form description/instructions" },
                ["submitButtonText"] = new { type = "string", @default = "Continue", description = "Submit button label" },
                ["cancelButtonText"] = new { type = "string", description = "Cancel button label (if allowCancel)" },
                ["allowCancel"] = new { type = "boolean", @default = false, description = "Show cancel button" },
                ["viewName"] = new { type = "string", @default = "Journey/_ClaimsCollection", description = "Custom view name" },
                ["localizedTitles"] = new { type = "object", description = "Title translations by culture code" },
                ["localizedDescriptions"] = new { type = "object", description = "Description translations by culture code" },
                ["fields"] = new
                {
                    type = "array",
                    required = true,
                    description = "Form fields to collect",
                    items = new
                    {
                        type = "object",
                        properties = new Dictionary<string, object>
                        {
                            ["name"] = new { type = "string", required = true, description = "Field name (used as form input name)" },
                            ["type"] = new
                            {
                                type = "string",
                                required = true,
                                @enum = new[] { "text", "email", "password", "number", "date", "tel", "url", "textarea", "select", "radio", "checkbox" },
                                description = "Field input type"
                            },
                            ["label"] = new { type = "string", description = "Field label displayed to user" },
                            ["placeholder"] = new { type = "string", description = "Input placeholder text" },
                            ["description"] = new { type = "string", description = "Help text below field" },
                            ["required"] = new { type = "boolean", @default = false, description = "Field is required" },
                            ["claimType"] = new { type = "string", description = "Claim type to store value (defaults to name)" },
                            ["defaultValue"] = new { type = "string", description = "Default value" },
                            ["pattern"] = new { type = "string", description = "Regex validation pattern" },
                            ["patternError"] = new { type = "string", description = "Error message for pattern mismatch" },
                            ["minLength"] = new { type = "number", description = "Minimum character length" },
                            ["maxLength"] = new { type = "number", description = "Maximum character length" },
                            ["min"] = new { type = "string", description = "Minimum value (for number/date)" },
                            ["max"] = new { type = "string", description = "Maximum value (for number/date)" },
                            ["rows"] = new { type = "number", @default = 3, description = "Rows for textarea" },
                            ["readOnly"] = new { type = "boolean", @default = false, description = "Field is read-only" },
                            ["hidden"] = new { type = "boolean", @default = false, description = "Field is hidden" },
                            ["group"] = new { type = "string", description = "Group name for fieldset grouping" },
                            ["options"] = new
                            {
                                type = "array",
                                description = "Options for select/radio fields",
                                items = new
                                {
                                    type = "object",
                                    properties = new Dictionary<string, object>
                                    {
                                        ["value"] = new { type = "string", required = true },
                                        ["label"] = new { type = "string", required = true },
                                        ["localizedLabels"] = new { type = "object", description = "Label translations" }
                                    }
                                }
                            },
                            ["showWhen"] = new
                            {
                                type = "object",
                                description = "Conditional visibility",
                                properties = new Dictionary<string, object>
                                {
                                    ["field"] = new { type = "string", required = true, description = "Field name to check" },
                                    ["operator"] = new { type = "string", @enum = new[] { "equals", "notEquals", "contains", "notEmpty", "empty" }, @default = "equals" },
                                    ["value"] = new { type = "string", description = "Value to compare" }
                                }
                            },
                            ["localizedLabels"] = new { type = "object", description = "Label translations by culture" },
                            ["localizedPlaceholders"] = new { type = "object", description = "Placeholder translations" },
                            ["localizedDescriptions"] = new { type = "object", description = "Description translations" }
                        }
                    }
                }
            },
            StepType.TermsAcceptance => new Dictionary<string, object>
            {
                ["termsUrl"] = new { type = "string", required = true, description = "URL to terms of service" },
                ["privacyUrl"] = new { type = "string", description = "URL to privacy policy" },
                ["requireCheckbox"] = new { type = "boolean", @default = true, description = "Require checkbox acceptance" },
                ["version"] = new { type = "string", description = "Terms version for tracking acceptance" }
            },
            StepType.CaptchaVerification => new Dictionary<string, object>
            {
                ["provider"] = new { type = "string", @enum = new[] { "recaptcha", "hcaptcha", "cloudflare" }, @default = "recaptcha", description = "CAPTCHA provider" },
                ["siteKey"] = new { type = "string", required = true, description = "CAPTCHA site key" },
                ["secretKey"] = new Dictionary<string, object> { ["type"] = "string", ["required"] = true, ["description"] = "CAPTCHA secret key", ["x-control"] = "secret-input" },
                ["scoreThreshold"] = new { type = "number", @default = 0.5, description = "Minimum score (reCAPTCHA v3)" }
            },
            StepType.ApiCall => new Dictionary<string, object>
            {
                ["url"] = new { type = "string", required = true, description = "API endpoint URL (supports {claim:name}, {state:userId}, {input:key} substitution)" },
                ["method"] = new { type = "string", @enum = new[] { "GET", "POST", "PUT", "PATCH", "DELETE" }, @default = "GET", description = "HTTP method" },
                ["timeout"] = new { type = "number", @default = 30, description = "Request timeout in seconds" },
                ["headers"] = new
                {
                    type = "object",
                    description = "Custom HTTP headers to send with request (supports placeholder substitution)",
                    additionalProperties = new { type = "string" }
                },
                ["authentication"] = new
                {
                    type = "object",
                    description = "API authentication configuration",
                    properties = new Dictionary<string, object>
                    {
                        ["type"] = new { type = "string", @enum = new[] { "none", "bearer", "basic", "apikey" }, @default = "none", description = "Authentication type" },
                        ["token"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Bearer token (supports placeholders like {claim:access_token})", ["x-control"] = "secret-input", ["showWhen"] = new { field = "type", value = "bearer" } },
                        ["username"] = new { type = "string", description = "Basic auth username", showWhen = new { field = "type", value = "basic" } },
                        ["password"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Basic auth password", ["x-control"] = "secret-input", ["showWhen"] = new { field = "type", value = "basic" } },
                        ["apiKey"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "API key value", ["x-control"] = "secret-input", ["showWhen"] = new { field = "type", value = "apikey" } },
                        ["headerName"] = new { type = "string", @default = "X-API-Key", description = "Header name for API key", showWhen = new { field = "type", value = "apikey" } }
                    }
                },
                ["retryCount"] = new { type = "number", @default = 0, description = "Number of retry attempts on failure" },
                ["retryDelay"] = new { type = "number", @default = 1000, description = "Delay between retries in milliseconds" },
                ["failOnError"] = new { type = "boolean", @default = true, description = "Fail journey if API returns error status" },
                ["continueOnStatus"] = new { type = "array", items = "number", description = "HTTP status codes that should continue (e.g., [404, 409])" },
                ["bodyTemplate"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "JSON template with placeholders for request body", ["x-control"] = "textarea" },
                ["bodyFromClaims"] = new { type = "array", items = "string", description = "List of claim types to include in request body" },
                ["inputMapping"] = new
                {
                    type = "object",
                    description = "Advanced input mapping from claims/state/input to request body fields",
                    additionalProperties = new
                    {
                        type = "object",
                        properties = new Dictionary<string, object>
                        {
                            ["from"] = new { type = "string", description = "Source claim/state/input name" },
                            ["source"] = new { type = "string", @enum = new[] { "claim", "state", "input", "constant" }, description = "Data source type" },
                            ["value"] = new { type = "string", description = "Constant value (when source=constant)" },
                            ["transform"] = new { type = "string", @enum = new[] { "uppercase", "lowercase", "trim", "base64encode", "base64decode", "urlencode", "urldecode" }, description = "Transform to apply" },
                            ["defaultValue"] = new { type = "string", description = "Default if source value is empty" },
                            ["required"] = new { type = "boolean", @default = false, description = "Include even if empty" }
                        }
                    }
                },
                ["outputMapping"] = new { type = "object", description = "Map JSON response paths to claim types (e.g., {\"user.id\": \"external_id\"})" },
                ["outputMappingAdvanced"] = new
                {
                    type = "object",
                    description = "Advanced output mapping with transforms",
                    additionalProperties = new
                    {
                        type = "object",
                        properties = new Dictionary<string, object>
                        {
                            ["path"] = new { type = "string", description = "JSON path in response" },
                            ["transform"] = new { type = "string", description = "Transform to apply" },
                            ["defaultValue"] = new { type = "string", description = "Default if not found" },
                            ["required"] = new { type = "boolean", @default = false, description = "Log warning if not found" }
                        }
                    }
                },
                ["responseValidation"] = new
                {
                    type = "array",
                    description = "Validation rules for response",
                    items = new
                    {
                        type = "object",
                        properties = new Dictionary<string, object>
                        {
                            ["path"] = new { type = "string", required = true, description = "JSON path to validate" },
                            ["type"] = new { type = "string", @enum = new[] { "required", "equals", "notequals", "contains", "matches", "in", "notin" }, description = "Validation type" },
                            ["expectedValue"] = new { type = "string", description = "Expected value for equals/contains" },
                            ["pattern"] = new { type = "string", description = "Regex pattern for matches" },
                            ["allowedValues"] = new { type = "array", items = "string", description = "Allowed values for 'in' validation" },
                            ["errorMessage"] = new { type = "string", description = "Custom error message" }
                        }
                    }
                },
                ["branchOnResponse"] = new
                {
                    type = "array",
                    description = "Branch to different steps based on response",
                    items = new
                    {
                        type = "object",
                        properties = new Dictionary<string, object>
                        {
                            ["onStatus"] = new { type = "array", items = "number", description = "HTTP status codes to match" },
                            ["path"] = new { type = "string", description = "JSON path to check" },
                            ["condition"] = new { type = "string", @enum = new[] { "equals", "notequals", "exists", "notexists", "contains", "true", "false" }, description = "Condition to evaluate" },
                            ["value"] = new { type = "string", description = "Value to compare" },
                            ["branchTo"] = new { type = "string", required = true, description = "Step ID to branch to" }
                        }
                    }
                },
                ["includeResponseMeta"] = new { type = "boolean", @default = false, description = "Add _api_status and _api_success claims" }
            },
            StepType.Condition => new Dictionary<string, object>
            {
                ["conditions"] = new
                {
                    type = "array",
                    required = true,
                    description = "Conditions to evaluate",
                    items = new
                    {
                        type = "object",
                        properties = new Dictionary<string, object>
                        {
                            ["source"] = new { type = "string", @enum = new[] { "claim", "context", "previous_step" }, description = "Data source" },
                            ["key"] = new { type = "string", required = true, description = "Key/claim name to check" },
                            ["operator"] = new { type = "string", @enum = new[] { "equals", "notEquals", "contains", "exists", "notExists", "greaterThan", "lessThan", "regex" } },
                            ["value"] = new { type = "string", description = "Value to compare" },
                            ["negate"] = new { type = "boolean", @default = false, description = "Negate the result" }
                        }
                    }
                },
                ["combineWith"] = new { type = "string", @enum = new[] { "and", "or" }, @default = "and", description = "How to combine conditions" },
                ["onTrue"] = new { type = "string", description = "Step ID when conditions are true" },
                ["onFalse"] = new { type = "string", description = "Step ID when conditions are false" }
            },
            StepType.Transform => new Dictionary<string, object>
            {
                ["mappings"] = new
                {
                    type = "array",
                    required = true,
                    description = "Claim transformations",
                    items = new
                    {
                        type = "object",
                        properties = new Dictionary<string, object>
                        {
                            ["source"] = new { type = "string", required = true, description = "Source claim type" },
                            ["target"] = new { type = "string", required = true, description = "Target claim type" },
                            ["transform"] = new { type = "string", @enum = new[] { "copy", "uppercase", "lowercase", "hash", "split", "join", "regex", "template" }, @default = "copy" },
                            ["transformArg"] = new { type = "string", description = "Argument for transform (regex pattern, delimiter, template)" }
                        }
                    }
                },
                ["removeSourceClaims"] = new { type = "boolean", @default = false, description = "Remove source claims after mapping" }
            },
            StepType.CreateUser => new Dictionary<string, object>
            {
                ["emailClaim"] = new { type = "string", @default = "email", description = "Claim containing email" },
                ["usernameClaim"] = new { type = "string", description = "Claim containing username (defaults to email)" },
                ["passwordClaim"] = new { type = "string", @default = "password", description = "Claim containing password" },
                ["requireEmailVerification"] = new { type = "boolean", @default = true, description = "Require email verification" },
                ["defaultRoles"] = new Dictionary<string, object>
                {
                    ["type"] = "array",
                    ["items"] = "string",
                    ["description"] = "Roles to assign to new user",
                    ["x-enumSource"] = new Dictionary<string, object>
                    {
                        ["endpoint"] = "/roles",
                        ["valueField"] = "name",
                        ["labelField"] = "name",
                        ["descriptionField"] = "description"
                    }
                },
                ["claimMappings"] = new { type = "object", description = "Map collected claims to user properties" }
            },
            StepType.PasswordReset => new Dictionary<string, object>
            {
                ["tokenLifetimeMinutes"] = new { type = "number", @default = 60, description = "Reset token lifetime" },
                ["requireCurrentPassword"] = new { type = "boolean", @default = false, description = "Require current password" }
            },
            StepType.PasswordChange => new Dictionary<string, object>
            {
                ["requireCurrentPassword"] = new { type = "boolean", @default = true, description = "Require current password" },
                ["minLength"] = new { type = "number", @default = 8, description = "Minimum password length" },
                ["requireUppercase"] = new { type = "boolean", @default = true, description = "Require uppercase letter" },
                ["requireLowercase"] = new { type = "boolean", @default = true, description = "Require lowercase letter" },
                ["requireDigit"] = new { type = "boolean", @default = true, description = "Require number" },
                ["requireSpecialChar"] = new { type = "boolean", @default = false, description = "Require special character" }
            },
            StepType.UpdateUser => new Dictionary<string, object>
            {
                ["editableFields"] = new { type = "array", items = "string", description = "Fields user can edit" },
                ["requireCurrentPassword"] = new { type = "boolean", @default = false, description = "Require current password to save changes" }
            },
            StepType.WebAuthn => new Dictionary<string, object>
            {
                ["allowPlatformAuthenticator"] = new { type = "boolean", @default = true, description = "Allow built-in authenticators (TouchID, Windows Hello)" },
                ["allowCrossPlatform"] = new { type = "boolean", @default = true, description = "Allow security keys" },
                ["requireResidentKey"] = new { type = "boolean", @default = false, description = "Require discoverable credentials (passkeys)" },
                ["passkeyOnly"] = new { type = "boolean", @default = false, description = "Only allow passkey login (no username)" }
            },
            StepType.Ldap => new Dictionary<string, object>
            {
                ["serverUrl"] = new { type = "string", required = true, description = "LDAP server URL" },
                ["baseDn"] = new { type = "string", required = true, description = "Base DN for user search" },
                ["searchFilter"] = new { type = "string", @default = "(sAMAccountName={0})", description = "LDAP search filter" },
                ["bindDn"] = new { type = "string", description = "Service account DN for binding" },
                ["bindPassword"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Service account password", ["x-control"] = "secret-input" },
                ["useSsl"] = new { type = "boolean", @default = true, description = "Use SSL/TLS" },
                ["claimMappings"] = new { type = "object", description = "Map LDAP attributes to claims" }
            },
            StepType.Saml => new Dictionary<string, object>
            {
                ["idpEntityId"] = new { type = "string", required = true, description = "IdP Entity ID" },
                ["idpSsoUrl"] = new { type = "string", required = true, description = "IdP Single Sign-On URL" },
                ["idpCertificate"] = new { type = "string", description = "IdP signing certificate (Base64)" },
                ["spEntityId"] = new { type = "string", description = "SP Entity ID (auto-generated if not set)" },
                ["assertionConsumerServiceUrl"] = new { type = "string", description = "ACS URL (auto-generated if not set)" },
                ["signRequests"] = new { type = "boolean", @default = false, description = "Sign SAML requests" },
                ["wantAssertionsSigned"] = new { type = "boolean", @default = true, description = "Require signed assertions" },
                ["claimMappings"] = new { type = "object", description = "Map SAML attributes to claims" }
            },
            StepType.PasswordlessEmail => new Dictionary<string, object>
            {
                ["codeLengthDigits"] = new { type = "number", @default = 6, description = "Number of digits in verification code" },
                ["codeExpiryMinutes"] = new { type = "number", @default = 15, description = "Minutes before code expires" },
                ["maxAttempts"] = new { type = "number", @default = 3, description = "Maximum verification attempts" },
                ["emailSubject"] = new { type = "string", description = "Custom email subject" },
                ["emailTemplate"] = new { type = "string", description = "Custom email template name" }
            },
            StepType.PasswordlessSms => new Dictionary<string, object>
            {
                ["codeLengthDigits"] = new { type = "number", @default = 6, description = "Number of digits in verification code" },
                ["codeExpiryMinutes"] = new { type = "number", @default = 10, description = "Minutes before code expires" },
                ["maxAttempts"] = new { type = "number", @default = 3, description = "Maximum verification attempts" },
                ["messageTemplate"] = new { type = "string", description = "Custom SMS message template" }
            },
            StepType.Webhook => new Dictionary<string, object>
            {
                ["url"] = new { type = "string", required = true, description = "Webhook endpoint URL" },
                ["async"] = new { type = "boolean", @default = true, description = "Don't wait for response" },
                ["timeout"] = new { type = "number", @default = 10, description = "Request timeout in seconds" },
                ["headers"] = new { type = "object", description = "Custom HTTP headers", additionalProperties = new { type = "string" } },
                ["includeUserData"] = new { type = "boolean", @default = true, description = "Include user data in payload" },
                ["includeJourneyData"] = new { type = "boolean", @default = false, description = "Include journey state in payload" },
                ["signPayload"] = new { type = "boolean", @default = false, description = "Sign payload with HMAC" },
                ["secretKey"] = new Dictionary<string, object> { ["type"] = "string", ["description"] = "Secret key for HMAC signature", ["x-control"] = "secret-input" }
            },
            StepType.Branch => new Dictionary<string, object>
            {
                ["targetStep"] = new { type = "string", required = true, description = "Step ID to branch to" }
            },
            StepType.LinkAccount => new Dictionary<string, object>
            {
                ["provider"] = new { type = "string", description = "External provider to link" },
                ["allowUnlink"] = new { type = "boolean", @default = true, description = "Allow unlinking accounts" },
                ["requireConfirmation"] = new { type = "boolean", @default = true, description = "Require user confirmation before linking" }
            },
            StepType.CustomPlugin => new Dictionary<string, object>
            {
                ["pluginName"] = new { type = "string", required = true, description = "Plugin assembly or WASM file name" },
                ["entryPoint"] = new { type = "string", @default = "execute", description = "Plugin entry point function" },
                ["config"] = new { type = "object", description = "Custom configuration passed to plugin" }
            },
            StepType.CustomPage => new Dictionary<string, object>
            {
                ["templatePath"] = new { type = "string", required = true, description = "Path to custom Razor template" },
                ["model"] = new { type = "object", description = "Data to pass to template" }
            },
            _ => null
        };
    }
}

#region DTOs

public class PolicyListItemDto
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string Type { get; set; } = null!;
    public bool Enabled { get; set; }
    public int Priority { get; set; }
    public bool IsGlobal { get; set; }
    public int StepCount { get; set; }
    public int Version { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

public class PolicyDetailDto
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string Type { get; set; } = null!;
    public string? TenantId { get; set; }
    public bool Enabled { get; set; }
    public int Priority { get; set; }
    public List<JourneyPolicyStep> Steps { get; set; } = new();
    public List<JourneyPolicyCondition>? Conditions { get; set; }
    public List<ClaimMapping>? OutputClaims { get; set; }
    public SessionConfiguration? Session { get; set; }
    public JourneyUiConfiguration? Ui { get; set; }
    public int Version { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }

    // Data collection properties
    public bool RequiresAuthentication { get; set; } = true;
    public bool PersistSubmissions { get; set; }
    public string? SubmissionCollection { get; set; }
    public int MaxSubmissions { get; set; }
    public bool AllowDuplicates { get; set; }
    public List<string>? DuplicateCheckFields { get; set; }
    public string? SuccessRedirectUrl { get; set; }
    public string? SuccessMessage { get; set; }
}

public class CreatePolicyRequest
{
    public string? Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string Type { get; set; } = null!;
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; } = 100;
    public List<JourneyPolicyStepDto> Steps { get; set; } = new();
    public List<JourneyPolicyCondition>? Conditions { get; set; }
    public List<ClaimMapping>? OutputClaims { get; set; }
    public SessionConfiguration? Session { get; set; }
    public JourneyUiConfiguration? Ui { get; set; }

    // Data collection properties (nullable to allow smart defaults based on journey type)
    public bool? RequiresAuthentication { get; set; }
    public bool? PersistSubmissions { get; set; }
    public string? SubmissionCollection { get; set; }
    public int? MaxSubmissions { get; set; }
    public bool? AllowDuplicates { get; set; }
    public List<string>? DuplicateCheckFields { get; set; }
    public string? SuccessRedirectUrl { get; set; }
    public string? SuccessMessage { get; set; }
}

public class UpdatePolicyRequest
{
    public string? Name { get; set; }
    public string? Description { get; set; }
    public bool? Enabled { get; set; }
    public int? Priority { get; set; }
    public List<JourneyPolicyStepDto>? Steps { get; set; }
    public List<JourneyPolicyCondition>? Conditions { get; set; }
    public List<ClaimMapping>? OutputClaims { get; set; }
    public SessionConfiguration? Session { get; set; }
    public JourneyUiConfiguration? Ui { get; set; }

    // Data collection properties
    public bool? RequiresAuthentication { get; set; }
    public bool? PersistSubmissions { get; set; }
    public string? SubmissionCollection { get; set; }
    public int? MaxSubmissions { get; set; }
    public bool? AllowDuplicates { get; set; }
    public List<string>? DuplicateCheckFields { get; set; }
    public string? SuccessRedirectUrl { get; set; }
    public string? SuccessMessage { get; set; }
}

public class ClonePolicyRequest
{
    public string? NewId { get; set; }
    public string? NewName { get; set; }
}

public class SetStatusRequest
{
    public bool Enabled { get; set; }
}

public class ValidatePolicyRequest
{
    public List<JourneyPolicyStepDto> Steps { get; set; } = new();
}

/// <summary>
/// DTO for step input to avoid ambiguity with core types
/// </summary>
public class JourneyPolicyStepDto
{
    public required string Id { get; init; }
    public required string Type { get; init; }
    public string? DisplayName { get; set; }
    public bool Optional { get; set; }
    public int Order { get; set; }
    public IDictionary<string, object>? Configuration { get; set; }
    public IDictionary<string, string>? Branches { get; set; }
}

public class ValidationResultDto
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
}

public class ValidateStepRequest
{
    public string Type { get; set; } = null!;
    public IDictionary<string, object>? Configuration { get; set; }
}

public class StepValidationResultDto
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
}

public class TestStepRequest
{
    public string Type { get; set; } = null!;
    public IDictionary<string, object>? Configuration { get; set; }
}

public class StepTestResultDto
{
    public bool Success { get; set; }
    public string Message { get; set; } = null!;
    public List<string>? Details { get; set; }
}

public class StepTypeInfoDto
{
    public string Type { get; set; } = null!;
    public string? TypeId { get; set; }
    public string Category { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string Description { get; set; } = null!;
    public bool IsAvailable { get; set; }

    /// <summary>
    /// JSON Schema describing the configuration options for this step type
    /// </summary>
    public Dictionary<string, object>? ConfigurationSchema { get; set; }
}

public class AvailablePluginDto
{
    public string Name { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string? Description { get; set; }
    public string? Version { get; set; }
    public string? Author { get; set; }
    public string Scope { get; set; } = null!;
    public bool IsGlobal { get; set; }
    public List<string>? RequiredClaims { get; set; }
    public List<string>? OutputClaims { get; set; }
    public Dictionary<string, object>? ConfigSchema { get; set; }
}

#endregion

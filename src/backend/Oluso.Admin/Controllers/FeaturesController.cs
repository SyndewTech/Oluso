using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oluso.Core.Api;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Licensing;

namespace Oluso.Admin.Controllers;

/// <summary>
/// Admin API for tenant features.
/// Allows admins to check feature availability for the current tenant context.
/// </summary>
[Route("api/admin/features")]
[Authorize(Policy = "AdminApi")]
public class FeaturesController : AdminBaseController
{
    private readonly IFeatureGate _featureGate;
    private readonly ILogger<FeaturesController> _logger;

    public FeaturesController(
        ITenantContext tenantContext,
        IFeatureGate featureGate,
        ILogger<FeaturesController> logger) : base(tenantContext)
    {
        _featureGate = featureGate;
        _logger = logger;
    }

    /// <summary>
    /// Get all features and limits for the current tenant
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<TenantFeaturesResponse>> GetTenantFeatures(CancellationToken cancellationToken)
    {
        // Get feature status for all known features
        var features = new Dictionary<string, FeatureStatusDto>();
        var featureKeys = new[]
        {
            "saml", "fido2", "ldap", "scim",
            "mfa", "social_login", "enterprise_sso", "passwordless", "adaptive_mfa",
            "custom_branding", "custom_emails", "custom_domain", "white_label",
            "audit_logs", "threat_detection", "ip_restrictions", "session_management", "data_residency",
            "webhooks", "custom_attributes", "roles_permissions", "admin_api", "user_subscriptions",
            "priority_support", "dedicated_support", "sla"
        };

        foreach (var key in featureKeys)
        {
            var result = await _featureGate.CheckFeatureAsync(key, cancellationToken: cancellationToken);
            features[key] = new FeatureStatusDto
            {
                Enabled = result.IsAllowed,
                DisplayName = GetFeatureDisplayName(key),
                Description = GetFeatureDescription(key),
                Category = GetFeatureCategory(key)
            };
        }

        return Ok(new TenantFeaturesResponse
        {
            TenantId = TenantId ?? "default",
            BillingEnabled = false, // No billing module installed by default
            Features = features,
            Limits = new Dictionary<string, LimitStatusDto>(), // No limits without billing
            IsTrialing = false
        });
    }

    /// <summary>
    /// Check if a specific feature is available
    /// </summary>
    [HttpGet("{featureKey}")]
    public async Task<ActionResult<FeatureCheckResponse>> CheckFeature(
        string featureKey,
        CancellationToken cancellationToken)
    {
        var result = await _featureGate.CheckFeatureAsync(featureKey, AdminUserId, cancellationToken);

        return Ok(new FeatureCheckResponse
        {
            Feature = featureKey,
            IsEnabled = result.IsAllowed,
            Reason = result.Message
        });
    }

    /// <summary>
    /// Check if the tenant is within a specific limit
    /// </summary>
    [HttpGet("limits/{limitType}")]
    public ActionResult<LimitCheckResponse> CheckLimit(
        string limitType,
        [FromQuery] int requestedAmount = 1)
    {
        // Without billing module, all limits are unrestricted
        return Ok(new LimitCheckResponse
        {
            LimitType = limitType,
            IsAllowed = true,
            IsUnlimited = true,
            Limit = 0,
            Current = 0,
            Remaining = int.MaxValue,
            Reason = "Limit checking requires billing module"
        });
    }

    /// <summary>
    /// Get all feature and limit definitions
    /// </summary>
    [HttpGet("definitions")]
    public ActionResult<FeatureDefinitionsResponse> GetDefinitions()
    {
        var definitions = new List<FeatureDefinition>
        {
            // Protocol features
            new() { Key = "saml", DisplayName = "SAML SSO", Description = "SAML 2.0 Identity Provider support", Category = "Protocol" },
            new() { Key = "fido2", DisplayName = "FIDO2/Passkeys", Description = "WebAuthn/FIDO2 passwordless authentication", Category = "Protocol" },
            new() { Key = "ldap", DisplayName = "LDAP Server", Description = "LDAP directory server integration", Category = "Protocol" },
            new() { Key = "scim", DisplayName = "SCIM Provisioning", Description = "Automated user provisioning with SCIM 2.0", Category = "Protocol" },

            // Authentication features
            new() { Key = "mfa", DisplayName = "Multi-Factor Authentication", Description = "Two-factor authentication", Category = "Authentication" },
            new() { Key = "social_login", DisplayName = "Social Login", Description = "Login with social providers", Category = "Authentication" },
            new() { Key = "enterprise_sso", DisplayName = "Enterprise SSO", Description = "Enterprise single sign-on", Category = "Authentication" },
            new() { Key = "passwordless", DisplayName = "Passwordless Authentication", Description = "Login without passwords", Category = "Authentication" },
            new() { Key = "adaptive_mfa", DisplayName = "Adaptive MFA", Description = "Risk-based MFA challenges", Category = "Authentication" },

            // Branding features
            new() { Key = "custom_branding", DisplayName = "Custom Branding", Description = "Customize login page appearance", Category = "Branding" },
            new() { Key = "custom_emails", DisplayName = "Custom Emails", Description = "Customize email templates", Category = "Branding" },
            new() { Key = "custom_domain", DisplayName = "Custom Domain", Description = "Use your own domain", Category = "Branding" },
            new() { Key = "white_label", DisplayName = "White Label", Description = "Remove all Oluso branding", Category = "Branding" },

            // Security features
            new() { Key = "audit_logs", DisplayName = "Audit Logs", Description = "Security and compliance audit logging", Category = "Security" },
            new() { Key = "threat_detection", DisplayName = "Threat Detection", Description = "Detect suspicious activities", Category = "Security" },
            new() { Key = "ip_restrictions", DisplayName = "IP Restrictions", Description = "Restrict access by IP address", Category = "Security" },
            new() { Key = "session_management", DisplayName = "Session Management", Description = "Advanced session controls", Category = "Security" },
            new() { Key = "data_residency", DisplayName = "Data Residency", Description = "Data location controls", Category = "Security" },

            // Integration features
            new() { Key = "webhooks", DisplayName = "Webhooks", Description = "Real-time event notifications", Category = "Integration" },
            new() { Key = "custom_attributes", DisplayName = "Custom Attributes", Description = "Custom user attributes", Category = "Integration" },
            new() { Key = "roles_permissions", DisplayName = "Roles & Permissions", Description = "Advanced access control", Category = "Integration" },
            new() { Key = "admin_api", DisplayName = "Admin API", Description = "Programmatic management API", Category = "Integration" },
            new() { Key = "user_subscriptions", DisplayName = "User Subscriptions", Description = "User-level billing", Category = "Integration" },

            // Support features
            new() { Key = "priority_support", DisplayName = "Priority Support", Description = "Faster support response", Category = "Support" },
            new() { Key = "dedicated_support", DisplayName = "Dedicated Support", Description = "Dedicated support engineer", Category = "Support" },
            new() { Key = "sla", DisplayName = "SLA", Description = "Service level agreement", Category = "Support" }
        };

        var limitDefinitions = new List<LimitDefinition>
        {
            new() { Key = "users", DisplayName = "Users", Description = "Maximum number of users" },
            new() { Key = "applications", DisplayName = "Applications", Description = "Maximum number of applications" },
            new() { Key = "connections", DisplayName = "Connections", Description = "Maximum number of identity connections" },
            new() { Key = "api_calls", DisplayName = "API Calls", Description = "API calls per month" },
            new() { Key = "mau", DisplayName = "Monthly Active Users", Description = "Monthly active users" }
        };

        return Ok(new FeatureDefinitionsResponse
        {
            Features = definitions,
            Limits = limitDefinitions
        });
    }

    private static string GetFeatureDisplayName(string key) => key switch
    {
        "saml" => "SAML SSO",
        "fido2" => "FIDO2/Passkeys",
        "ldap" => "LDAP Server",
        "scim" => "SCIM Provisioning",
        "mfa" => "Multi-Factor Authentication",
        "social_login" => "Social Login",
        "enterprise_sso" => "Enterprise SSO",
        "passwordless" => "Passwordless Authentication",
        "adaptive_mfa" => "Adaptive MFA",
        "custom_branding" => "Custom Branding",
        "custom_emails" => "Custom Emails",
        "custom_domain" => "Custom Domain",
        "white_label" => "White Label",
        "audit_logs" => "Audit Logs",
        "threat_detection" => "Threat Detection",
        "ip_restrictions" => "IP Restrictions",
        "session_management" => "Session Management",
        "data_residency" => "Data Residency",
        "webhooks" => "Webhooks",
        "custom_attributes" => "Custom Attributes",
        "roles_permissions" => "Roles & Permissions",
        "admin_api" => "Admin API",
        "user_subscriptions" => "User Subscriptions",
        "priority_support" => "Priority Support",
        "dedicated_support" => "Dedicated Support",
        "sla" => "SLA",
        _ => key
    };

    private static string GetFeatureDescription(string key) => key switch
    {
        "saml" => "SAML 2.0 Identity Provider support",
        "fido2" => "WebAuthn/FIDO2 passwordless authentication",
        "ldap" => "LDAP directory server integration",
        "scim" => "Automated user provisioning with SCIM 2.0",
        "mfa" => "Two-factor authentication",
        "audit_logs" => "Security and compliance audit logging",
        "webhooks" => "Real-time event notifications",
        _ => $"Enable {GetFeatureDisplayName(key)}"
    };

    private static string GetFeatureCategory(string key) => key switch
    {
        "saml" or "fido2" or "ldap" or "scim" => "Protocol",
        "mfa" or "social_login" or "enterprise_sso" or "passwordless" or "adaptive_mfa" => "Authentication",
        "custom_branding" or "custom_emails" or "custom_domain" or "white_label" => "Branding",
        "audit_logs" or "threat_detection" or "ip_restrictions" or "session_management" or "data_residency" => "Security",
        "webhooks" or "custom_attributes" or "roles_permissions" or "admin_api" or "user_subscriptions" => "Integration",
        "priority_support" or "dedicated_support" or "sla" => "Support",
        _ => "Other"
    };
}

#region DTOs

public class TenantFeaturesResponse
{
    public string TenantId { get; set; } = null!;
    public bool BillingEnabled { get; set; }
    public Dictionary<string, FeatureStatusDto> Features { get; set; } = new();
    public Dictionary<string, LimitStatusDto> Limits { get; set; } = new();
    public PlanSummaryDto? Plan { get; set; }
    public string? SubscriptionStatus { get; set; }
    public string? CurrentPeriodEnd { get; set; }
    public bool IsTrialing { get; set; }
    public string? TrialEnd { get; set; }
}

public class PlanSummaryDto
{
    public string Id { get; set; } = null!;
    public string Name { get; set; } = null!;
    public string? DisplayName { get; set; }
    public string? BillingInterval { get; set; }
}

public class FeatureStatusDto
{
    public bool Enabled { get; set; }
    public string? Value { get; set; }
    public string DisplayName { get; set; } = null!;
    public string Description { get; set; } = null!;
    public string Category { get; set; } = null!;
}

public class LimitStatusDto
{
    public int Limit { get; set; }
    public int Current { get; set; }
    public int Remaining { get; set; }
    public bool IsUnlimited { get; set; }
    public string DisplayName { get; set; } = null!;
    public double UsagePercentage { get; set; }
}

public class FeatureCheckResponse
{
    public string Feature { get; set; } = null!;
    public bool IsEnabled { get; set; }
    public string? Reason { get; set; }
    public string? Value { get; set; }
    public string? UpgradeUrl { get; set; }
}

public class LimitCheckResponse
{
    public string LimitType { get; set; } = null!;
    public bool IsAllowed { get; set; }
    public bool IsUnlimited { get; set; }
    public int Limit { get; set; }
    public int Current { get; set; }
    public int Remaining { get; set; }
    public string? Reason { get; set; }
    public bool HasOverageCharge { get; set; }
    public decimal? OveragePricePerUnit { get; set; }
    public string? UpgradeUrl { get; set; }
}

public class FeatureDefinitionsResponse
{
    public List<FeatureDefinition> Features { get; set; } = new();
    public List<LimitDefinition> Limits { get; set; } = new();
}

public class FeatureDefinition
{
    public string Key { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string Description { get; set; } = null!;
    public string Category { get; set; } = null!;
}

public class LimitDefinition
{
    public string Key { get; set; } = null!;
    public string DisplayName { get; set; } = null!;
    public string Description { get; set; } = null!;
    public string? Category { get; set; }
}

#endregion

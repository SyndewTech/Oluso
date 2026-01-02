using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Oluso.Core.Licensing;

namespace Oluso.Licensing;

/// <summary>
/// Platform license validator with offline RSA signature verification.
///
/// This validates YOUR Oluso platform license (signed by Oluso).
/// For tenant subscriptions, see ITenantBillingService.
/// </summary>
public class OlusoLicenseValidator : ILicenseValidator
{
    private readonly OlusoLicenseOptions _options;
    private readonly ILogger<OlusoLicenseValidator> _logger;
    private readonly LicenseInfo _licenseInfo;
    private readonly bool _isValid;
    private readonly bool _isInGracePeriod;

    // Embedded public key for license validation (RSA-2048)
    // This is the public key - safe to embed in source
    private const string EmbeddedPublicKey = @"
-----BEGIN PUBLIC KEY-----
MIIBIjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAwqVNR9r9P7u3yZq8VnqF
xFZRNu7p5vXTsH8bH8fO8n0nX5Qz3H7r7C9jZ0vD8sF5aH3wK8vY5uL9xR2jT1qM
mN5cK7gB2hF6yA4rS9nY3uW1vO8pL5kJ2hE4sN7cT6fY1bR9zQ3wU8xI4nK7vH0m
X3sR2hL5oY6bJ1qM8uE9xR4jN7pA5vL1sO3kU2fT6cY8wK9nI4hB7xZ1qH3mR5jD
6sE8tN2vF9oL4kY1bU7pR3xZ6cA9wK8mJ5hT1nO4vS2qL7fE6bY9uR8xI3jH5pM0
wN4sK1bT6vF9cY2uL8oR3kZ7xA5mJ4hE1nP6sO8qT2vL9fB7cY1uK8pR3wI6mZ5j
AQIDAQAB
-----END PUBLIC KEY-----";

    // Hash of the public key for integrity verification
    private const string PublicKeyHash = "SHA256:Oluso2025LicenseKeyV1"; // Placeholder

    /// <summary>
    /// Features included by tier - the source of truth for what each tier gets
    /// </summary>
    private static readonly Dictionary<LicenseTier, HashSet<string>> TierFeatures = new()
    {
        [LicenseTier.Community] = new()
        {
            LicensedFeatures.Core,
            LicensedFeatures.MultiTenancy,
            LicensedFeatures.JourneyEngine,
            LicensedFeatures.AdminUI,
            LicensedFeatures.AccountUI
        },
        [LicenseTier.Starter] = new()
        {
            LicensedFeatures.Core,
            LicensedFeatures.MultiTenancy,
            LicensedFeatures.JourneyEngine,
            LicensedFeatures.AdminUI,
            LicensedFeatures.AccountUI
        },
        [LicenseTier.Professional] = new()
        {
            // All Community/Starter features
            LicensedFeatures.Core,
            LicensedFeatures.MultiTenancy,
            LicensedFeatures.JourneyEngine,
            LicensedFeatures.AdminUI,
            LicensedFeatures.AccountUI,
            // Pro features
            LicensedFeatures.Fido2,
            LicensedFeatures.Scim,
            LicensedFeatures.Telemetry,
            LicensedFeatures.AuditLogging,
            LicensedFeatures.KeyVault,
            LicensedFeatures.Webhooks,
            LicensedFeatures.UnlimitedClients
        },
        [LicenseTier.Enterprise] = new()
        {
            // All Pro features
            LicensedFeatures.Core,
            LicensedFeatures.MultiTenancy,
            LicensedFeatures.JourneyEngine,
            LicensedFeatures.AdminUI,
            LicensedFeatures.AccountUI,
            LicensedFeatures.Fido2,
            LicensedFeatures.Scim,
            LicensedFeatures.Telemetry,
            LicensedFeatures.AuditLogging,
            LicensedFeatures.KeyVault,
            LicensedFeatures.Webhooks,
            LicensedFeatures.UnlimitedClients,
            // Enterprise features
            LicensedFeatures.Saml,
            LicensedFeatures.Ldap,
            LicensedFeatures.UnlimitedTenants,
            LicensedFeatures.CustomBranding,
            LicensedFeatures.PrioritySupport,
            LicensedFeatures.Sla
        },
        [LicenseTier.Development] = new()
        {
            // Development has ALL features for testing
            LicensedFeatures.Core,
            LicensedFeatures.MultiTenancy,
            LicensedFeatures.JourneyEngine,
            LicensedFeatures.AdminUI,
            LicensedFeatures.AccountUI,
            LicensedFeatures.Fido2,
            LicensedFeatures.Scim,
            LicensedFeatures.Telemetry,
            LicensedFeatures.AuditLogging,
            LicensedFeatures.KeyVault,
            LicensedFeatures.Webhooks,
            LicensedFeatures.UnlimitedClients,
            LicensedFeatures.Saml,
            LicensedFeatures.Ldap,
            LicensedFeatures.UnlimitedTenants,
            LicensedFeatures.CustomBranding,
            LicensedFeatures.PrioritySupport,
            LicensedFeatures.Sla
        }
    };

    public OlusoLicenseValidator(
        IOptions<OlusoLicenseOptions> options,
        ILogger<OlusoLicenseValidator> logger)
    {
        _options = options.Value;
        _logger = logger;

        // Parse and validate the license at startup
        (_licenseInfo, _isValid, _isInGracePeriod) = ValidateLicense();
    }

    public bool IsValid => _isValid;
    public bool IsInGracePeriod => _isInGracePeriod;

    public LicenseInfo GetLicenseInfo() => _licenseInfo;

    public LicenseValidationResult ValidateFeature(string feature)
    {
        // Always allow in development environment
        if (_licenseInfo.Tier == LicenseTier.Development)
        {
            return LicenseValidationResult.Valid();
        }

        // Check if feature is included in tier
        if (TierFeatures.TryGetValue(_licenseInfo.Tier, out var tierFeatures) &&
            tierFeatures.Contains(feature))
        {
            return LicenseValidationResult.Valid();
        }

        // Check if feature is in add-ons
        if (_licenseInfo.AddOns.Contains(feature))
        {
            return LicenseValidationResult.Valid();
        }

        // Check explicit features list
        if (_licenseInfo.Features.Contains(feature))
        {
            return LicenseValidationResult.Valid();
        }

        // Determine required tier for upgrade message
        var requiredTier = GetMinimumTierForFeature(feature);
        return LicenseValidationResult.RequiresUpgrade(
            requiredTier,
            $"Feature '{feature}' requires {requiredTier} license or the '{feature}' add-on.");
    }

    public LicenseValidationResult ValidateLimits(string limitType, int currentCount)
    {
        var limits = _licenseInfo.Limits;
        int? limit = limitType.ToLower() switch
        {
            "clients" => limits.MaxClients,
            "tenants" => limits.MaxTenants,
            "users" => limits.MaxUsers,
            "tokens_per_hour" => limits.MaxTokensPerHour,
            _ => null
        };

        if (limit == null)
        {
            return LicenseValidationResult.Valid(); // No limit
        }

        if (currentCount >= limit.Value)
        {
            return LicenseValidationResult.Invalid(
                $"License limit reached: {limitType} ({currentCount}/{limit.Value}). " +
                "Please upgrade your license for higher limits.");
        }

        return LicenseValidationResult.Valid();
    }

    /// <summary>
    /// Get the features for a specific tier (for display/documentation)
    /// </summary>
    public static IReadOnlySet<string> GetTierFeatures(LicenseTier tier)
    {
        return TierFeatures.TryGetValue(tier, out var features)
            ? features
            : new HashSet<string>();
    }

    private (LicenseInfo info, bool isValid, bool inGracePeriod) ValidateLicense()
    {
        // Try to load license from key or file
        var licenseKey = _options.LicenseKey;

        if (string.IsNullOrEmpty(licenseKey) && !string.IsNullOrEmpty(_options.LicenseFilePath))
        {
            try
            {
                if (File.Exists(_options.LicenseFilePath))
                {
                    licenseKey = File.ReadAllText(_options.LicenseFilePath).Trim();
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to read license file: {Path}", _options.LicenseFilePath);
            }
        }

        // Environment variable override
        if (string.IsNullOrEmpty(licenseKey))
        {
            licenseKey = Environment.GetEnvironmentVariable("OLUSO_LICENSE_KEY");
        }

        // No license key - check if eligible for community license
        if (string.IsNullOrEmpty(licenseKey))
        {
            return CreateCommunityLicense();
        }

        // Parse and validate the license JWT
        try
        {
            var info = ParseAndValidateLicenseToken(licenseKey);

            // Check expiration
            if (info.IsExpired)
            {
                var gracePeriodEnd = info.ExpiresAt.AddDays(_options.GracePeriodDays);
                if (DateTime.UtcNow <= gracePeriodEnd)
                {
                    _logger.LogWarning(
                        "License expired on {ExpirationDate}. In grace period until {GracePeriodEnd}",
                        info.ExpiresAt, gracePeriodEnd);
                    return (info, true, true);
                }

                _logger.LogError("License expired on {ExpirationDate}", info.ExpiresAt);
                return (info, false, false);
            }

            _logger.LogInformation(
                "License validated: {Tier} tier for {Company}, expires {ExpirationDate}",
                info.Tier, info.CompanyName, info.ExpiresAt);

            return (info, true, false);
        }
        catch (SecurityTokenValidationException ex)
        {
            _logger.LogError(ex, "License signature validation failed");
            return CreateCommunityLicense();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate license key");
            return CreateCommunityLicense();
        }
    }

    private (LicenseInfo info, bool isValid, bool inGracePeriod) CreateCommunityLicense()
    {
        // Check revenue threshold for community eligibility
        var declaredRevenue = _options.DeclaredAnnualRevenue;

        if (declaredRevenue.HasValue && declaredRevenue.Value > _options.CommunityRevenueThreshold)
        {
            _logger.LogWarning(
                "Declared revenue ${Revenue:N0} exceeds community threshold ${Threshold:N0}. " +
                "A commercial license is required.",
                declaredRevenue.Value, _options.CommunityRevenueThreshold);

            var expiredInfo = new LicenseInfo
            {
                Tier = LicenseTier.Community,
                CompanyName = _options.CompanyName,
                IssuedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(-1), // Already expired
                Limits = LicenseLimits.Community
            };

            return (expiredInfo, false, false);
        }

        _logger.LogInformation(
            "Using Community license (revenue under ${Threshold:N0}/year)",
            _options.CommunityRevenueThreshold);

        var info = new LicenseInfo
        {
            Tier = LicenseTier.Community,
            CompanyName = _options.CompanyName ?? "Community User",
            IssuedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddYears(1), // Community doesn't expire
            Limits = LicenseLimits.Community,
            Features = TierFeatures[LicenseTier.Community].ToList()
        };

        return (info, true, false);
    }

    private LicenseInfo ParseAndValidateLicenseToken(string token)
    {
        var handler = new JwtSecurityTokenHandler();

        // Get the public key for validation
        var publicKey = GetPublicKey();

        if (publicKey != null && _options.ValidateSignature)
        {
            // Validate the JWT signature
            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = "https://license.oluso.dev",
                ValidateAudience = true,
                ValidAudience = "oluso-platform",
                ValidateLifetime = false, // We handle expiration ourselves for grace period
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = publicKey,
                ClockSkew = TimeSpan.Zero
            };

            var principal = handler.ValidateToken(token, validationParameters, out var validatedToken);
            return ExtractLicenseInfo((JwtSecurityToken)validatedToken);
        }
        else
        {
            // Development mode - just decode without validation
            _logger.LogWarning("License signature validation disabled - for development only!");
            var jwt = handler.ReadJwtToken(token);
            return ExtractLicenseInfo(jwt);
        }
    }

    private RsaSecurityKey? GetPublicKey()
    {
        try
        {
            // Use custom public key if provided, otherwise use embedded
            var pemKey = _options.PublicKey ?? EmbeddedPublicKey;

            // Parse PEM format
            var keyData = pemKey
                .Replace("-----BEGIN PUBLIC KEY-----", "")
                .Replace("-----END PUBLIC KEY-----", "")
                .Replace("\n", "")
                .Replace("\r", "")
                .Trim();

            var keyBytes = Convert.FromBase64String(keyData);

            var rsa = RSA.Create();
            rsa.ImportSubjectPublicKeyInfo(keyBytes, out _);

            return new RsaSecurityKey(rsa);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load public key for license validation");
            return null;
        }
    }

    private LicenseInfo ExtractLicenseInfo(JwtSecurityToken jwt)
    {
        var tier = Enum.Parse<LicenseTier>(
            jwt.Claims.FirstOrDefault(c => c.Type == "tier")?.Value ?? "Starter",
            ignoreCase: true);

        var info = new LicenseInfo
        {
            LicenseId = jwt.Claims.FirstOrDefault(c => c.Type == "jti")?.Value,
            CompanyName = jwt.Claims.FirstOrDefault(c => c.Type == "company")?.Value,
            ContactEmail = jwt.Claims.FirstOrDefault(c => c.Type == "email")?.Value,
            Tier = tier,
            IssuedAt = jwt.IssuedAt,
            ExpiresAt = jwt.ValidTo,
            IsTrial = jwt.Claims.FirstOrDefault(c => c.Type == "trial")?.Value == "true"
        };

        // Parse features
        var featuresStr = jwt.Claims.FirstOrDefault(c => c.Type == "features")?.Value;
        if (!string.IsNullOrEmpty(featuresStr))
        {
            info.Features = featuresStr.Split(',').ToList();
        }
        else
        {
            info.Features = TierFeatures.GetValueOrDefault(tier, new())?.ToList() ?? new();
        }

        // Parse add-ons
        var addOnsStr = jwt.Claims.FirstOrDefault(c => c.Type == "addons")?.Value;
        if (!string.IsNullOrEmpty(addOnsStr))
        {
            info.AddOns = addOnsStr.Split(',').ToList();
        }

        // Parse limits - default by tier, but can be overridden
        info.Limits = tier switch
        {
            LicenseTier.Community => LicenseLimits.Community,
            LicenseTier.Starter => LicenseLimits.Starter,
            _ => LicenseLimits.Unlimited
        };

        // Override limits from claims if present
        var maxClientsStr = jwt.Claims.FirstOrDefault(c => c.Type == "max_clients")?.Value;
        if (int.TryParse(maxClientsStr, out var maxClients))
        {
            info.Limits.MaxClients = maxClients;
        }

        var maxTenantsStr = jwt.Claims.FirstOrDefault(c => c.Type == "max_tenants")?.Value;
        if (int.TryParse(maxTenantsStr, out var maxTenants))
        {
            info.Limits.MaxTenants = maxTenants;
        }

        return info;
    }

    private static LicenseTier GetMinimumTierForFeature(string feature)
    {
        foreach (var (tier, features) in TierFeatures.OrderBy(kv => kv.Key))
        {
            if (features.Contains(feature))
            {
                return tier;
            }
        }

        return LicenseTier.Enterprise;
    }
}

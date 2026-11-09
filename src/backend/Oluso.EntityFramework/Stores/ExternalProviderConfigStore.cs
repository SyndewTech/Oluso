using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;

namespace Oluso.EntityFramework.Stores;

/// <summary>
/// Entity Framework implementation of IExternalProviderConfigStore.
/// Bridges IdentityProvider entities to the dynamic authentication system.
/// </summary>
public class ExternalProviderConfigStore : IExternalProviderConfigStore
{
    private readonly IOlusoDbContext _context;
    private readonly ITenantContext _tenantContext;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public ExternalProviderConfigStore(IOlusoDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    public async Task<IReadOnlyList<ExternalProviderDefinition>> GetEnabledProvidersAsync(
        bool includeTenantOverrides = true,
        CancellationToken cancellationToken = default)
    {
        var providers = await GetProvidersQuery(includeTenantOverrides)
            .Where(p => p.Enabled)
            .OrderBy(p => p.DisplayOrder)
            .ToListAsync(cancellationToken);

        // When tenant overrides are allowed, deduplicate by scheme so a
        // tenant-specific row shadows the global row with the same name.
        if (includeTenantOverrides)
        {
            providers = providers
                .GroupBy(p => p.Scheme, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.OrderBy(p => p.TenantId == null ? 1 : 0).First())
                .OrderBy(p => p.DisplayOrder)
                .ToList();
        }

        return providers.Select(MapToDefinition).ToList();
    }

    public async Task<ExternalProviderDefinition?> GetBySchemeAsync(
        string scheme,
        bool includeTenantOverrides = true,
        CancellationToken cancellationToken = default)
    {
        // Order tenant-specific rows first so FirstOrDefault picks them over
        // the global row when both exist for the same scheme.
        var provider = await GetProvidersQuery(includeTenantOverrides)
            .Where(p => p.Scheme == scheme)
            .OrderBy(p => p.TenantId == null ? 1 : 0)
            .FirstOrDefaultAsync(cancellationToken);

        return provider != null ? MapToDefinition(provider) : null;
    }

    public async Task<ExternalProviderDefinition?> GetByIdAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        if (!int.TryParse(id, out var intId))
            return null;

        var provider = await GetProvidersQuery(includeTenantOverrides: true)
            .FirstOrDefaultAsync(p => p.Id == intId, cancellationToken);

        return provider != null ? MapToDefinition(provider) : null;
    }

    private IQueryable<IdentityProvider> GetProvidersQuery(bool includeTenantOverrides)
    {
        if (!includeTenantOverrides)
        {
            return _context.IdentityProviders.Where(p => p.TenantId == null);
        }

        return _context.IdentityProviders
            .Where(p => p.TenantId == _tenantContext.TenantId || p.TenantId == null);
    }

    private static ExternalProviderDefinition MapToDefinition(IdentityProvider provider)
    {
        var config = ParseConfiguration(provider.Properties);
        var rawJson = ParseRawJson(provider.Properties);
        var isDirectLogin = IsDirectLoginProvider(provider.ProviderType);

        // Allow custom directLoginPath in Properties to override the default
        var directLoginPath = isDirectLogin
            ? config.GetValueOrDefault("directLoginPath", GetDirectLoginPath(provider.ProviderType) ?? "")
            : null;

        return new ExternalProviderDefinition
        {
            Id = provider.Id.ToString(),
            TenantId = provider.TenantId,
            Scheme = provider.Scheme,
            DisplayName = provider.DisplayName,
            IconUrl = provider.IconUrl,
            ProviderType = MapProviderType(provider.ProviderType),
            Enabled = provider.Enabled,
            IsDirectLogin = isDirectLogin,
            DirectLoginPath = !string.IsNullOrEmpty(directLoginPath) ? directLoginPath : null,
            ClientId = config.GetValueOrDefault("clientId", ""),
            ClientSecret = config.GetValueOrDefault("clientSecret", ""),
            AuthorizationEndpoint = config.GetValueOrDefault("authorizationEndpoint"),
            TokenEndpoint = config.GetValueOrDefault("tokenEndpoint"),
            UserInfoEndpoint = config.GetValueOrDefault("userInfoEndpoint"),
            Authority = config.GetValueOrDefault("authority"),
            MetadataAddress = config.GetValueOrDefault("metadataUrl"),
            Scopes = ParseScopes(config),
            AppleTeamId = config.GetValueOrDefault("teamId"),
            AppleKeyId = config.GetValueOrDefault("keyId"),
            ApplePrivateKey = config.GetValueOrDefault("privateKey"),
            ProxyMode = config.GetBoolValue("proxyMode"),
            StoreUserLocally = config.GetBoolValue("storeUserLocally", true),
            CacheExternalTokens = config.GetBoolValue("cacheExternalTokens"),
            TokenCacheDurationSeconds = config.GetIntValue("tokenCacheDurationSeconds", 3600),
            ProxyIncludeClaims = ParseStringArray(config.GetValueOrDefault("proxyIncludeClaims")),
            ProxyExcludeClaims = ParseStringArray(config.GetValueOrDefault("proxyExcludeClaims")),
            IncludeExternalAccessToken = config.GetBoolValue("includeExternalAccessToken"),
            IncludeExternalIdToken = config.GetBoolValue("includeExternalIdToken"),
            AutoProvisionUsers = config.GetBoolValue("autoProvisionUsers", true),
            DefaultRoles = ParseDefaultRoles(rawJson),
            AccessControl = ParseAccessControl(rawJson),
            Properties = config
        };
    }

    /// <summary>
    /// Parse Properties JSON to a <see cref="JsonElement"/> so structured
    /// fields (defaultRoles array, accessControl object) can be read with
    /// proper typing rather than via the flat string-string config dict.
    /// </summary>
    private static JsonElement? ParseRawJson(string? json)
    {
        if (string.IsNullOrEmpty(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }
        catch
        {
            return null;
        }
    }

    private static IReadOnlyList<string> ParseDefaultRoles(JsonElement? root)
    {
        if (root is not { ValueKind: JsonValueKind.Object } element) return Array.Empty<string>();
        if (!element.TryGetProperty("defaultRoles", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return Array.Empty<string>();

        var roles = new List<string>();
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var v = item.GetString();
                if (!string.IsNullOrWhiteSpace(v)) roles.Add(v);
            }
        }
        return roles;
    }

    private static ExternalAccessControl? ParseAccessControl(JsonElement? root)
    {
        if (root is not { ValueKind: JsonValueKind.Object } element) return null;
        if (!element.TryGetProperty("accessControl", out var ac) || ac.ValueKind != JsonValueKind.Object)
            return null;

        var mode = ac.TryGetProperty("mode", out var m) && m.ValueKind == JsonValueKind.String
            ? m.GetString() ?? "open"
            : "open";

        var allowedEmails = Array.Empty<string>() as IReadOnlyList<string>;
        if (ac.TryGetProperty("allowedEmails", out var emails) && emails.ValueKind == JsonValueKind.Array)
        {
            var list = new List<string>();
            foreach (var e in emails.EnumerateArray())
            {
                if (e.ValueKind == JsonValueKind.String)
                {
                    var v = e.GetString();
                    if (!string.IsNullOrWhiteSpace(v)) list.Add(v);
                }
            }
            allowedEmails = list;
        }

        ClaimGate? claimGate = null;
        if (ac.TryGetProperty("claimGate", out var cg) && cg.ValueKind == JsonValueKind.Object)
        {
            var path = cg.TryGetProperty("claimPath", out var cp) && cp.ValueKind == JsonValueKind.String
                ? cp.GetString() ?? ""
                : "";

            var anyOf = Array.Empty<string>() as IReadOnlyList<string>;
            if (cg.TryGetProperty("anyOf", out var any) && any.ValueKind == JsonValueKind.Array)
            {
                var list = new List<string>();
                foreach (var v in any.EnumerateArray())
                {
                    if (v.ValueKind == JsonValueKind.String)
                    {
                        var s = v.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) list.Add(s);
                    }
                }
                anyOf = list;
            }

            claimGate = new ClaimGate { ClaimPath = path, AnyOf = anyOf };
        }

        return new ExternalAccessControl
        {
            Mode = mode,
            AllowedEmails = allowedEmails,
            ClaimGate = claimGate
        };
    }

    private static Dictionary<string, string> ParseConfiguration(string? json)
    {
        if (string.IsNullOrEmpty(json))
            return new Dictionary<string, string>();

        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, object>>(json, JsonOptions);
            if (dict == null)
                return new Dictionary<string, string>();

            return dict.ToDictionary(
                kvp => kvp.Key,
                kvp => kvp.Value?.ToString() ?? "");
        }
        catch
        {
            return new Dictionary<string, string>();
        }
    }

    private static IReadOnlyList<string> ParseScopes(Dictionary<string, string> config)
    {
        var scopesValue = config.GetValueOrDefault("scopes");
        if (string.IsNullOrEmpty(scopesValue))
            return Array.Empty<string>();

        // Try parsing as JSON array
        if (scopesValue.StartsWith("["))
        {
            try
            {
                return JsonSerializer.Deserialize<List<string>>(scopesValue, JsonOptions) ?? new List<string>();
            }
            catch
            {
                // Fall through to space-separated parsing
            }
        }

        // Parse as space-separated string
        return scopesValue.Split(' ', StringSplitOptions.RemoveEmptyEntries);
    }

    private static IReadOnlyList<string> ParseStringArray(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return Array.Empty<string>();

        if (value.StartsWith("["))
        {
            try
            {
                return JsonSerializer.Deserialize<List<string>>(value, JsonOptions) ?? new List<string>();
            }
            catch
            {
                // Fall through
            }
        }

        return value.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .ToArray();
    }

    private static string MapProviderType(ExternalProviderType type)
    {
        return type switch
        {
            ExternalProviderType.Google => "google",
            ExternalProviderType.Microsoft => "microsoft",
            ExternalProviderType.Facebook => "facebook",
            ExternalProviderType.Apple => "apple",
            ExternalProviderType.GitHub => "github",
            ExternalProviderType.LinkedIn => "linkedin",
            ExternalProviderType.Twitter => "twitter",
            ExternalProviderType.Oidc => "oidc",
            ExternalProviderType.OAuth2 => "oauth",
            ExternalProviderType.Saml2 => "Saml2", // Must match the check in Login.cshtml.cs
            ExternalProviderType.Ldap => "ldap",
            _ => type.ToString().ToLowerInvariant()
        };
    }

    /// <summary>
    /// Determines if a provider type uses direct login (credentials entered on our login page)
    /// rather than redirect-based authentication (OAuth/SAML).
    /// </summary>
    private static bool IsDirectLoginProvider(ExternalProviderType type)
    {
        return type switch
        {
            ExternalProviderType.Ldap => true,
            // Future: ExternalProviderType.Radius => true,
            _ => false
        };
    }

    /// <summary>
    /// Gets the login page path for direct login providers.
    /// All direct login providers use the unified /account/direct-login page.
    /// </summary>
    private static string? GetDirectLoginPath(ExternalProviderType type)
    {
        // All direct login providers use the same unified page
        // The scheme is passed as a query parameter
        return IsDirectLoginProvider(type) ? "/account/direct-login" : null;
    }
}

internal static class DictionaryExtensions
{
    public static string GetValueOrDefault(this Dictionary<string, string> dict, string key, string defaultValue = "")
    {
        return dict.TryGetValue(key, out var value) ? value : defaultValue;
    }

    public static bool GetBoolValue(this Dictionary<string, string> dict, string key, bool defaultValue = false)
    {
        if (!dict.TryGetValue(key, out var value))
            return defaultValue;

        if (bool.TryParse(value, out var boolValue))
            return boolValue;

        return value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
               value.Equals("1", StringComparison.Ordinal);
    }

    public static int GetIntValue(this Dictionary<string, string> dict, string key, int defaultValue = 0)
    {
        if (!dict.TryGetValue(key, out var value))
            return defaultValue;

        return int.TryParse(value, out var intValue) ? intValue : defaultValue;
    }
}

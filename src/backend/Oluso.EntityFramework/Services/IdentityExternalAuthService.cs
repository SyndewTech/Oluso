using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;

// Alias to avoid conflict with Microsoft.AspNetCore.Identity.ExternalLoginInfo
using OlusoExternalLoginInfo = Oluso.Core.Services.ExternalLoginInfo;

namespace Oluso.EntityFramework.Services;

/// <summary>
/// Default IExternalAuthService implementation using ASP.NET Core Identity.
/// Supports proxy mode, token caching, and multi-tenancy.
/// </summary>
public class IdentityExternalAuthService : IExternalAuthService
{
    private readonly SignInManager<OlusoUser> _signInManager;
    private readonly UserManager<OlusoUser> _userManager;
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ITenantContext _tenantContext;
    private readonly IExternalProviderStore? _providerStore;
    private readonly IExternalProviderConfigStore? _providerConfigStore;
    private readonly IClientStore? _clientStore;
    private readonly IDistributedCache? _tokenCache;
    private readonly ILogger<IdentityExternalAuthService> _logger;

    public IdentityExternalAuthService(
        SignInManager<OlusoUser> signInManager,
        UserManager<OlusoUser> userManager,
        IHttpContextAccessor httpContextAccessor,
        ITenantContext tenantContext,
        ILogger<IdentityExternalAuthService> logger,
        IExternalProviderStore? providerStore = null,
        IExternalProviderConfigStore? providerConfigStore = null,
        IClientStore? clientStore = null,
        IDistributedCache? tokenCache = null)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _httpContextAccessor = httpContextAccessor;
        _tenantContext = tenantContext;
        _providerStore = providerStore;
        _providerConfigStore = providerConfigStore;
        _clientStore = clientStore;
        _tokenCache = tokenCache;
        _logger = logger;
    }

    private HttpContext HttpContext =>
        _httpContextAccessor.HttpContext ?? throw new InvalidOperationException("No HttpContext available");

    public async Task<IReadOnlyList<ExternalProviderInfo>> GetAvailableProvidersAsync(CancellationToken cancellationToken = default)
    {
        var schemes = await _signInManager.GetExternalAuthenticationSchemesAsync();
        var result = new List<ExternalProviderInfo>();

        foreach (var scheme in schemes)
        {
            // Get additional info from provider store if available
            var providerConfig = await GetProviderConfigAsync(scheme.Name, cancellationToken);

            result.Add(new ExternalProviderInfo
            {
                Scheme = scheme.Name,
                DisplayName = scheme.DisplayName ?? scheme.Name,
                ProviderType = providerConfig?.ProviderType,
                Enabled = providerConfig?.StoreUserLocally != false || providerConfig?.ProxyMode == true
            });
        }

        // Also get database-configured providers
        if (_providerStore != null)
        {
            var dbProviders = await _providerStore.GetEnabledProvidersAsync(cancellationToken);
            foreach (var provider in dbProviders)
            {
                if (!result.Any(r => r.Scheme.Equals(provider.Scheme, StringComparison.OrdinalIgnoreCase)))
                {
                    result.Add(new ExternalProviderInfo
                    {
                        Scheme = provider.Scheme,
                        DisplayName = provider.DisplayName ?? provider.Scheme,
                        IconUrl = provider.IconUrl,
                        ProviderType = provider.ProviderType,
                        Enabled = true
                    });
                }
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<ExternalProviderInfo>> GetAvailableProvidersAsync(string clientId, CancellationToken cancellationToken = default)
    {
        // First get all available providers
        var allProviders = await GetAvailableProvidersAsync(cancellationToken);

        // If no client ID provided or client store not available, return all
        if (string.IsNullOrEmpty(clientId) || _clientStore == null)
        {
            return allProviders;
        }

        // Load client and check for IdP restrictions
        var client = await _clientStore.FindClientByIdAsync(clientId, cancellationToken);
        if (client == null)
        {
            _logger.LogWarning("Client {ClientId} not found when filtering IdP restrictions", clientId);
            return allProviders;
        }

        // If no restrictions configured, return all providers
        var restrictions = client.IdentityProviderRestrictions?.Select(r => r.Provider).ToList();
        if (restrictions == null || restrictions.Count == 0)
        {
            return allProviders;
        }

        // Filter providers by the restriction list
        var allowedProviders = allProviders
            .Where(p => restrictions.Any(r =>
                r.Equals(p.Scheme, StringComparison.OrdinalIgnoreCase) ||
                r.Equals(p.DisplayName, StringComparison.OrdinalIgnoreCase)))
            .ToList();

        _logger.LogDebug(
            "Client {ClientId} has IdP restrictions [{Restrictions}], filtered to [{Allowed}] providers",
            clientId,
            string.Join(", ", restrictions),
            string.Join(", ", allowedProviders.Select(p => p.Scheme)));

        return allowedProviders;
    }

    public async Task<ExternalProviderConfig?> GetProviderConfigAsync(string provider, CancellationToken cancellationToken = default)
    {
        if (_providerStore == null)
        {
            // Return default config for scheme-based providers
            return new ExternalProviderConfig
            {
                Scheme = provider,
                ProxyMode = false,
                StoreUserLocally = true,
                AutoProvisionUsers = true
            };
        }

        var dbProvider = await _providerStore.GetBySchemeAsync(provider, cancellationToken);
        if (dbProvider == null)
        {
            return null;
        }

        return new ExternalProviderConfig
        {
            Scheme = dbProvider.Scheme,
            DisplayName = dbProvider.DisplayName,
            ProviderType = dbProvider.ProviderType,
            ProxyMode = dbProvider.ProxyMode,
            StoreUserLocally = dbProvider.StoreUserLocally,
            CacheExternalTokens = dbProvider.CacheExternalTokens,
            TokenCacheDurationSeconds = dbProvider.TokenCacheDurationSeconds,
            ProxyIncludeClaims = dbProvider.ProxyIncludeClaims ?? Array.Empty<string>(),
            ProxyExcludeClaims = dbProvider.ProxyExcludeClaims ?? Array.Empty<string>(),
            IncludeExternalAccessToken = dbProvider.IncludeExternalAccessToken,
            IncludeExternalIdToken = dbProvider.IncludeExternalIdToken,
            AutoProvisionUsers = dbProvider.AutoProvisionUsers
        };
    }

    public async Task<ExternalChallengeResult> ChallengeAsync(
        string provider,
        string returnUrl,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Check if this is a SAML provider - SAML uses its own login endpoint instead of ASP.NET authentication handlers
            if (_providerConfigStore != null)
            {
                var providerConfig = await _providerConfigStore.GetBySchemeAsync(provider, cancellationToken);
                if (providerConfig?.ProviderType?.Equals("Saml2", StringComparison.OrdinalIgnoreCase) == true)
                {
                    // SAML providers use a custom endpoint - redirect to /saml/login/{scheme}
                    // The SAML endpoint will handle state management and redirect to the IdP
                    var samlLoginUrl = $"/saml/login/{Uri.EscapeDataString(provider)}?returnUrl={Uri.EscapeDataString(returnUrl)}";
                    _logger.LogInformation("Initiating SAML login for provider {Provider} via {Url}", provider, samlLoginUrl);
                    return ExternalChallengeResult.Success(samlLoginUrl);
                }
            }

            var properties = _signInManager.ConfigureExternalAuthenticationProperties(provider, returnUrl);

            // Store tenant context in auth properties for callback
            if (_tenantContext.HasTenant)
            {
                properties.Items["TenantId"] = _tenantContext.TenantId;
            }

            await HttpContext.ChallengeAsync(provider, properties);

            _logger.LogInformation("Initiated external login challenge for provider {Provider}", provider);

            return ExternalChallengeResult.Success(properties.RedirectUri ?? returnUrl);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initiate external login for provider {Provider}", provider);
            return ExternalChallengeResult.Failed($"Failed to initiate login: {ex.Message}");
        }
    }

    public async Task<ExternalLoginResult?> GetExternalLoginResultAsync(CancellationToken cancellationToken = default)
    {
        var authResult = await HttpContext.AuthenticateAsync(IdentityConstants.ExternalScheme);
        if (!authResult.Succeeded || authResult.Principal == null)
        {
            return null;
        }

        var loginInfo = await _signInManager.GetExternalLoginInfoAsync();
        if (loginInfo == null)
        {
            _logger.LogWarning("External login info not found after successful authentication");
            return ExternalLoginResult.Failed("Could not retrieve external login information");
        }

        var provider = loginInfo.LoginProvider;
        var providerKey = loginInfo.ProviderKey;

        // Get provider configuration
        var providerConfig = await GetProviderConfigAsync(provider, cancellationToken);

        // Extract claims
        var email = loginInfo.Principal.FindFirstValue(ClaimTypes.Email);
        var name = loginInfo.Principal.FindFirstValue(ClaimTypes.Name);
        var firstName = loginInfo.Principal.FindFirstValue(ClaimTypes.GivenName);
        var lastName = loginInfo.Principal.FindFirstValue(ClaimTypes.Surname);
        var picture = loginInfo.Principal.FindFirstValue("picture") ??
                      loginInfo.Principal.FindFirstValue("urn:google:picture");

        // Extract all claims
        var claims = loginInfo.Principal.Claims
            .GroupBy(c => c.Type)
            .ToDictionary(g => g.Key, g => g.First().Value);

        // Extract tokens
        var accessToken = authResult.Properties?.GetTokenValue("access_token");
        var idToken = authResult.Properties?.GetTokenValue("id_token");
        var refreshToken = authResult.Properties?.GetTokenValue("refresh_token");
        var expiresAt = authResult.Properties?.GetTokenValue("expires_at");

        DateTime? tokenExpiresAt = null;
        if (!string.IsNullOrEmpty(expiresAt) && DateTime.TryParse(expiresAt, out var expiry))
        {
            tokenExpiresAt = expiry;
        }

        _logger.LogInformation("External login result received from provider {Provider} for key {ProviderKey}",
            provider, providerKey);

        return new ExternalLoginResult
        {
            Succeeded = true,
            Provider = provider,
            ProviderKey = providerKey,
            Email = email,
            Name = name,
            FirstName = firstName,
            LastName = lastName,
            ProfilePictureUrl = picture,
            Claims = claims,
            AccessToken = accessToken,
            IdToken = idToken,
            RefreshToken = refreshToken,
            TokenExpiresAt = tokenExpiresAt,
            ProviderConfig = providerConfig
        };
    }

    public async Task SignOutExternalAsync(CancellationToken cancellationToken = default)
    {
        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
    }

    public async Task<IList<OlusoExternalLoginInfo>> GetUserLoginsAsync(string userId, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return Array.Empty<OlusoExternalLoginInfo>();
        }

        var logins = await _userManager.GetLoginsAsync(user);
        return logins.Select(l => new OlusoExternalLoginInfo
        {
            Provider = l.LoginProvider,
            ProviderKey = l.ProviderKey,
            DisplayName = l.ProviderDisplayName
        }).ToList();
    }

    public async Task<string?> FindUserByLoginAsync(string provider, string providerKey, CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByLoginAsync(provider, providerKey);

        if (user == null)
        {
            return null;
        }

        // Verify tenant match
        if (_tenantContext.HasTenant && user.TenantId != _tenantContext.TenantId && user.TenantId != null)
        {
            return null;
        }

        return user.Id;
    }

    public async Task<ExternalLoginOperationResult> LinkLoginAsync(
        string userId,
        string provider,
        string providerKey,
        string? displayName,
        CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return ExternalLoginOperationResult.Failed("User not found");
        }

        var loginInfo = new UserLoginInfo(provider, providerKey, displayName ?? provider);
        var result = await _userManager.AddLoginAsync(user, loginInfo);

        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            _logger.LogWarning("Failed to link login for user {UserId}: {Errors}", userId, errors);
            return ExternalLoginOperationResult.Failed(errors);
        }

        _logger.LogInformation("Linked provider {Provider} to user {UserId}", provider, userId);
        return ExternalLoginOperationResult.Success();
    }

    public async Task<ExternalLoginOperationResult> UnlinkLoginAsync(
        string userId,
        string provider,
        string providerKey,
        CancellationToken cancellationToken = default)
    {
        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            return ExternalLoginOperationResult.Failed("User not found");
        }

        // Ensure user has another login method before unlinking
        var hasPassword = await _userManager.HasPasswordAsync(user);
        var logins = await _userManager.GetLoginsAsync(user);

        if (!hasPassword && logins.Count <= 1)
        {
            return ExternalLoginOperationResult.Failed("Cannot remove the only login method. Please set a password first.");
        }

        var result = await _userManager.RemoveLoginAsync(user, provider, providerKey);

        if (!result.Succeeded)
        {
            var errors = string.Join(", ", result.Errors.Select(e => e.Description));
            _logger.LogWarning("Failed to unlink login for user {UserId}: {Errors}", userId, errors);
            return ExternalLoginOperationResult.Failed(errors);
        }

        _logger.LogInformation("Unlinked provider {Provider} from user {UserId}", provider, userId);
        return ExternalLoginOperationResult.Success();
    }

    public async Task CacheExternalTokensAsync(
        string sessionKey,
        ExternalTokenData tokens,
        TimeSpan? expiry = null,
        CancellationToken cancellationToken = default)
    {
        if (_tokenCache == null)
        {
            _logger.LogDebug("Token cache not available, skipping token caching");
            return;
        }

        var cacheKey = GetTokenCacheKey(sessionKey);
        var json = System.Text.Json.JsonSerializer.Serialize(tokens);

        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = expiry ?? TimeSpan.FromHours(1)
        };

        await _tokenCache.SetStringAsync(cacheKey, json, options, cancellationToken);
        _logger.LogDebug("Cached external tokens for session {SessionKey}", sessionKey);
    }

    public async Task<ExternalTokenData?> GetCachedTokensAsync(string sessionKey, CancellationToken cancellationToken = default)
    {
        if (_tokenCache == null)
        {
            return null;
        }

        var cacheKey = GetTokenCacheKey(sessionKey);
        var json = await _tokenCache.GetStringAsync(cacheKey, cancellationToken);

        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        return System.Text.Json.JsonSerializer.Deserialize<ExternalTokenData>(json);
    }

    private string GetTokenCacheKey(string sessionKey)
    {
        var tenantPrefix = _tenantContext.TenantId ?? "global";
        return $"external_tokens:{tenantPrefix}:{sessionKey}";
    }
}

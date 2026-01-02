using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OAuth;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Oluso.Core.Services;
using Oluso.EntityFramework.Services;
using Oluso.ExternalAuth;

namespace Oluso;

/// <summary>
/// Extension methods for configuring external authentication providers.
/// All providers are configured with tenant-aware settings when multi-tenancy is enabled.
/// </summary>
public static class ExternalAuthenticationExtensions
{
    /// <summary>
    /// Adds external authentication services including IExternalAuthService.
    /// This registers the base infrastructure for external authentication.
    /// </summary>
    public static OlusoBuilder AddExternalAuthentication(
        this OlusoBuilder builder,
        Action<ExternalAuthenticationOptions>? configure = null)
    {
        var options = new ExternalAuthenticationOptions();
        configure?.Invoke(options);

        // Register IExternalAuthService
        builder.Services.AddScoped<IExternalAuthService, IdentityExternalAuthService>();

        return builder;
    }

    /// <summary>
    /// Enables dynamic external providers that are configured per-tenant in the database.
    /// This allows each tenant to have their own Google, GitHub, etc. credentials.
    /// </summary>
    /// <remarks>
    /// When enabled, providers can be configured in the database with their credentials,
    /// and Oluso will dynamically resolve them based on the current tenant context.
    ///
    /// Static providers (configured via AddGoogle, AddGitHub, etc.) take precedence
    /// and are used as fallbacks when no tenant-specific configuration exists.
    /// </remarks>
    public static OlusoBuilder AddDynamicExternalProviders(
        this OlusoBuilder builder,
        Action<DynamicExternalProviderOptions>? configure = null)
    {
        var options = new DynamicExternalProviderOptions();
        configure?.Invoke(options);

        // IHttpContextAccessor is needed for DynamicOAuthPostConfigureOptions to resolve scoped services
        builder.Services.AddHttpContextAccessor();

        // Register caches as singletons
        builder.Services.TryAddSingleton<DynamicSchemeCache>();
        builder.Services.TryAddSingleton<DynamicOptionsCache>();

        // Replace the default authentication scheme provider with our dynamic one
        // Must be singleton because ASP.NET Core resolves it during startup from root provider
        builder.Services.AddSingleton<IAuthenticationSchemeProvider, DynamicAuthenticationSchemeProvider>();

        // Register post-configure options for all supported provider types
        builder.Services.TryAddSingleton<IPostConfigureOptions<Microsoft.AspNetCore.Authentication.Google.GoogleOptions>, DynamicOAuthPostConfigureOptions>();
        builder.Services.TryAddSingleton<IPostConfigureOptions<Microsoft.AspNetCore.Authentication.MicrosoftAccount.MicrosoftAccountOptions>, DynamicOAuthPostConfigureOptions>();
        builder.Services.TryAddSingleton<IPostConfigureOptions<Microsoft.AspNetCore.Authentication.Facebook.FacebookOptions>, DynamicOAuthPostConfigureOptions>();
        builder.Services.TryAddSingleton<IPostConfigureOptions<Microsoft.AspNetCore.Authentication.Twitter.TwitterOptions>, DynamicOAuthPostConfigureOptions>();
        builder.Services.TryAddSingleton<IPostConfigureOptions<AspNet.Security.OAuth.GitHub.GitHubAuthenticationOptions>, DynamicOAuthPostConfigureOptions>();
        builder.Services.TryAddSingleton<IPostConfigureOptions<AspNet.Security.OAuth.LinkedIn.LinkedInAuthenticationOptions>, DynamicOAuthPostConfigureOptions>();
        builder.Services.TryAddSingleton<IPostConfigureOptions<AspNet.Security.OAuth.Apple.AppleAuthenticationOptions>, DynamicOAuthPostConfigureOptions>();
        builder.Services.TryAddSingleton<IPostConfigureOptions<OpenIdConnectOptions>, DynamicOAuthPostConfigureOptions>();
        builder.Services.TryAddSingleton<IPostConfigureOptions<OAuthOptions>, DynamicOAuthPostConfigureOptions>();

        // Register IExternalAuthService if not already registered
        builder.Services.TryAddScoped<IExternalAuthService, IdentityExternalAuthService>();

        // Register placeholder handlers for dynamic schemes
        // These are registered with placeholder credentials to pass startup validation.
        // Real credentials are loaded from database via PostConfigure at runtime.
        if (options.RegisterPlaceholderSchemes)
        {
            const string placeholder = "__dynamic_placeholder__";
            builder.Services.AddAuthentication()
                .AddGoogle("Google", opt => { opt.ClientId = placeholder; opt.ClientSecret = placeholder; })
                .AddMicrosoftAccount("Microsoft", opt => { opt.ClientId = placeholder; opt.ClientSecret = placeholder; })
                .AddFacebook("Facebook", opt => { opt.AppId = placeholder; opt.AppSecret = placeholder; })
                .AddTwitter("Twitter", opt => { opt.ConsumerKey = placeholder; opt.ConsumerSecret = placeholder; })
                .AddGitHub("GitHub", opt => { opt.ClientId = placeholder; opt.ClientSecret = placeholder; })
                .AddLinkedIn("LinkedIn", opt => { opt.ClientId = placeholder; opt.ClientSecret = placeholder; });
                // .AddApple("Apple", opt =>
                // {
                //     opt.ClientId = placeholder;
                //     opt.ClientSecret = placeholder;
                //     opt.TeamId = placeholder;
                //     opt.KeyId = placeholder;
                //     // Apple requires a valid URL for MetadataEndpoint - use the real Apple endpoint
                //     // since it's public metadata that doesn't require credentials
                //     opt.MetadataEndpoint = "https://appleid.apple.com/.well-known/openid-configuration";
                //     // Provide a dummy private key loader that will be replaced by PostConfigure
                //     opt.PrivateKey = (keyId, ct) => Task.FromResult(ReadOnlyMemory<char>.Empty);
                //     opt.GenerateClientSecret = false; // Disable until real config is loaded
                // });
        }

        return builder;
    }

    /// <summary>
    /// Registers a custom IExternalProviderConfigStore implementation for loading
    /// provider configurations from database.
    /// </summary>
    public static OlusoBuilder AddExternalProviderStore<TStore>(this OlusoBuilder builder)
        where TStore : class, IExternalProviderConfigStore
    {
        builder.Services.AddScoped<IExternalProviderConfigStore, TStore>();
        return builder;
    }

    /// <summary>
    /// Adds Google authentication
    /// </summary>
    public static OlusoBuilder AddGoogle(
        this OlusoBuilder builder,
        string clientId,
        string clientSecret,
        Action<GoogleAuthenticationOptions>? configure = null)
    {
        var options = new GoogleAuthenticationOptions
        {
            ClientId = clientId,
            ClientSecret = clientSecret
        };
        configure?.Invoke(options);

        builder.Services.AddAuthentication()
            .AddGoogle(opt =>
            {
                opt.ClientId = clientId;
                opt.ClientSecret = clientSecret;
                opt.SaveTokens = options.SaveTokens;

                if (options.Scopes.Any())
                {
                    opt.Scope.Clear();
                    foreach (var scope in options.Scopes)
                    {
                        opt.Scope.Add(scope);
                    }
                }

                options.ConfigureOAuth?.Invoke(opt);
            });

        return builder;
    }

    /// <summary>
    /// Adds Microsoft Account authentication
    /// </summary>
    public static OlusoBuilder AddMicrosoftAccount(
        this OlusoBuilder builder,
        string clientId,
        string clientSecret,
        Action<MicrosoftAuthenticationOptions>? configure = null)
    {
        var options = new MicrosoftAuthenticationOptions
        {
            ClientId = clientId,
            ClientSecret = clientSecret
        };
        configure?.Invoke(options);

        builder.Services.AddAuthentication()
            .AddMicrosoftAccount(opt =>
            {
                opt.ClientId = clientId;
                opt.ClientSecret = clientSecret;
                opt.SaveTokens = options.SaveTokens;

                if (options.Scopes.Any())
                {
                    opt.Scope.Clear();
                    foreach (var scope in options.Scopes)
                    {
                        opt.Scope.Add(scope);
                    }
                }

                options.ConfigureOAuth?.Invoke(opt);
            });

        return builder;
    }

    /// <summary>
    /// Adds Facebook authentication
    /// </summary>
    public static OlusoBuilder AddFacebook(
        this OlusoBuilder builder,
        string appId,
        string appSecret,
        Action<FacebookAuthenticationOptions>? configure = null)
    {
        var options = new FacebookAuthenticationOptions
        {
            AppId = appId,
            AppSecret = appSecret
        };
        configure?.Invoke(options);

        builder.Services.AddAuthentication()
            .AddFacebook(opt =>
            {
                opt.AppId = appId;
                opt.AppSecret = appSecret;
                opt.SaveTokens = options.SaveTokens;

                if (options.Scopes.Any())
                {
                    opt.Scope.Clear();
                    foreach (var scope in options.Scopes)
                    {
                        opt.Scope.Add(scope);
                    }
                }

                options.ConfigureOAuth?.Invoke(opt);
            });

        return builder;
    }

    /// <summary>
    /// Adds Twitter/X authentication
    /// </summary>
    public static OlusoBuilder AddTwitter(
        this OlusoBuilder builder,
        string consumerKey,
        string consumerSecret,
        Action<TwitterAuthenticationOptions>? configure = null)
    {
        var options = new TwitterAuthenticationOptions
        {
            ConsumerKey = consumerKey,
            ConsumerSecret = consumerSecret
        };
        configure?.Invoke(options);

        builder.Services.AddAuthentication()
            .AddTwitter(opt =>
            {
                opt.ConsumerKey = consumerKey;
                opt.ConsumerSecret = consumerSecret;
                opt.SaveTokens = options.SaveTokens;
                opt.RetrieveUserDetails = options.RetrieveUserDetails;

                options.ConfigureOAuth?.Invoke(opt);
            });

        return builder;
    }

    /// <summary>
    /// Adds GitHub authentication
    /// </summary>
    public static OlusoBuilder AddGitHub(
        this OlusoBuilder builder,
        string clientId,
        string clientSecret,
        Action<GitHubAuthenticationOptions>? configure = null)
    {
        var options = new GitHubAuthenticationOptions
        {
            ClientId = clientId,
            ClientSecret = clientSecret
        };
        configure?.Invoke(options);

        builder.Services.AddAuthentication()
            .AddGitHub(opt =>
            {
                opt.ClientId = clientId;
                opt.ClientSecret = clientSecret;
                opt.SaveTokens = options.SaveTokens;

                if (options.Scopes.Any())
                {
                    opt.Scope.Clear();
                    foreach (var scope in options.Scopes)
                    {
                        opt.Scope.Add(scope);
                    }
                }

                options.ConfigureOAuth?.Invoke(opt);
            });

        return builder;
    }

    /// <summary>
    /// Adds LinkedIn authentication
    /// </summary>
    public static OlusoBuilder AddLinkedIn(
        this OlusoBuilder builder,
        string clientId,
        string clientSecret,
        Action<LinkedInAuthenticationOptions>? configure = null)
    {
        var options = new LinkedInAuthenticationOptions
        {
            ClientId = clientId,
            ClientSecret = clientSecret
        };
        configure?.Invoke(options);

        builder.Services.AddAuthentication()
            .AddLinkedIn(opt =>
            {
                opt.ClientId = clientId;
                opt.ClientSecret = clientSecret;
                opt.SaveTokens = options.SaveTokens;

                if (options.Scopes.Any())
                {
                    opt.Scope.Clear();
                    foreach (var scope in options.Scopes)
                    {
                        opt.Scope.Add(scope);
                    }
                }

                options.ConfigureOAuth?.Invoke(opt);
            });

        return builder;
    }

    /// <summary>
    /// Adds Apple Sign In authentication
    /// </summary>
    public static OlusoBuilder AddApple(
        this OlusoBuilder builder,
        string clientId,
        string teamId,
        string keyId,
        string privateKey,
        Action<AppleAuthenticationOptions>? configure = null)
    {
        var options = new AppleAuthenticationOptions
        {
            ClientId = clientId,
            TeamId = teamId,
            KeyId = keyId,
            PrivateKey = privateKey
        };
        configure?.Invoke(options);

        builder.Services.AddAuthentication()
            .AddApple(opt =>
            {
                opt.ClientId = clientId;
                opt.TeamId = teamId;
                opt.KeyId = keyId;
                opt.SaveTokens = options.SaveTokens;

                // Apple requires a generated client secret
                opt.GenerateClientSecret = true;

                // Set the private key via the key loader
                opt.PrivateKey = (keyId, cancellationToken) =>
                    Task.FromResult(privateKey.AsMemory());

                options.ConfigureOAuth?.Invoke(opt);
            });

        return builder;
    }

    /// <summary>
    /// Adds a generic OAuth 2.0 provider
    /// </summary>
    public static OlusoBuilder AddOAuthProvider(
        this OlusoBuilder builder,
        string scheme,
        string displayName,
        Action<OAuthOptions> configure)
    {
        builder.Services.AddAuthentication()
            .AddOAuth(scheme, displayName, configure);

        return builder;
    }

    /// <summary>
    /// Adds a generic OpenID Connect provider
    /// </summary>
    public static OlusoBuilder AddOidcProvider(
        this OlusoBuilder builder,
        string scheme,
        string displayName,
        Action<Microsoft.AspNetCore.Authentication.OpenIdConnect.OpenIdConnectOptions> configure)
    {
        builder.Services.AddAuthentication()
            .AddOpenIdConnect(scheme, displayName, configure);

        return builder;
    }
}

/// <summary>
/// Options for external authentication configuration
/// </summary>
public class ExternalAuthenticationOptions
{
    /// <summary>
    /// Default behavior for saving tokens from external providers
    /// </summary>
    public bool SaveTokensByDefault { get; set; } = true;

    /// <summary>
    /// Whether to automatically provision users on first login
    /// </summary>
    public bool AutoProvisionUsers { get; set; } = true;
}

/// <summary>
/// Base options for OAuth providers
/// </summary>
public abstract class OAuthProviderOptions
{
    /// <summary>
    /// Whether to save external tokens for later use
    /// </summary>
    public bool SaveTokens { get; set; } = true;

    /// <summary>
    /// Additional scopes to request
    /// </summary>
    public List<string> Scopes { get; set; } = new();
}

/// <summary>
/// Google authentication options
/// </summary>
public class GoogleAuthenticationOptions : OAuthProviderOptions
{
    public required string ClientId { get; set; }
    public required string ClientSecret { get; set; }

    /// <summary>
    /// Additional configuration for the underlying OAuth options
    /// </summary>
    public Action<Microsoft.AspNetCore.Authentication.Google.GoogleOptions>? ConfigureOAuth { get; set; }

    public GoogleAuthenticationOptions()
    {
        // Default Google scopes
        Scopes = new List<string> { "openid", "profile", "email" };
    }
}

/// <summary>
/// Microsoft Account authentication options
/// </summary>
public class MicrosoftAuthenticationOptions : OAuthProviderOptions
{
    public required string ClientId { get; set; }
    public required string ClientSecret { get; set; }

    public Action<Microsoft.AspNetCore.Authentication.MicrosoftAccount.MicrosoftAccountOptions>? ConfigureOAuth { get; set; }

    public MicrosoftAuthenticationOptions()
    {
        Scopes = new List<string> { "openid", "profile", "email" };
    }
}

/// <summary>
/// Facebook authentication options
/// </summary>
public class FacebookAuthenticationOptions : OAuthProviderOptions
{
    public required string AppId { get; set; }
    public required string AppSecret { get; set; }

    public Action<Microsoft.AspNetCore.Authentication.Facebook.FacebookOptions>? ConfigureOAuth { get; set; }

    public FacebookAuthenticationOptions()
    {
        Scopes = new List<string> { "email", "public_profile" };
    }
}

/// <summary>
/// Twitter/X authentication options
/// </summary>
public class TwitterAuthenticationOptions
{
    public required string ConsumerKey { get; set; }
    public required string ConsumerSecret { get; set; }
    public bool SaveTokens { get; set; } = true;
    public bool RetrieveUserDetails { get; set; } = true;

    public Action<Microsoft.AspNetCore.Authentication.Twitter.TwitterOptions>? ConfigureOAuth { get; set; }
}

/// <summary>
/// GitHub authentication options
/// </summary>
public class GitHubAuthenticationOptions : OAuthProviderOptions
{
    public required string ClientId { get; set; }
    public required string ClientSecret { get; set; }

    public Action<AspNet.Security.OAuth.GitHub.GitHubAuthenticationOptions>? ConfigureOAuth { get; set; }

    public GitHubAuthenticationOptions()
    {
        Scopes = new List<string> { "user:email", "read:user" };
    }
}

/// <summary>
/// LinkedIn authentication options
/// </summary>
public class LinkedInAuthenticationOptions : OAuthProviderOptions
{
    public required string ClientId { get; set; }
    public required string ClientSecret { get; set; }

    public Action<AspNet.Security.OAuth.LinkedIn.LinkedInAuthenticationOptions>? ConfigureOAuth { get; set; }

    public LinkedInAuthenticationOptions()
    {
        Scopes = new List<string> { "openid", "profile", "email" };
    }
}

/// <summary>
/// Apple Sign In authentication options
/// </summary>
public class AppleAuthenticationOptions : OAuthProviderOptions
{
    public required string ClientId { get; set; }
    public required string TeamId { get; set; }
    public required string KeyId { get; set; }
    public required string PrivateKey { get; set; }

    public Action<AspNet.Security.OAuth.Apple.AppleAuthenticationOptions>? ConfigureOAuth { get; set; }

    public AppleAuthenticationOptions()
    {
        Scopes = new List<string> { "name", "email" };
    }
}

/// <summary>
/// Options for dynamic external provider configuration
/// </summary>
public class DynamicExternalProviderOptions
{
    /// <summary>
    /// Whether to register placeholder schemes for common providers (Google, Microsoft, etc.)
    /// that will be configured dynamically from the database.
    /// Default is true. Placeholder schemes are registered with dummy credentials that pass validation,
    /// then replaced with real credentials at runtime via PostConfigure.
    /// </summary>
    public bool RegisterPlaceholderSchemes { get; set; } = true;

    /// <summary>
    /// Cache duration for provider configurations in minutes.
    /// Default is 5 minutes.
    /// </summary>
    public int CacheDurationMinutes { get; set; } = 5;

    /// <summary>
    /// Whether to allow tenant-specific provider overrides.
    /// When true, tenant-specific configurations take precedence over global ones.
    /// Default is true.
    /// </summary>
    public bool AllowTenantOverrides { get; set; } = true;
}

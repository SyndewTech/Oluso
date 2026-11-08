using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Core.Authentication;

/// <summary>
/// Identity's default token provider (password-reset and email-confirmation links) with
/// the lifetime read from the tenant's <see cref="TenantAccountEmailSettings"/>. Tokens
/// carry their creation time, so only validation needs the tenant's value.
/// </summary>
public class TenantAccountLinkTokenProvider<TUser> : DataProtectorTokenProvider<TUser> where TUser : class
{
    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly ILogger<DataProtectorTokenProvider<TUser>> _logger;
    private readonly ITenantSettingsProvider _tenantSettings;

    public TenantAccountLinkTokenProvider(
        IDataProtectionProvider dataProtectionProvider,
        IOptions<DataProtectionTokenProviderOptions> options,
        ILogger<DataProtectorTokenProvider<TUser>> logger,
        ITenantSettingsProvider tenantSettings)
        : base(dataProtectionProvider, options, logger)
    {
        _dataProtectionProvider = dataProtectionProvider;
        _logger = logger;
        _tenantSettings = tenantSettings;
    }

    public override async Task<bool> ValidateAsync(string purpose, string token, UserManager<TUser> manager, TUser user)
    {
        var settings = await _tenantSettings.GetAccountEmailSettingsAsync() ?? new TenantAccountEmailSettings();
        var tenantOptions = new DataProtectionTokenProviderOptions
        {
            Name = Options.Name,
            TokenLifespan = TimeSpan.FromMinutes(settings.AccountLinkLifetimeMinutes),
        };

        // Same protector name, so tokens issued by this provider validate; only the lifetime differs.
        return await new DataProtectorTokenProvider<TUser>(_dataProtectionProvider, Microsoft.Extensions.Options.Options.Create(tenantOptions), _logger)
            .ValidateAsync(purpose, token, manager, user);
    }
}

public static class TenantAccountLinkTokenProviderExtensions
{
    /// <summary>
    /// Replaces Identity's default token provider with <see cref="TenantAccountLinkTokenProvider{TUser}"/>.
    /// Call after <c>AddDefaultTokenProviders()</c>.
    /// </summary>
    public static IdentityBuilder AddTenantAccountLinkLifetime(this IdentityBuilder builder)
    {
        var providerType = typeof(TenantAccountLinkTokenProvider<>).MakeGenericType(builder.UserType);
        return builder.AddTokenProvider(TokenOptions.DefaultProvider, providerType);
    }
}

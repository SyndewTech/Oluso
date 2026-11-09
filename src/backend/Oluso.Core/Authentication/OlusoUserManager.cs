using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Core.Authentication;

/// <summary>
/// Applies the user's tenant lockout policy (<see cref="TenantPasswordPolicy.MaxFailedAttempts"/>,
/// <see cref="TenantPasswordPolicy.LockoutDurationMinutes"/>) to every failed sign-in attempt.
/// Identity's lockout options are one process-wide setting, so the tenant's values are read
/// per user here; users whose tenant has no policy get the platform's Identity options.
/// <para>
/// Every successful password set (create, change, reset, add) stamps
/// <see cref="OlusoUser.PasswordChangedAt"/> and records the new hash in the user's password
/// history, kept to the tenant's <see cref="TenantPasswordPolicy.PasswordHistoryCount"/>.
/// </para>
/// </summary>
public class OlusoUserManager : UserManager<OlusoUser>
{
    private readonly ITenantStore _tenants;
    private readonly ITenantSettingsProvider _tenantSettings;
    private readonly IPasswordHistoryStore _passwordHistory;

    public OlusoUserManager(
        IUserStore<OlusoUser> store,
        IOptions<IdentityOptions> optionsAccessor,
        IPasswordHasher<OlusoUser> passwordHasher,
        IEnumerable<IUserValidator<OlusoUser>> userValidators,
        IEnumerable<IPasswordValidator<OlusoUser>> passwordValidators,
        ILookupNormalizer keyNormalizer,
        IdentityErrorDescriber errors,
        IServiceProvider services,
        ILogger<UserManager<OlusoUser>> logger,
        ITenantStore tenants,
        ITenantSettingsProvider tenantSettings,
        IPasswordHistoryStore passwordHistory)
        : base(store, optionsAccessor, passwordHasher, userValidators, passwordValidators, keyNormalizer, errors, services, logger)
    {
        _tenants = tenants;
        _tenantSettings = tenantSettings;
        _passwordHistory = passwordHistory;
    }

    public override Task<IdentityResult> CreateAsync(OlusoUser user, string password) =>
        SetPasswordAsync(user, () => base.CreateAsync(user, password));

    public override Task<IdentityResult> AddPasswordAsync(OlusoUser user, string password) =>
        SetPasswordAsync(user, () => base.AddPasswordAsync(user, password));

    public override Task<IdentityResult> ChangePasswordAsync(OlusoUser user, string currentPassword, string newPassword) =>
        SetPasswordAsync(user, () => base.ChangePasswordAsync(user, currentPassword, newPassword));

    public override Task<IdentityResult> ResetPasswordAsync(OlusoUser user, string token, string newPassword) =>
        SetPasswordAsync(user, () => base.ResetPasswordAsync(user, token, newPassword));

    /// <summary>
    /// Runs a password-setting operation with <see cref="OlusoUser.PasswordChangedAt"/> stamped, so
    /// the operation's own save persists it, and records the new hash in the password history once
    /// the operation succeeded. A failed operation saves nothing and the stamp is put back.
    /// </summary>
    private async Task<IdentityResult> SetPasswordAsync(OlusoUser user, Func<Task<IdentityResult>> setPassword)
    {
        var previouslyChangedAt = user.PasswordChangedAt;
        user.PasswordChangedAt = DateTime.UtcNow;

        var result = await setPassword();
        if (!result.Succeeded)
        {
            user.PasswordChangedAt = previouslyChangedAt;
            return result;
        }

        if (!string.IsNullOrEmpty(user.PasswordHash))
        {
            var settings = string.IsNullOrEmpty(user.TenantId)
                ? await _tenantSettings.GetPasswordSettingsAsync(CancellationToken)
                : await _tenantSettings.GetPasswordSettingsAsync(user.TenantId, CancellationToken);

            await _passwordHistory.RecordAsync(user.Id, user.PasswordHash, Math.Max(settings.PasswordHistoryCount, 0), CancellationToken);
        }
        return result;
    }

    public override async Task<IdentityResult> AccessFailedAsync(OlusoUser user)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        if (Store is not IUserLockoutStore<OlusoUser> lockoutStore)
        {
            throw new NotSupportedException("The user store does not support lockout.");
        }

        var (maxFailedAttempts, lockoutDuration) = await LockoutPolicyAsync(user);

        var count = await lockoutStore.IncrementAccessFailedCountAsync(user, CancellationToken);
        if (count < maxFailedAttempts)
        {
            return await UpdateUserAsync(user);
        }

        Logger.LogWarning("User {UserId} is locked out for {Minutes} minutes after {Count} failed attempts",
            user.Id, lockoutDuration.TotalMinutes, count);
        await lockoutStore.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.Add(lockoutDuration), CancellationToken);
        await lockoutStore.ResetAccessFailedCountAsync(user, CancellationToken);
        return await UpdateUserAsync(user);
    }

    private async Task<(int MaxFailedAttempts, TimeSpan Duration)> LockoutPolicyAsync(OlusoUser user)
    {
        var policy = string.IsNullOrEmpty(user.TenantId)
            ? null
            : (await _tenants.GetByIdAsync(user.TenantId, CancellationToken))?.PasswordPolicy;

        return policy != null
            ? (policy.MaxFailedAttempts, TimeSpan.FromMinutes(policy.LockoutDurationMinutes))
            : (Options.Lockout.MaxFailedAccessAttempts, Options.Lockout.DefaultLockoutTimeSpan);
    }
}

public static class TenantLockoutIdentityExtensions
{
    /// <summary>
    /// Uses <see cref="OlusoUserManager"/> so each tenant's lockout policy applies, with the
    /// tenant policy's own defaults as the platform default for users without one.
    /// </summary>
    public static IdentityBuilder AddTenantLockout(this IdentityBuilder builder)
    {
        var defaults = TenantPasswordPolicy.Default;
        builder.Services.Configure<IdentityOptions>(options =>
        {
            options.Lockout.AllowedForNewUsers = true;
            options.Lockout.MaxFailedAccessAttempts = defaults.MaxFailedAttempts;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(defaults.LockoutDurationMinutes);
        });
        return builder.AddUserManager<OlusoUserManager>();
    }
}

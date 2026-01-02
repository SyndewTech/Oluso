using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Oluso.Enterprise.Ldap.Entities;
using Oluso.Enterprise.Ldap.Stores;

namespace Oluso.Enterprise.Ldap.EntityFramework;

/// <summary>
/// Entity Framework implementation of ILdapServiceAccountStore
/// </summary>
public class LdapServiceAccountStore : ILdapServiceAccountStore
{
    private readonly LdapDbContext _context;
    private readonly IPasswordHasher<LdapServiceAccount> _passwordHasher;

    public LdapServiceAccountStore(
        LdapDbContext context,
        IPasswordHasher<LdapServiceAccount> passwordHasher)
    {
        _context = context;
        _passwordHasher = passwordHasher;
    }

    public async Task<IReadOnlyList<LdapServiceAccount>> GetAllAsync(
        string tenantId,
        CancellationToken cancellationToken = default)
    {
        // Tenant filter is applied automatically via PluginDbContextBase
        return await _context.LdapServiceAccounts
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);
    }

    public async Task<LdapServiceAccount?> GetByIdAsync(
        string tenantId,
        string id,
        CancellationToken cancellationToken = default)
    {
        // Tenant filter is applied automatically via PluginDbContextBase
        return await _context.LdapServiceAccounts
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
    }

    public async Task<LdapServiceAccount?> GetByBindDnAsync(
        string bindDn,
        CancellationToken cancellationToken = default)
    {
        // Cross-tenant lookup for LDAP bind - must ignore tenant filter
        return await _context.LdapServiceAccounts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.BindDn == bindDn, cancellationToken);
    }

    public async Task<LdapServiceAccount> CreateAsync(
        LdapServiceAccount account,
        CancellationToken cancellationToken = default)
    {
        _context.LdapServiceAccounts.Add(account);
        await _context.SaveChangesAsync(cancellationToken);
        return account;
    }

    public async Task<LdapServiceAccount> UpdateAsync(
        LdapServiceAccount account,
        CancellationToken cancellationToken = default)
    {
        account.UpdatedAt = DateTime.UtcNow;
        _context.LdapServiceAccounts.Update(account);
        await _context.SaveChangesAsync(cancellationToken);
        return account;
    }

    public async Task DeleteAsync(
        string tenantId,
        string id,
        CancellationToken cancellationToken = default)
    {
        var account = await GetByIdAsync(tenantId, id, cancellationToken);
        if (account != null)
        {
            _context.LdapServiceAccounts.Remove(account);
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task UpdateLastUsedAsync(
        string id,
        CancellationToken cancellationToken = default)
    {
        // Cross-tenant update for LDAP bind tracking - must ignore tenant filter
        var account = await _context.LdapServiceAccounts
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

        if (account != null)
        {
            account.LastUsedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<LdapServiceAccount?> ValidateCredentialsAsync(
        string bindDn,
        string password,
        CancellationToken cancellationToken = default)
    {
        var account = await GetByBindDnAsync(bindDn, cancellationToken);
        if (account == null || !account.IsEnabled || account.IsExpired)
        {
            return null;
        }

        var result = _passwordHasher.VerifyHashedPassword(account, account.PasswordHash, password);
        if (result == PasswordVerificationResult.Failed)
        {
            return null;
        }

        // Update last used timestamp (fire and forget, don't await)
        _ = UpdateLastUsedAsync(account.Id, CancellationToken.None);

        return account;
    }
}

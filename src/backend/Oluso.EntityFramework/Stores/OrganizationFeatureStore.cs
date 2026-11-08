using Microsoft.EntityFrameworkCore;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.EntityFramework.Stores;

public class OrganizationFeatureStore : IOrganizationFeatureStore
{
    private readonly OlusoDbContext _context;

    public OrganizationFeatureStore(OlusoDbContext context)
    {
        _context = context;
    }

    public async Task<IReadOnlyList<OrganizationFeature>> GetFeaturesAsync(string organizationId, CancellationToken cancellationToken = default)
    {
        return await _context.OrganizationFeatures
            .Where(f => f.OrganizationId == organizationId)
            .OrderBy(f => f.FeatureKey)
            .ToListAsync(cancellationToken);
    }

    public async Task<bool> IsFeatureEnabledAsync(string organizationId, string featureKey, CancellationToken cancellationToken = default)
    {
        var feature = await _context.OrganizationFeatures
            .FirstOrDefaultAsync(f => f.OrganizationId == organizationId && f.FeatureKey == featureKey, cancellationToken);

        if (feature == null)
            return false;

        return feature.Enabled && (feature.ExpiresAt == null || feature.ExpiresAt > DateTime.UtcNow);
    }

    public async Task<bool> HasAnyFeaturesConfiguredAsync(string organizationId, CancellationToken cancellationToken = default)
    {
        return await _context.OrganizationFeatures
            .AnyAsync(f => f.OrganizationId == organizationId, cancellationToken);
    }

    public async Task SetFeaturesAsync(string organizationId, IEnumerable<OrganizationFeatureUpdate> updates, string? grantedByUserId, CancellationToken cancellationToken = default)
    {
        var existing = await _context.OrganizationFeatures
            .Where(f => f.OrganizationId == organizationId)
            .ToListAsync(cancellationToken);

        var updateList = updates.ToList();
        var updatedKeys = updateList.Select(u => u.FeatureKey).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Remove features not in the update list
        var toRemove = existing.Where(e => !updatedKeys.Contains(e.FeatureKey)).ToList();
        _context.OrganizationFeatures.RemoveRange(toRemove);

        foreach (var update in updateList)
        {
            var existingFeature = existing.FirstOrDefault(e =>
                e.FeatureKey.Equals(update.FeatureKey, StringComparison.OrdinalIgnoreCase));

            if (existingFeature != null)
            {
                existingFeature.Enabled = update.Enabled;
                existingFeature.ExpiresAt = update.ExpiresAt;
                existingFeature.MaxSeats = update.MaxSeats;
                existingFeature.GrantedByUserId = grantedByUserId;
                existingFeature.GrantedAt = DateTime.UtcNow;
            }
            else
            {
                _context.OrganizationFeatures.Add(new OrganizationFeature
                {
                    OrganizationId = organizationId,
                    FeatureKey = update.FeatureKey,
                    Enabled = update.Enabled,
                    ExpiresAt = update.ExpiresAt,
                    MaxSeats = update.MaxSeats,
                    GrantedByUserId = grantedByUserId,
                    GrantedAt = DateTime.UtcNow,
                });
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task EnableFeatureAsync(string organizationId, string featureKey, string? grantedByUserId, DateTime? expiresAt = null, CancellationToken cancellationToken = default)
    {
        var feature = await _context.OrganizationFeatures
            .FirstOrDefaultAsync(f => f.OrganizationId == organizationId && f.FeatureKey == featureKey, cancellationToken);

        if (feature != null)
        {
            feature.Enabled = true;
            feature.ExpiresAt = expiresAt;
            feature.GrantedByUserId = grantedByUserId;
            feature.GrantedAt = DateTime.UtcNow;
        }
        else
        {
            _context.OrganizationFeatures.Add(new OrganizationFeature
            {
                OrganizationId = organizationId,
                FeatureKey = featureKey,
                Enabled = true,
                ExpiresAt = expiresAt,
                GrantedByUserId = grantedByUserId,
                GrantedAt = DateTime.UtcNow,
            });
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task DisableFeatureAsync(string organizationId, string featureKey, CancellationToken cancellationToken = default)
    {
        var feature = await _context.OrganizationFeatures
            .FirstOrDefaultAsync(f => f.OrganizationId == organizationId && f.FeatureKey == featureKey, cancellationToken);

        if (feature != null)
        {
            feature.Enabled = false;
            await _context.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task SyncSubscriptionFeaturesAsync(string organizationId, string subscriptionId, IEnumerable<OrganizationFeatureUpdate> subscriptionFeatures, CancellationToken cancellationToken = default)
    {
        var existing = await _context.OrganizationFeatures
            .Where(f => f.OrganizationId == organizationId && f.Source == "subscription" && f.SourceSubscriptionId == subscriptionId)
            .ToListAsync(cancellationToken);

        var updateList = subscriptionFeatures.ToList();
        var updatedKeys = updateList.Select(u => u.FeatureKey).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Remove subscription features no longer in the plan
        var toRemove = existing.Where(e => !updatedKeys.Contains(e.FeatureKey)).ToList();
        _context.OrganizationFeatures.RemoveRange(toRemove);

        foreach (var update in updateList)
        {
            var existingFeature = existing.FirstOrDefault(e =>
                e.FeatureKey.Equals(update.FeatureKey, StringComparison.OrdinalIgnoreCase));

            if (existingFeature != null)
            {
                existingFeature.Enabled = update.Enabled;
                existingFeature.ExpiresAt = update.ExpiresAt;
                existingFeature.MaxSeats = update.MaxSeats;
                existingFeature.GrantedAt = DateTime.UtcNow;
            }
            else
            {
                _context.OrganizationFeatures.Add(new OrganizationFeature
                {
                    OrganizationId = organizationId,
                    FeatureKey = update.FeatureKey,
                    Enabled = update.Enabled,
                    ExpiresAt = update.ExpiresAt,
                    MaxSeats = update.MaxSeats,
                    GrantedAt = DateTime.UtcNow,
                    Source = "subscription",
                    SourceSubscriptionId = subscriptionId,
                });
            }
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveSubscriptionFeaturesAsync(string organizationId, string subscriptionId, CancellationToken cancellationToken = default)
    {
        var toRemove = await _context.OrganizationFeatures
            .Where(f => f.OrganizationId == organizationId && f.Source == "subscription" && f.SourceSubscriptionId == subscriptionId)
            .ToListAsync(cancellationToken);

        _context.OrganizationFeatures.RemoveRange(toRemove);
        await _context.SaveChangesAsync(cancellationToken);
    }
}

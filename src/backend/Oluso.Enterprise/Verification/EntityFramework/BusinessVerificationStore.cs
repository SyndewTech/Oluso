using Microsoft.EntityFrameworkCore;
using Oluso.Enterprise.Verification.Stores;

namespace Oluso.Enterprise.Verification.EntityFramework;

public sealed class BusinessVerificationStore : IBusinessVerificationStore
{
    private static readonly BusinessVerificationStatus[] OpenStatuses = { BusinessVerificationStatus.Pending, BusinessVerificationStatus.InReview };

    private readonly VerificationDbContext _db;

    public BusinessVerificationStore(VerificationDbContext db) => _db = db;

    public Task<VerifiedBusiness?> FindBusinessAsync(string tenantId, string country, string registrationNumber, CancellationToken cancellationToken = default) =>
        _db.Businesses.FirstOrDefaultAsync(b => b.TenantId == tenantId && b.Country == country && b.RegistrationNumber == registrationNumber, cancellationToken);

    public Task<VerifiedBusiness?> GetBusinessAsync(string tenantId, string businessId, CancellationToken cancellationToken = default) =>
        _db.Businesses.FirstOrDefaultAsync(b => b.TenantId == tenantId && b.Id == businessId, cancellationToken);

    public async Task<IReadOnlyList<VerifiedBusiness>> GetBusinessesAsync(string tenantId, IReadOnlyCollection<string> businessIds, CancellationToken cancellationToken = default) =>
        businessIds.Count == 0
            ? Array.Empty<VerifiedBusiness>()
            : await _db.Businesses.Where(b => b.TenantId == tenantId && businessIds.Contains(b.Id)).ToListAsync(cancellationToken);

    public async Task<(IReadOnlyList<VerifiedBusiness> Items, int TotalCount)> ListBusinessesAsync(
        string tenantId, BusinessVerificationStatus? status, string? search, int skip, int take, CancellationToken cancellationToken = default)
    {
        var query = _db.Businesses.Where(b => b.TenantId == tenantId);
        if (status != null) query = query.Where(b => b.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            var number = term.Replace(" ", string.Empty).Replace("-", string.Empty).ToUpperInvariant();
            query = query.Where(b => (b.RegisteredName != null && EF.Functions.Like(b.RegisteredName, $"%{term}%")) || b.RegistrationNumber.Contains(number));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(b => b.UpdatedAt).Skip(skip).Take(take).ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task AddBusinessAsync(VerifiedBusiness business, CancellationToken cancellationToken = default)
    {
        _db.Businesses.Add(business);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateBusinessAsync(VerifiedBusiness business, CancellationToken cancellationToken = default)
    {
        if (_db.Entry(business).State == EntityState.Detached) _db.Businesses.Update(business);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public Task<BusinessApplicant?> GetApplicantAsync(string tenantId, string applicantId, CancellationToken cancellationToken = default) =>
        _db.Applicants.FirstOrDefaultAsync(a => a.TenantId == tenantId && a.Id == applicantId, cancellationToken);

    public Task<BusinessApplicant?> FindApplicantForSubjectAsync(string tenantId, string businessId, string subjectId, CancellationToken cancellationToken = default) =>
        _db.Applicants.FirstOrDefaultAsync(a => a.TenantId == tenantId && a.BusinessId == businessId && a.SubjectId == subjectId, cancellationToken);

    public Task<BusinessApplicant?> FindUnclaimedApplicantForJourneyAsync(string tenantId, string businessId, string journeyId, CancellationToken cancellationToken = default) =>
        _db.Applicants.FirstOrDefaultAsync(a => a.TenantId == tenantId && a.BusinessId == businessId && a.JourneyId == journeyId && a.SubjectId == null, cancellationToken);

    public async Task<IReadOnlyList<BusinessApplicant>> ListUnclaimedApplicantsForJourneyAsync(string tenantId, string journeyId, CancellationToken cancellationToken = default) =>
        await _db.Applicants.Where(a => a.TenantId == tenantId && a.JourneyId == journeyId && a.SubjectId == null).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<BusinessApplicant>> ListApplicantsForBusinessAsync(string tenantId, string businessId, CancellationToken cancellationToken = default) =>
        await _db.Applicants.Where(a => a.TenantId == tenantId && a.BusinessId == businessId).OrderBy(a => a.CreatedAt).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<BusinessApplicant>> ListApplicantsForSubjectAsync(string tenantId, string subjectId, CancellationToken cancellationToken = default) =>
        await _db.Applicants.Where(a => a.TenantId == tenantId && a.SubjectId == subjectId).OrderByDescending(a => a.UpdatedAt).ToListAsync(cancellationToken);

    public async Task<(IReadOnlyList<BusinessApplicant> Items, int TotalCount)> ListOpenApplicantsAsync(string tenantId, int skip, int take, CancellationToken cancellationToken = default)
    {
        var query = _db.Applicants.Where(a => a.TenantId == tenantId && OpenStatuses.Contains(a.Status));
        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderBy(a => a.UpdatedAt).Skip(skip).Take(take).ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task AddApplicantAsync(BusinessApplicant applicant, CancellationToken cancellationToken = default)
    {
        _db.Applicants.Add(applicant);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateApplicantAsync(BusinessApplicant applicant, CancellationToken cancellationToken = default)
    {
        if (_db.Entry(applicant).State == EntityState.Detached) _db.Applicants.Update(applicant);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task RemoveApplicantAsync(BusinessApplicant applicant, CancellationToken cancellationToken = default)
    {
        _db.Applicants.Remove(applicant);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public Task<BusinessVerificationCheck?> FindOpenSessionForApplicantAsync(string tenantId, string applicantId, CancellationToken cancellationToken = default) =>
        _db.Checks
            .Where(c => c.TenantId == tenantId && c.ApplicantId == applicantId && c.Kind == BusinessCheckKinds.HostedSession)
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task AddCheckAsync(BusinessVerificationCheck check, CancellationToken cancellationToken = default)
    {
        _db.Checks.Add(check);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<VerificationDecisionResult> TryDecideAsync(VerificationDecisionWrite write, CancellationToken cancellationToken = default)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        if ((write.CloseSession != null && !await ClaimSessionAsync(write.CloseSession, cancellationToken))
            || (write.Business != null && !await ClaimAsync(write.Business, cancellationToken))
            || (write.Applicant != null && !await ClaimAsync(write.Applicant, cancellationToken)))
        {
            await transaction.RollbackAsync(cancellationToken);
            foreach (var entity in new object?[] { write.CloseSession, write.Business, write.Applicant }.Concat(write.Followers))
            {
                if (entity != null) await _db.Entry(entity).ReloadAsync(cancellationToken);
            }
            return new VerificationDecisionResult(false, Array.Empty<BusinessApplicant>());
        }

        var applied = new List<BusinessApplicant>();
        foreach (var follower in write.Followers)
        {
            if (await ClaimAsync(follower, cancellationToken)) applied.Add(follower);
            else await _db.Entry(follower).ReloadAsync(cancellationToken);
        }

        _db.Checks.AddRange(write.NewChecks);
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new VerificationDecisionResult(true, applied);
    }

    private async Task<bool> ClaimSessionAsync(BusinessVerificationCheck check, CancellationToken cancellationToken)
    {
        Tracked(check);
        return await _db.Checks
            .Where(c => c.Id == check.Id && c.TenantId == check.TenantId && c.Outcome == BusinessCheckOutcomes.Open)
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.Outcome, check.Outcome)
                .SetProperty(c => c.CompletedAt, check.CompletedAt), cancellationToken) > 0;
    }

    private async Task<bool> ClaimAsync(VerifiedBusiness business, CancellationToken cancellationToken)
    {
        var entry = Tracked(business);
        var status = entry.Property(b => b.Status).OriginalValue;
        var reason = entry.Property(b => b.StatusReason).OriginalValue;
        return await _db.Businesses
            .Where(b => b.Id == business.Id && b.TenantId == business.TenantId && b.Status == status && b.StatusReason == reason)
            .ExecuteUpdateAsync(s => s
                .SetProperty(b => b.Status, business.Status)
                .SetProperty(b => b.StatusReason, business.StatusReason), cancellationToken) > 0;
    }

    private async Task<bool> ClaimAsync(BusinessApplicant applicant, CancellationToken cancellationToken)
    {
        var entry = Tracked(applicant);
        var status = entry.Property(a => a.Status).OriginalValue;
        var reason = entry.Property(a => a.StatusReason).OriginalValue;
        return await _db.Applicants
            .Where(a => a.Id == applicant.Id && a.TenantId == applicant.TenantId && a.Status == status && a.StatusReason == reason)
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.Status, applicant.Status)
                .SetProperty(a => a.StatusReason, applicant.StatusReason), cancellationToken) > 0;
    }

    private Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<TEntity> Tracked<TEntity>(TEntity entity) where TEntity : class
    {
        var entry = _db.Entry(entity);
        if (entry.State is EntityState.Detached or EntityState.Added)
            throw new InvalidOperationException($"A decision can only be written for a {typeof(TEntity).Name} loaded through this store.");
        return entry;
    }

    public Task<BusinessVerificationCheck?> GetCheckAsync(string tenantId, string checkId, CancellationToken cancellationToken = default) =>
        _db.Checks.FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Id == checkId, cancellationToken);

    public Task<BusinessVerificationCheck?> FindSessionAsync(string tenantId, string providerId, string sessionReference, CancellationToken cancellationToken = default) =>
        _db.Checks.FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Kind == BusinessCheckKinds.HostedSession
            && c.ProviderId == providerId && c.SessionReference == sessionReference, cancellationToken);

    public async Task<IReadOnlyList<BusinessVerificationCheck>> ListChecksForBusinessAsync(string tenantId, string businessId, CancellationToken cancellationToken = default) =>
        await _db.Checks.Where(c => c.TenantId == tenantId && c.BusinessId == businessId).OrderByDescending(c => c.CreatedAt).ToListAsync(cancellationToken);
}

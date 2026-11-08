using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.EntityFramework.Stores;

public class FeedbackStore : IFeedbackStore
{
    private readonly IOlusoDbContext _context;
    private readonly ILogger<FeedbackStore> _logger;

    public FeedbackStore(IOlusoDbContext context, ILogger<FeedbackStore> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<FeedbackSubmission> CreateAsync(FeedbackSubmission feedback, CancellationToken cancellationToken = default)
    {
        _context.FeedbackSubmissions.Add(feedback);
        await _context.SaveChangesAsync(cancellationToken);
        return feedback;
    }

    public async Task<FeedbackSubmission?> GetByIdAsync(string? tenantId, string id, CancellationToken cancellationToken = default)
    {
        return await InTenant(tenantId).FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
    }

    public async Task<(IReadOnlyList<FeedbackSubmission> Items, int Total)> ListAsync(
        string? tenantId, int page = 1, int pageSize = 25,
        string? category = null, string? status = null,
        CancellationToken cancellationToken = default)
    {
        var query = InTenant(tenantId);

        if (!string.IsNullOrEmpty(category))
            query = query.Where(f => f.Category == category);

        if (!string.IsNullOrEmpty(status))
            query = query.Where(f => f.Status == status);

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(f => f.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task UpdateStatusAsync(string? tenantId, string id, string status, CancellationToken cancellationToken = default)
    {
        var feedback = await InTenant(tenantId).FirstOrDefaultAsync(f => f.Id == id, cancellationToken);

        if (feedback == null)
            throw new InvalidOperationException($"Feedback submission {id} not found");

        feedback.Status = status;
        feedback.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// One tenant's feedback, or every tenant's when the tenant is null. The tenant query filter is lifted for the
    /// latter, since the request's own tenant would otherwise narrow it again.
    /// </summary>
    private IQueryable<FeedbackSubmission> InTenant(string? tenantId) => tenantId == null
        ? _context.FeedbackSubmissions.IgnoreQueryFilters()
        : _context.FeedbackSubmissions.Where(f => f.TenantId == tenantId);
}

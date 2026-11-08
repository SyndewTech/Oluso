using Microsoft.AspNetCore.Mvc;
using Oluso.Core.Api;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Storage;
using Oluso.Enterprise.Verification.Dto;
using Oluso.Enterprise.Verification.Stores;

namespace Oluso.Enterprise.Verification.Controllers;

/// <summary>
/// The businesses the signed-in user acts for, as they see them: status, registry details
/// and the documents submitted for the business. A user sees only businesses they are
/// linked to; documents of other people linked to the same business are included, since
/// they're the business's evidence.
/// </summary>
[Route("api/account/businesses")]
public class AccountBusinessesController : AccountBaseController
{
    private readonly IBusinessVerificationStore _store;
    private readonly IFileAttachmentService _attachments;
    private readonly IFileAttachmentStore _attachmentStore;
    private readonly IFileUploader _files;

    public AccountBusinessesController(
        ITenantContext tenantContext,
        IBusinessVerificationStore store,
        IFileAttachmentService attachments,
        IFileAttachmentStore attachmentStore,
        IFileUploader files) : base(tenantContext)
    {
        _store = store;
        _attachments = attachments;
        _attachmentStore = attachmentStore;
        _files = files;
    }

    private string Tenant => TenantId ?? throw new UnauthorizedAccessException("No organisation in context");

    [HttpGet]
    public async Task<ActionResult<IEnumerable<MyBusinessDto>>> List(CancellationToken cancellationToken)
    {
        var links = await _store.ListApplicantsForSubjectAsync(TenantId, UserId, cancellationToken);
        var businesses = (await _store.GetBusinessesAsync(TenantId, links.Select(l => l.BusinessId).Distinct().ToList(), cancellationToken))
            .ToDictionary(b => b.Id);
        var result = new List<MyBusinessDto>();
        foreach (var link in links)
        {
            if (businesses.TryGetValue(link.BusinessId, out var business))
                result.Add(await ToDtoAsync(business, link, cancellationToken));
        }
        return Ok(result);
    }

    [HttpGet("{businessId}")]
    public async Task<ActionResult<MyBusinessDto>> Get(string businessId, CancellationToken cancellationToken)
    {
        var link = await _store.FindApplicantForSubjectAsync(TenantId, businessId, UserId, cancellationToken);
        var business = link == null ? null : await _store.GetBusinessAsync(TenantId, businessId, cancellationToken);
        return business == null ? NotFound() : Ok(await ToDtoAsync(business, link!, cancellationToken));
    }

    /// <summary>A document submitted for a business the user acts for.</summary>
    [HttpGet("{businessId}/documents/{documentId}")]
    public async Task<IActionResult> Document(string businessId, string documentId, CancellationToken cancellationToken)
    {
        if (await _store.FindApplicantForSubjectAsync(TenantId, businessId, UserId, cancellationToken) == null) return NotFound();
        var applicants = await _store.ListApplicantsForBusinessAsync(TenantId, businessId, cancellationToken);
        if (!applicants.Any(a => a.DocumentIds.Contains(documentId))) return NotFound();

        var file = await _attachmentStore.GetByIdAsync(documentId, cancellationToken);
        if (file == null || file.TenantId != TenantId || file.IsDeleted) return NotFound();
        var bytes = await _files.DownloadAsync(file.StoragePath, cancellationToken);
        if (bytes == null) return NotFound();
        var view = await _attachments.ResolveAsync(file, cancellationToken);
        return File(bytes, view.ContentType, view.FileName);
    }

    private async Task<MyBusinessDto> ToDtoAsync(VerifiedBusiness business, BusinessApplicant link, CancellationToken cancellationToken) => new()
    {
        BusinessId = business.Id,
        ApplicantId = link.Id,
        Country = business.Country,
        RegistrationNumber = business.RegistrationNumber,
        RegisteredName = business.RegisteredName,
        RegisteredAddress = business.RegisteredAddress,
        BusinessStatus = business.Status.ToWire(),
        Status = link.Status.ToWire(),
        StatusReason = link.StatusReason,
        Relationship = link.Relationship,
        Documents = await VerificationDocuments.ListAsync(_attachmentStore, _attachments, TenantId, link, cancellationToken),
        UpdatedAt = link.UpdatedAt,
    };
}

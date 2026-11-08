using Oluso.Core.Domain.Entities;

namespace Oluso.Core.Domain.Interfaces;

public interface IWhatsAppContactStore
{
    Task<WhatsAppContact?> GetByPhoneAsync(string tenantId, string phoneNumber, CancellationToken cancellationToken = default);
    Task<WhatsAppContact?> GetByIdAsync(string tenantId, string id, CancellationToken cancellationToken = default);
    Task<(IReadOnlyList<WhatsAppContact> Items, int TotalCount)> SearchAsync(
        string tenantId, string? search, WhatsAppConsentStatus? consent, int skip, int take, CancellationToken cancellationToken = default);
    Task<WhatsAppContact> UpsertAsync(WhatsAppContact contact, CancellationToken cancellationToken = default);
}

public interface IWhatsAppTemplateStore
{
    Task<IReadOnlyList<WhatsAppTemplate>> ListAsync(string tenantId, bool approvedOnly, CancellationToken cancellationToken = default);
    Task<WhatsAppTemplate?> GetAsync(string tenantId, string name, string language, CancellationToken cancellationToken = default);

    /// <summary>Replaces the tenant's template set with <paramref name="templates"/>; rows no longer present are removed.</summary>
    Task ReplaceAllAsync(string tenantId, IReadOnlyList<WhatsAppTemplate> templates, CancellationToken cancellationToken = default);
}

public interface IWhatsAppInboundMessageStore
{
    Task<bool> ExistsAsync(string tenantId, string providerMessageId, CancellationToken cancellationToken = default);
    Task AddAsync(WhatsAppInboundMessage message, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<WhatsAppInboundMessage>> ListForContactAsync(string tenantId, string contactId, int take, CancellationToken cancellationToken = default);
}

public interface IWhatsAppAccountStore
{
    /// <summary>Cross-tenant lookup used by the webhook to route a payload before verification.</summary>
    Task<WhatsAppAccount?> GetByPhoneNumberIdAsync(string phoneNumberId, CancellationToken cancellationToken = default);
    Task<WhatsAppAccount?> GetForTenantAsync(string tenantId, CancellationToken cancellationToken = default);
    Task<WhatsAppAccount> UpsertAsync(WhatsAppAccount account, CancellationToken cancellationToken = default);
    Task RemoveForTenantAsync(string tenantId, CancellationToken cancellationToken = default);
}

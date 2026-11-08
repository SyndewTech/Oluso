using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Messaging.WhatsApp;

namespace Oluso.Tests.Messaging.WhatsApp;

/// <summary>
/// HttpMessageHandler that records every request (method, URL, auth header,
/// body) and replays a queue of scripted responses.
/// </summary>
internal sealed class RecordingHandler : HttpMessageHandler
{
    public sealed record Captured(HttpMethod Method, string Url, string? Authorization, string? Body);

    private readonly Queue<Func<HttpResponseMessage>> _responses = new();
    public List<Captured> Requests { get; } = new();

    public RecordingHandler Enqueue(HttpStatusCode status, string body, TimeSpan? retryAfter = null)
    {
        _responses.Enqueue(() =>
        {
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            if (retryAfter.HasValue) response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(retryAfter.Value);
            return response;
        });
        return this;
    }

    public RecordingHandler EnqueueJson(object body, HttpStatusCode status = HttpStatusCode.OK) =>
        Enqueue(status, JsonSerializer.Serialize(body));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new Captured(request.Method, request.RequestUri!.ToString(), request.Headers.Authorization?.ToString(), body));
        if (_responses.Count == 0)
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}", Encoding.UTF8, "application/json") };
        return _responses.Dequeue()();
    }
}

internal sealed class InMemoryContactStore : IWhatsAppContactStore
{
    public readonly List<WhatsAppContact> Rows = new();

    public Task<WhatsAppContact?> GetByPhoneAsync(string tenantId, string phoneNumber, CancellationToken ct = default) =>
        Task.FromResult(Rows.FirstOrDefault(r => r.TenantId == tenantId && r.PhoneNumber == phoneNumber));

    public Task<WhatsAppContact?> GetByIdAsync(string tenantId, string id, CancellationToken ct = default) =>
        Task.FromResult(Rows.FirstOrDefault(r => r.TenantId == tenantId && r.Id == id));

    public Task<(IReadOnlyList<WhatsAppContact> Items, int TotalCount)> SearchAsync(string tenantId, string? search, WhatsAppConsentStatus? consent, int skip, int take, CancellationToken ct = default)
    {
        var q = Rows.Where(r => r.TenantId == tenantId);
        if (consent.HasValue) q = q.Where(r => r.ConsentStatus == consent);
        var list = q.ToList();
        return Task.FromResult<(IReadOnlyList<WhatsAppContact>, int)>((list.Skip(skip).Take(take).ToList(), list.Count));
    }

    public Task<WhatsAppContact> UpsertAsync(WhatsAppContact contact, CancellationToken ct = default)
    {
        var existing = Rows.FirstOrDefault(r => r.TenantId == contact.TenantId && r.PhoneNumber == contact.PhoneNumber);
        if (existing == null)
        {
            Rows.Add(contact);
            return Task.FromResult(contact);
        }
        if (!ReferenceEquals(existing, contact))
        {
            existing.DisplayName = contact.DisplayName ?? existing.DisplayName;
            existing.ConsentStatus = contact.ConsentStatus;
            existing.ConsentSource = contact.ConsentSource;
            existing.ConsentAt = contact.ConsentAt;
            existing.OptedOutAt = contact.OptedOutAt;
            existing.LastInboundAt = contact.LastInboundAt;
            existing.LastInboundMessageId = contact.LastInboundMessageId;
            existing.LastOutboundAt = contact.LastOutboundAt;
        }
        return Task.FromResult(existing);
    }
}

internal sealed class InMemoryInboundStore : IWhatsAppInboundMessageStore
{
    public readonly List<WhatsAppInboundMessage> Rows = new();

    public Task<bool> ExistsAsync(string tenantId, string providerMessageId, CancellationToken ct = default) =>
        Task.FromResult(Rows.Any(r => r.TenantId == tenantId && r.ProviderMessageId == providerMessageId));

    public Task AddAsync(WhatsAppInboundMessage message, CancellationToken ct = default)
    {
        Rows.Add(message);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<WhatsAppInboundMessage>> ListForContactAsync(string tenantId, string contactId, int take, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<WhatsAppInboundMessage>>(Rows.Where(r => r.TenantId == tenantId && r.ContactId == contactId).Take(take).ToList());
}

internal sealed class InMemoryTemplateStore : IWhatsAppTemplateStore
{
    public readonly List<WhatsAppTemplate> Rows = new();

    public Task<IReadOnlyList<WhatsAppTemplate>> ListAsync(string tenantId, bool approvedOnly, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<WhatsAppTemplate>>(Rows.Where(r => r.TenantId == tenantId && (!approvedOnly || r.IsApproved)).ToList());

    public Task<WhatsAppTemplate?> GetAsync(string tenantId, string name, string language, CancellationToken ct = default) =>
        Task.FromResult(Rows.FirstOrDefault(r => r.TenantId == tenantId && r.Name == name && r.Language == language));

    public Task ReplaceAllAsync(string tenantId, IReadOnlyList<WhatsAppTemplate> templates, CancellationToken ct = default)
    {
        Rows.RemoveAll(r => r.TenantId == tenantId);
        Rows.AddRange(templates);
        return Task.CompletedTask;
    }
}

internal sealed class InMemoryAccountStore : IWhatsAppAccountStore
{
    public readonly List<WhatsAppAccount> Rows = new();

    public Task<WhatsAppAccount?> GetByPhoneNumberIdAsync(string phoneNumberId, CancellationToken ct = default) =>
        Task.FromResult(Rows.FirstOrDefault(r => r.PhoneNumberId == phoneNumberId));

    public Task<WhatsAppAccount?> GetForTenantAsync(string tenantId, CancellationToken ct = default) =>
        Task.FromResult(Rows.FirstOrDefault(r => r.TenantId == tenantId));

    public Task<WhatsAppAccount> UpsertAsync(WhatsAppAccount account, CancellationToken ct = default)
    {
        Rows.RemoveAll(r => r.TenantId == account.TenantId);
        Rows.Add(account);
        return Task.FromResult(account);
    }

    public Task RemoveForTenantAsync(string tenantId, CancellationToken ct = default)
    {
        Rows.RemoveAll(r => r.TenantId == tenantId);
        return Task.CompletedTask;
    }
}

internal sealed class CapturingEvents : IOlusoEventService
{
    public readonly List<OlusoEvent> Raised = new();
    public Task RaiseAsync(OlusoEvent evt, CancellationToken cancellationToken = default)
    {
        Raised.Add(evt);
        return Task.CompletedTask;
    }
}

internal static class WhatsAppRig
{
    public const string Tenant = "tenant-1";

    public static ResolvedWhatsAppSettings Settings(string? defaultCountry = "NG", string? appSecret = "app-secret") =>
        new(Tenant, "token-123", "PN123", "v21.0", "WABA1", appSecret, "verify-me", defaultCountry, WhatsAppCredentialSource.TenantSettings);

    public static IServiceProvider Services(CapturingEvents events)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IOlusoEventService>(events);
        return services.BuildServiceProvider();
    }

    public static WhatsAppContactService ContactService(InMemoryContactStore store, CapturingEvents events) =>
        new(store, Services(events), NullLogger<WhatsAppContactService>.Instance);

    public static MetaGraphClient Graph(RecordingHandler handler) =>
        new(new HttpClient(handler), NullLogger<MetaGraphClient>.Instance) { Delay = (_, _) => Task.CompletedTask };
}

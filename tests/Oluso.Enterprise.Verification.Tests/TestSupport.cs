using System.Net;
using System.Text;
using Microsoft.Data.Sqlite;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Services;
using Oluso.Core.Storage;
using Oluso.Core.UserJourneys;
using Oluso.Enterprise.Verification.EntityFramework;
using Oluso.Enterprise.Verification.Services;
using Oluso.Enterprise.Verification.Stores;
using Oluso.Storage;

namespace Oluso.Enterprise.Verification.Tests;

/// <summary>Records each request with all its headers and body, and replays scripted responses.</summary>
internal sealed class FakeHttp : HttpMessageHandler
{
    public sealed record Sent(HttpMethod Method, string Url, Dictionary<string, string> Headers, string? Body);

    private readonly Queue<Func<HttpResponseMessage>> _responses = new();
    public List<Sent> Requests { get; } = new();

    public FakeHttp Respond(HttpStatusCode status, string body)
    {
        _responses.Enqueue(() => new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        return this;
    }

    public FakeHttp Throw(Exception exception)
    {
        _responses.Enqueue(() => throw exception);
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);
        var body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new Sent(request.Method, request.RequestUri!.ToString(), headers, body));
        if (_responses.Count == 0)
            throw new InvalidOperationException($"No scripted response for {request.Method} {request.RequestUri}");
        return _responses.Dequeue()();
    }
}

internal sealed class InMemoryVerificationSettings : IIdentityVerificationSettingsService
{
    public Dictionary<string, IdentityVerificationSettings> ByTenant { get; } = new();

    public Task<IdentityVerificationSettings> GetAsync(string tenantId, CancellationToken cancellationToken = default) =>
        Task.FromResult(ByTenant.TryGetValue(tenantId, out var s) ? s : new IdentityVerificationSettings());

    public Task SaveAsync(string tenantId, IdentityVerificationSettings settings, CancellationToken cancellationToken = default)
    {
        ByTenant[tenantId] = settings;
        return Task.CompletedTask;
    }
}

/// <summary>A business provider whose answers each test scripts; optionally hosted.</summary>
internal class ScriptedBusinessProvider : IBusinessVerificationProvider
{
    public ScriptedBusinessProvider(string id = "scripted", params string[] countries)
    {
        Id = id;
        SupportedBusinessCountries = countries.Length == 0 ? new[] { "NG" } : countries;
    }

    public string Id { get; }
    public string DisplayName => "Scripted " + Id;
    public virtual IReadOnlyList<VerificationCredentialField> CredentialFields { get; } = new[] { new VerificationCredentialField("key", "Key", true) };
    public IReadOnlyList<VerificationProviderOption> Options { get; } = Array.Empty<VerificationProviderOption>();
    public IReadOnlyCollection<string> SupportedBusinessCountries { get; }

    public Func<BusinessLookupRequest, BusinessLookupResult> Answer { get; set; } = _ => throw new InvalidOperationException("No answer scripted");
    public VerificationConnectionCheck Check { get; set; } = new(true, "ok");
    public List<BusinessLookupRequest> Lookups { get; } = new();

    public Task<VerificationConnectionCheck> CheckConnectionAsync(VerificationProviderConnection connection, CancellationToken cancellationToken = default) =>
        Task.FromResult(Check);

    public Task<BusinessLookupResult> LookupBusinessAsync(VerificationProviderConnection connection, BusinessLookupRequest request, CancellationToken cancellationToken = default)
    {
        Lookups.Add(request);
        return Task.FromResult(Answer(request));
    }

    public static BusinessLookupResult Found(string name, BusinessActivityStatus status = BusinessActivityStatus.Active, params BusinessPerson[] people) =>
        BusinessLookupResult.Found(new BusinessRegistryRecord
        {
            RegisteredName = name,
            RegistrationNumber = "RC1234567",
            ActivityStatus = status,
            RawStatus = status == BusinessActivityStatus.Inactive ? "INACTIVE" : "ACTIVE",
            People = people,
        }, "ref-1");
}

/// <summary>A provider with a redirect-style hosted flow and a shared-secret webhook signature.</summary>
internal sealed class ScriptedHostedProvider : ScriptedBusinessProvider, IHostedBusinessVerificationProvider
{
    public const string Secret = "whsec";

    public ScriptedHostedProvider() : base("hosted", "NG") { }

    public IReadOnlyCollection<string> HostedBusinessCountries { get; } = new[] { "NG" };
    public List<HostedSessionRequest> Started { get; } = new();
    public Dictionary<string, HostedSessionResult> Results { get; } = new();
    private int _resultReads;
    public int ResultReads => _resultReads;

    public Task<HostedSessionStart> StartBusinessSessionAsync(VerificationProviderConnection connection, HostedSessionRequest request, CancellationToken cancellationToken = default)
    {
        Started.Add(request);
        var reference = $"sess-{Started.Count}";
        return Task.FromResult(new HostedSessionStart(reference, Launch(reference, request)));
    }

    public HostedSessionLaunch? ResumeBusinessSession(VerificationProviderConnection connection, string reference, HostedSessionRequest request) =>
        Launch(reference, request);

    private static HostedSessionLaunch Launch(string reference, HostedSessionRequest request) => new()
    {
        Kind = HostedSessionLaunchKind.Redirect,
        RedirectUrl = $"https://provider.test/kyb/{reference}?return={Uri.EscapeDataString(request.ReturnUrl)}",
    };

    /// <summary>Awaited before each answer, so a test can hold several readers at the same point.</summary>
    public Func<Task>? BeforeResult { get; set; }

    public async Task<HostedSessionResult> GetBusinessSessionResultAsync(VerificationProviderConnection connection, string reference, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _resultReads);
        if (BeforeResult != null) await BeforeResult();
        return Results.TryGetValue(reference, out var result) ? result : new HostedSessionResult { State = HostedSessionState.Pending };
    }

    public VerificationWebhook ReadWebhook(VerificationProviderConnection connection, IReadOnlyDictionary<string, string> headers, byte[] body) =>
        headers.TryGetValue("x-signature", out var signature) && signature == Secret
            ? new VerificationWebhook(true, Encoding.UTF8.GetString(body))
            : VerificationWebhook.Rejected("bad signature");
}

internal sealed class CapturingSink : IOlusoEventSink
{
    public List<OlusoEvent> Events { get; } = new();

    public Task HandleAsync(OlusoEvent evt, CancellationToken cancellationToken = default)
    {
        lock (Events) Events.Add(evt);
        return Task.CompletedTask;
    }
}

internal sealed class TestClock(DateTimeOffset start) : TimeProvider
{
    private DateTimeOffset _now = start;

    public override DateTimeOffset GetUtcNow() => _now;

    public void Advance(TimeSpan by) => _now += by;
}

/// <summary>An uploader that keeps files in memory.</summary>
internal sealed class InMemoryFileUploader
{
    public Dictionary<string, byte[]> Files { get; } = new();
    public Mock<IFileUploader> Mock { get; } = new();

    public InMemoryFileUploader()
    {
        Mock.SetupGet(u => u.Provider).Returns(FileStorageProvider.Local);
        Mock.Setup(u => u.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string?>(), It.IsAny<IDictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .Returns<string, Stream, string?, IDictionary<string, string>?, CancellationToken>(async (path, content, _, _, ct) =>
            {
                using var buffer = new MemoryStream();
                await content.CopyToAsync(buffer, ct);
                Files[path] = buffer.ToArray();
                return new FileUploadResult { Success = true, StorageReference = path, Provider = FileStorageProvider.Local, SizeBytes = buffer.Length };
            });
        Mock.Setup(u => u.DownloadAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>((path, _) => Task.FromResult(Files.TryGetValue(path, out var bytes) ? bytes : null));
        Mock.Setup(u => u.DownloadStreamAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, CancellationToken>((path, _) => Task.FromResult<Stream?>(Files.TryGetValue(path, out var bytes) ? new MemoryStream(bytes) : null));
    }
}

internal sealed class InMemoryFileAttachmentStore : IFileAttachmentStore
{
    public Dictionary<string, FileAttachment> Rows { get; } = new();

    public Task<FileAttachment> CreateAsync(FileAttachment attachment, CancellationToken cancellationToken = default)
    {
        Rows[attachment.Id] = attachment;
        return Task.FromResult(attachment);
    }

    public Task<FileAttachment> UpdateAsync(FileAttachment attachment, CancellationToken cancellationToken = default)
    {
        Rows[attachment.Id] = attachment;
        return Task.FromResult(attachment);
    }

    public Task<FileAttachment?> GetByIdAsync(string id, CancellationToken cancellationToken = default) =>
        Task.FromResult(Rows.GetValueOrDefault(id));

    public Task<IReadOnlyList<FileAttachment>> GetByEntityAsync(string entityType, string entityId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<FileAttachment>>(Rows.Values.Where(r => r.EntityType == entityType && r.EntityId == entityId && !r.IsDeleted).ToList());

    public Task<IReadOnlyList<FileAttachment>> GetByEntityHashAsync(string entityType, IReadOnlyCollection<string> entityIdHashes, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<FileAttachment>>(Array.Empty<FileAttachment>());

    public Task<IReadOnlyList<FileAttachment>> GetBySubEntityAsync(string entityType, string entityId, string subEntityType, string subEntityId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<FileAttachment>>(Rows.Values.Where(r => r.EntityType == entityType && r.EntityId == entityId && r.SubEntityType == subEntityType && r.SubEntityId == subEntityId).ToList());

    public Task<IReadOnlyList<FileAttachment>> GetPersonalFilesAsync(string userId, string? folder = null, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<FileAttachment>>(Array.Empty<FileAttachment>());

    public Task<IReadOnlyList<string>> GetUserFoldersAsync(string userId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

    public Task<bool> SoftDeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        if (!Rows.TryGetValue(id, out var row)) return Task.FromResult(false);
        row.IsDeleted = true;
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<string>> HardDeleteByEntityAsync(string entityType, string entityId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

    public Task<IReadOnlyList<FileAttachment>> GetAllForEntityAsync(string tenantId, string entityType, string entityId, IReadOnlyCollection<string> entityIdHashes,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<FileAttachment>>(Rows.Values
            .Where(r => r.TenantId == tenantId && r.EntityType == entityType && (r.EntityId == entityId || (r.EntityIdHash != null && entityIdHashes.Contains(r.EntityIdHash)))).ToList());

    public Task<bool> HardDeleteAsync(string id, CancellationToken cancellationToken = default) => Task.FromResult(Rows.Remove(id));

    public Task<IReadOnlyList<FileAttachmentOwner>> GetOwnersAsync(string entityType, FileAttachmentOwner? after, int take, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<FileAttachmentOwner>>(Rows.Values
            .Where(r => r.EntityType == entityType && r.TenantId != null && r.EntityId != null)
            .Select(r => new FileAttachmentOwner(r.TenantId!, r.EntityId!)).Distinct()
            .OrderBy(o => o.TenantId, StringComparer.Ordinal).ThenBy(o => o.EntityId, StringComparer.Ordinal)
            .Where(o => after == null || string.CompareOrdinal(o.TenantId, after.TenantId) > 0
                        || (o.TenantId == after.TenantId && string.CompareOrdinal(o.EntityId, after.EntityId) > 0))
            .Take(take).ToList());
}

/// <summary>
/// Once armed for N parties, holds the next N transactions at their start until all N have
/// arrived, then lets every later transaction through. A decision opens its transaction only
/// after loading what it decides on, so N racing deciders all read the same state first.
/// </summary>
internal sealed class TransactionGate : DbTransactionInterceptor
{
    private readonly object _lock = new();
    private int _waiting;
    private TaskCompletionSource? _released;

    public void Arm(int parties)
    {
        lock (_lock)
        {
            _waiting = parties;
            _released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
        DbConnection connection, TransactionStartingEventData eventData, InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
    {
        Task? wait = null;
        lock (_lock)
        {
            if (_released != null)
            {
                wait = _released.Task;
                if (--_waiting == 0)
                {
                    _released.SetResult();
                    _released = null;
                }
            }
        }
        if (wait != null) await wait.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
        return result;
    }
}

/// <summary>Files posted to a step, built from bytes.</summary>
internal static class Upload
{
    public static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7\n1 0 obj\n<<>>\nendobj\n%%EOF");
    public static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01 };
    public static readonly byte[] Html = Encoding.ASCII.GetBytes("<html><script>alert(1)</script></html>");

    public static JourneyInputFile File(string field, string name, byte[] bytes) =>
        new(field, name, "application/octet-stream", bytes.Length, () => new MemoryStream(bytes));
}

/// <summary>
/// The module's services over a real SQLite database (in memory), real attachment storage
/// on an in-memory uploader, the core journey state store and a capturing event sink.
/// </summary>
internal sealed class VerificationHarness : IDisposable
{
    public const string Tenant = "t1";

    private readonly SqliteConnection _connection;

    public VerificationHarness(params IVerificationProvider[] providers) : this(null, providers)
    {
    }

    public VerificationHarness(Action<IServiceCollection>? configure, params IVerificationProvider[] providers)
    {
        ConnectionString = $"Data Source=kyb-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        _connection = new SqliteConnection(ConnectionString);
        _connection.Open();

        Providers = providers.Length == 0 ? new IVerificationProvider[] { new ScriptedBusinessProvider() } : providers;
        Settings.ByTenant[Tenant] = Routed(Providers[0].Id);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHttpContextAccessor();
        services.AddDbContext<VerificationDbContextSqlite>(o => o.UseSqlite(ConnectionString).AddInterceptors(Gate));
        services.AddScoped<VerificationDbContext>(sp => sp.GetRequiredService<VerificationDbContextSqlite>());
        services.AddScoped<IBusinessVerificationStore, BusinessVerificationStore>();
        services.AddSingleton<IIdentityVerificationSettingsService>(Settings);
        foreach (var provider in Providers) services.AddSingleton<IVerificationProvider>(provider);
        services.AddSingleton<IVerificationProviderRegistry, VerificationProviderRegistry>();
        services.AddScoped<IBusinessVerificationRouter, BusinessVerificationRouter>();
        services.AddSingleton<IOlusoEventSink>(Sink);
        services.AddSingleton<IOlusoEventService, OlusoEventService>();
        services.AddSingleton(Uploader.Mock.Object);
        services.AddSingleton<IFileAttachmentStore>(Attachments);
        services.AddSingleton(Mock.Of<IEntityReferenceProtector>());
        services.AddScoped<IFileAttachmentService, FileAttachmentService>();
        services.AddScoped<IJourneyFileUploadService, JourneyFileUploadService>();
        services.AddSingleton<IJourneyStateStore>(Journeys);
        services.AddSingleton(Email.Object);
        services.AddSingleton(Users.Object);
        services.AddSingleton<IProfileService>(Profiles.Object);
        services.AddScoped<BusinessVerificationNotifier>();
        services.AddSingleton<TimeProvider>(Clock);
        services.AddScoped<IBusinessVerificationService>(sp => ActivatorUtilities.CreateInstance<BusinessVerificationService>(sp, Clock));
        configure?.Invoke(services);
        Services = services.BuildServiceProvider();

        using var scope = Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<VerificationDbContext>().Database.EnsureCreated();
    }

    /// <summary>A named in-memory database kept alive by the harness; every context opens its own connection to it.</summary>
    public string ConnectionString { get; }

    /// <summary>Holds armed transactions until all parties arrive; see <see cref="TransactionGate"/>.</summary>
    public TransactionGate Gate { get; } = new();

    public IVerificationProvider[] Providers { get; }
    public InMemoryVerificationSettings Settings { get; } = new();
    public CapturingSink Sink { get; } = new();
    public InMemoryFileUploader Uploader { get; } = new();
    public InMemoryFileAttachmentStore Attachments { get; } = new();
    public InMemoryJourneyStateStore Journeys { get; } = new();
    public Mock<IEmailSender> Email { get; } = new();
    public Mock<IOlusoUserService> Users { get; } = new();
    public Mock<IExtendedProfileService> Profiles { get; } = new();
    public TestClock Clock { get; } = new(new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero));
    public ServiceProvider Services { get; }

    public ScriptedBusinessProvider Provider => (ScriptedBusinessProvider)Providers[0];

    /// <summary>A fresh scope, like one request.</summary>
    public IServiceScope Scope() => Services.CreateScope();

    public IBusinessVerificationService Service(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<IBusinessVerificationService>();

    public IBusinessVerificationStore Store(IServiceScope scope) => scope.ServiceProvider.GetRequiredService<IBusinessVerificationStore>();

    public IEnumerable<T> Events<T>() where T : OlusoEvent => Sink.Events.OfType<T>();

    public static IdentityVerificationSettings Routed(string providerId = "scripted", string country = "NG", int reuseDays = 0) => new()
    {
        Providers =
        {
            [providerId] = new VerificationProviderSettings
            {
                Enabled = true,
                Environment = VerificationEnvironment.Sandbox,
                Credentials = { ["key"] = "secret" },
            },
        },
        BusinessRouting = { [country] = providerId },
        BusinessReuseMaxAgeDays = reuseDays,
    };

    public static BusinessVerificationRequest Request(string? subject = "user-1", string journey = "j1", string number = "RC1234567",
        string name = "Acme Foods Ltd", ApplicantEvidenceRequirement evidence = ApplicantEvidenceRequirement.None, ApplicantName? applicant = null,
        BusinessVerificationMode mode = BusinessVerificationMode.Lookup, IReadOnlyList<AcceptedJourneyFile>? documents = null) => new()
    {
        TenantId = Tenant,
        SubjectId = subject,
        JourneyId = journey,
        StepId = "kyb",
        Country = "NG",
        RegistrationNumber = number,
        BusinessName = name,
        RequireNameMatch = true,
        Evidence = evidence,
        Applicant = applicant,
        DirectorRoles = new[] { "director", "proprietor" },
        Mode = mode,
        ReturnUrl = mode == BusinessVerificationMode.Hosted ? $"https://acme.test/journey/{journey}/callback" : null,
        Documents = documents ?? Array.Empty<AcceptedJourneyFile>(),
    };

    public void Dispose()
    {
        Services.Dispose();
        _connection.Dispose();
    }
}

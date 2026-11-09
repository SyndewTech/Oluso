using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Core.Storage;
using Oluso.Core.UserJourneys;
using Oluso.Storage;
using Oluso.UserJourneys.Steps;
using Xunit;

namespace Oluso.Tests.UserJourneys;

/// <summary>Files posted to journey steps: content checks, storage, the form's file field and who may read them.</summary>
public class JourneyFileUploadTests
{
    private const string Tenant = "t1";

    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.4\n%%EOF");
    private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 0x49, 0x48, 0x44, 0x52 };
    private static readonly byte[] Jpeg = { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 0x4A, 0x46, 0x49, 0x46 };
    private static readonly byte[] Svg = Encoding.ASCII.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>");

    private readonly Dictionary<string, FileAttachment> _rows = new();
    private readonly Dictionary<string, byte[]> _blobs = new();
    private readonly Mock<IExtendedProfileService> _profiles = new();

    private static JourneyInputFile File(string field, string name, byte[] bytes, long? length = null) =>
        new(field, name, "application/octet-stream", length ?? bytes.Length, () => new MemoryStream(bytes));

    private static readonly JourneyUploadPolicy DocumentsOnly = new()
    {
        AllowedKinds = new[] { FileContentSniffer.Pdf, FileContentSniffer.Jpeg, FileContentSniffer.Png },
        MaxBytes = 1024,
        MaxFiles = 2,
    };

    private IServiceProvider Services()
    {
        var store = new Mock<IFileAttachmentStore>();
        store.Setup(s => s.CreateAsync(It.IsAny<FileAttachment>(), It.IsAny<CancellationToken>()))
            .Returns<FileAttachment, CancellationToken>((a, _) => { _rows[a.Id] = a; return Task.FromResult(a); });
        store.Setup(s => s.UpdateAsync(It.IsAny<FileAttachment>(), It.IsAny<CancellationToken>()))
            .Returns<FileAttachment, CancellationToken>((a, _) => { _rows[a.Id] = a; return Task.FromResult(a); });
        store.Setup(s => s.GetByEntityAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<string, string, CancellationToken>((type, id, _) =>
                Task.FromResult<IReadOnlyList<FileAttachment>>(_rows.Values.Where(r => r.EntityType == type && r.EntityId == id).ToList()));

        var uploader = new Mock<IFileUploader>();
        uploader.SetupGet(u => u.Provider).Returns(FileStorageProvider.Local);
        uploader.Setup(u => u.UploadAsync(It.IsAny<string>(), It.IsAny<Stream>(), It.IsAny<string?>(), It.IsAny<IDictionary<string, string>?>(), It.IsAny<CancellationToken>()))
            .Returns<string, Stream, string?, IDictionary<string, string>?, CancellationToken>(async (path, content, _, _, ct) =>
            {
                using var buffer = new MemoryStream();
                await content.CopyToAsync(buffer, ct);
                _blobs[path] = buffer.ToArray();
                return new FileUploadResult { Success = true, StorageReference = path, SizeBytes = buffer.Length };
            });

        _profiles.Setup(p => p.GetUserPermissionsAsync(It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<string>());
        _profiles.Setup(p => p.GetUserPermissionsAsync("reviewer", Tenant, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { "submissions.read" });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(store.Object);
        services.AddSingleton(uploader.Object);
        services.AddSingleton(Mock.Of<IEntityReferenceProtector>());
        services.AddSingleton<IProfileService>(_profiles.Object);
        services.AddScoped<IFileAttachmentService, FileAttachmentService>();
        services.AddScoped<IJourneyFileUploadService, JourneyFileUploadService>();
        return services.BuildServiceProvider();
    }

    // ── content checks ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("pdf")]
    [InlineData("png")]
    [InlineData("jpeg")]
    public void Documents_and_photos_are_recognised_by_their_bytes(string kind)
    {
        var bytes = kind switch { "pdf" => Pdf, "png" => Png, _ => Jpeg };
        FileContentSniffer.Sniff(bytes)!.Kind.Should().Be(kind);
    }

    [Fact]
    public void Active_content_is_never_recognised()
    {
        FileContentSniffer.Sniff(Svg).Should().BeNull();
        FileContentSniffer.Sniff(Encoding.ASCII.GetBytes("<html><body>hi</body></html>")).Should().BeNull();
        FileContentSniffer.Sniff(Array.Empty<byte>()).Should().BeNull();
    }

    [Fact]
    public async Task Files_are_accepted_or_refused_by_content_size_count_and_extension()
    {
        var uploads = Services().GetRequiredService<IJourneyFileUploadService>();

        async Task<string?> Check(params JourneyInputFile[] files) => await uploads.ValidateAsync(files, DocumentsOnly, new List<AcceptedJourneyFile>());

        (await Check(File("f", "letter.pdf", Pdf), File("f", "photo.JPG", Jpeg))).Should().BeNull();
        (await Check(File("f", "drawing.svg", Svg))).Should().Be(JourneyUploadErrors.TypeNotAllowed);
        (await Check(File("f", "invoice.pdf", Png))).Should().Be(JourneyUploadErrors.ExtensionMismatch);
        (await Check(File("f", "letter.pdf", Pdf, length: 2048))).Should().Be(JourneyUploadErrors.TooLarge);
        (await Check(File("f", "empty.pdf", Array.Empty<byte>()))).Should().Be(JourneyUploadErrors.Empty);
        (await Check(File("f", "a.pdf", Pdf), File("f", "b.pdf", Pdf), File("f", "c.pdf", Pdf))).Should().Be(JourneyUploadErrors.TooMany);
        (await uploads.ValidateAsync(new[] { File("f", "photo.webp", Encoding.ASCII.GetBytes("RIFF\0\0\0\0WEBPVP8 ")) }, DocumentsOnly, new()))
            .Should().Be(JourneyUploadErrors.TypeNotAllowed, "webp isn't in the policy");
    }

    [Fact]
    public async Task Stored_files_get_the_sniffed_type_a_tenant_path_and_a_clean_name()
    {
        var uploads = Services().GetRequiredService<IJourneyFileUploadService>();
        var accepted = new List<AcceptedJourneyFile>();
        await uploads.ValidateAsync(new[] { File("f", "..\\..\\etc/My \"CAC\" cert.pdf", Pdf) }, DocumentsOnly, accepted);

        var saved = await uploads.SaveAsync(new JourneyUploadRequest
        {
            TenantId = Tenant, JourneyId = "j1", UserId = null, File = accepted.Single(),
            EntityType = JourneyUploads.EntityType, EntityId = "j1",
        });

        saved.ContentType.Should().Be("application/pdf");
        saved.FileName.Should().Be("My CAC cert.pdf");
        saved.StoragePath.Should().StartWith($"attachments/{Tenant}/{JourneyUploads.EntityType}/").And.EndWith(".pdf");
        saved.UploadedByUserId.Should().Be(JourneyUploads.PendingUploader("j1"));
        _blobs[saved.StoragePath].Should().Equal(Pdf);
    }

    // ── the form's file field ──────────────────────────────────────────────

    private static StepExecutionContext FormContext(IServiceProvider services, Dictionary<string, object>? values, IReadOnlyList<JourneyInputFile>? files,
        Dictionary<string, object>? data = null, string? userId = "user-1") => new()
    {
        JourneyId = "j1",
        StepId = "form",
        TenantId = Tenant,
        UserId = userId,
        ServiceProvider = services,
        JourneyData = data ?? new Dictionary<string, object>(),
        Input = values == null ? null : new JourneyStepInput { StepId = "form", Values = values, Files = files },
        Configuration = new JourneyStepConfiguration
        {
            Id = "form",
            Type = "dynamic_form",
            Settings = JsonSerializer.Deserialize<Dictionary<string, object>>("""
                { "fields": [ { "name": "certificate", "type": "file", "label": "Certificate", "required": true, "fileTypes": ["pdf"], "maxFileSizeMb": 1, "maxFiles": 1, "capture": true } ] }
                """),
        },
    };

    [Fact]
    public async Task A_file_field_renders_with_its_accept_list_and_camera_option()
    {
        var result = await new DynamicFormStepHandler().ExecuteAsync(FormContext(Services(), null, null));

        var field = ((DynamicFormViewModel)result.StepResult!.ViewModel!).Fields.Single();
        field.Type.Should().Be("file");
        field.FileAccept.Should().Be("application/pdf,.pdf");
        field.FileCapture.Should().BeTrue();
        field.MaxFileSizeMb.Should().Be(1);
    }

    [Fact]
    public async Task A_posted_file_is_stored_and_its_attachment_id_becomes_the_value()
    {
        var services = Services();
        var context = FormContext(services, new() { ["__submitted"] = "true" }, new[] { File("certificate", "cert.pdf", Pdf) });

        var result = await new DynamicFormStepHandler().ExecuteAsync(context);

        result.Outcome.Should().Be(StepOutcome.Continue);
        var id = context.GetDataString("certificate");
        _rows.Should().ContainKey(id!);
        _rows[id!].Should().Match<FileAttachment>(a => a.EntityType == JourneyUploads.EntityType && a.EntityId == "j1"
            && a.SubEntityId == "certificate" && a.UploadedByUserId == "user-1" && a.ContentType == "application/pdf");
    }

    [Fact]
    public async Task A_required_file_field_without_a_file_or_with_a_bad_one_shows_the_form_again()
    {
        var missing = await new DynamicFormStepHandler().ExecuteAsync(FormContext(Services(), new() { ["__submitted"] = "true" }, null));
        ((DynamicFormViewModel)missing.StepResult!.ViewModel!).Fields.Single().Error.Should().NotBeNull();

        var wrong = await new DynamicFormStepHandler().ExecuteAsync(
            FormContext(Services(), new() { ["__submitted"] = "true" }, new[] { File("certificate", "cert.png", Png) }));
        wrong.Outcome.Should().Be(StepOutcome.RequireInput);
        ((DynamicFormViewModel)wrong.StepResult!.ViewModel!).Fields.Single().Error.Should().Contain("PDF");
        _rows.Should().BeEmpty();
    }

    [Fact]
    public async Task A_file_kept_from_an_earlier_submission_satisfies_a_required_field()
    {
        var result = await new DynamicFormStepHandler().ExecuteAsync(
            FormContext(Services(), new() { ["__submitted"] = "true" }, null, new Dictionary<string, object> { ["certificate"] = "earlier-id" }));

        result.Outcome.Should().Be(StepOutcome.Continue);
    }

    // ── who may read them ──────────────────────────────────────────────────

    [Fact]
    public async Task The_uploader_and_submission_readers_can_read_journey_uploads()
    {
        var services = Services();
        await new DynamicFormStepHandler().ExecuteAsync(
            FormContext(services, new() { ["__submitted"] = "true" }, new[] { File("certificate", "cert.pdf", Pdf) }));
        var provider = ActivatorUtilities.CreateInstance<JourneyUploadFileAccessProvider>(services);

        (await provider.CanReadAsync(Tenant, "user-1", "j1", null, null)).Should().BeTrue();
        (await provider.CanReadAsync(Tenant, "reviewer", "j1", null, null)).Should().BeTrue();
        (await provider.CanReadAsync(Tenant, "someone-else", "j1", null, null)).Should().BeFalse();
        (await provider.CanDeleteAsync(Tenant, "user-1", _rows.Values.Single())).Should().BeFalse();
    }

    [Fact]
    public async Task Files_posted_before_sign_up_belong_to_the_user_the_journey_signs_up()
    {
        var services = Services();
        await new DynamicFormStepHandler().ExecuteAsync(
            FormContext(services, new() { ["__submitted"] = "true" }, new[] { File("certificate", "cert.pdf", Pdf) }, userId: null));
        _rows.Values.Single().UploadedByUserId.Should().Be(JourneyUploads.PendingUploader("j1"));

        await ActivatorUtilities.CreateInstance<JourneyUploadUserAttachedHandler>(services)
            .OnUserAttachedAsync(new JourneyUserAttached("j1", Tenant, "new-user", new Dictionary<string, object>()));

        _rows.Values.Single().UploadedByUserId.Should().Be("new-user");
    }

    // ── journey engine hooks ───────────────────────────────────────────────

    [Fact]
    public async Task The_status_revision_counts_bumps_on_running_journeys_only()
    {
        var store = new InMemoryJourneyStateStore();
        var running = new JourneyState { Id = "a", TenantId = Tenant, ClientId = "c", PolicyId = "p", CurrentStepId = "s", Status = JourneyStatus.InProgress };
        await store.SaveAsync(running);
        await store.SaveAsync(running with { Id = "b", Status = JourneyStatus.Completed });

        (await JourneyStatusRevision.BumpAsync(store, "a")).Should().BeTrue();
        (await JourneyStatusRevision.BumpAsync(store, "a")).Should().BeTrue();
        (await JourneyStatusRevision.BumpAsync(store, "b")).Should().BeFalse();
        (await JourneyStatusRevision.BumpAsync(store, "missing")).Should().BeFalse();
        JourneyStatusRevision.Read((await store.GetAsync("a"))!.Data).Should().Be(2);
    }

    private sealed class SignUpStep : IStepHandler
    {
        public string StepType => "sign_up_test";

        public Task<StepHandlerResult> ExecuteAsync(StepExecutionContext context, CancellationToken cancellationToken = default)
        {
            context.SetAuthenticated("new-user", "pwd");
            return Task.FromResult(StepHandlerResult.ShowUi("done"));
        }
    }

    private sealed class RecordingAttachHandler : IJourneyUserAttachedHandler
    {
        public List<JourneyUserAttached> Calls { get; } = new();

        public Task OnUserAttachedAsync(JourneyUserAttached attached, CancellationToken cancellationToken = default)
        {
            Calls.Add(attached);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task The_orchestrator_announces_the_user_a_journey_signs_up()
    {
        var recorder = new RecordingAttachHandler();
        var policies = new InMemoryJourneyPolicyStore();
        await policies.SaveAsync(new JourneyPolicy
        {
            Id = "p1", Slug = "p1", Name = "Sign up", TenantId = Tenant,
            Steps = new List<JourneyPolicyStep> { new() { Id = "s1", Type = "sign_up_test", Order = 1 } },
        });
        var states = new InMemoryJourneyStateStore();
        await states.SaveAsync(new JourneyState
        {
            Id = "j1", TenantId = Tenant, ClientId = "c", PolicyId = "p1", CurrentStepId = "s1",
            Status = JourneyStatus.InProgress, ExpiresAt = DateTime.UtcNow.AddHours(1), Data = new Dictionary<string, object> { ["x"] = "y" },
        });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IStepHandler, SignUpStep>();
        services.AddSingleton<IJourneyUserAttachedHandler>(recorder);
        var sp = services.BuildServiceProvider();
        var orchestrator = new DefaultJourneyOrchestrator(sp, policies, states, new DefaultStepHandlerRegistry(sp),
            new DefaultConditionEvaluator(), Microsoft.Extensions.Logging.Abstractions.NullLogger<DefaultJourneyOrchestrator>.Instance);

        await orchestrator.ContinueJourneyAsync("j1", new JourneyStepInput { StepId = "s1" });
        await orchestrator.ContinueJourneyAsync("j1", new JourneyStepInput { StepId = "s1" });

        recorder.Calls.Should().ContainSingle("only the first time the journey learns its user");
        recorder.Calls[0].Should().Match<JourneyUserAttached>(c => c.JourneyId == "j1" && c.UserId == "new-user" && c.TenantId == Tenant);
        recorder.Calls[0].JourneyData.Should().ContainKey("x");
    }
}

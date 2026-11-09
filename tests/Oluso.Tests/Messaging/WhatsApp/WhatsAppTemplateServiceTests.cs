using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Messaging.WhatsApp;
using Xunit;

namespace Oluso.Tests.Messaging.WhatsApp;

public class WhatsAppTemplateServiceTests
{
    private const string Tenant = WhatsAppRig.Tenant;
    private readonly RecordingHandler _http = new();
    private readonly InMemoryTemplateStore _templates = new();
    private readonly InMemoryAccountStore _accounts = new();

    private WhatsAppTemplateService Service(ResolvedWhatsAppSettings? settings)
    {
        var resolver = new Mock<IWhatsAppSettingsResolver>();
        resolver.Setup(r => r.ResolveAsync(Tenant, It.IsAny<CancellationToken>())).ReturnsAsync(settings);
        return new WhatsAppTemplateService(WhatsAppRig.Graph(_http), resolver.Object, _templates, _accounts, NullLogger<WhatsAppTemplateService>.Instance);
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("Hello", 0)]
    [InlineData("Hi {{1}}, your order {{2}} ships {{3}}", 3)]
    [InlineData("{{ 2 }} then {{1}}", 2)]
    public void Counts_positional_body_placeholders(string? body, int expected)
    {
        WhatsAppTemplateService.CountPlaceholders(body).Should().Be(expected);
    }

    [Fact]
    public async Task Sync_follows_paging_and_replaces_the_registry()
    {
        _templates.Rows.Add(new Oluso.Core.Domain.Entities.WhatsAppTemplate { TenantId = Tenant, ProviderTemplateId = "old", Name = "gone", Language = "en", Status = "APPROVED" });
        _http.Enqueue(HttpStatusCode.OK, """
            {"data":[{"id":"1","name":"order_update","language":"en","status":"APPROVED","category":"UTILITY",
                      "components":[{"type":"HEADER","format":"TEXT","text":"Order {{1}}"},{"type":"BODY","text":"Hi {{1}}, order {{2}} is ready"},{"type":"FOOTER","text":"Thanks"}]}],
             "paging":{"cursors":{"after":"A"},"next":"https://graph.facebook.com/v21.0/WABA1/message_templates?after=A"}}
            """)
             .Enqueue(HttpStatusCode.OK, """
            {"data":[{"id":"2","name":"otp","language":"en_GB","status":"PENDING","category":"AUTHENTICATION","components":[{"type":"BODY","text":"{{1}} is your code"}]}],
             "paging":{"cursors":{"after":"B"}}}
            """);

        var (ok, error, count) = await Service(WhatsAppRig.Settings()).SyncAsync(Tenant);

        ok.Should().BeTrue(error);
        count.Should().Be(2);
        _http.Requests.Should().HaveCount(2);
        _http.Requests[0].Url.Should().StartWith("https://graph.facebook.com/v21.0/WABA1/message_templates?fields=");
        _http.Requests[1].Url.Should().Be("https://graph.facebook.com/v21.0/WABA1/message_templates?after=A");

        _templates.Rows.Should().HaveCount(2).And.NotContain(t => t.Name == "gone");
        var order = _templates.Rows.Single(t => t.Name == "order_update");
        order.BodyParameterCount.Should().Be(2);
        order.HeaderFormat.Should().Be("TEXT");
        order.IsApproved.Should().BeTrue();
        _templates.Rows.Single(t => t.Name == "otp").IsApproved.Should().BeFalse();
    }

    [Fact]
    public async Task Sync_needs_a_business_account_id()
    {
        var settings = WhatsAppRig.Settings() with { BusinessAccountId = null };

        var (ok, error, _) = await Service(settings).SyncAsync(Tenant);

        ok.Should().BeFalse();
        error.Should().Contain("Business Account ID");
        _http.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Verify_account_calls_meta_and_records_the_phone_number_mapping()
    {
        _http.Enqueue(HttpStatusCode.OK, """{"display_phone_number":"+1 555-000-1111","verified_name":"Oluso Clinic","quality_rating":"GREEN","id":"PN123"}""");

        var check = await Service(WhatsAppRig.Settings()).VerifyAccountAsync(Tenant);

        check.Success.Should().BeTrue(check.Error);
        check.Account!.VerifiedName.Should().Be("Oluso Clinic");
        _http.Requests.Single().Url.Should().StartWith("https://graph.facebook.com/v21.0/PN123?fields=");
        var row = _accounts.Rows.Should().ContainSingle().Subject;
        row.TenantId.Should().Be(Tenant);
        row.PhoneNumberId.Should().Be("PN123");
        row.LastVerifiedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Verify_account_reports_bad_tokens_in_metas_words()
    {
        _http.Enqueue(HttpStatusCode.Unauthorized, """{"error":{"message":"Invalid OAuth access token - Cannot parse access token","type":"OAuthException","code":190}}""");

        var check = await Service(WhatsAppRig.Settings()).VerifyAccountAsync(Tenant);

        check.Success.Should().BeFalse();
        check.Error.Should().Contain("190").And.Contain("Invalid OAuth access token");
        _accounts.Rows.Should().BeEmpty();
    }
}

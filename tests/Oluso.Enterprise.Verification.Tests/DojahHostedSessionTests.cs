using System.Net;
using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Oluso.Enterprise.Verification.Providers;
using Xunit;

namespace Oluso.Enterprise.Verification.Tests;

/// <summary>
/// Dojah's hosted (EasyOnboard widget) sessions against its documented contract:
/// docs.dojah.io/api-reference/widget-sdks/web-javascript (widget options),
/// /api-reference/verifications/get-verification (result), /api-reference/core-concepts/webhooks-signatures.
/// </summary>
public class DojahHostedSessionTests
{
    private const string WebhookSecret = "dojah-webhook-secret";

    private readonly FakeHttp _http = new();

    private DojahVerificationProvider Provider() => new(new HttpClient(_http), NullLogger<DojahVerificationProvider>.Instance);

    private static VerificationProviderConnection Connection(bool hosted = true) =>
        new("t1", VerificationEnvironment.Sandbox,
            hosted
                ? new Dictionary<string, string>
                {
                    ["appId"] = "app-1", ["secretKey"] = "sk_test", ["publicKey"] = "pk_test", ["widgetId"] = "widget-9", ["webhookSecret"] = WebhookSecret,
                }
                : new Dictionary<string, string> { ["appId"] = "app-1", ["secretKey"] = "sk_test" },
            new Dictionary<string, string> { ["lookup"] = "advance" });

    private static HostedSessionRequest Request() => new()
    {
        Country = "NG",
        RegistrationNumber = "RC1234567",
        ReturnUrl = "https://acme.test/journey/j1/callback",
        Metadata = new Dictionary<string, string> { ["applicant_id"] = "a1" },
    };

    [Fact]
    public void Hosted_fields_are_optional_so_lookups_work_without_them()
    {
        Provider().CredentialFields.Where(f => !f.Required).Select(f => f.Name)
            .Should().BeEquivalentTo("publicKey", "widgetId", "webhookSecret");
        Provider().HostedBusinessCountries.Should().Equal("NG");
    }

    [Fact]
    public async Task A_session_opens_the_EasyOnboard_widget_with_our_reference()
    {
        var start = await Provider().StartBusinessSessionAsync(Connection(), Request());

        start.Reference.Length.Should().BeGreaterThanOrEqualTo(10, "Dojah requires reference_id of at least 10 characters");
        start.Launch.Kind.Should().Be(HostedSessionLaunchKind.Widget);
        start.Launch.WidgetScriptUrl.Should().Be("https://widget.dojah.io/widget.js");
        var options = start.Launch.WidgetOptions!;
        options["app_id"].Should().Be("app-1");
        options["p_key"].Should().Be("pk_test");
        options["type"].Should().Be("custom");
        ((IDictionary<string, object>)options["config"])["widget_id"].Should().Be("widget-9");
        options["reference_id"].Should().Be(start.Reference);
        options.Should().NotContainKey("secretKey");
        options.Values.OfType<string>().Should().NotContain(new[] { "sk_test", WebhookSecret }, "secrets never reach the browser");
        _http.Requests.Should().BeEmpty();

        var reopened = Provider().ResumeBusinessSession(Connection(), start.Reference, Request());
        reopened!.WidgetOptions!["reference_id"].Should().Be(start.Reference);
    }

    [Fact]
    public async Task A_session_without_the_hosted_credentials_is_a_configuration_error()
    {
        await Provider().Invoking(p => p.StartBusinessSessionAsync(Connection(hosted: false), Request()))
            .Should().ThrowAsync<VerificationConfigurationException>();
    }

    [Fact]
    public async Task The_outcome_is_read_from_the_verification_endpoint()
    {
        _http.Respond(HttpStatusCode.OK, """
        {
          "entity": {
            "reference_id": "kyb0123456789",
            "status": true,
            "verification_status": "Completed",
            "verification_type": "RC-NUMBER",
            "data": {
              "business_data": {
                "status": true,
                "message": "Successful",
                "data": { "company_name": "ACME FOODS LIMITED", "rc_number": "1234567", "address": "1 Marina, Lagos", "status": "ACTIVE", "type_of_company": "COMPANY" }
              }
            }
          }
        }
        """);

        var result = await Provider().GetBusinessSessionResultAsync(Connection(), "kyb0123456789");

        var sent = _http.Requests.Single();
        sent.Method.Should().Be(HttpMethod.Get);
        sent.Url.Should().Be("https://sandbox.dojah.io/api/v1/kyc/verification?reference_id=kyb0123456789");
        sent.Headers["Authorization"].Should().Be("sk_test");
        sent.Headers["AppId"].Should().Be("app-1");
        result.State.Should().Be(HostedSessionState.Approved);
        result.ProviderReference.Should().Be("kyb0123456789");
        result.Record!.RegisteredName.Should().Be("ACME FOODS LIMITED");
        result.Record.ActivityStatus.Should().Be(BusinessActivityStatus.Active);
        result.Record.Address.Should().Be("1 Marina, Lagos");
    }

    [Theory]
    [InlineData("Completed", "false", HostedSessionState.Rejected)]
    [InlineData("Failed", "false", HostedSessionState.Rejected)]
    [InlineData("Abandoned", "false", HostedSessionState.Abandoned)]
    [InlineData("Ongoing", "false", HostedSessionState.Pending)]
    [InlineData("Pending", "false", HostedSessionState.Pending)]
    public async Task Verification_statuses_map_to_session_states(string status, string passed, HostedSessionState expected)
    {
        _http.Respond(HttpStatusCode.OK, $$"""{ "reference_id": "kyb0123456789", "status": {{passed}}, "verification_status": "{{status}}" }""");

        var result = await Provider().GetBusinessSessionResultAsync(Connection(), "kyb0123456789");

        result.State.Should().Be(expected);
        result.Record.Should().BeNull();
    }

    [Fact]
    public async Task No_record_yet_waits_and_a_failing_provider_is_unavailable()
    {
        _http.Respond(HttpStatusCode.NotFound, """{ "error": "not found" }""")
            .Respond(HttpStatusCode.ServiceUnavailable, "")
            .Throw(new HttpRequestException("connection reset"));

        (await Provider().GetBusinessSessionResultAsync(Connection(), "kyb0123456789")).State.Should().Be(HostedSessionState.Pending);
        (await Provider().GetBusinessSessionResultAsync(Connection(), "kyb0123456789")).State.Should().Be(HostedSessionState.Unavailable);
        (await Provider().GetBusinessSessionResultAsync(Connection(), "kyb0123456789")).State.Should().Be(HostedSessionState.Unavailable);
    }

    [Fact]
    public void A_webhook_signed_with_the_webhook_secret_is_authentic()
    {
        var body = Encoding.UTF8.GetBytes("""{"reference_id":"kyb0123456789","verification_status":"Completed","status":true}""");
        var signature = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(WebhookSecret), body)).ToLowerInvariant();

        var webhook = Provider().ReadWebhook(Connection(), new Dictionary<string, string> { ["X-Dojah-Signature"] = signature }, body);

        webhook.Authentic.Should().BeTrue();
        webhook.SessionReference.Should().Be("kyb0123456789");
    }

    [Fact]
    public void A_webhook_with_a_wrong_or_missing_signature_or_no_secret_is_rejected()
    {
        var body = Encoding.UTF8.GetBytes("""{"reference_id":"kyb0123456789"}""");
        var forged = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes("someone-else"), body)).ToLowerInvariant();
        var tampered = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(WebhookSecret), Encoding.UTF8.GetBytes("{}"))).ToLowerInvariant();

        Provider().ReadWebhook(Connection(), new Dictionary<string, string> { ["x-dojah-signature"] = forged }, body).Authentic.Should().BeFalse();
        Provider().ReadWebhook(Connection(), new Dictionary<string, string> { ["x-dojah-signature"] = tampered }, body).Authentic.Should().BeFalse();
        Provider().ReadWebhook(Connection(), new Dictionary<string, string>(), body).Authentic.Should().BeFalse();
        Provider().ReadWebhook(Connection(hosted: false), new Dictionary<string, string> { ["x-dojah-signature"] = forged }, body).Authentic.Should().BeFalse();
    }
}

using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Oluso.Enterprise.Verification;
using Oluso.Enterprise.Verification.Providers;
using Xunit;

namespace Oluso.Enterprise.Verification.Tests;

/// <summary>
/// Dojah CAC lookups against the documented request/response shapes
/// (docs.dojah.io/docs/nigeria/lookup-cac, /api-reference/wallet-utilities/get-balance).
/// </summary>
public class DojahVerificationProviderTests
{
    private const string AdvanceBody = """
    {
      "entity": {
        "company_name": "JOHN DOE LIMITED",
        "rc_number": "1234567",
        "address": "John doe street opposite dunamis church",
        "state": "Plateau",
        "city": "Jos Town (Capital)",
        "lga": "Jos south",
        "email": "abc@gmail.com",
        "type_of_company": "BUSINESS_NAME",
        "date_of_registration": "2024-07-19T08:00:06.224+00:00",
        "nature_of_business": null,
        "share_capital": null,
        "share_details": {},
        "affiliates": [
          { "first_name": "JOHN", "last_name": "MUSA", "affiliate_type": "PROPRIETOR", "gender": "MALE", "phone_number": "+2348132464910", "nationality": "Nigeria", "country": "NIGERIA" }
        ]
      }
    }
    """;

    private const string BasicBody = """
    {
      "entity": {
        "company_name": "JOHN DOE LIMITED",
        "type_of_company": "BUSINESS_NAME",
        "status": "Not Active",
        "rc_number": "1234567",
        "business_number": "1234567",
        "state": "Lagos",
        "city": "Lagos",
        "lga": "Kosofe",
        "business": "7a666454-8ce0-44e2-ac32-3e8cb04b6b72"
      }
    }
    """;

    private readonly FakeHttp _http = new();

    private DojahVerificationProvider Provider() =>
        new(new HttpClient(_http), NullLogger<DojahVerificationProvider>.Instance);

    private static VerificationProviderConnection Connection(VerificationEnvironment env = VerificationEnvironment.Sandbox, string lookup = "advance") =>
        new("t1", env,
            new Dictionary<string, string> { ["appId"] = "app-123", ["secretKey"] = "prod_sk_abc" },
            new Dictionary<string, string> { ["lookup"] = lookup });

    private static BusinessLookupRequest Request(string number = "RC1234567", string type = "RC") =>
        new() { Country = "NG", RegistrationNumber = number, RegistrationType = type, BusinessName = "John Doe Ltd" };

    [Fact]
    public async Task Advance_lookup_sends_the_documented_request_and_maps_affiliates()
    {
        _http.Respond(HttpStatusCode.OK, AdvanceBody);

        var result = await Provider().LookupBusinessAsync(Connection(), Request());

        var sent = _http.Requests.Single();
        sent.Method.Should().Be(HttpMethod.Get);
        sent.Url.Should().Be("https://sandbox.dojah.io/api/v1/kyc/cac/advance?rc_number=1234567&company_type=COMPANY");
        sent.Headers["Authorization"].Should().Be("prod_sk_abc", "Dojah wants the raw secret key, no Bearer prefix");
        sent.Headers["AppId"].Should().Be("app-123");

        result.Outcome.Should().Be(BusinessLookupOutcome.Found);
        result.Record!.RegisteredName.Should().Be("JOHN DOE LIMITED");
        result.Record.RegistrationNumber.Should().Be("RC1234567");
        result.Record.EntityType.Should().Be("BUSINESS_NAME");
        result.Record.RegistrationDate.Should().Be("2024-07-19T08:00:06.224+00:00");
        result.Record.Address.Should().Be("John doe street opposite dunamis church");
        result.Record.ActivityStatus.Should().Be(BusinessActivityStatus.Unknown, "the advance response carries no status");
        result.Record.People.Should().ContainSingle().Which.Should().Be(new BusinessPerson("JOHN MUSA", "PROPRIETOR"));
    }

    [Fact]
    public async Task Basic_lookup_on_live_reads_the_status_and_builds_the_location()
    {
        _http.Respond(HttpStatusCode.OK, BasicBody);

        var result = await Provider().LookupBusinessAsync(Connection(VerificationEnvironment.Live, "basic"), Request("BN1234567", "BN"));

        _http.Requests.Single().Url.Should().Be("https://api.dojah.io/api/v1/kyc/cac/basic?rc_number=1234567&company_type=BUSINESS_NAME");
        result.Record!.ActivityStatus.Should().Be(BusinessActivityStatus.Inactive);
        result.Record.RawStatus.Should().Be("Not Active");
        result.Record.Address.Should().Be("Kosofe, Lagos, Lagos");
        result.ProviderReference.Should().Be("7a666454-8ce0-44e2-ac32-3e8cb04b6b72");
    }

    [Theory]
    [InlineData("IT", "INCORPORATED_TRUSTEES")]
    [InlineData("LP", "LIMITED_PARTNERSHIP")]
    [InlineData("LLP", "LIMITED_LIABILITY_PARTNERSHIP")]
    public async Task Registration_types_map_to_company_type(string type, string companyType)
    {
        _http.Respond(HttpStatusCode.OK, AdvanceBody);

        await Provider().LookupBusinessAsync(Connection(), Request(type + "998877", type));

        _http.Requests.Single().Url.Should().EndWith($"rc_number=998877&company_type={companyType}");
    }

    [Theory]
    [InlineData(HttpStatusCode.NotFound, BusinessLookupOutcome.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized, BusinessLookupOutcome.Unauthorized)]
    [InlineData(HttpStatusCode.PaymentRequired, BusinessLookupOutcome.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden, BusinessLookupOutcome.Unauthorized)]
    [InlineData(HttpStatusCode.BadRequest, BusinessLookupOutcome.InvalidRequest)]
    [InlineData(HttpStatusCode.TooManyRequests, BusinessLookupOutcome.ProviderUnavailable)]
    [InlineData(HttpStatusCode.FailedDependency, BusinessLookupOutcome.ProviderUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError, BusinessLookupOutcome.ProviderUnavailable)]
    [InlineData(HttpStatusCode.GatewayTimeout, BusinessLookupOutcome.ProviderUnavailable)]
    public async Task Error_statuses_map_to_categories_without_retrying(HttpStatusCode status, BusinessLookupOutcome expected)
    {
        _http.Respond(status, """{ "error": "something" }""");

        var result = await Provider().LookupBusinessAsync(Connection(), Request());

        result.Outcome.Should().Be(expected);
        result.Message.Should().Contain("something");
        _http.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task An_empty_entity_is_not_found()
    {
        _http.Respond(HttpStatusCode.OK, """{ "entity": {} }""");

        (await Provider().LookupBusinessAsync(Connection(), Request())).Outcome.Should().Be(BusinessLookupOutcome.NotFound);
    }

    [Fact]
    public async Task A_timeout_is_provider_unavailable()
    {
        _http.Throw(new TaskCanceledException("timed out"));

        (await Provider().LookupBusinessAsync(Connection(), Request())).Outcome.Should().Be(BusinessLookupOutcome.ProviderUnavailable);
    }

    [Fact]
    public async Task Missing_credentials_never_send_a_request()
    {
        var connection = new VerificationProviderConnection("t1", VerificationEnvironment.Sandbox,
            new Dictionary<string, string> { ["appId"] = "app" }, new Dictionary<string, string> { ["lookup"] = "advance" });

        var act = () => Provider().LookupBusinessAsync(connection, Request());

        await act.Should().ThrowAsync<VerificationConfigurationException>();
        _http.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Connection_check_uses_the_free_balance_endpoint()
    {
        _http.Respond(HttpStatusCode.OK, """{ "entity": { "wallet_balance": "14132.00" } }""");

        var check = await Provider().CheckConnectionAsync(Connection());

        _http.Requests.Single().Url.Should().Be("https://sandbox.dojah.io/api/v1/balance");
        check.Success.Should().BeTrue();
        check.Message.Should().Contain("14132.00");
    }

    [Fact]
    public async Task Connection_check_reports_rejected_credentials()
    {
        _http.Respond(HttpStatusCode.Unauthorized, """{ "error": "Invalid AppId" }""");

        var check = await Provider().CheckConnectionAsync(Connection());

        check.Success.Should().BeFalse();
        check.Message.Should().Contain("Invalid AppId");
    }
}

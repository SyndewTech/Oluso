using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Oluso.Enterprise.Verification;
using Oluso.Enterprise.Verification.Providers;
using Xunit;

namespace Oluso.Enterprise.Verification.Tests;

/// <summary>
/// Youverify company checks against the documented shapes (doc.youverify.co: api-reference/overview,
/// quickcheck/kyb--global/premium-company-search, quickcheck/kyb--nigeria/taxid-and-business-verification).
/// </summary>
public class YouverifyVerificationProviderTests
{
    private const string FoundBody = """
    {
      "success": true,
      "statusCode": 200,
      "message": "success",
      "data": {
        "businessId": "65114a8ded14f6cf554e6b34",
        "type": "advance_company_check",
        "searchTerm": "RC00000000",
        "name": "John Doe Inc",
        "registrationNumber": "RC00000000",
        "registrationDate": "2017-06-09T01:00:00.000+01:00",
        "email": "johnfakedoe@gmail.com",
        "phone": "+1 000 000 0000",
        "typeOfEntity": "PRIVATE COMPANY LIMITED BY SHARES",
        "address": "1ST FLOOR,SUM HOUSE,Fake Street",
        "status": "found",
        "companyStatus": "ACTIVE",
        "keyPersonnel": [
          { "name": "PETER DOE", "designation": "WITNESS", "occupation": "LAWYER", "status": "ACTIVE" },
          { "name": "SUSAN DOE", "designation": "SECRETARY_COMPANY", "gender": "FEMALE", "status": "ACTIVE" }
        ],
        "id": "69b28dfda0abed50a12224b8"
      },
      "links": []
    }
    """;

    private readonly FakeHttp _http = new();

    private YouverifyVerificationProvider Provider() =>
        new(new HttpClient(_http), NullLogger<YouverifyVerificationProvider>.Instance);

    private static VerificationProviderConnection Connection(VerificationEnvironment env = VerificationEnvironment.Sandbox, string check = "standard") =>
        new("t1", env, new Dictionary<string, string> { ["token"] = "yv-secret" }, new Dictionary<string, string> { ["check"] = check });

    private static BusinessLookupRequest Request(string? name = "John Doe Inc") =>
        new() { Country = "NG", RegistrationNumber = "RC00000000", RegistrationType = "RC", BusinessName = name };

    [Fact]
    public async Task Sends_the_documented_request_and_maps_key_personnel()
    {
        _http.Respond(HttpStatusCode.OK, FoundBody);

        var result = await Provider().LookupBusinessAsync(Connection(), Request());

        var sent = _http.Requests.Single();
        sent.Method.Should().Be(HttpMethod.Post);
        sent.Url.Should().Be("https://api.sandbox.youverify.co/v2/api/verifications/global/company-advance-check");
        sent.Headers["token"].Should().Be("yv-secret");
        sent.Headers.Should().NotContainKey("Authorization");
        using var body = JsonDocument.Parse(sent.Body!);
        body.RootElement.GetProperty("registrationNumber").GetString().Should().Be("RC00000000");
        body.RootElement.GetProperty("countryCode").GetString().Should().Be("NG");
        body.RootElement.GetProperty("isConsent").GetBoolean().Should().BeTrue();
        body.RootElement.GetProperty("premium").GetBoolean().Should().BeFalse();
        body.RootElement.GetProperty("registrationName").GetString().Should().Be("John Doe Inc");

        result.Outcome.Should().Be(BusinessLookupOutcome.Found);
        result.ProviderReference.Should().Be("69b28dfda0abed50a12224b8");
        result.Record!.RegisteredName.Should().Be("John Doe Inc");
        result.Record.EntityType.Should().Be("PRIVATE COMPANY LIMITED BY SHARES");
        result.Record.Address.Should().Be("1ST FLOOR,SUM HOUSE,Fake Street");
        result.Record.ActivityStatus.Should().Be(BusinessActivityStatus.Active);
        result.Record.People.Should().Equal(new BusinessPerson("PETER DOE", "WITNESS"), new BusinessPerson("SUSAN DOE", "SECRETARY_COMPANY"));
    }

    [Fact]
    public async Task Live_premium_check_targets_production()
    {
        _http.Respond(HttpStatusCode.OK, FoundBody);

        await Provider().LookupBusinessAsync(Connection(VerificationEnvironment.Live, "premium"), Request(name: null));

        var sent = _http.Requests.Single();
        sent.Url.Should().StartWith("https://api.youverify.co/");
        using var body = JsonDocument.Parse(sent.Body!);
        body.RootElement.GetProperty("premium").GetBoolean().Should().BeTrue();
        body.RootElement.TryGetProperty("registrationName", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Entity_check_directors_are_read_too()
    {
        _http.Respond(HttpStatusCode.OK, """
        { "success": true, "data": { "id": "x", "status": "found", "name": "Acme Payments Ltd", "companyStatus": "ACTIVE",
          "directors": [ { "firstName": "Michael", "lastName": "Okonkwo", "designation": "DIRECTOR" } ] } }
        """);

        var result = await Provider().LookupBusinessAsync(Connection(), Request());

        result.Record!.People.Should().ContainSingle().Which.Should().Be(new BusinessPerson("Michael Okonkwo", "DIRECTOR"));
    }

    [Fact]
    public async Task Not_found_is_a_200_with_status_not_found()
    {
        _http.Respond(HttpStatusCode.OK, """{ "success": true, "statusCode": 200, "message": "success", "data": { "id": "v1", "status": "not_found" } }""");

        var result = await Provider().LookupBusinessAsync(Connection(), Request());

        result.Outcome.Should().Be(BusinessLookupOutcome.NotFound);
        result.ProviderReference.Should().Be("v1");
    }

    [Fact]
    public async Task Inactive_company_status_is_read()
    {
        _http.Respond(HttpStatusCode.OK, """{ "data": { "status": "found", "name": "Old Co", "companyStatus": "INACTIVE" } }""");

        (await Provider().LookupBusinessAsync(Connection(), Request())).Record!.ActivityStatus.Should().Be(BusinessActivityStatus.Inactive);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, """{"success":false,"statusCode":401,"message":"Permission denied","name":"UnauthorizedError","data":{}}""", BusinessLookupOutcome.Unauthorized)]
    [InlineData(HttpStatusCode.PaymentRequired, """{"success":false,"statusCode":402,"message":"Insufficient fund","name":"PaymentRequiredError","data":{}}""", BusinessLookupOutcome.Unauthorized)]
    [InlineData(HttpStatusCode.BadRequest, """{"success":false,"statusCode":400,"message":"registrationNumber is required","name":"ValidationError"}""", BusinessLookupOutcome.InvalidRequest)]
    [InlineData(HttpStatusCode.NotFound, """{"success":false,"statusCode":404,"message":"You have attempted to get a resource that does not exist.","name":"ResourceNotFoundError","data":{}}""", BusinessLookupOutcome.ProviderUnavailable)]
    [InlineData(HttpStatusCode.TooManyRequests, """{"success":false,"statusCode":429,"message":"Too many requests","name":"RateLimitError","data":{}}""", BusinessLookupOutcome.ProviderUnavailable)]
    [InlineData(HttpStatusCode.InternalServerError, """{"success":false,"statusCode":500,"message":"Internal Server Error","name":"Error","data":{}}""", BusinessLookupOutcome.ProviderUnavailable)]
    public async Task Documented_errors_map_to_categories(HttpStatusCode status, string body, BusinessLookupOutcome expected)
    {
        _http.Respond(status, body);

        var result = await Provider().LookupBusinessAsync(Connection(), Request());

        result.Outcome.Should().Be(expected);
        _http.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task Connection_check_uses_the_supported_countries_endpoint()
    {
        _http.Respond(HttpStatusCode.OK, """{ "success": true, "data": [ { "country": "Nigeria", "code": "NG", "subDivisions": [] } ] }""");

        var check = await Provider().CheckConnectionAsync(Connection(VerificationEnvironment.Live));

        _http.Requests.Single().Url.Should().Be("https://api.youverify.co/v2/api/verifications/global/supported-countries-and-region");
        _http.Requests.Single().Headers["token"].Should().Be("yv-secret");
        check.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Connection_check_reports_a_rejected_key()
    {
        _http.Respond(HttpStatusCode.Unauthorized, """{"success":false,"statusCode":401,"message":"Permission denied","name":"UnauthorizedError","data":{}}""");

        var check = await Provider().CheckConnectionAsync(Connection());

        check.Success.Should().BeFalse();
        check.Message.Should().Contain("Permission denied");
    }
}

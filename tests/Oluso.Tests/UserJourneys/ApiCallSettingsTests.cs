using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Core.UserJourneys;
using Oluso.UserJourneys.Steps;
using Xunit;

namespace Oluso.Tests.UserJourneys;

/// <summary>
/// An api_call step reads its nested settings whatever their casing, including once its
/// client secret has been decrypted, and sends the field names exactly as configured.
/// </summary>
public class ApiCallSettingsTests
{
    private const string Settings = """
    {
      "url": "https://api.example/api/internal/merchants",
      "method": "POST",
      "failOnError": true,
      "authentication": {
        "type": "client_credentials",
        "tokenEndpoint": "https://identity.example/connect/token",
        "clientId": "provisioning",
        "clientSecret": "s3cret",
        "scope": "internal:provisioning"
      },
      "inputMapping": {
        "userUniqueIdentifier": { "source": "state", "from": "sub", "required": true },
        "business_name": { "source": "state", "from": "businessName", "required": true },
        "Country": { "source": "state", "from": "country", "required": true },
        "email": { "source": "state", "from": "email" }
      },
      "outputMapping": { "merchantId": "merchantId" }
    }
    """;

    private sealed class Partner : HttpMessageHandler
    {
        public HttpRequestMessage? Request;
        public string? Body;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            Body = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"merchantId\":\"m1\"}") };
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_step_signs_in_and_sends_the_mapped_fields(bool secretDecrypted)
    {
        var settings = JsonSerializer.Deserialize<Dictionary<string, object>>(Settings)!;
        if (secretDecrypted)
        {
            new JourneyStepSecretResolver().WriteValue(settings, "authentication.clientSecret", "s3cret");
        }

        var partner = new Partner();
        var clients = new Mock<IHttpClientFactory>();
        clients.Setup(f => f.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(partner, false));
        var tokens = new Mock<IClientCredentialsTokenCache>();
        tokens.Setup(c => c.GetAccessTokenAsync("https://identity.example/connect/token", "provisioning", "s3cret",
                "internal:provisioning", null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync("tok");
        var services = new ServiceCollection()
            .AddSingleton(clients.Object)
            .AddSingleton(tokens.Object)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .BuildServiceProvider();

        var context = new StepExecutionContext
        {
            JourneyId = "j1",
            StepId = "provision",
            TenantId = "t1",
            Configuration = new JourneyStepConfiguration { Id = "provision", Type = "api_call", Settings = settings },
            JourneyData = new Dictionary<string, object>
            {
                ["sub"] = "u1", ["email"] = "ada@x.test", ["businessName"] = "Acme", ["country"] = "NG",
            },
            Input = new JourneyStepInput { StepId = "provision" },
            ServiceProvider = services,
        };

        var result = await new ApiCallStepHandler().ExecuteAsync(context);

        result.Outcome.Should().Be(StepOutcome.Continue);
        partner.Request!.Headers.Authorization!.ToString().Should().Be("Bearer tok");
        JsonDocument.Parse(partner.Body!).RootElement.EnumerateObject().Select(p => (p.Name, p.Value.GetString()))
            .Should().Equal(("userUniqueIdentifier", "u1"), ("business_name", "Acme"), ("Country", "NG"), ("email", "ada@x.test"));
        context.GetData<string>("merchantId").Should().Be("m1");
    }

    [Fact]
    public void Settings_read_whatever_their_casing_and_numbers_written_as_text()
    {
        var settings = JsonSerializer.Deserialize<Dictionary<string, object>>("""{ "timeout": "45", "mapping": { "From": "sub", "SOURCE": "state" } }""")!;
        var context = new StepExecutionContext
        {
            JourneyId = "j1",
            StepId = "s",
            TenantId = "t1",
            Configuration = new JourneyStepConfiguration { Id = "s", Type = "api_call", Settings = settings },
            JourneyData = new Dictionary<string, object>(),
            Input = new JourneyStepInput { StepId = "s" },
            ServiceProvider = new ServiceCollection().BuildServiceProvider(),
        };

        context.GetConfig("timeout", 30).Should().Be(45);
        var mapping = context.GetConfig<ApiInputMapping>("mapping")!;
        (mapping.From, mapping.Source).Should().Be(("sub", "state"));
    }

    private static StepExecutionContext Context(string type, string settingsJson, Dictionary<string, object>? input = null, IServiceProvider? services = null) => new()
    {
        JourneyId = "j1",
        StepId = "s",
        TenantId = "t1",
        Configuration = new JourneyStepConfiguration { Id = "s", Type = type, Settings = JsonSerializer.Deserialize<Dictionary<string, object>>(settingsJson)! },
        JourneyData = new Dictionary<string, object>(),
        Input = new JourneyStepInput { StepId = "s", Values = input },
        ServiceProvider = services ?? new ServiceCollection().AddSingleton(typeof(ILogger<>), typeof(NullLogger<>)).BuildServiceProvider(),
    };

    [Fact]
    public async Task Form_fields_read_whatever_their_casing_and_with_values_written_as_text()
    {
        const string form = """
        { "fields": [ { "Name": "code", "TYPE": "text", "Required": "true", "maxlength": "3", "Options": [ 1, { "Value": "B", "label": "Bee" } ] } ] }
        """;

        var missing = await new DynamicFormStepHandler().ExecuteAsync(Context("dynamic_form", form, new() { ["__submitted"] = "1", ["code"] = "" }));
        var tooLong = await new DynamicFormStepHandler().ExecuteAsync(Context("dynamic_form", form, new() { ["__submitted"] = "1", ["code"] = "ABCD" }));
        var context = Context("dynamic_form", form, new() { ["__submitted"] = "1", ["code"] = "AB" });
        var ok = await new DynamicFormStepHandler().ExecuteAsync(context);

        missing.Outcome.Should().Be(StepOutcome.RequireInput, "the field is required");
        tooLong.Outcome.Should().Be(StepOutcome.RequireInput, "the field allows at most 3 characters");
        ok.Outcome.Should().Be(StepOutcome.Continue);
        context.GetData<string>("code").Should().Be("AB");
    }

    [Theory]
    [InlineData("""{"merchantId":"exact","MerchantId":"other"}""", "exact")]
    [InlineData("""{"MerchantId":"m2"}""", "m2")]
    [InlineData("""{"data":{"Merchant":{"ID":"m3"}}}""", null)]
    public void Response_paths_match_exactly_first_then_whatever_the_casing(string response, string? expected)
    {
        using var doc = JsonDocument.Parse(response);
        var found = doc.RootElement.TryGetPropertyIgnoreCase("merchantId", out var value) ? JsonLookup.AsString(value) : null;

        found.Should().Be(expected);
    }
}

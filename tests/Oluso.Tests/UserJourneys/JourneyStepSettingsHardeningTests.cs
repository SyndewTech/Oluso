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
/// Steps read the settings the builder writes: the reader is forgiving, transform and
/// condition use the builder's shapes, webhook payloads are objects (or JSON text), api_call
/// fills its body and routes error responses, forms honour showWhen, and step targets in
/// settings are found from each step's schema.
/// </summary>
public class JourneyStepSettingsHardeningTests
{
    private static IServiceProvider Services(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection().AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    private static StepExecutionContext Context(string type, string settingsJson, Dictionary<string, object>? input = null,
        Dictionary<string, object>? data = null, IServiceProvider? services = null) => new()
    {
        JourneyId = "j1",
        StepId = "s1",
        TenantId = "t1",
        Configuration = new JourneyStepConfiguration { Id = "s1", Type = type, Settings = JsonSerializer.Deserialize<Dictionary<string, object>>(settingsJson)! },
        JourneyData = data ?? new Dictionary<string, object>(),
        Input = new JourneyStepInput { StepId = "s1", Values = input },
        ServiceProvider = services ?? Services(),
    };

    // ── Settings reader ─────────────────────────────────────────────────────

    [Fact]
    public void Booleans_and_lists_can_be_written_as_text_and_bad_entries_are_left_out()
    {
        var problems = new List<string>();
        var settings = JsonSerializer.Deserialize<Dictionary<string, object>>("""
            { "flag": "true", "domains": "acme.com, beta.io", "headers": { "X-A": "1", "X-B": null },
              "rules": [ { "From": "sub", "required": "true" }, null, 42 ] }
            """)!;

        StepValueReader.Read<bool>(settings["flag"]).Should().BeTrue();
        StepValueReader.Read<List<string>>(settings["domains"]).Should().Equal("acme.com", "beta.io");
        StepValueReader.Read<Dictionary<string, string>>(settings["headers"], null, problems.Add)!.Keys.Should().Equal("X-A");
        var rules = StepValueReader.Read<List<ApiInputMapping>>(settings["rules"], null, problems.Add)!;
        rules.Should().ContainSingle().Which.Should().Match<ApiInputMapping>(r => r.From == "sub" && r.Required);
        problems.Should().HaveCount(3, "the empty header, the empty rule and the unreadable rule are each reported");
    }

    // ── Transform ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Transform_runs_the_builders_mappings()
    {
        var context = Context("transform", """
            { "mappings": [
                { "source": "email", "sourceType": "journeyData", "target": "email_lower", "transform": "lowercase" },
                { "source": "full", "target": "first", "transform": "split", "transformConfig": { "delimiter": " " } },
                { "target": "greeting", "transform": "template", "transformConfig": { "template": "Hi {data:first}" } },
                { "source": "plan", "target": "tier", "transform": "map", "transformConfig": { "pairs": "pro=gold\nfree=basic" } },
                { "source": "missing", "target": "fallback", "defaultValue": "none" }
            ] }
            """, data: new() { ["email"] = "Ada@X.Test", ["full"] = "Ada Lovelace", ["plan"] = "pro" });

        var result = await new TransformStepHandler().ExecuteAsync(context);

        result.Outcome.Should().Be(StepOutcome.Continue);
        (context.GetData<string>("email_lower"), context.GetData<string>("first"), context.GetData<string>("greeting"),
                context.GetData<string>("tier"), context.GetData<string>("fallback"))
            .Should().Be(("ada@x.test", "Ada", "Hi Ada", "gold", "none"));
    }

    [Fact]
    public async Task Transform_settings_are_checked_and_its_schema_publishes_the_catalog()
    {
        var invalid = await new TransformStepHandler().ValidateConfigurationAsync(
            JsonSerializer.Deserialize<Dictionary<string, object>>("""{ "mappings": [ { "source": "a", "target": "b", "transform": "nope" } ] }"""));
        invalid.IsValid.Should().BeFalse();

        var schema = JsonSerializer.SerializeToElement(new TransformStepHandler().GetConfigurationSchema());
        var transforms = schema.GetProperty("mappings").GetProperty("items").GetProperty("properties").GetProperty("transform").GetProperty("x-transforms");
        transforms.EnumerateArray().Select(t => t.GetProperty("name").GetString())
            .Should().BeEquivalentTo(TransformStepHandler.Catalog.Select(t => t.Name));
    }

    // ── Condition ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Condition_reads_the_builders_conditions_and_branches_to_a_step()
    {
        var context = Context("condition", """
            { "conditions": [
                { "type": "journeyData", "field": "country", "operator": "equals", "value": "GB", "logicalOperator": "or" },
                { "type": "journeyData", "field": "country", "operator": "equals", "value": "NG" }
              ],
              "onTrue": "kyc", "onFalse": "done" }
            """, data: new() { ["country"] = "NG" });

        var result = await new ConditionStepHandler().ExecuteAsync(context);

        result.BranchId.Should().Be("kyc");
    }

    // ── Webhook ─────────────────────────────────────────────────────────────

    private sealed class Receiver : HttpMessageHandler
    {
        public string? Body;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") };
        }
    }

    private static (IServiceProvider Services, Receiver Receiver) Http()
    {
        var receiver = new Receiver();
        var clients = new Mock<IHttpClientFactory>();
        clients.Setup(c => c.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(receiver, false));
        return (Services(s => s.AddSingleton(clients.Object)), receiver);
    }

    [Theory]
    [InlineData("""{ "user": "{data:sub}", "nested": { "plan": "pro" } }""")]
    [InlineData("""   "{ \"user\": \"{data:sub}\", \"nested\": { \"plan\": \"pro\" } }"  """)]
    public async Task Webhook_payload_may_be_an_object_or_JSON_text_and_fills_placeholders(string payload)
    {
        var (services, receiver) = Http();
        var context = Context("webhook", $$"""{ "url": "https://hooks.test/in", "waitForResponse": true, "payload": {{payload}} }""",
            data: new() { ["sub"] = "u1" }, services: services);

        var result = await new WebhookStepHandler().ExecuteAsync(context);

        result.Outcome.Should().Be(StepOutcome.Continue);
        JsonDocument.Parse(receiver.Body!).RootElement.GetProperty("user").GetString().Should().Be("u1");
    }

    [Fact]
    public async Task Webhook_payload_that_is_not_JSON_fails_instead_of_sending_the_default()
    {
        var (services, receiver) = Http();
        var context = Context("webhook", """{ "url": "https://hooks.test/in", "payload": "{ not json" }""", services: services);

        var result = await new WebhookStepHandler().ExecuteAsync(context);

        result.Error.Should().Be("webhook_payload_invalid");
        receiver.Body.Should().BeNull();
    }

    [Fact]
    public async Task Webhook_sends_only_listed_journey_values()
    {
        var (services, receiver) = Http();
        var context = Context("webhook", """{ "url": "https://hooks.test/in", "waitForResponse": true, "includeUserData": false, "includeJourneyData": true, "journeyDataKeys": ["plan"] }""",
            data: new() { ["plan"] = "pro", ["reset_code"] = "123456" }, services: services);

        await new WebhookStepHandler().ExecuteAsync(context);

        var data = JsonDocument.Parse(receiver.Body!).RootElement.GetProperty("data");
        data.EnumerateObject().Select(p => p.Name).Should().Equal("plan");
    }

    // ── api_call ────────────────────────────────────────────────────────────

    private sealed class Api(HttpStatusCode status, string body, bool unreachable = false) : HttpMessageHandler
    {
        public string? Sent;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (unreachable) throw new HttpRequestException("connection refused");
            Sent = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }
    }

    private static async Task<(StepHandlerResult Result, Api Api)> CallAsync(string settings, Api api, Dictionary<string, object>? data = null)
    {
        var clients = new Mock<IHttpClientFactory>();
        clients.Setup(c => c.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(api, false));
        var context = Context("api_call", settings, data: data, services: Services(s => s.AddSingleton(clients.Object)));
        return (await new ApiCallStepHandler().ExecuteAsync(context), api);
    }

    [Fact]
    public async Task Api_call_body_fills_placeholders_at_every_level()
    {
        var (_, api) = await CallAsync("""{ "url": "https://api.test/x", "method": "POST", "body": { "user": "{data:sub}", "meta": { "email": "{data:email}" }, "tags": ["{data:sub}"] } }""",
            new Api(HttpStatusCode.OK, "{}"), new() { ["sub"] = "u1", ["email"] = "ada@x.test" });

        var sent = JsonDocument.Parse(api.Sent!).RootElement;
        (sent.GetProperty("user").GetString(), sent.GetProperty("meta").GetProperty("email").GetString(), sent.GetProperty("tags")[0].GetString())
            .Should().Be(("u1", "ada@x.test", "u1"));
    }

    [Fact]
    public async Task Api_call_routes_error_statuses_and_honours_failOnError_when_unreachable()
    {
        var (notFound, _) = await CallAsync("""{ "url": "https://api.test/x", "branchOnResponse": [ { "onStatus": [404], "branchTo": "create" } ] }""",
            new Api(HttpStatusCode.NotFound, ""));
        notFound.BranchId.Should().Be("create", "a status rule fires for an error with an empty body");

        var (unreachable, _) = await CallAsync("""{ "url": "https://api.test/x", "failOnError": false }""", new Api(HttpStatusCode.OK, "", unreachable: true));
        unreachable.Outcome.Should().Be(StepOutcome.Continue);

        var (notJson, _) = await CallAsync("""{ "url": "https://api.test/x", "responseValidation": [ { "path": "id", "condition": "exists" } ] }""",
            new Api(HttpStatusCode.OK, "<html>ok</html>"));
        notJson.Error.Should().Be("api_validation_failed");
    }

    // ── Dynamic form ────────────────────────────────────────────────────────

    private const string ConditionalForm = """
        { "fields": [
            { "name": "type", "type": "select", "required": true, "options": ["individual", "company"] },
            { "name": "companyNumber", "type": "text", "required": true, "showWhen": { "field": "type", "operator": "equals", "value": "company" } }
        ] }
        """;

    [Fact]
    public async Task A_required_field_hidden_by_its_condition_is_not_required_and_not_kept()
    {
        var context = Context("dynamic_form", ConditionalForm, new() { ["__submitted"] = "1", ["type"] = "individual", ["companyNumber"] = "stale" });

        var result = await new DynamicFormStepHandler().ExecuteAsync(context);

        result.Outcome.Should().Be(StepOutcome.Continue);
        context.JourneyData.Should().NotContainKey("companyNumber");
    }

    [Fact]
    public async Task A_shown_conditional_field_is_still_required()
    {
        var result = await new DynamicFormStepHandler().ExecuteAsync(
            Context("dynamic_form", ConditionalForm, new() { ["__submitted"] = "1", ["type"] = "company" }));

        result.Outcome.Should().Be(StepOutcome.RequireInput);
    }

    [Fact]
    public void A_plugin_form_uses_the_dynamic_form_shape()
    {
        var form = DynamicFormStepHandler.FromDefinition(JsonDocument.Parse("""
            { "title": "Verify", "fields": [ { "name": "code", "type": "text", "label": "Code", "required": true } ] }
            """).RootElement, "en");

        form.Title.Should().Be("Verify");
        form.Fields.Should().ContainSingle().Which.Name.Should().Be("code");
    }

    // ── Step references ─────────────────────────────────────────────────────

    [Fact]
    public void Step_targets_are_found_from_the_steps_schema_at_any_depth()
    {
        var picker = new Dictionary<string, object> { ["type"] = "string", ["x-enumSource"] = new Dictionary<string, object> { ["endpoint"] = "$steps" } };
        var schema = new Dictionary<string, object>
        {
            ["onTrue"] = picker,
            ["rules"] = new Dictionary<string, object>
            {
                ["type"] = "array",
                ["items"] = new Dictionary<string, object> { ["type"] = "object", ["properties"] = new Dictionary<string, object> { ["goTo"] = picker } },
            },
        };
        var settings = JsonSerializer.Deserialize<Dictionary<string, object>>("""{ "onTrue": "a", "rules": [ { "goTo": "b" }, { "goTo": "" } ] }""")!;

        JourneyStepReferences.FromSettings(schema, settings).Should().Equal(("onTrue", "a"), ("rules.goTo", "b"));
    }

    [Fact]
    public void MFA_offers_the_same_methods_on_every_screen()
    {
        MfaStepHandler.DefaultMethods.Should().Equal("totp", "email");
    }
}

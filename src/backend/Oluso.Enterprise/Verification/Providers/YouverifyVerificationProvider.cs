using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Oluso.Enterprise.Verification.Providers;

/// <summary>
/// Youverify (youverify.co): KYB company checks.
/// <para>Docs: https://doc.youverify.co/api-reference/overview (base URLs, envelope, errors),
/// https://doc.youverify.co/get-started/authentication (token header),
/// https://doc.youverify.co/api-reference/quickcheck/kyb--global/premium-company-search,
/// https://doc.youverify.co/api-reference/quickcheck/kyb--nigeria/taxid-and-business-verification
/// (the documented shape of an advance_company_check result),
/// https://doc.youverify.co/api-reference/cowork/entity-management/get-supported-countries-and-regions.</para>
/// <para>Auth: secret key in the <c>token</c> header (no "Bearer"). Sandbox
/// https://api.sandbox.youverify.co, live https://api.youverify.co. A not-found business
/// is a 200 with <c>data.status = "not_found"</c>.</para>
/// <para>No hosted flow: Youverify documents no hosted or embeddable KYB session, and its
/// webhook signature page (doc.youverify.co/webhooks/signature-verification) is still a
/// placeholder, so webhooks couldn't be authenticated. Lookups stay synchronous.</para>
/// </summary>
public sealed class YouverifyVerificationProvider : IBusinessVerificationProvider
{
    public const string ProviderId = "youverify";
    public const string TokenField = "token";
    public const string CheckOption = "check";
    public const string StandardCheck = "standard";
    public const string PremiumCheck = "premium";

    private readonly HttpClient _http;
    private readonly ILogger<YouverifyVerificationProvider> _logger;

    public YouverifyVerificationProvider(HttpClient http, ILogger<YouverifyVerificationProvider> logger)
    {
        _http = http;
        _logger = logger;
    }

    public string Id => ProviderId;
    public string DisplayName => "Youverify";

    public IReadOnlyList<VerificationCredentialField> CredentialFields { get; } = new[]
    {
        new VerificationCredentialField(TokenField, "API secret key", Secret: true, "Youverify workspace → Settings → API Keys, for the chosen environment"),
    };

    public IReadOnlyList<VerificationProviderOption> Options { get; } = new[]
    {
        new VerificationProviderOption(CheckOption, "Company check", new[] { StandardCheck, PremiumCheck }, StandardCheck,
            "Premium adds Youverify's premium registry checks (Nigeria, South Africa, Kenya) at a higher price."),
    };

    public IReadOnlyCollection<string> SupportedBusinessCountries { get; } = new[] { "NG" };

    public static string BaseUrl(VerificationEnvironment environment) =>
        environment == VerificationEnvironment.Live ? "https://api.youverify.co" : "https://api.sandbox.youverify.co";

    public async Task<VerificationConnectionCheck> CheckConnectionAsync(VerificationProviderConnection connection, CancellationToken cancellationToken = default)
    {
        using var request = Authorised(connection, HttpMethod.Get, $"{BaseUrl(connection.Environment)}/v2/api/verifications/global/supported-countries-and-region");
        try
        {
            using var response = await _http.SendAsync(request, cancellationToken);
            var root = ProviderHttp.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            return response.IsSuccessStatusCode
                ? new VerificationConnectionCheck(true, $"Youverify {connection.Environment} accepted the key")
                : new VerificationConnectionCheck(false, $"Youverify rejected the key ({ProviderHttp.Describe(response.StatusCode, ProviderHttp.ErrorText(root, "message", "name"))})");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return new VerificationConnectionCheck(false, $"Couldn't reach Youverify: {ex.Message}");
        }
    }

    public async Task<BusinessLookupResult> LookupBusinessAsync(
        VerificationProviderConnection connection, BusinessLookupRequest request, CancellationToken cancellationToken = default)
    {
        var country = request.Country.ToUpperInvariant();
        var payload = new Dictionary<string, object>
        {
            ["registrationNumber"] = request.RegistrationNumber,
            ["countryCode"] = country,
            ["isConsent"] = true,
            ["premium"] = string.Equals(connection.Option(CheckOption), PremiumCheck, StringComparison.OrdinalIgnoreCase),
        };
        if (country == "NG" && !string.IsNullOrWhiteSpace(request.BusinessName))
            payload["registrationName"] = request.BusinessName.Trim();

        using var message = Authorised(connection, HttpMethod.Post, $"{BaseUrl(connection.Environment)}/v2/api/verifications/global/company-advance-check");
        message.Content = JsonContent.Create(payload);
        try
        {
            using var response = await _http.SendAsync(message, cancellationToken);
            var root = ProviderHttp.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (!response.IsSuccessStatusCode)
            {
                var detail = ProviderHttp.Describe(response.StatusCode, ProviderHttp.ErrorText(root, "message", "name"));
                _logger.LogWarning("Youverify company check for {Number} answered {Detail}", request.RegistrationNumber, detail);
                // A missing business is a 200 with status not_found; a 404 means the endpoint itself wasn't found.
                var outcome = (int)response.StatusCode == 404 ? BusinessLookupOutcome.ProviderUnavailable : ProviderHttp.Classify(response.StatusCode);
                return BusinessLookupResult.Failure(outcome, $"Youverify: {detail}");
            }

            if (root is not { ValueKind: JsonValueKind.Object } body
                || !body.TryGetProperty("data", out var data)
                || data.ValueKind != JsonValueKind.Object)
            {
                return BusinessLookupResult.Failure(BusinessLookupOutcome.ProviderUnavailable, "Youverify answered without a data object.");
            }

            var reference = ProviderHttp.String(data, "id");
            var status = ProviderHttp.String(data, "status");
            if (!string.Equals(status, "found", StringComparison.OrdinalIgnoreCase))
            {
                return string.Equals(status, "not_found", StringComparison.OrdinalIgnoreCase)
                    ? BusinessLookupResult.Failure(BusinessLookupOutcome.NotFound, "Youverify found no company for the number.", reference)
                    : BusinessLookupResult.Failure(BusinessLookupOutcome.ProviderUnavailable, $"Youverify returned status '{status ?? "(none)"}'.", reference);
            }

            if (ProviderHttp.String(data, "name") is not { } name)
                return BusinessLookupResult.Failure(BusinessLookupOutcome.ProviderUnavailable, "Youverify reported the company found but returned no name.", reference);

            var rawStatus = ProviderHttp.String(data, "companyStatus");
            return BusinessLookupResult.Found(new BusinessRegistryRecord
            {
                RegisteredName = name,
                RegistrationNumber = request.RegistrationNumber,
                EntityType = ProviderHttp.String(data, "typeOfEntity"),
                RegistrationDate = ProviderHttp.String(data, "registrationDate"),
                Address = ProviderHttp.String(data, "address") ?? ProviderHttp.String(data, "headOfficeAddress"),
                RawStatus = rawStatus,
                ActivityStatus = RegistryStatus.Parse(rawStatus),
                People = People(data),
            }, reference);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Youverify company check for {Number} couldn't complete", request.RegistrationNumber);
            return BusinessLookupResult.Failure(BusinessLookupOutcome.ProviderUnavailable, $"Couldn't reach Youverify: {ex.Message}");
        }
    }

    /// <summary>
    /// <c>keyPersonnel</c> ({name, designation}) as the company checks document it, and
    /// <c>directors</c> ({firstName, lastName, designation}) as the entity KYB check does.
    /// </summary>
    private static List<BusinessPerson> People(JsonElement data)
    {
        var people = ProviderHttp.Array(data, "keyPersonnel")
            .Select(p => new BusinessPerson(ProviderHttp.String(p, "name") ?? string.Empty, ProviderHttp.String(p, "designation")))
            .Concat(ProviderHttp.Array(data, "directors")
                .Select(p => new BusinessPerson(
                    string.Join(' ', new[] { ProviderHttp.String(p, "firstName"), ProviderHttp.String(p, "middleName"), ProviderHttp.String(p, "lastName") }
                        .Where(n => n != null)),
                    ProviderHttp.String(p, "designation"))))
            .Where(p => p.Name.Length > 0);
        return people.ToList();
    }

    private static HttpRequestMessage Authorised(VerificationProviderConnection connection, HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation("token", connection.Require(TokenField));
        request.Headers.Accept.ParseAdd("application/json");
        return request;
    }
}

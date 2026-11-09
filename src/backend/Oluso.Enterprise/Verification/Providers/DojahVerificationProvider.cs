using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Oluso.Enterprise.Verification.Providers;

/// <summary>
/// Dojah (dojah.io): Nigerian CAC lookups.
/// <para>Docs: https://docs.dojah.io/docs/nigeria/lookup-cac (Lookup CAC basic/advance),
/// https://docs.dojah.io/api-reference/wallet-utilities/get-balance (balance),
/// https://docs.dojah.io/api-reference/core-concepts/errors-status-codes.</para>
/// <para>Auth: <c>AppId</c> header plus the secret key in <c>Authorization</c>, sent raw
/// (no "Bearer"). Sandbox https://sandbox.dojah.io, live https://api.dojah.io.</para>
/// <para>Lookups: <c>GET /api/v1/kyc/cac/basic</c> (name, type, status, location) or
/// <c>GET /api/v1/kyc/cac/advance</c> (adds address, registration date and affiliates;
/// its documented response has no status field), both with <c>rc_number</c> (digits)
/// and <c>company_type</c>. The tenant chooses the tier.</para>
/// <para>Hosted sessions use an EasyOnboard flow (a KYB flow the organisation builds in
/// Dojah's dashboard) opened as the web widget: https://widget.dojah.io/widget.js with
/// <c>app_id</c>, <c>p_key</c> (public key), <c>type: "custom"</c>, <c>config.widget_id</c>
/// and our <c>reference_id</c> (docs.dojah.io/api-reference/widget-sdks/web-javascript,
/// /api-reference/hosted-flows-easyonboard/how-hosted-flows-work). Dojah documents no
/// redirect URL for the widget, so its onSuccess sends the browser to the journey's
/// callback. The outcome is read server-side from <c>GET /api/v1/kyc/verification?reference_id=</c>
/// (verification_status Ongoing, Pending, Completed, Failed or Abandoned, plus a boolean
/// status; /api-reference/verifications/get-verification), never from the browser.</para>
/// <para>Webhooks (service <c>kyc_widget</c>) carry <c>x-dojah-signature</c>: the hex
/// HMAC-SHA256 of the raw body keyed with the webhook secret shown under Developers →
/// Webhooks (/api-reference/core-concepts/webhooks-signatures).</para>
/// </summary>
public sealed class DojahVerificationProvider : IHostedBusinessVerificationProvider
{
    public const string ProviderId = "dojah";
    public const string AppIdField = "appId";
    public const string SecretKeyField = "secretKey";
    public const string PublicKeyField = "publicKey";
    public const string WidgetIdField = "widgetId";
    public const string WebhookSecretField = "webhookSecret";
    public const string WidgetScriptUrl = "https://widget.dojah.io/widget.js";
    public const string SignatureHeader = "x-dojah-signature";
    public const string LookupOption = "lookup";
    public const string AdvanceLookup = "advance";
    public const string BasicLookup = "basic";

    private static readonly IReadOnlyDictionary<string, string> CompanyTypes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["RC"] = "COMPANY",
        ["BN"] = "BUSINESS_NAME",
        ["IT"] = "INCORPORATED_TRUSTEES",
        ["LP"] = "LIMITED_PARTNERSHIP",
        ["LLP"] = "LIMITED_LIABILITY_PARTNERSHIP",
    };

    private readonly HttpClient _http;
    private readonly ILogger<DojahVerificationProvider> _logger;

    public DojahVerificationProvider(HttpClient http, ILogger<DojahVerificationProvider> logger)
    {
        _http = http;
        _logger = logger;
    }

    public string Id => ProviderId;
    public string DisplayName => "Dojah";

    public IReadOnlyList<VerificationCredentialField> CredentialFields { get; } = new[]
    {
        new VerificationCredentialField(AppIdField, "App ID", Secret: false, "Dojah dashboard → Developers → Configuration"),
        new VerificationCredentialField(SecretKeyField, "Secret key", Secret: true, "The app's secret key for the chosen environment"),
        new VerificationCredentialField(PublicKeyField, "Public key", Secret: false, "Hosted verification only: the app's public key (p_key) the EasyOnboard widget opens with", Required: false),
        new VerificationCredentialField(WidgetIdField, "EasyOnboard widget ID", Secret: false, "Hosted verification only: the published KYB flow's widget_id (EasyOnboard → your flow)", Required: false),
        new VerificationCredentialField(WebhookSecretField, "Webhook secret", Secret: true, "Hosted verification only: the secret shown under Developers → Webhooks for the kyc_widget subscription", Required: false),
    };

    public IReadOnlyList<VerificationProviderOption> Options { get; } = new[]
    {
        new VerificationProviderOption(LookupOption, "CAC lookup", new[] { AdvanceLookup, BasicLookup }, AdvanceLookup,
            "Advance returns directors and the registration date but no trading status; basic returns the status but no directors."),
    };

    public IReadOnlyCollection<string> SupportedBusinessCountries { get; } = new[] { "NG" };

    public IReadOnlyCollection<string> HostedBusinessCountries { get; } = new[] { "NG" };

    public static string BaseUrl(VerificationEnvironment environment) =>
        environment == VerificationEnvironment.Live ? "https://api.dojah.io" : "https://sandbox.dojah.io";

    public async Task<VerificationConnectionCheck> CheckConnectionAsync(VerificationProviderConnection connection, CancellationToken cancellationToken = default)
    {
        using var request = Authorised(connection, HttpMethod.Get, $"{BaseUrl(connection.Environment)}/api/v1/balance");
        try
        {
            using var response = await _http.SendAsync(request, cancellationToken);
            var root = ProviderHttp.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (!response.IsSuccessStatusCode)
                return new VerificationConnectionCheck(false, $"Dojah rejected the credentials ({ProviderHttp.Describe(response.StatusCode, ProviderHttp.ErrorText(root, "error", "message"))})");

            var balance = root is { ValueKind: JsonValueKind.Object } r && r.TryGetProperty("entity", out var entity)
                ? ProviderHttp.String(entity, "wallet_balance")
                : null;
            return new VerificationConnectionCheck(true, balance == null
                ? $"Dojah {connection.Environment} accepted the credentials"
                : $"Dojah {connection.Environment} accepted the credentials (wallet balance NGN {balance})");
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return new VerificationConnectionCheck(false, $"Couldn't reach Dojah: {ex.Message}");
        }
    }

    public async Task<BusinessLookupResult> LookupBusinessAsync(
        VerificationProviderConnection connection, BusinessLookupRequest request, CancellationToken cancellationToken = default)
    {
        if (!string.Equals(request.Country, "NG", StringComparison.OrdinalIgnoreCase))
            return BusinessLookupResult.Failure(BusinessLookupOutcome.InvalidRequest, $"Dojah CAC lookups are Nigerian only, not {request.Country}.");
        if (request.RegistrationType == null || !CompanyTypes.TryGetValue(request.RegistrationType, out var companyType))
            return BusinessLookupResult.Failure(BusinessLookupOutcome.InvalidRequest, $"Unknown CAC registration type '{request.RegistrationType}'.");

        var tier = connection.Option(LookupOption).ToLowerInvariant();
        var digits = request.RegistrationNumber[request.RegistrationType.Length..];
        var url = $"{BaseUrl(connection.Environment)}/api/v1/kyc/cac/{tier}?rc_number={Uri.EscapeDataString(digits)}&company_type={companyType}";

        using var message = Authorised(connection, HttpMethod.Get, url);
        try
        {
            using var response = await _http.SendAsync(message, cancellationToken);
            var root = ProviderHttp.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (!response.IsSuccessStatusCode)
            {
                var outcome = ProviderHttp.Classify(response.StatusCode);
                var detail = ProviderHttp.Describe(response.StatusCode, ProviderHttp.ErrorText(root, "error", "message"));
                _logger.LogWarning("Dojah CAC {Tier} lookup for {Number} answered {Detail}", tier, request.RegistrationNumber, detail);
                return BusinessLookupResult.Failure(outcome, $"Dojah: {detail}");
            }

            if (root is not { ValueKind: JsonValueKind.Object } body
                || !body.TryGetProperty("entity", out var entity)
                || entity.ValueKind != JsonValueKind.Object
                || ProviderHttp.String(entity, "company_name") is not { } name)
            {
                return BusinessLookupResult.Failure(BusinessLookupOutcome.NotFound, "Dojah returned no company for the number.");
            }

            var rawStatus = ProviderHttp.String(entity, "status");
            var people = ProviderHttp.Array(entity, "affiliates")
                .Select(a => new BusinessPerson(
                    string.Join(' ', new[] { ProviderHttp.String(a, "first_name"), ProviderHttp.String(a, "middle_name"), ProviderHttp.String(a, "last_name") }
                        .Where(p => p != null)),
                    ProviderHttp.String(a, "affiliate_type")))
                .Where(p => p.Name.Length > 0)
                .ToList();

            return BusinessLookupResult.Found(new BusinessRegistryRecord
            {
                RegisteredName = name,
                RegistrationNumber = request.RegistrationNumber,
                EntityType = ProviderHttp.String(entity, "type_of_company"),
                RegistrationDate = ProviderHttp.String(entity, "date_of_registration"),
                Address = ProviderHttp.String(entity, "address") ?? JoinLocation(entity),
                RawStatus = rawStatus,
                ActivityStatus = RegistryStatus.Parse(rawStatus),
                People = people,
            }, ProviderHttp.String(entity, "business"));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Dojah CAC lookup for {Number} couldn't complete", request.RegistrationNumber);
            return BusinessLookupResult.Failure(BusinessLookupOutcome.ProviderUnavailable, $"Couldn't reach Dojah: {ex.Message}");
        }
    }

    public Task<HostedSessionStart> StartBusinessSessionAsync(VerificationProviderConnection connection, HostedSessionRequest request, CancellationToken cancellationToken = default)
    {
        // Dojah creates the session when the widget opens; the reference is ours (at least 10 characters).
        var reference = "kyb" + Convert.ToHexString(RandomNumberGenerator.GetBytes(12)).ToLowerInvariant();
        return Task.FromResult(new HostedSessionStart(reference, Widget(connection, reference, request)));
    }

    public HostedSessionLaunch? ResumeBusinessSession(VerificationProviderConnection connection, string reference, HostedSessionRequest request) =>
        Widget(connection, reference, request);

    private static HostedSessionLaunch Widget(VerificationProviderConnection connection, string reference, HostedSessionRequest request) => new()
    {
        Kind = HostedSessionLaunchKind.Widget,
        WidgetScriptUrl = WidgetScriptUrl,
        WidgetOptions = new Dictionary<string, object>
        {
            ["app_id"] = connection.Require(AppIdField),
            ["p_key"] = connection.Require(PublicKeyField),
            ["type"] = "custom",
            ["config"] = new Dictionary<string, object> { ["widget_id"] = connection.Require(WidgetIdField) },
            ["reference_id"] = reference,
            ["metadata"] = request.Metadata,
        },
    };

    public async Task<HostedSessionResult> GetBusinessSessionResultAsync(VerificationProviderConnection connection, string reference, CancellationToken cancellationToken = default)
    {
        using var message = Authorised(connection, HttpMethod.Get,
            $"{BaseUrl(connection.Environment)}/api/v1/kyc/verification?reference_id={Uri.EscapeDataString(reference)}");
        try
        {
            using var response = await _http.SendAsync(message, cancellationToken);
            var root = ProviderHttp.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            if (!response.IsSuccessStatusCode)
            {
                var detail = ProviderHttp.Describe(response.StatusCode, ProviderHttp.ErrorText(root, "error", "message"));
                // Dojah has no record until the applicant opens the widget.
                if ((int)response.StatusCode == 404)
                    return new HostedSessionResult { State = HostedSessionState.Pending, Message = $"Dojah: {detail}" };
                _logger.LogWarning("Dojah verification {Reference} answered {Detail}", reference, detail);
                return new HostedSessionResult { State = HostedSessionState.Unavailable, Message = $"Dojah: {detail}" };
            }

            if (root is not { ValueKind: JsonValueKind.Object } body)
                return new HostedSessionResult { State = HostedSessionState.Unavailable, Message = "Dojah answered without a JSON object." };
            var verification = body.TryGetProperty("entity", out var entity) && entity.ValueKind == JsonValueKind.Object ? entity : body;
            return ReadVerification(verification);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Dojah verification {Reference} couldn't be read", reference);
            return new HostedSessionResult { State = HostedSessionState.Unavailable, Message = $"Couldn't reach Dojah: {ex.Message}" };
        }
    }

    /// <summary>
    /// verification_status Completed with status true approves; Completed with status false,
    /// or Failed, rejects; Abandoned is abandoned; Ongoing and Pending wait.
    /// </summary>
    internal static HostedSessionResult ReadVerification(JsonElement verification)
    {
        var state = ProviderHttp.String(verification, "verification_status")?.ToLowerInvariant() switch
        {
            "completed" => Bool(verification, "status") ? HostedSessionState.Approved : HostedSessionState.Rejected,
            "failed" => HostedSessionState.Rejected,
            "abandoned" => HostedSessionState.Abandoned,
            _ => HostedSessionState.Pending,
        };
        return new HostedSessionResult
        {
            State = state,
            Record = state == HostedSessionState.Approved ? BusinessRecord(verification) : null,
            ProviderReference = ProviderHttp.String(verification, "reference_id"),
            Message = $"Dojah verification_status: {ProviderHttp.String(verification, "verification_status") ?? "(none)"}",
        };
    }

    /// <summary>
    /// The business check's data (<c>data.business_data.data</c>). Dojah doesn't publish its
    /// fields for EasyOnboard KYB, so the CAC lookup's names are read with common alternatives.
    /// </summary>
    private static BusinessRegistryRecord? BusinessRecord(JsonElement verification)
    {
        if (!verification.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("business_data", out var check) || check.ValueKind != JsonValueKind.Object)
            return null;
        var business = check.TryGetProperty("data", out var inner) && inner.ValueKind == JsonValueKind.Object ? inner : check;
        var name = ProviderHttp.String(business, "company_name") ?? ProviderHttp.String(business, "business_name") ?? ProviderHttp.String(business, "name");
        if (name == null) return null;

        var rawStatus = ProviderHttp.String(business, "status") ?? ProviderHttp.String(business, "company_status");
        return new BusinessRegistryRecord
        {
            RegisteredName = name,
            RegistrationNumber = ProviderHttp.String(business, "rc_number") ?? ProviderHttp.String(business, "registration_number") ?? string.Empty,
            EntityType = ProviderHttp.String(business, "type_of_company") ?? ProviderHttp.String(business, "company_type"),
            RegistrationDate = ProviderHttp.String(business, "date_of_registration") ?? ProviderHttp.String(business, "registration_date"),
            Address = ProviderHttp.String(business, "address"),
            RawStatus = rawStatus,
            ActivityStatus = RegistryStatus.Parse(rawStatus),
        };
    }

    public VerificationWebhook ReadWebhook(VerificationProviderConnection connection, IReadOnlyDictionary<string, string> headers, byte[] body)
    {
        if (!connection.Credentials.TryGetValue(WebhookSecretField, out var secret) || string.IsNullOrWhiteSpace(secret))
            return VerificationWebhook.Rejected("Dojah's webhook secret isn't configured.");
        var signature = headers.FirstOrDefault(h => string.Equals(h.Key, SignatureHeader, StringComparison.OrdinalIgnoreCase)).Value;
        if (string.IsNullOrWhiteSpace(signature))
            return VerificationWebhook.Rejected($"The {SignatureHeader} header is missing.");

        var expected = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), body)).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(signature.Trim().ToLowerInvariant())))
            return VerificationWebhook.Rejected("The signature doesn't match the body.");

        var root = ProviderHttp.Parse(Encoding.UTF8.GetString(body));
        if (root is not { ValueKind: JsonValueKind.Object } payload)
            return new VerificationWebhook(true, null, "The body isn't a JSON object.");
        var reference = ProviderHttp.String(payload, "reference_id")
            ?? (payload.TryGetProperty("entity", out var entity) ? ProviderHttp.String(entity, "reference_id") : null);
        return new VerificationWebhook(true, reference, reference == null ? "The body names no reference_id." : null);
    }

    private static bool Bool(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && (value.ValueKind == JsonValueKind.True
            || (value.ValueKind == JsonValueKind.String && bool.TryParse(value.GetString(), out var parsed) && parsed));

    private static string? JoinLocation(JsonElement entity)
    {
        var parts = new[] { ProviderHttp.String(entity, "lga"), ProviderHttp.String(entity, "city"), ProviderHttp.String(entity, "state") }
            .Where(p => p != null)
            .ToList();
        return parts.Count == 0 ? null : string.Join(", ", parts);
    }

    private static HttpRequestMessage Authorised(VerificationProviderConnection connection, HttpMethod method, string url)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation("Authorization", connection.Require(SecretKeyField));
        request.Headers.TryAddWithoutValidation("AppId", connection.Require(AppIdField));
        request.Headers.Accept.ParseAdd("application/json");
        return request;
    }
}

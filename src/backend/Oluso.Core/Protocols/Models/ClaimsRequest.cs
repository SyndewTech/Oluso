using System.Text.Json;

namespace Oluso.Core.Protocols.Models;

/// <summary>
/// The <c>claims</c> authorization request parameter (OpenID Connect Core 1.0 section 5.5): a JSON object whose
/// <c>id_token</c> and <c>userinfo</c> members name the individual claims wanted in each place. Each claim is
/// <c>null</c> (wanted, voluntary) or an object with <c>essential</c>, <c>value</c> or <c>values</c> (section
/// 5.5.1). Other top-level members are extensions and are ignored, as are unknown members of a claim's object.
/// </summary>
public sealed class ClaimsRequest
{
    public const string ParameterName = "claims";
    private const string IdTokenMember = "id_token";
    private const string UserInfoMember = "userinfo";

    /// <summary>
    /// The internal claim, like max_age_requested, that carries the names requested in the ID token from the code to
    /// the token endpoint and into the refresh token (space separated). It never reaches a token.
    /// </summary>
    public const string IdTokenClaimsCarrier = "id_token_claims_requested";

    private ClaimsRequest(IReadOnlyDictionary<string, RequestedClaim> idToken, IReadOnlyDictionary<string, RequestedClaim> userInfo)
    {
        IdToken = idToken;
        UserInfo = userInfo;
    }

    /// <summary>The claims requested in the ID token.</summary>
    public IReadOnlyDictionary<string, RequestedClaim> IdToken { get; }

    /// <summary>The claims requested from the UserInfo endpoint.</summary>
    public IReadOnlyDictionary<string, RequestedClaim> UserInfo { get; }

    /// <summary>The claim as requested in either place, the ID token's request first.</summary>
    public RequestedClaim? Find(string name) =>
        IdToken.TryGetValue(name, out var idTokenClaim) ? idTokenClaim
        : UserInfo.TryGetValue(name, out var userInfoClaim) ? userInfoClaim
        : null;

    /// <summary>
    /// Reads the parameter. False, with the reason, when it is not a JSON object of the section 5.5 shape.
    /// </summary>
    public static bool TryParse(string json, out ClaimsRequest? request, out string? error)
    {
        request = null;
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException)
        {
            error = "The claims parameter is not valid JSON";
            return false;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = "The claims parameter must be a JSON object";
                return false;
            }

            if (!TryReadMember(document.RootElement, IdTokenMember, out var idToken, out error)
                || !TryReadMember(document.RootElement, UserInfoMember, out var userInfo, out error))
            {
                return false;
            }

            request = new ClaimsRequest(idToken, userInfo);
            return true;
        }
    }

    private static bool TryReadMember(
        JsonElement root, string member, out IReadOnlyDictionary<string, RequestedClaim> claims, out string? error)
    {
        var result = new Dictionary<string, RequestedClaim>(StringComparer.Ordinal);
        claims = result;
        error = null;

        if (!root.TryGetProperty(member, out var element) || element.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            error = $"The claims parameter's {member} member must be a JSON object";
            return false;
        }

        foreach (var property in element.EnumerateObject())
        {
            var claim = property.Value;
            if (claim.ValueKind == JsonValueKind.Null)
            {
                result[property.Name] = RequestedClaim.Voluntary;
                continue;
            }

            if (claim.ValueKind != JsonValueKind.Object)
            {
                error = $"The requested claim {property.Name} in {member} must be null or a JSON object";
                return false;
            }

            var essential = false;
            if (claim.TryGetProperty("essential", out var essentialElement))
            {
                if (essentialElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                {
                    error = $"essential for the requested claim {property.Name} in {member} must be a boolean";
                    return false;
                }
                essential = essentialElement.GetBoolean();
            }

            List<string>? values = null;
            if (claim.TryGetProperty("value", out var valueElement))
            {
                values = new List<string> { ValueText(valueElement) };
            }
            else if (claim.TryGetProperty("values", out var valuesElement))
            {
                if (valuesElement.ValueKind != JsonValueKind.Array)
                {
                    error = $"values for the requested claim {property.Name} in {member} must be a JSON array";
                    return false;
                }
                values = valuesElement.EnumerateArray().Select(ValueText).ToList();
            }

            result[property.Name] = new RequestedClaim(essential, values);
        }

        return true;
    }

    private static string ValueText(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();
}

/// <summary>
/// One requested claim (OpenID Connect Core 1.0 section 5.5.1). <see cref="Values"/> holds <c>value</c> as a single
/// entry, or <c>values</c>, in preference order; null when neither was given.
/// </summary>
public sealed record RequestedClaim(bool Essential, IReadOnlyList<string>? Values)
{
    public static readonly RequestedClaim Voluntary = new(false, null);
}

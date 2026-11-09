using System.Security.Cryptography;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Protocols.Models;
using Oluso.Core.UserJourneys;

namespace Oluso.Core.Protocols;

/// <summary>
/// An authorization request made by a page without a redirect: the SDK's passkey sign-in, the embedded
/// and headless journeys, and FedCM. The code goes back to a script on <see cref="Origin"/>, never to a
/// redirect URI, so it is bound to a pseudo redirect URI (<see cref="PostMessage"/>, <see cref="FedCm"/>)
/// that the token request must repeat, and to a PKCE S256 challenge. Validated by
/// <see cref="IAuthorizationCodeIssuer"/> against the same client rules as the authorize endpoint.
/// </summary>
public sealed record DirectAuthorizationRequest
{
    /// <summary>The code is handed to the page by script (postMessage or a fetch response), like Google's <c>postmessage</c>.</summary>
    public const string PostMessage = "postmessage";

    /// <summary>The code is the token of a FedCM ID assertion, handed to the page by the browser.</summary>
    public const string FedCm = "fedcm";

    /// <summary>Asked for when the request names no scope; only those the client is allowed are granted.</summary>
    public const string DefaultScope = "openid profile email";

    public string? ClientId { get; init; }

    /// <summary>Space-separated scopes. Each must be allowed for the client, as at the authorize endpoint.</summary>
    public string? Scope { get; init; }

    public string? CodeChallenge { get; init; }

    /// <summary>Only <c>S256</c>. Absent means S256 (what the SDK computes).</summary>
    public string? CodeChallengeMethod { get; init; }

    /// <summary>The origin of the page the code is delivered to. It must be one the client registered.</summary>
    public string? Origin { get; init; }

    /// <summary><see cref="PostMessage"/> or <see cref="FedCm"/>.</summary>
    public string RedirectUri { get; init; } = PostMessage;

    public string? Nonce { get; init; }
}

/// <summary>Outcome of validating a <see cref="DirectAuthorizationRequest"/>.</summary>
public sealed class DirectAuthorizationValidation
{
    public string? Error { get; init; }
    public string? ErrorDescription { get; init; }
    public bool IsValid => Error == null;

    /// <summary>The client the request is for (valid requests only).</summary>
    public Client? Client { get; init; }

    /// <summary>The scopes that may be granted: requested and allowed for the client.</summary>
    public IReadOnlyList<string> Scopes { get; init; } = Array.Empty<string>();

    /// <summary>The page origin, normalized (scheme://host[:port]).</summary>
    public string? Origin { get; init; }

    /// <summary>The origin is not one the client registered: answer 403 rather than 400.</summary>
    public bool OriginRejected => Error == OidcConstants.Errors.AccessDenied;

    public static DirectAuthorizationValidation Failure(string error, string description) =>
        new() { Error = error, ErrorDescription = description };
}

/// <summary>Who signed in and how, for the code that completes the sign-in.</summary>
public sealed record SignInCompletion
{
    public required string UserId { get; init; }

    /// <summary>The method that signed the user in (<c>fido2</c>, <c>pwd</c>, ...), as the sign-in audit records it.</summary>
    public required string AuthenticationMethod { get; init; }

    /// <summary>Authentication method references for the ID token (<c>amr</c>); the method above is included.</summary>
    public IReadOnlyList<string> Amr { get; init; } = Array.Empty<string>();

    public string? Acr { get; init; }

    public DateTimeOffset? AuthTime { get; init; }

    /// <summary>The login session the tokens belong to (<c>sid</c>); a new one when absent.</summary>
    public string? SessionId { get; init; }

    /// <summary>
    /// Claims the sign-in produced beyond the user's profile (journey output claims). Profile claims are not
    /// taken from here: the token endpoint's profile service issues them for the granted scopes.
    /// </summary>
    public IEnumerable<KeyValuePair<string, object>>? Claims { get; init; }

    /// <summary>The scopes the user consented to during this sign-in, when a consent step asked.</summary>
    public IReadOnlyCollection<string>? ConsentedScopes { get; init; }

    /// <summary>
    /// The user authenticated just now, so the sign-in is audited. False when an existing session is reused
    /// (FedCM), which was audited when it signed in.
    /// </summary>
    public bool IsNewSignIn { get; init; } = true;
}

/// <summary>An issued code, or why none was.</summary>
public sealed class AuthorizationCodeIssueResult
{
    public string? Code { get; init; }
    public string? Error { get; init; }
    public string? ErrorDescription { get; init; }
    public bool Succeeded => Code != null;

    /// <summary>The request's origin is not registered for the client (403).</summary>
    public bool OriginRejected => Error == OidcConstants.Errors.AccessDenied && ErrorDescription == IAuthorizationCodeIssuer.OriginNotAllowed;

    public static AuthorizationCodeIssueResult Failure(string error, string? description) =>
        new() { Error = error, ErrorDescription = description };
}

/// <summary>
/// Completes sign-ins that return an authorization code to a page instead of a redirect URI (SDK passkey,
/// embedded and headless journeys, FedCM). It validates the request with the authorize endpoint's client
/// rules (enabled client, authorization_code grant, scopes the client is allowed, PKCE S256, an origin the
/// client registered, no PAR/JAR-only clients, consent), then issues the code the same way the authorize
/// endpoint does (<see cref="AuthorizationCodes"/>) with the session, amr and auth_time, records the sign-in
/// and the client's use. The token endpoint then builds the tokens through the profile service, as for any code.
/// </summary>
public interface IAuthorizationCodeIssuer
{
    const string OriginNotAllowed = "Origin not allowed for this client";

    /// <summary>The client may receive codes on this page origin (before anything else is asked of the user).</summary>
    Task<DirectAuthorizationValidation> ValidateClientAsync(
        string? clientId, string? origin, string redirectUri = DirectAuthorizationRequest.PostMessage, CancellationToken cancellationToken = default);

    /// <summary>
    /// The client could receive codes on a page through <paramref name="redirectUri"/> (FedCM client metadata):
    /// the same client rules, with the origin checked when one is given, else the client must have registered
    /// at least one page origin.
    /// </summary>
    Task<DirectAuthorizationValidation> ValidatePageClientAsync(
        string? clientId, string? origin, string redirectUri, CancellationToken cancellationToken = default);

    /// <summary>The whole request is valid: client, origin, scopes and PKCE.</summary>
    Task<DirectAuthorizationValidation> ValidateAsync(DirectAuthorizationRequest request, CancellationToken cancellationToken = default);

    /// <summary>Validates the request again and issues the code that completes this sign-in.</summary>
    Task<AuthorizationCodeIssueResult> IssueAsync(
        DirectAuthorizationRequest request, SignInCompletion signIn, CancellationToken cancellationToken = default);

    /// <summary>
    /// Issues the code a completed journey (embedded or headless) returns: the user, methods, consent and
    /// output claims come from the journey.
    /// </summary>
    Task<AuthorizationCodeIssueResult> IssueForJourneyAsync(
        DirectAuthorizationRequest request, JourneyState journey, JourneyCompletionResult completion, CancellationToken cancellationToken = default);
}

/// <summary>What an authorization code records, whichever endpoint issues it.</summary>
public sealed record NewAuthorizationCode
{
    public required string ClientId { get; init; }
    public required string SubjectId { get; init; }
    public required string RedirectUri { get; init; }
    public required IEnumerable<string> Scopes { get; init; }
    public string? CodeChallenge { get; init; }
    public string? CodeChallengeMethod { get; init; }
    public string? Nonce { get; init; }
    public string? SessionId { get; init; }
    public DateTime? AuthTime { get; init; }
    public IEnumerable<string>? Amr { get; init; }
    public string? Acr { get; init; }

    /// <summary>
    /// Extra claims carried to the token endpoint: strings only; protocol claims and the user claims the
    /// profile service owns (<see cref="AuthorizationCodes.ProfileClaims"/>) are dropped.
    /// </summary>
    public IEnumerable<KeyValuePair<string, object>>? Claims { get; init; }

    public IEnumerable<string>? Resources { get; init; }
    public string? TenantId { get; init; }

    /// <summary>The client's <c>AuthorizationCodeLifetime</c>; 0 or less uses the default.</summary>
    public int LifetimeSeconds { get; init; }
}

/// <summary>
/// The one shape of an authorization code: how it is generated, how long it lives and how the sign-in's
/// session, methods and time travel with it to the token endpoint. Used by the authorize endpoint and by
/// <see cref="IAuthorizationCodeIssuer"/>, so no endpoint mints its own.
/// </summary>
public static class AuthorizationCodes
{
    public const int DefaultLifetimeSeconds = 300;

    /// <summary>Code property holding the space-separated <c>amr</c> values.</summary>
    public const string AmrProperty = "amr";

    /// <summary>Code property holding the <c>acr</c>.</summary>
    public const string AcrProperty = "acr";

    /// <summary>Claims a sign-in cannot set through the code's claim bag: the token endpoint derives them.</summary>
    private static readonly HashSet<string> ProtocolClaims = new(StringComparer.Ordinal)
    {
        "sub", "sid", "amr", "acr", "auth_time", "nonce", "iss", "aud", "exp", "iat", "nbf", "jti",
        "azp", "client_id", "scope", "cnf", "at_hash", "c_hash"
    };

    /// <summary>
    /// User claims the profile service issues, gated by the granted scopes (and, for roles and permissions, by
    /// their own rules). A sign-in never carries them through the code: if it did, the token endpoint would
    /// add them whatever was granted.
    /// </summary>
    public static readonly IReadOnlySet<string> ProfileClaims = new HashSet<string>(StringComparer.Ordinal)
    {
        "name", "given_name", "family_name", "middle_name", "nickname", "preferred_username", "profile",
        "picture", "website", "email", "email_verified", "gender", "birthdate", "zoneinfo", "locale",
        "phone_number", "phone_number_verified", "address", "updated_at",
        "role", "roles", "permission", "permissions",
        // Upstream provider tokens kept on the session cookie; never forwarded to a client.
        "external_access_token", "external_id_token"
    };

    /// <summary>
    /// The session claims a signed-in session contributes to a code: where it signed in (<c>idp</c>), the
    /// tenant, and whether it is remembered. Everything else about the user comes from the profile service.
    /// </summary>
    public static readonly IReadOnlySet<string> SessionClaims = new HashSet<string>(StringComparer.Ordinal)
    {
        "idp", "tenant_id", Authorization.ReservedClaimKeys.RememberMe
    };

    public static string NewCode()
    {
        var bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=');
    }

    public static AuthorizationCode Create(NewAuthorizationCode code)
    {
        var now = DateTime.UtcNow;
        var claims = (code.Claims ?? Enumerable.Empty<KeyValuePair<string, object>>())
            .Where(c => c.Value is string && !ProtocolClaims.Contains(c.Key) && !ProfileClaims.Contains(c.Key))
            .GroupBy(c => c.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (string)g.Last().Value, StringComparer.Ordinal);

        if (code.AuthTime.HasValue)
        {
            claims[OidcConstants.StandardClaims.AuthTime] =
                new DateTimeOffset(DateTime.SpecifyKind(code.AuthTime.Value, DateTimeKind.Utc)).ToUnixTimeSeconds().ToString();
        }

        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        var amr = (code.Amr ?? Enumerable.Empty<string>())
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (amr.Count > 0) properties[AmrProperty] = string.Join(' ', amr);
        if (!string.IsNullOrEmpty(code.Acr)) properties[AcrProperty] = code.Acr;

        return new AuthorizationCode
        {
            Code = NewCode(),
            ClientId = code.ClientId,
            SubjectId = code.SubjectId,
            RedirectUri = code.RedirectUri,
            Scopes = code.Scopes.Distinct(StringComparer.Ordinal).ToList(),
            CodeChallenge = code.CodeChallenge,
            CodeChallengeMethod = code.CodeChallengeMethod,
            Nonce = code.Nonce,
            SessionId = code.SessionId,
            CreationTime = now,
            Expiration = now.AddSeconds(code.LifetimeSeconds > 0 ? code.LifetimeSeconds : DefaultLifetimeSeconds),
            Claims = claims,
            Properties = properties.Count > 0 ? properties : null,
            Resources = code.Resources?.ToList() ?? new List<string>(),
            TenantId = code.TenantId
        };
    }

    /// <summary>The <c>amr</c> values a code carries.</summary>
    public static IReadOnlyList<string> ReadAmr(AuthorizationCode code) =>
        code.Properties?.TryGetValue(AmrProperty, out var amr) == true && !string.IsNullOrWhiteSpace(amr)
            ? amr.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            : Array.Empty<string>();

    /// <summary>The <c>acr</c> a code carries.</summary>
    public static string? ReadAcr(AuthorizationCode code) =>
        code.Properties?.TryGetValue(AcrProperty, out var acr) == true && !string.IsNullOrEmpty(acr) ? acr : null;
}

using Oluso.Core.Common;

namespace Oluso.Core.Protocols.Validation;

/// <summary>
/// Validates request objects (OpenID Connect Core 1.0 section 6, RFC 9101): the authorization request
/// parameters a client passes as a JWT, by value in <c>request</c> or by reference in <c>request_uri</c>.
/// </summary>
public interface IRequestObjectValidator
{
    /// <summary>
    /// Resolves and verifies the request object the client <paramref name="clientId"/> sent. Pass the
    /// <c>request</c> value or a by-reference <c>request_uri</c> (never a PAR <c>request_uri</c>), not both.
    /// On success, <see cref="RequestObjectValidationResult.Parameters"/> holds the authorization request
    /// parameters it carries; they take precedence over the same parameters sent outside it.
    /// </summary>
    Task<RequestObjectValidationResult> ValidateAsync(
        string clientId,
        string? request,
        string? requestUri,
        CancellationToken cancellationToken = default);
}

/// <summary>The outcome of validating a request object.</summary>
public class RequestObjectValidationResult : ValidationResult
{
    /// <summary>The authorization request parameters in the request object, as single string values.</summary>
    public IReadOnlyDictionary<string, string> Parameters { get; init; } = new Dictionary<string, string>();

    /// <summary>The request object itself (fetched from the request_uri when sent by reference).</summary>
    public string? RequestObject { get; init; }

    /// <summary>Whether the request object was signed (and its signature verified), rather than alg none.</summary>
    public bool IsSigned { get; init; }

    public static RequestObjectValidationResult Success(
        IReadOnlyDictionary<string, string> parameters, string requestObject, bool isSigned) => new()
    {
        Parameters = parameters,
        RequestObject = requestObject,
        IsSigned = isSigned
    };

    public new static RequestObjectValidationResult Failure(string error, string? description = null) => new()
    {
        Error = error,
        ErrorDescription = description
    };
}

/// <summary>
/// The request object signing algorithms the server verifies (advertised as
/// request_object_signing_alg_values_supported). HS256/384/512 use the client secret, which is why a client that
/// registers one has its secret kept recoverable (see <c>IClientSecretProtector</c>). <c>none</c> is an unsigned
/// request object (OpenID Connect Core 1.0 section 6.1), refused for clients that require request objects.
/// </summary>
public static class RequestObjectSigningAlgorithms
{
    public const string None = "none";

    /// <summary>The client property holding the client's registered request_object_signing_alg.</summary>
    public const string ClientProperty = "request_object_signing_alg";

    public static readonly IReadOnlyList<string> Supported = new[]
    {
        None,
        "RS256", "RS384", "RS512",
        "PS256", "PS384", "PS512",
        "ES256", "ES384", "ES512",
        "HS256", "HS384", "HS512"
    };

    /// <summary>The supported signed algorithms (everything but none).</summary>
    public static bool IsSupportedSigned(string? alg) =>
        !string.IsNullOrEmpty(alg) && alg != None && Supported.Contains(alg, StringComparer.Ordinal);
}

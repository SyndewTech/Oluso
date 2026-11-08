using Microsoft.AspNetCore.Http;
using Oluso.Core.Protocols.DPoP;

namespace Oluso.Protocols.DPoP;

/// <summary>
/// Enforces DPoP sender-constraint where access tokens are used (RFC 9449 §7).
/// </summary>
public interface IDPoPResourceValidator
{
    /// <summary>
    /// Checks how a validated access token was presented. A token bound to a key (<c>cnf.jkt</c>) is accepted
    /// only under the <c>DPoP</c> scheme with one valid proof for this request, signed by that key and naming
    /// this token (<c>ath</c>); a bound token presented any other way is refused, so it cannot be downgraded
    /// to a bearer token. An unbound token cannot be presented under the <c>DPoP</c> scheme.
    /// </summary>
    /// <param name="scheme">The <c>Authorization</c> scheme the token arrived under, or null when it arrived
    /// another way (the SignalR query string).</param>
    Task<DPoPValidationResult> ValidateAsync(
        HttpRequest request, string? scheme, string accessToken, string? boundJkt, CancellationToken cancellationToken = default);
}

public sealed class DPoPResourceValidator : IDPoPResourceValidator
{
    private readonly IDPoPProofValidator _proofs;

    public DPoPResourceValidator(IDPoPProofValidator proofs) => _proofs = proofs;

    public async Task<DPoPValidationResult> ValidateAsync(
        HttpRequest request, string? scheme, string accessToken, string? boundJkt, CancellationToken cancellationToken = default)
    {
        var presentedAsDPoP = scheme == PresentedAccessToken.DPoPScheme;
        if (string.IsNullOrEmpty(boundJkt))
        {
            return presentedAsDPoP
                ? DPoPValidationResult.Failure("invalid_token", "The access token is not DPoP-bound; present it with the Bearer scheme.")
                : new DPoPValidationResult { IsValid = true };
        }

        if (!presentedAsDPoP)
            return DPoPValidationResult.Failure("invalid_token", "The access token is DPoP-bound; present it with the DPoP scheme and a DPoP proof.");

        var proofs = request.Headers["DPoP"];
        if (proofs.Count != 1 || string.IsNullOrEmpty(proofs[0]))
            return DPoPValidationResult.Failure("invalid_dpop_proof", "Exactly one DPoP proof is required.");

        return await _proofs.ValidateAsync(new DPoPValidationContext
        {
            Proof = proofs[0]!,
            HttpMethod = request.Method,
            HttpUri = $"{request.Scheme}://{request.Host}{request.PathBase}{request.Path}",
            ExpectedJwkThumbprint = boundJkt,
            ExpectedAccessTokenHash = _proofs.ComputeAccessTokenHash(accessToken),
        }, cancellationToken);
    }
}

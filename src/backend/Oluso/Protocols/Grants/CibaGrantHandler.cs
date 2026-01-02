using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Core.Protocols.Grants;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Services;

namespace Oluso.Protocols.Grants;

/// <summary>
/// Handles urn:openid:params:grant-type:ciba grant type
/// (OpenID Connect Client Initiated Backchannel Authentication)
/// </summary>
public class CibaGrantHandler : IGrantHandler
{
    private readonly ICibaService _cibaService;

    public string GrantType => OidcConstants.GrantTypes.Ciba;

    public CibaGrantHandler(ICibaService cibaService)
    {
        _cibaService = cibaService;
    }

    public async Task<GrantResult> HandleAsync(TokenRequest request, CancellationToken cancellationToken = default)
    {
        // Validate auth_req_id is present
        if (string.IsNullOrEmpty(request.AuthReqId))
        {
            return GrantResult.Failure(OidcConstants.Errors.InvalidRequest, "auth_req_id is required");
        }

        // Get the CIBA request status
        var status = await _cibaService.GetStatusAsync(
            request.AuthReqId,
            request.Client?.ClientId ?? "",
            cancellationToken);

        // Handle different statuses
        switch (status.Status)
        {
            case CibaRequestStatus.Pending:
                // Return authorization_pending error with slow_down if polling too fast
                return GrantResult.Failure(
                    OidcConstants.Errors.AuthorizationPending,
                    "The authorization request is still pending");

            case CibaRequestStatus.Denied:
                return GrantResult.Failure(
                    OidcConstants.Errors.AccessDenied,
                    status.ErrorDescription ?? "The user denied the authorization request");

            case CibaRequestStatus.Expired:
                return GrantResult.Failure(
                    OidcConstants.Errors.ExpiredToken,
                    status.ErrorDescription ?? "The auth_req_id has expired");

            case CibaRequestStatus.Consumed:
                return GrantResult.Failure(
                    OidcConstants.Errors.InvalidGrant,
                    "The auth_req_id has already been used");

            case CibaRequestStatus.Approved:
                // Success! Return the grant
                return new GrantResult
                {
                    SubjectId = status.SubjectId,
                    SessionId = status.SessionId,
                    Scopes = status.Scopes?.ToList() ?? new List<string>(),
                    Claims = new Dictionary<string, object>()
                };

            default:
                return GrantResult.Failure(
                    OidcConstants.Errors.InvalidGrant,
                    "Unknown CIBA request status");
        }
    }
}

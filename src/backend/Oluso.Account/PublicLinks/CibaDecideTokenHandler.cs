using System.Globalization;
using Microsoft.Extensions.Logging;
using Oluso.Account.Events;
using Oluso.Core.Authentication;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Locale;
using Oluso.Core.Services;

namespace Oluso.Account.PublicLinks;

/// <summary>
/// Approve/deny link for a CIBA sign-in request, emailed when the user can't be reached by
/// push. The token is bound to the user the request names and to the request (by its row id,
/// so the auth_req_id never appears in a link or log), and lives only as long as the request.
/// Approving or denying does what the account API's CIBA endpoints do, for that one request.
/// </summary>
public sealed class CibaDecideTokenHandler : IActionTokenHandler
{
    public const string ApproveOption = "approve";
    public const string DenyOption = "deny";

    private readonly IClientStore _clients;
    private readonly IOlusoEventService _events;
    private readonly ILogger<CibaDecideTokenHandler> _logger;
    private readonly ICibaStore? _store;
    private readonly ICibaService? _ciba;

    public CibaDecideTokenHandler(
        IClientStore clients,
        IOlusoEventService events,
        ILogger<CibaDecideTokenHandler> logger,
        ICibaStore? store = null,
        ICibaService? ciba = null)
    {
        _clients = clients;
        _events = events;
        _logger = logger;
        _store = store;
        _ciba = ciba;
    }

    public string Action => CibaNotifications.DecideAction;

    public async Task<ActionTokenPreview?> PreviewAsync(ActionTokenClaim claim, CancellationToken cancellationToken = default)
    {
        var request = await FindPendingAsync(claim, cancellationToken);
        if (request == null)
        {
            return new ActionTokenPreview
            {
                Title = UiStringDefaults.Resolve("ciba_link_title"),
                AlreadyActioned = true,
                AlreadyActionedReason = UiStringDefaults.Resolve("ciba_link_not_pending")
            };
        }

        var client = await _clients.FindClientByIdAsync(request.ClientId, cancellationToken);
        var clientName = client?.ClientName is { } name && !string.IsNullOrWhiteSpace(name) ? name : request.ClientId;

        return new ActionTokenPreview
        {
            Title = UiStringDefaults.Resolve("ciba_link_title"),
            Summary = UiStringDefaults.Resolve("ciba_link_summary", [clientName]),
            Description = string.IsNullOrWhiteSpace(request.BindingMessage)
                ? null
                : UiStringDefaults.Resolve("ciba_link_binding", [request.BindingMessage]),
            Options =
            {
                new ActionTokenOption { Key = ApproveOption, Label = UiStringDefaults.Resolve("ciba_link_approve"), Variant = "primary" },
                new ActionTokenOption { Key = DenyOption, Label = UiStringDefaults.Resolve("ciba_link_deny"), Variant = "danger" }
            }
        };
    }

    public async Task<ActionTokenExecuteResult> ExecuteAsync(
        ActionTokenClaim claim, string option, string? comments, CancellationToken cancellationToken = default)
    {
        var request = await FindPendingAsync(claim, cancellationToken)
            ?? throw new InvalidOperationException(UiStringDefaults.Resolve("ciba_link_not_pending"));

        switch (option)
        {
            case ApproveOption:
                if (!await _ciba!.ApproveRequestAsync(request.AuthReqId, claim.UserId, sessionId: null, cancellationToken))
                {
                    throw new InvalidOperationException(UiStringDefaults.Resolve("ciba_link_not_pending"));
                }
                await _events.RaiseAsync(new CibaRequestApprovedEvent
                {
                    SubjectId = claim.UserId,
                    TenantId = claim.TenantId,
                    AuthReqId = request.AuthReqId
                }, cancellationToken);
                _logger.LogInformation("User {UserId} approved a CIBA request from client {ClientId} by email link", claim.UserId, request.ClientId);
                return new ActionTokenExecuteResult { Message = UiStringDefaults.Resolve("ciba_link_approved") };

            case DenyOption:
                if (!await _ciba!.DenyRequestAsync(request.AuthReqId, cancellationToken))
                {
                    throw new InvalidOperationException(UiStringDefaults.Resolve("ciba_link_not_pending"));
                }
                await _events.RaiseAsync(new CibaRequestDeniedEvent
                {
                    SubjectId = claim.UserId,
                    TenantId = claim.TenantId,
                    AuthReqId = request.AuthReqId
                }, cancellationToken);
                _logger.LogInformation("User {UserId} denied a CIBA request from client {ClientId} by email link", claim.UserId, request.ClientId);
                return new ActionTokenExecuteResult { Message = UiStringDefaults.Resolve("ciba_link_denied") };

            default:
                throw new InvalidOperationException($"Unknown option '{option}'.");
        }
    }

    /// <summary>The claim's request, while it's still the user's and still awaiting a decision.</summary>
    private async Task<CibaRequest?> FindPendingAsync(ActionTokenClaim claim, CancellationToken cancellationToken)
    {
        if (_store == null || _ciba == null
            || !int.TryParse(claim.SubjectId, NumberStyles.None, CultureInfo.InvariantCulture, out var requestId))
        {
            return null;
        }

        var pending = await _store.GetPendingBySubjectAsync(claim.UserId, cancellationToken);
        return pending.FirstOrDefault(r => r.Id == requestId
            && r.Status == CibaRequestStatus.Pending
            && r.ExpiresAt > DateTime.UtcNow);
    }
}

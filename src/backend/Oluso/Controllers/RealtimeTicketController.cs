using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Oluso.Core.Protocols.Models;
using Oluso.Realtime;

namespace Oluso.Controllers;

/// <summary>
/// Issues the short-lived ticket a browser hands the realtime hub instead of its access token
/// (see <see cref="HubTicket"/>). Only an access token can obtain one; a ticket cannot be traded for another.
/// </summary>
[ApiController]
[Route("api/realtime/ticket")]
[Authorize(AuthenticationSchemes = OidcConstants.AccessTokenAuthenticationScheme)]
public class RealtimeTicketController : ControllerBase
{
    private readonly IHubTicketStore _tickets;

    public RealtimeTicketController(IHubTicketStore tickets) => _tickets = tickets;

    [HttpPost]
    public async Task<ActionResult<RealtimeTicketResponse>> Issue(CancellationToken cancellationToken) =>
        Ok(new RealtimeTicketResponse(
            await _tickets.IssueAsync(User, cancellationToken),
            (int)HubTicket.Lifetime.TotalSeconds));
}

/// <param name="Ticket">Hand to the realtime hub as its access token.</param>
/// <param name="ExpiresIn">Seconds the ticket stays valid.</param>
public sealed record RealtimeTicketResponse(string Ticket, int ExpiresIn);

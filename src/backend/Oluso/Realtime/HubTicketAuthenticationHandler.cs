using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Oluso.Protocols.DPoP;

namespace Oluso.Realtime;

/// <summary>
/// Authenticates realtime hub requests that present a <see cref="HubTicket"/> — in the <c>access_token</c>
/// query string (WebSocket and SSE) or as <c>Authorization: Bearer</c> (negotiate, long polling).
/// </summary>
public sealed class HubTicketAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "Oluso.HubTicket";

    private readonly IHubTicketStore _tickets;

    public HubTicketAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, IHubTicketStore tickets)
        : base(options, logger, encoder)
    {
        _tickets = tickets;
    }

    /// <summary>The hub ticket a realtime request presents, or null.</summary>
    public static string? TicketFrom(HttpRequest request)
    {
        if (!request.Path.StartsWithSegments("/hubs")) return null;
        var presented = request.Query["access_token"].FirstOrDefault();
        if (string.IsNullOrEmpty(presented)
            && PresentedAccessToken.FromAuthorizationHeader(request.Headers.Authorization) is { IsDPoP: false } bearer)
        {
            presented = bearer.Token;
        }
        return HubTicket.IsTicket(presented) ? presented : null;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var ticket = TicketFrom(Request);
        if (ticket == null) return AuthenticateResult.NoResult();
        var principal = await _tickets.RedeemAsync(ticket, SchemeName, Context.RequestAborted);
        return principal == null
            ? AuthenticateResult.Fail("The realtime ticket is unknown or has expired.")
            : AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }
}

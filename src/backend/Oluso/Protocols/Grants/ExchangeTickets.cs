namespace Oluso.Protocols.Grants;

/// <summary>
/// One-time tickets issued by token exchange (<see cref="Oluso.Core.Protocols.Models.OidcConstants.TokenTypes.Ticket"/>):
/// a short-lived JWT naming a subject for one audience client, redeemed once by that client at the token
/// endpoint for its own tokens. Single use is enforced through an action token whose raw value is the
/// ticket's <c>jti</c>.
/// </summary>
public static class ExchangeTickets
{
    /// <summary>The action-token action a ticket's <c>jti</c> is recorded under.</summary>
    public const string Action = "oauth.token_exchange.ticket";

    /// <summary>Key of the <see cref="ExchangeTicketDescriptor"/> in a grant result's custom data.</summary>
    public const string CustomDataKey = "exchange_ticket";

    /// <summary>Key of a token-exchange audience (non-ticket) in a grant result's custom data.</summary>
    public const string AudienceCustomDataKey = "exchange_audience";

    /// <summary>Configuration key of the ticket lifetime in seconds (default and maximum <see cref="MaxLifetimeSeconds"/>).</summary>
    public const string LifetimeConfigurationKey = "Oluso:TokenExchange:TicketLifetimeSeconds";

    public const int MaxLifetimeSeconds = 60;

    /// <summary>Clock skew allowed when a ticket is redeemed.</summary>
    public static readonly TimeSpan RedeemClockSkew = TimeSpan.FromSeconds(5);
}

/// <summary>What the token service needs to sign a ticket the grant handler has recorded.</summary>
public sealed record ExchangeTicketDescriptor(string Jti, string Audience, int LifetimeSeconds);

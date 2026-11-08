using Microsoft.AspNetCore.WebUtilities;
using Oluso.Core.Protocols;

namespace Oluso.UI;

/// <summary>
/// Helpers for the standalone auth Razor pages (Login, Register,
/// ForgotPassword, DirectLogin). These pages must only respond when
/// they're the UI surface of an in-flight protocol flow — they are not
/// publicly navigable URLs. Without this gate, anyone can browse to
/// /Account/Login outside the OAuth/OIDC/SAML cycle and get a usable
/// credential form, which is both a UX inconsistency and a small attack
/// surface.
/// </summary>
public static class AuthFlowGate
{
    /// <summary>
    /// Returns true when the supplied <paramref name="returnUrl"/> carries a
    /// <c>correlation_id</c> that resolves to a live <see cref="ProtocolState"/>.
    /// Page handlers should call this in their <c>OnGet</c> and return
    /// <c>NotFound()</c> when it returns false.
    /// </summary>
    public static async Task<bool> HasActiveFlowAsync(
        IProtocolStateStore protocolStateStore,
        string? returnUrl,
        CancellationToken cancellationToken = default)
    {
        var correlationId = ExtractCorrelationId(returnUrl);
        if (string.IsNullOrEmpty(correlationId)) return false;
        var state = await protocolStateStore.GetAsync(correlationId, cancellationToken);
        return state is not null;
    }

    /// <summary>
    /// The client of the protocol flow <paramref name="returnUrl"/> belongs to,
    /// or null when it carries no live flow.
    /// </summary>
    public static async Task<string?> GetClientIdAsync(
        IProtocolStateStore? protocolStateStore,
        string? returnUrl,
        CancellationToken cancellationToken = default)
    {
        var correlationId = ExtractCorrelationId(returnUrl);
        if (protocolStateStore == null || string.IsNullOrEmpty(correlationId)) return null;
        var state = await protocolStateStore.GetAsync(correlationId, cancellationToken);
        return string.IsNullOrEmpty(state?.ClientId) ? null : state.ClientId;
    }

    /// <summary>
    /// Pulls the <c>correlation_id</c> query parameter out of a callback-style
    /// return URL such as <c>/oauth/authorize/callback?correlation_id=&lt;guid&gt;</c>.
    /// Returns null if the URL has no query string or no correlation_id.
    /// </summary>
    public static string? ExtractCorrelationId(string? returnUrl)
    {
        if (string.IsNullOrEmpty(returnUrl)) return null;
        var queryStart = returnUrl.IndexOf('?');
        if (queryStart < 0) return null;

        var query = QueryHelpers.ParseQuery(returnUrl[queryStart..]);
        return query.TryGetValue("correlation_id", out var value) ? value.ToString() : null;
    }
}

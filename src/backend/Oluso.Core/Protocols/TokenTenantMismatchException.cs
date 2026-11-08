namespace Oluso.Core.Protocols;

/// <summary>
/// A token was to be issued for a tenant other than the one the request is routed to: the user has no access to
/// the routed tenant, so their token would be for another one, which this tenant's key must not sign. The token
/// endpoint answers <c>access_denied</c>.
/// </summary>
public sealed class TokenTenantMismatchException : InvalidOperationException
{
    public TokenTenantMismatchException(string tokenTenantId, string routedTenantId)
        : base($"A token for tenant '{tokenTenantId}' cannot be issued through tenant '{routedTenantId}'.")
    {
        TokenTenantId = tokenTenantId;
        RoutedTenantId = routedTenantId;
    }

    public string TokenTenantId { get; }
    public string RoutedTenantId { get; }
}

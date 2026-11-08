using Microsoft.Extensions.Logging;
using Oluso.Core.Authorization;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;

namespace Oluso.Core.Services;

/// <summary>
/// Emits token claims from custom /authorize parameters that a client has
/// allowlisted via <see cref="Client.AllowedAuthorizeParameters"/>.
///
/// The raw parameter values are captured at the authorization request and
/// arrive here as <see cref="ClaimsProviderContext.AdditionalData"/> (keyed by
/// parameter name). This provider is the single policy point that turns them
/// into claims: it loads the client's mapping (parameter → claim type) and
/// emits each value under its mapped claim — but never under a reserved /
/// system-managed key. Mapping onto a reserved key is already blocked on write
/// (admin + DCR) and at SaveChanges by <see cref="ReservedClaimKeys"/> /
/// ReservedAuthInterceptor; the check here is defense in depth so a stale or
/// hand-edited row can still never mint sub/role/tenant_id/permissions/etc.
/// </summary>
public class AuthorizeParameterClaimsProvider : IClaimsProvider
{
    private readonly IClientStore _clientStore;
    private readonly ILogger<AuthorizeParameterClaimsProvider> _logger;

    public AuthorizeParameterClaimsProvider(
        IClientStore clientStore,
        ILogger<AuthorizeParameterClaimsProvider> logger)
    {
        _clientStore = clientStore;
        _logger = logger;
    }

    public string ProviderId => "authorize-parameters";

    public int Priority => 100;

    public IEnumerable<string>? TriggerScopes => null;

    public bool ShouldProvide(ClaimsProviderContext context) =>
        context.AdditionalData is { Count: > 0 } && !string.IsNullOrEmpty(context.ClientId);

    public async Task<ClaimsProviderResult> GetClaimsAsync(
        ClaimsProviderContext context,
        CancellationToken cancellationToken = default)
    {
        var claims = new Dictionary<string, object>(StringComparer.Ordinal);

        if (context.AdditionalData is not { Count: > 0 } additionalData || string.IsNullOrEmpty(context.ClientId))
        {
            return ClaimsProviderResult.Succeeded(claims);
        }

        var client = await _clientStore.FindClientByIdAsync(context.ClientId, cancellationToken);
        if (client?.AllowedAuthorizeParameters == null || client.AllowedAuthorizeParameters.Count == 0)
        {
            return ClaimsProviderResult.Succeeded(claims);
        }

        foreach (var mapping in client.AllowedAuthorizeParameters)
        {
            if (!additionalData.TryGetValue(mapping.Parameter, out var value) || value is null)
            {
                continue;
            }

            if (ReservedClaimKeys.IsReserved(mapping.ClaimType) || ReservedClaimKeys.IsPermissionEntry(mapping.ClaimType))
            {
                // Should be unreachable — blocked on write — but never emit a
                // system-managed claim from a client-controlled parameter.
                _logger.LogWarning(
                    "Refusing to emit reserved claim '{ClaimType}' from authorize parameter '{Parameter}' for client {ClientId}",
                    mapping.ClaimType, mapping.Parameter, context.ClientId);
                continue;
            }

            claims[mapping.ClaimType] = value;
        }

        return ClaimsProviderResult.Succeeded(claims);
    }
}

using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Core.Protocols.Grants;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Services;

namespace Oluso.Protocols.Grants;

/// <summary>
/// Default implementation of grant handler registry.
/// Combines built-in IGrantHandler implementations with custom IExtensionGrantValidator implementations.
/// Extension validators take precedence, allowing users to override built-in grant types.
/// </summary>
/// TODO: Consider making this extensible via DI in the future. how to handle multiple registries?
public class GrantHandlerRegistry : IGrantHandlerRegistry
{
    private readonly Dictionary<string, IGrantHandler> _handlers;

    public GrantHandlerRegistry(
        IEnumerable<IGrantHandler> handlers,
        IEnumerable<IExtensionGrantValidator> extensionValidators,
        IClientStore clientStore)
    {
        // Start with built-in handlers
        _handlers = handlers.ToDictionary(h => h.GrantType, StringComparer.Ordinal);

        // Extension validators can override built-in handlers or add new grant types
        foreach (var validator in extensionValidators)
        {
            _handlers[validator.GrantType] = new ExtensionGrantHandlerAdapter(validator, clientStore);
        }
    }

    public IGrantHandler? GetHandler(string grantType)
    {
        _handlers.TryGetValue(grantType, out var handler);
        return handler;
    }

    public IEnumerable<string> SupportedGrantTypes => _handlers.Keys;
}

/// <summary>
/// Adapts an IExtensionGrantValidator to the IGrantHandler interface
/// </summary>
internal class ExtensionGrantHandlerAdapter : IGrantHandler
{
    private readonly IExtensionGrantValidator _validator;
    private readonly IClientStore _clientStore;

    public string GrantType => _validator.GrantType;

    public ExtensionGrantHandlerAdapter(IExtensionGrantValidator validator, IClientStore clientStore)
    {
        _validator = validator;
        _clientStore = clientStore;
    }

    public async Task<GrantResult> HandleAsync(TokenRequest request, CancellationToken cancellationToken = default)
    {
        // Get full client entity for the validator context
        var client = await _clientStore.FindClientByIdAsync(request.Client!.ClientId, cancellationToken);
        if (client == null)
        {
            return GrantResult.Failure(OidcConstants.Errors.InvalidClient, "Client not found");
        }

        var context = new ExtensionGrantValidationContext
        {
            Client = client,
            Request = request.Raw
        };

        await _validator.ValidateAsync(context);

        if (!context.Result.IsValid)
        {
            return GrantResult.Failure(
                context.Result.Error ?? OidcConstants.Errors.InvalidGrant,
                context.Result.ErrorDescription);
        }

        return new GrantResult
        {
            SubjectId = context.Result.SubjectId,
            Scopes = request.RequestedScopes.ToList(),
            Claims = context.Result.Claims?.ToDictionary(c => c.Type, c => (object)c.Value)
                ?? new Dictionary<string, object>()
        };
    }
}

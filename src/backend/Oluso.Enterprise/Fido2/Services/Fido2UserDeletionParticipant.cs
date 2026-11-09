using Microsoft.Extensions.DependencyInjection;
using Oluso.Core.Services;
using Oluso.Enterprise.Fido2.Stores;

namespace Oluso.Enterprise.Fido2.Services;

/// <summary>
/// Removes a deleted user's passkeys. The store is resolved on use so resolving
/// the user service doesn't open the FIDO2 context.
/// </summary>
public sealed class Fido2UserDeletionParticipant : IUserDeletionParticipant
{
    private readonly IServiceProvider _services;

    public Fido2UserDeletionParticipant(IServiceProvider services)
    {
        _services = services;
    }

    public async Task DeleteUserDataAsync(string userId, CancellationToken cancellationToken = default)
    {
        var store = _services.GetService<IFido2CredentialStore>();
        if (store == null) return;

        foreach (var credential in await store.GetByUserIdAsync(userId, cancellationToken))
        {
            await store.RemoveAsync(credential.Id, cancellationToken);
        }
    }
}

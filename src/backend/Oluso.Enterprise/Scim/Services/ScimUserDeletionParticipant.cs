using Microsoft.Extensions.DependencyInjection;
using Oluso.Core.Services;
using Oluso.Enterprise.Scim.Stores;

namespace Oluso.Enterprise.Scim.Services;

/// <summary>
/// Drops the inbound SCIM mapping of a deleted user, so the identity provider's
/// external id no longer points at an account that doesn't exist. Outbound
/// provider mappings stay: the queued Remove needs them to deprovision downstream.
/// </summary>
public sealed class ScimUserDeletionParticipant : IUserDeletionParticipant
{
    private readonly IServiceProvider _services;

    public ScimUserDeletionParticipant(IServiceProvider services)
    {
        _services = services;
    }

    public Task DeleteUserDataAsync(string userId, CancellationToken cancellationToken = default) =>
        _services.GetRequiredService<IScimResourceMappingStore>()
            .DeleteByInternalIdAsync("User", userId, cancellationToken);
}

using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Core.Services;

namespace Oluso.Tests.Protocols;

/// <summary>The shared client user check, as the token paths get it.</summary>
internal static class ClientUserAccessKit
{
    public static IClientUserAccess For(IProfileService profiles, IOlusoUserService? users = null) =>
        new ClientUserAccess(profiles, users ?? Mock.Of<IOlusoUserService>(), NullLogger<ClientUserAccess>.Instance);

    public static IClientUserAccess Open() => For(Mock.Of<IProfileService>());
}

using Microsoft.Extensions.DependencyInjection;
using Oluso.Account.Controllers;

namespace Oluso.Account;

/// <summary>
/// Extension methods for adding Oluso Account API
/// </summary>
public static class OlusoAccountExtensions
{
    /// <summary>
    /// Add Oluso Account API controllers for end-user self-service
    /// (profile, security, sessions, connected apps, passkeys)
    /// </summary>
    public static IMvcBuilder AddOlusoAccount(this IMvcBuilder mvcBuilder)
    {
        // Add account controllers from this assembly
        mvcBuilder.AddApplicationPart(typeof(ProfileController).Assembly);

        return mvcBuilder;
    }
}

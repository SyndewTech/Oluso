using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Oluso.Account.Controllers;
using Oluso.Core.Protocols.Models;

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

        // Register AccountApi authorization policy
        mvcBuilder.Services.AddAuthorization(authOptions =>
        {
            // AccountApi policy: End-user self-service access
            // Uses OIDC access tokens - for end users authenticated via the identity server
            // Requires authenticated user (no admin role needed)
            // Used for account management endpoints like profile, sessions, passkeys
            authOptions.AddPolicy("AccountApi", policy =>
            {
                policy.AuthenticationSchemes.Add(OidcConstants.AccessTokenAuthenticationScheme);
                policy.RequireAuthenticatedUser();
            });
        });

        return mvcBuilder;
    }
}

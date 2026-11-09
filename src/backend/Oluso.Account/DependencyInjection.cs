using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Oluso.Account.Controllers;
using Oluso.Core.Authentication;
using Oluso.Core.Authorization;
using Oluso.Core.Events;
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

        // Register auto-generated event property source for account events
        mvcBuilder.Services.AddEventPropertySource<AccountEventPropertySource>();

        // Approve/deny link in the CIBA email sent when the user has no push.
        mvcBuilder.Services.AddScoped<IActionTokenHandler, PublicLinks.CibaDecideTokenHandler>();

        // Register authorization handler for manager requirement
        mvcBuilder.Services.AddScoped<IAuthorizationHandler, ManagerAuthorizationHandler>();

        // Tokens of scope-restricted clients are accepted only on endpoints that
        // declare a scope the token carries — see ScopeRestrictedClients.
        mvcBuilder.Services.AddScopeRestrictedClients();

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
                policy.Requirements.Add(new ScopeRestrictedClientRequirement());
            });

            // IsManager policy: Requires the user to have the is_manager claim = true
            // Used for manager-only workspace endpoints (approvals, team management)
            authOptions.AddPolicy("IsManager", policy =>
            {
                policy.AuthenticationSchemes.Add(OidcConstants.AccessTokenAuthenticationScheme);
                policy.RequireAuthenticatedUser();
                policy.Requirements.Add(new ManagerRequirement());
                policy.Requirements.Add(new ScopeRestrictedClientRequirement());
            });
        });

        return mvcBuilder;
    }
}

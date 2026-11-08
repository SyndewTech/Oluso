using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using Oluso.Core.Protocols.Models;

namespace Oluso.Core.Authentication;

/// <summary>
/// How and when the session signed in (<c>amr</c>, <c>auth_time</c>), stamped on the
/// session cookie. They describe the sign-in, not the user, so a principal rebuilt from
/// the user (security-stamp refresh) must carry them over. <c>max_age</c> and session
/// MFA reuse read them from the cookie.
/// </summary>
public static class SessionAuthenticationClaims
{
    private static readonly string[] SignInClaimTypes =
    {
        OidcConstants.StandardClaims.Amr,
        OidcConstants.StandardClaims.AuthTime
    };

    public static IReadOnlyList<Claim> Create(IEnumerable<string> authenticationMethods, DateTimeOffset authTime)
    {
        var claims = authenticationMethods
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Distinct(StringComparer.Ordinal)
            .Select(m => new Claim(OidcConstants.StandardClaims.Amr, m))
            .ToList();
        claims.Add(new Claim(OidcConstants.StandardClaims.AuthTime, authTime.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64));
        return claims;
    }

    public static IServiceCollection PreserveSessionAuthenticationClaims(this IServiceCollection services)
    {
        services.Configure<SecurityStampValidatorOptions>(options =>
        {
            var previous = options.OnRefreshingPrincipal;
            options.OnRefreshingPrincipal = async context =>
            {
                CarryOver(context.CurrentPrincipal, context.NewPrincipal);
                if (previous != null) await previous(context);
            };
        });
        return services;
    }

    public static void CarryOver(ClaimsPrincipal? current, ClaimsPrincipal? rebuilt)
    {
        if (current == null || rebuilt?.Identity is not ClaimsIdentity identity) return;

        foreach (var type in SignInClaimTypes)
        {
            if (identity.HasClaim(c => c.Type == type)) continue;
            foreach (var claim in current.FindAll(type))
            {
                identity.AddClaim(new Claim(claim.Type, claim.Value, claim.ValueType));
            }
        }
    }
}

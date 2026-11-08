using Oluso.Core.Services;
using Oluso.Enterprise.Saml.Stores;

namespace Oluso.Enterprise.Saml.IdentityProvider;

/// <summary>
/// Whether a user may have an assertion for a service provider. Asked by every path that issues one (the SSO
/// endpoint and the journey-driven protocol service).
/// </summary>
internal static class SamlUserAccess
{
    public const string EmailNameIdFormat = "urn:oasis:names:tc:SAML:1.1:nameid-format:emailAddress";

    /// <summary>
    /// Null when <paramref name="userId"/> may have the assertion; otherwise why not, worded for the user. An
    /// email NameID needs a verified address, since the service provider keys the account on it; so does a
    /// service provider that requires one.
    /// </summary>
    public static async Task<string?> FindDenialAsync(
        ISamlServiceProviderStore serviceProviders,
        IOlusoUserService users,
        string spEntityId,
        string? nameIdFormat,
        string userId,
        CancellationToken cancellationToken)
    {
        var requiresVerifiedEmail = string.Equals(nameIdFormat, EmailNameIdFormat, StringComparison.Ordinal);
        if (!requiresVerifiedEmail)
        {
            var serviceProvider = await serviceProviders.GetByEntityIdAsync(spEntityId, cancellationToken);
            requiresVerifiedEmail = serviceProvider?.RequireVerifiedEmail == true;
        }

        if (!requiresVerifiedEmail)
        {
            return null;
        }

        var user = (await users.FindByIdsAcrossTenantsAsync(new[] { userId }, cancellationToken)).GetValueOrDefault(userId);
        return user != null && !string.IsNullOrEmpty(user.Email) && user.EmailVerified
            ? null
            : ClientUserAccess.UnverifiedEmailDescription;
    }
}

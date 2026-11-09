using Oluso.Core.Services;

namespace Oluso.Admin.Controllers;

/// <summary>
/// Checks shared by the two admin endpoints that write a tenant's protocol configuration
/// (<see cref="TenantsController"/> and the Settings page's Protocol tab in <see cref="SettingsController"/>).
/// </summary>
internal static class ProtocolConfigurationRules
{
    /// <summary>The first problem with the values, as a message; null when they are valid.</summary>
    public static string? Validate(int? prefixScopeSuffixMaxLength, IEnumerable<string>? dynamicRegistrationAllowedScopes)
    {
        if (prefixScopeSuffixMaxLength is { } max
            && (max < ApiScopePrefixes.MinDefaultSuffixMaxLength || max > ApiScopePrefixes.MaxDefaultSuffixMaxLength))
        {
            return $"Default maximum suffix length for prefix scopes must be between " +
                   $"{ApiScopePrefixes.MinDefaultSuffixMaxLength} and {ApiScopePrefixes.MaxDefaultSuffixMaxLength}.";
        }

        // A self-registered client has no admin behind it to hold a sensitive
        // scope's permission, so dynamic registration can never offer one.
        var sensitiveScopes = dynamicRegistrationAllowedScopes?
            .Select(Service.ServiceScopes.Find)
            .OfType<Service.ServiceScopeDefinition>()
            .Where(d => d.RequiresExplicitGrant)
            .Select(d => d.Name)
            .ToList();
        if (sensitiveScopes is { Count: > 0 })
        {
            return $"Dynamically registered clients can't be given {string.Join(", ", sensitiveScopes)}. " +
                   "Grant it on the client's Service API settings instead.";
        }

        return null;
    }

    /// <summary>
    /// The value to store for a requested default: null (the built-in default) when it equals
    /// <see cref="ApiScopePrefixes.DefaultSuffixMaxLength"/>, so a later change of the built-in default still applies.
    /// </summary>
    public static int? StoredPrefixScopeSuffixMaxLength(int requested) =>
        requested == ApiScopePrefixes.DefaultSuffixMaxLength ? null : requested;
}

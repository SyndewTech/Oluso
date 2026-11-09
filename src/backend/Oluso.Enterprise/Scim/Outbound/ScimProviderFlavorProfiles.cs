using Oluso.Enterprise.Scim.Entities;

namespace Oluso.Enterprise.Scim.Outbound;

/// <summary>
/// Internal user properties an outbound mapping can read. <c>custom:&lt;key&gt;</c>
/// reads a key from the user's custom properties.
/// </summary>
public static class ScimOutboundProperties
{
    public const string UserName = "UserName";
    public const string Email = "Email";
    public const string FirstName = "FirstName";
    public const string LastName = "LastName";
    public const string DisplayName = "DisplayName";
    public const string FullName = "FullName";
    public const string PhoneNumber = "PhoneNumber";
    public const string Locale = "Locale";
    public const string TimeZone = "TimeZone";
    public const string Id = "Id";
    public const string ProfilePictureUrl = "ProfilePictureUrl";
    public const string CustomPrefix = "custom:";

    public static readonly string[] All =
    {
        UserName, Email, FirstName, LastName, DisplayName, FullName, PhoneNumber, Locale, TimeZone, Id, ProfilePictureUrl
    };
}

public static class ScimOutboundTransformations
{
    public const string None = "none";
    public const string Lowercase = "lowercase";
    public const string Uppercase = "uppercase";
    public const string Trim = "trim";
    public const string NormalizeEmail = "normalize_email";
    public const string NormalizePhone = "normalize_phone";
    public const string ExtractDomain = "extract_domain";
    public const string FormatName = "format_name";

    public static readonly string[] All =
    {
        None, Lowercase, Uppercase, Trim, NormalizeEmail, NormalizePhone, ExtractDomain, FormatName
    };
}

public sealed record ScimOutboundDefaultMapping(string ScimAttribute, string InternalProperty);

/// <summary>
/// How a family of SCIM service providers deviates from the base protocol,
/// so the same push pipeline can talk to each without per-target code paths.
/// </summary>
public sealed class ScimProviderFlavorProfile
{
    public required string Key { get; init; }
    public required string DisplayName { get; init; }
    public required string Description { get; init; }

    /// <summary>Guidance shown to the administrator configuring this flavor.</summary>
    public string? Notes { get; init; }

    public required string DefaultUserNameSource { get; init; }

    /// <summary>The provider rejects a userName that is not a full email address.</summary>
    public bool RequiresEmailUserName { get; init; }

    /// <summary>False when the provider drops phoneNumbers; we then leave them out.</summary>
    public bool SupportsPhoneNumbers { get; init; } = true;

    /// <summary>False when the provider drops the enterprise extension.</summary>
    public bool SupportsEnterpriseExtension { get; init; } = true;

    public bool SupportsGroups { get; init; } = true;

    /// <summary>
    /// Team groups carry the team extension (<see cref="ScimTeamGroupExtension"/>): the team's code, its parent's group,
    /// its leads and the module that manages it, for providers that build on the team hierarchy. Off for everything else,
    /// whose payloads stay plain RFC 7643 Groups.
    /// </summary>
    public bool SendsTeamExtension { get; init; }

    /// <summary>
    /// Filter attributes we may look a user up by before creating one, in
    /// the order tried. Providers that don't index externalId omit it.
    /// </summary>
    public required IReadOnlyList<string> UserLookupAttributes { get; init; }

    public required IReadOnlyList<string> GroupLookupAttributes { get; init; }

    public required IReadOnlyList<ScimOutboundDefaultMapping> DefaultUserMappings { get; init; }
}

public static class ScimProviderFlavorProfiles
{
    /// <summary>The user mappings most SCIM servers accept; flavors registered by other modules can reuse them.</summary>
    public static readonly IReadOnlyList<ScimOutboundDefaultMapping> StandardUserMappings = new ScimOutboundDefaultMapping[]
    {
        new("name.givenName", ScimOutboundProperties.FirstName),
        new("name.familyName", ScimOutboundProperties.LastName),
        new("displayName", ScimOutboundProperties.DisplayName),
        new("emails[type eq \"work\"].value", ScimOutboundProperties.Email),
        new("phoneNumbers[type eq \"work\"].value", ScimOutboundProperties.PhoneNumber),
        new("locale", ScimOutboundProperties.Locale),
        new("timezone", ScimOutboundProperties.TimeZone),
    };

    public static readonly ScimProviderFlavorProfile Generic = new()
    {
        Key = ScimProviderFlavors.Generic,
        DisplayName = "Generic SCIM 2.0",
        Description = "Any RFC 7643/7644 service provider.",
        DefaultUserNameSource = ScimProviderUserNameSources.UserName,
        UserLookupAttributes = new[] { "externalId", "userName" },
        GroupLookupAttributes = new[] { "externalId", "displayName" },
        DefaultUserMappings = StandardUserMappings,
    };

    public static readonly ScimProviderFlavorProfile Stalwart = new()
    {
        Key = ScimProviderFlavors.Stalwart,
        DisplayName = "Stalwart Mail Server",
        Description = "Stalwart's SCIM 2.0 endpoint (Enterprise Edition).",
        Notes = "userName must be a full email address in a domain with allowScimProvisioning enabled. " +
                "Authenticate with an API key issued on a service-principal account placed in the tenant being provisioned. " +
                "Groups become mail-enabled group accounts in the service principal's domain, so leave group sync off unless that is wanted. " +
                "The primary email is read-only on Stalwart and is derived from userName; phone numbers and the enterprise extension are dropped.",
        DefaultUserNameSource = ScimProviderUserNameSources.Email,
        RequiresEmailUserName = true,
        SupportsPhoneNumbers = false,
        SupportsEnterpriseExtension = false,
        UserLookupAttributes = new[] { "externalId", "userName" },
        GroupLookupAttributes = new[] { "externalId", "displayName" },
        DefaultUserMappings = new ScimOutboundDefaultMapping[]
        {
            new("displayName", ScimOutboundProperties.DisplayName),
            new("name.givenName", ScimOutboundProperties.FirstName),
            new("name.familyName", ScimOutboundProperties.LastName),
            new("locale", ScimOutboundProperties.Locale),
            new("timezone", ScimOutboundProperties.TimeZone),
        },
    };
}

/// <summary>
/// The flavors this instance knows: the built-in ones plus any a module registers as a
/// <see cref="ScimProviderFlavorProfile"/> singleton. An unknown key reads as <see cref="ScimProviderFlavorProfiles.Generic"/>.
/// </summary>
public sealed class ScimProviderFlavorRegistry
{
    private readonly IReadOnlyList<ScimProviderFlavorProfile> _profiles;

    public ScimProviderFlavorRegistry(IEnumerable<ScimProviderFlavorProfile> profiles)
    {
        _profiles = profiles
            .GroupBy(p => p.Key, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.Last())
            .ToList();
    }

    public IReadOnlyList<ScimProviderFlavorProfile> All => _profiles;

    public ScimProviderFlavorProfile Get(string? key) =>
        _profiles.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase))
            ?? ScimProviderFlavorProfiles.Generic;

    public bool IsKnown(string? key) =>
        _profiles.Any(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
}

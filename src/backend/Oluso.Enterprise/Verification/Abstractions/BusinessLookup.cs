namespace Oluso.Enterprise.Verification;

/// <summary>A registry lookup, already normalised for the country.</summary>
public sealed record BusinessLookupRequest
{
    /// <summary>ISO 3166-1 alpha-2.</summary>
    public required string Country { get; init; }

    /// <summary>The registration number in the country's canonical form (see <see cref="BusinessRegistrationNumber"/>).</summary>
    public required string RegistrationNumber { get; init; }

    /// <summary>The registry's code for the kind of registration, when the country has several (e.g. RC, BN, IT in Nigeria).</summary>
    public string? RegistrationType { get; init; }

    /// <summary>The name the applicant gave; providers that can narrow a search with it do.</summary>
    public string? BusinessName { get; init; }
}

/// <summary>What a lookup came to. Everything but <see cref="Found"/> carries no record.</summary>
public enum BusinessLookupOutcome
{
    Found,

    /// <summary>The registry has no business under that number.</summary>
    NotFound,

    /// <summary>The provider or the registry behind it failed, timed out or rate-limited; trying later may work.</summary>
    ProviderUnavailable,

    /// <summary>The provider refused the request as malformed (e.g. a number in the wrong format).</summary>
    InvalidRequest,

    /// <summary>The provider rejected the credentials, the account lacks the product, or the wallet is empty.</summary>
    Unauthorized,
}

/// <summary>Whether the registry lists the business as trading.</summary>
public enum BusinessActivityStatus
{
    /// <summary>The provider didn't report a status.</summary>
    Unknown = 0,
    Active = 1,
    Inactive = 2,
}

/// <summary>A person the registry lists against the business.</summary>
public sealed record BusinessPerson(string Name, string? Role);

/// <summary>The registry's record of a business, in provider-neutral terms.</summary>
public sealed record BusinessRegistryRecord
{
    public required string RegisteredName { get; init; }
    public required string RegistrationNumber { get; init; }

    /// <summary>The registry's description of the entity (e.g. COMPANY, BUSINESS_NAME, PRIVATE COMPANY LIMITED BY SHARES).</summary>
    public string? EntityType { get; init; }

    /// <summary>As the provider reported it (ISO date or date-time).</summary>
    public string? RegistrationDate { get; init; }

    public string? Address { get; init; }

    public BusinessActivityStatus ActivityStatus { get; init; }

    /// <summary>The status text the provider returned, kept for review.</summary>
    public string? RawStatus { get; init; }

    public IReadOnlyList<BusinessPerson> People { get; init; } = Array.Empty<BusinessPerson>();
}

public sealed record BusinessLookupResult
{
    public required BusinessLookupOutcome Outcome { get; init; }
    public BusinessRegistryRecord? Record { get; init; }

    /// <summary>The provider's id for this check, for support tickets and reconciliation.</summary>
    public string? ProviderReference { get; init; }

    /// <summary>Why the lookup didn't find a record; for logs and the admin view, never shown to the applicant.</summary>
    public string? Message { get; init; }

    public static BusinessLookupResult Found(BusinessRegistryRecord record, string? providerReference) =>
        new() { Outcome = BusinessLookupOutcome.Found, Record = record, ProviderReference = providerReference };

    public static BusinessLookupResult Failure(BusinessLookupOutcome outcome, string message, string? providerReference = null) =>
        new() { Outcome = outcome, Message = message, ProviderReference = providerReference };
}

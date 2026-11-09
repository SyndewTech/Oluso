using Oluso.Core.Services;

namespace Oluso.Core.UserJourneys;

/// <summary>
/// The stored-profile values a journey condition may test on the journey's user: a fixed
/// set of identity and verification fields, then the user's custom profile properties.
/// Lockout state, failure counts and credentials are never reachable. Values are only
/// compared; they are not written to journey state or shown to the user.
/// </summary>
public static class UserConditionFields
{
    /// <summary>Null when the field is unset or blank, so "not exists" covers both.</summary>
    public static string? GetValue(OlusoUserInfo user, string field)
    {
        var value = field.ToLowerInvariant() switch
        {
            "id" or "userid" or "user_id" or "sub" => user.Id,
            "username" => user.Username,
            "email" => user.Email,
            "email_verified" or "emailverified" => Flag(user.EmailVerified),
            "phone_number" or "phonenumber" => user.PhoneNumber,
            "phone_number_verified" or "phonenumberverified" => Flag(user.PhoneNumberVerified),
            "first_name" or "firstname" or "given_name" => user.FirstName,
            "last_name" or "lastname" or "family_name" => user.LastName,
            "display_name" or "displayname" or "name" => user.DisplayName,
            "picture" => user.Picture,
            "tenant_id" or "tenantid" => user.TenantId,
            "two_factor_enabled" or "twofactorenabled" or "mfa_enabled" => Flag(user.TwoFactorEnabled),
            "has_password" or "haspassword" => Flag(user.HasPassword),
            "is_federated" or "isfederated" => Flag(user.IsFederated),
            _ => CustomProperty(user, field)
        };

        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static string Flag(bool value) => value ? "true" : "false";

    private static string? CustomProperty(OlusoUserInfo user, string field)
    {
        if (user.CustomProperties == null) return null;
        if (user.CustomProperties.TryGetValue(field, out var exact)) return exact;
        return user.CustomProperties
            .FirstOrDefault(p => string.Equals(p.Key, field, StringComparison.OrdinalIgnoreCase))
            .Value;
    }
}

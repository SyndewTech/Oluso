using Oluso.Core.Domain.Entities;
using Oluso.Enterprise.Scim.Models;

namespace Oluso.Enterprise.Scim.Services;

/// <summary>
/// Maps between SCIM User resources and Oluso users
/// </summary>
public interface IScimUserMapper
{
    /// <summary>
    /// Convert an Oluso user to a SCIM User resource
    /// </summary>
    /// <param name="user">The internal Oluso user</param>
    /// <param name="baseUrl">The base URL for SCIM resources</param>
    /// <param name="externalId">Optional external ID from the SCIM client's perspective</param>
    ScimUser ToScimUser(OlusoUser user, string baseUrl, string? externalId = null);

    /// <summary>
    /// Apply SCIM User data to an existing Oluso user
    /// </summary>
    void ApplyToUser(OlusoUser user, ScimUser scimUser);

    /// <summary>
    /// Create a new Oluso user from SCIM User data
    /// </summary>
    OlusoUser CreateFromScimUser(ScimUser scimUser, string tenantId);
}

/// <summary>
/// Default implementation of SCIM user mapping
/// </summary>
public class DefaultScimUserMapper : IScimUserMapper
{
    public ScimUser ToScimUser(OlusoUser user, string baseUrl, string? externalId = null)
    {
        var scimUser = new ScimUser
        {
            Id = user.Id,
            ExternalId = externalId ?? user.Id, // Use mapped external ID or fall back to internal ID
            UserName = user.UserName ?? user.Email ?? "",
            DisplayName = user.DisplayName,
            Active = user.IsActive,
            Locale = user.Locale,
            Timezone = user.TimeZone,
            Meta = new ScimMeta
            {
                ResourceType = "User",
                Created = user.CreatedAt,
                LastModified = user.UpdatedAt ?? user.CreatedAt,
                Location = $"{baseUrl}/Users/{user.Id}",
                Version = $"W/\"{user.UpdatedAt?.Ticks ?? user.CreatedAt.Ticks}\""
            }
        };

        // Name
        if (!string.IsNullOrEmpty(user.FirstName) || !string.IsNullOrEmpty(user.LastName))
        {
            scimUser.Name = new ScimName
            {
                GivenName = user.FirstName,
                FamilyName = user.LastName,
                Formatted = $"{user.FirstName} {user.LastName}".Trim()
            };
        }

        // Email
        if (!string.IsNullOrEmpty(user.Email))
        {
            scimUser.Emails = new List<ScimMultiValuedAttribute>
            {
                new()
                {
                    Value = user.Email,
                    Type = "work",
                    Primary = true
                }
            };
        }

        // Phone
        if (!string.IsNullOrEmpty(user.PhoneNumber))
        {
            scimUser.PhoneNumbers = new List<ScimMultiValuedAttribute>
            {
                new()
                {
                    Value = user.PhoneNumber,
                    Type = "work",
                    Primary = true
                }
            };
        }

        return scimUser;
    }

    public void ApplyToUser(OlusoUser user, ScimUser scimUser)
    {
        // UserName
        if (!string.IsNullOrEmpty(scimUser.UserName))
        {
            user.UserName = scimUser.UserName;
            user.NormalizedUserName = scimUser.UserName.ToUpperInvariant();
        }

        // Name
        if (scimUser.Name != null)
        {
            user.FirstName = scimUser.Name.GivenName;
            user.LastName = scimUser.Name.FamilyName;
        }

        // DisplayName
        if (!string.IsNullOrEmpty(scimUser.DisplayName))
        {
            user.DisplayName = scimUser.DisplayName;
        }
        else if (scimUser.Name != null)
        {
            user.DisplayName = $"{scimUser.Name.GivenName} {scimUser.Name.FamilyName}".Trim();
        }

        // Active status
        user.IsActive = scimUser.Active;

        // Email (primary)
        var primaryEmail = scimUser.Emails?.FirstOrDefault(e => e.Primary) ?? scimUser.Emails?.FirstOrDefault();
        if (primaryEmail != null && !string.IsNullOrEmpty(primaryEmail.Value))
        {
            user.Email = primaryEmail.Value;
            user.NormalizedEmail = primaryEmail.Value.ToUpperInvariant();
            user.EmailConfirmed = true; // SCIM-provisioned emails are considered verified
        }

        // Phone (primary)
        var primaryPhone = scimUser.PhoneNumbers?.FirstOrDefault(p => p.Primary) ?? scimUser.PhoneNumbers?.FirstOrDefault();
        if (primaryPhone != null && !string.IsNullOrEmpty(primaryPhone.Value))
        {
            user.PhoneNumber = primaryPhone.Value;
        }

        // Locale
        if (!string.IsNullOrEmpty(scimUser.Locale))
        {
            user.Locale = scimUser.Locale;
        }

        // Timezone
        if (!string.IsNullOrEmpty(scimUser.Timezone))
        {
            user.TimeZone = scimUser.Timezone;
        }

        user.UpdatedAt = DateTime.UtcNow;
    }

    public OlusoUser CreateFromScimUser(ScimUser scimUser, string tenantId)
    {
        var user = new OlusoUser
        {
            Id = Guid.NewGuid().ToString(),
            TenantId = tenantId,
            CreatedAt = DateTime.UtcNow,
            IsActive = scimUser.Active,
            EmailConfirmed = true,
            LockoutEnabled = false
        };

        ApplyToUser(user, scimUser);

        return user;
    }
}

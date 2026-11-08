using System.Text.Json.Nodes;
using Oluso.Core.Domain.Entities;
using Oluso.Enterprise.Scim.Entities;
using Oluso.Enterprise.Scim.Models;

namespace Oluso.Enterprise.Scim.Services;

/// <summary>
/// Maps between SCIM User resources and Oluso users for the inbound
/// (service provider) side. Writes go through the client's attribute
/// mappings; with none configured the module's defaults apply.
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
    /// Create a new Oluso user from a SCIM User (POST). Required mappings must resolve.
    /// </summary>
    /// <exception cref="ScimInboundMappingException">A required attribute is missing or an expression failed.</exception>
    OlusoUser CreateFromScimUser(ScimUser scimUser, string tenantId, ScimClient client, IReadOnlyList<ScimAttributeMapping> mappings);

    /// <summary>
    /// Replace an Oluso user from a full SCIM User representation (PUT, RFC
    /// 7644 §3.5.1). Mappings that resolve to nothing clear their target;
    /// <c>userName</c>, email and <c>active</c> are left unchanged when omitted.
    /// </summary>
    /// <exception cref="ScimInboundMappingException">An expression failed.</exception>
    void ApplyReplace(OlusoUser user, ScimUser scimUser, ScimClient client, IReadOnlyList<ScimAttributeMapping> mappings);

    /// <summary>
    /// Apply PATCH operations (RFC 7644 §3.5.2): the operations are applied
    /// to the user's current SCIM representation and the result is mapped
    /// back, writing only what the operations touched.
    /// </summary>
    /// <exception cref="ScimInboundMappingException">An expression failed.</exception>
    void ApplyPatch(OlusoUser user, ScimPatchRequest patch, ScimClient client, IReadOnlyList<ScimAttributeMapping> mappings);

    /// <summary>
    /// Dry run of the mappings against a sample resource, without touching any user.
    /// </summary>
    ScimInboundPreview Preview(JsonObject resource, ScimClient client, IReadOnlyList<ScimAttributeMapping> mappings, ScimInboundMode mode);
}

/// <summary>
/// Default implementation of SCIM user mapping
/// </summary>
public class DefaultScimUserMapper : IScimUserMapper
{
    private readonly ScimInboundMapper _inbound;

    public DefaultScimUserMapper(ScimInboundMapper inbound)
    {
        _inbound = inbound;
    }

    public ScimUser ToScimUser(OlusoUser user, string baseUrl, string? externalId = null)
    {
        var scimUser = new ScimUser
        {
            Id = user.Id,
            ExternalId = externalId ?? user.Id, // Use mapped external ID or fall back to internal ID
            UserName = user.UserName ?? user.Email ?? "",
            DisplayName = user.DisplayName,
            Active = user.IsActive, // always emitted in responses
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

    public OlusoUser CreateFromScimUser(ScimUser scimUser, string tenantId, ScimClient client, IReadOnlyList<ScimAttributeMapping> mappings)
    {
        var user = new OlusoUser
        {
            Id = Guid.NewGuid().ToString(),
            TenantId = tenantId,
            CreatedAt = DateTime.UtcNow,
            IsActive = true,
            // Trusted only at a domain the tenant has verified, which the provisioning service checks.
            EmailConfirmed = false,
            LockoutEnabled = false
        };

        _inbound.Apply(user, ScimInboundMapper.ToJson(scimUser), client, mappings, ScimInboundMode.Create);
        return user;
    }

    public void ApplyReplace(OlusoUser user, ScimUser scimUser, ScimClient client, IReadOnlyList<ScimAttributeMapping> mappings)
    {
        _inbound.Apply(user, ScimInboundMapper.ToJson(scimUser), client, mappings, ScimInboundMode.Replace);
    }

    public void ApplyPatch(OlusoUser user, ScimPatchRequest patch, ScimClient client, IReadOnlyList<ScimAttributeMapping> mappings)
    {
        var current = ScimInboundMapper.ToJson(ToScimUser(user, string.Empty));
        current.Remove("meta");
        current.Remove("id");

        var removed = ScimPatchDocument.Apply(current, patch.Operations);
        _inbound.Apply(user, current, client, mappings, ScimInboundMode.Patch, removed);
    }

    public ScimInboundPreview Preview(JsonObject resource, ScimClient client, IReadOnlyList<ScimAttributeMapping> mappings, ScimInboundMode mode) =>
        _inbound.Preview(resource, client, mappings, mode);
}

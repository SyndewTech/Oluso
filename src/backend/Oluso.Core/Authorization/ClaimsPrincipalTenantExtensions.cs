using System.Security.Claims;
using System.Text.Json;

namespace Oluso.Core.Authorization;

/// <summary>
/// Reads the tenants a principal may act in. <c>allowed_tenants</c> is issued as one
/// claim per tenant; depending on the token path it reaches a controller as several
/// claims, as a single JSON array, or as one plain value, so all three are accepted.
/// Falls back to <c>tenant_ids</c> and then <c>tenant_id</c> for older tokens.
/// </summary>
public static class ClaimsPrincipalTenantExtensions
{
    public static IReadOnlyList<string> GetAllowedTenantIds(this ClaimsPrincipal user)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? id)
        {
            if (!string.IsNullOrWhiteSpace(id) && seen.Add(id.Trim())) result.Add(id.Trim());
        }

        void AddClaimValues(string claimType)
        {
            foreach (var claim in user.FindAll(claimType))
            {
                var value = claim.Value;
                if (string.IsNullOrWhiteSpace(value)) continue;

                if (value.StartsWith('['))
                {
                    try
                    {
                        var parsed = JsonSerializer.Deserialize<List<string>>(value);
                        if (parsed is not null)
                        {
                            foreach (var id in parsed) Add(id);
                            continue;
                        }
                    }
                    catch (JsonException)
                    {
                        // Not JSON after all; treat as a plain value below.
                    }
                }

                if (value.Contains(','))
                {
                    foreach (var id in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) Add(id);
                }
                else
                {
                    Add(value);
                }
            }
        }

        AddClaimValues(ReservedClaimKeys.AllowedTenants);
        if (result.Count > 0) return result;

        AddClaimValues("tenant_ids");
        if (result.Count > 0) return result;

        AddClaimValues("tenant_id");
        return result;
    }
}

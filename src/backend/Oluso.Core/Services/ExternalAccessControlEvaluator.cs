using System.Security.Claims;

namespace Oluso.Core.Services;

/// <summary>
/// Pure-function evaluator for the IdP access-control gate. Used by both the
/// standalone external callback and the journey external login step so the
/// gate behaves identically across login paths. No I/O — caller passes in
/// the external principal and the IdP's <see cref="ExternalAccessControl"/>.
/// </summary>
public static class ExternalAccessControlEvaluator
{
    public enum Decision
    {
        /// <summary>No gate configured (mode=open) or principal passes.</summary>
        Allow,
        /// <summary>Email allowlist mode and the principal's email isn't in the list.</summary>
        DeniedByEmail,
        /// <summary>Claim-gate mode and the principal's claim doesn't match.</summary>
        DeniedByClaim
    }

    /// <summary>
    /// Evaluate the access-control gate against an external principal.
    /// Returns <see cref="Decision.Allow"/> when no gate is configured, the
    /// mode is unknown, or the principal passes the configured gate.
    /// </summary>
    public static Decision Evaluate(ClaimsPrincipal principal, ExternalAccessControl? accessControl)
    {
        if (accessControl == null) return Decision.Allow;
        if (string.IsNullOrEmpty(accessControl.Mode)
            || string.Equals(accessControl.Mode, "open", StringComparison.OrdinalIgnoreCase))
            return Decision.Allow;

        if (string.Equals(accessControl.Mode, "emailAllowlist", StringComparison.OrdinalIgnoreCase))
        {
            return EvaluateEmailAllowlist(principal, accessControl.AllowedEmails);
        }

        if (string.Equals(accessControl.Mode, "claimGate", StringComparison.OrdinalIgnoreCase))
        {
            return EvaluateClaimGate(principal, accessControl.ClaimGate);
        }

        // Unknown mode = treat as open. Belt-and-braces: a typo'd mode in
        // the JSON shouldn't lock everyone out without admin awareness.
        return Decision.Allow;
    }

    private static Decision EvaluateEmailAllowlist(ClaimsPrincipal principal, IReadOnlyList<string> allowedEmails)
    {
        // Empty allowlist with mode=emailAllowlist denies everyone — the
        // admin opted in to a gate but provided no entries. That's an
        // unambiguous "deny all" signal, not "open".
        if (allowedEmails.Count == 0) return Decision.DeniedByEmail;

        var email = principal.FindFirstValue(ClaimTypes.Email)
            ?? principal.FindFirstValue("email");
        if (string.IsNullOrEmpty(email)) return Decision.DeniedByEmail;

        foreach (var allowed in allowedEmails)
        {
            if (string.Equals(email, allowed, StringComparison.OrdinalIgnoreCase))
                return Decision.Allow;
        }
        return Decision.DeniedByEmail;
    }

    private static Decision EvaluateClaimGate(ClaimsPrincipal principal, ClaimGate? gate)
    {
        if (gate == null
            || string.IsNullOrWhiteSpace(gate.ClaimPath)
            || gate.AnyOf.Count == 0)
        {
            // Misconfigured gate — same belt-and-braces as above. The
            // admin-side validation should have caught this on save.
            return Decision.DeniedByClaim;
        }

        // Claims may arrive under either the short JWT name ("groups") or
        // the long WS-Federation URI (ClaimTypes.Role etc.) depending on
        // MapInboundClaims. Check both. Multi-valued claims arrive as
        // multiple Claim entries with the same Type, so iterate all.
        var matches = principal.Claims
            .Where(c => string.Equals(c.Type, gate.ClaimPath, StringComparison.OrdinalIgnoreCase)
                     || string.Equals(c.Type, MapShortNameToClaimType(gate.ClaimPath), StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Value);

        foreach (var value in matches)
        {
            // Some IdPs collapse a multi-valued claim into a single JSON
            // array string ("[\"admins\",\"users\"]"). Try parsing first,
            // fall back to a literal compare.
            foreach (var actual in ExpandMaybeJsonArray(value))
            {
                foreach (var allowed in gate.AnyOf)
                {
                    if (string.Equals(actual, allowed, StringComparison.OrdinalIgnoreCase))
                        return Decision.Allow;
                }
            }
        }

        return Decision.DeniedByClaim;
    }

    private static string MapShortNameToClaimType(string shortName)
        => shortName.ToLowerInvariant() switch
        {
            "email" => ClaimTypes.Email,
            "name" => ClaimTypes.Name,
            "given_name" => ClaimTypes.GivenName,
            "family_name" => ClaimTypes.Surname,
            "role" or "roles" => ClaimTypes.Role,
            _ => shortName
        };

    private static IEnumerable<string> ExpandMaybeJsonArray(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            yield return value;
            yield break;
        }

        var trimmed = value.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] != '[')
        {
            yield return value;
            yield break;
        }

        List<string>? parsed = null;
        try
        {
            parsed = System.Text.Json.JsonSerializer.Deserialize<List<string>>(value);
        }
        catch
        {
            // Not a JSON array after all — fall through to literal compare.
        }

        if (parsed != null)
        {
            foreach (var item in parsed)
                yield return item;
        }
        else
        {
            yield return value;
        }
    }
}

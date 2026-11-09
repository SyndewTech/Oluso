namespace Oluso.Core.Services;

public static class OlusoUserInfoExtensions
{
    /// <summary>
    /// Resolve the best human-readable name for a user, in order:
    /// <c>DisplayName</c> → <c>"FirstName LastName"</c> → <c>Username</c> →
    /// <c>Email</c>. Each candidate is whitespace-checked, not just null-checked:
    /// external-login users can end up with an empty-string <c>DisplayName</c>
    /// when the IdP sends no name claims, which a plain <c>DisplayName ?? Email</c>
    /// would wrongly treat as present. Returns null only when nothing usable
    /// exists (caller can fall back to an id).
    /// </summary>
    public static string? ResolveDisplayName(this OlusoUserInfo? user)
    {
        if (user is null) return null;
        if (!string.IsNullOrWhiteSpace(user.DisplayName)) return user.DisplayName.Trim();
        var full = $"{user.FirstName} {user.LastName}".Trim();
        if (!string.IsNullOrWhiteSpace(full)) return full;
        if (!string.IsNullOrWhiteSpace(user.Username)) return user.Username;
        if (!string.IsNullOrWhiteSpace(user.Email)) return user.Email;
        return null;
    }
}

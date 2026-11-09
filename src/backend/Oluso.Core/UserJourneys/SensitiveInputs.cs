namespace Oluso.Core.UserJourneys;

/// <summary>
/// Central list of input keys that hold credentials or one-time codes,
/// plus helpers to keep them out of places they shouldn't reach
/// (plugin sandboxes, persisted journey state, audit dumps).
/// </summary>
/// <remarks>
/// Match is case-insensitive on the full key name. The set covers the
/// canonical names used by built-in step handlers. Custom handlers that
/// introduce new credential-bearing inputs should add their key here so
/// every redaction/scrub site picks it up at once.
/// </remarks>
public static class SensitiveInputs
{
    public static readonly IReadOnlySet<string> Keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        // Passwords and aliases
        "password", "pwd", "pass", "passwd",
        "currentPassword", "current_password",
        "newPassword", "new_password",
        "confirmPassword", "confirm_password",

        // Generic credentials
        "secret", "credential",

        // Tokens
        "token", "access_token", "refresh_token", "id_token",
        "magic_link_token", "auth_code",

        // OTP / MFA codes
        "code", "mfa_code", "otp", "totp", "hotp",
        "verification_code",
        "setup_totp_code", "setup_sms_code", "recovery_code",

        // JourneyData-persisted credential keys used by built-in handlers
        "passwordless_code", "reset_code", "reset_token", "mfa_setup_key",
        "password_expired_reset_token",

        // Other secrets
        "pin", "cvv", "cvc", "ssn", "social_security",
        "private_key", "api_key", "apikey"
    };

    /// <summary>
    /// True when the key is in the sensitive set.
    /// </summary>
    public static bool IsSensitive(string key) => !string.IsNullOrEmpty(key) && Keys.Contains(key);

    /// <summary>
    /// Returns a shallow copy of <paramref name="source"/> with sensitive
    /// keys removed. Use when forwarding inputs to a sandbox we don't fully
    /// trust (plugins, webhooks where signPayload isn't set, etc.).
    /// </summary>
    public static IDictionary<string, object> RedactCopy(IDictionary<string, object>? source)
    {
        if (source == null || source.Count == 0) return new Dictionary<string, object>();
        var copy = new Dictionary<string, object>(source.Count, StringComparer.Ordinal);
        foreach (var (key, value) in source)
        {
            if (IsSensitive(key)) continue;
            copy[key] = value;
        }
        return copy;
    }

    /// <summary>
    /// Removes all sensitive keys from <paramref name="target"/> in place.
    /// Call this once a step handler has finished consuming a credential
    /// so the value doesn't survive into later steps or persisted state.
    /// </summary>
    public static void Scrub(IDictionary<string, object>? target)
    {
        if (target == null || target.Count == 0) return;
        var toRemove = new List<string>();
        foreach (var key in target.Keys)
        {
            if (IsSensitive(key)) toRemove.Add(key);
        }
        foreach (var key in toRemove)
        {
            target.Remove(key);
        }
    }
}

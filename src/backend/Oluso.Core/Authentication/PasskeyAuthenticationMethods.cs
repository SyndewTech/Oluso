namespace Oluso.Core.Authentication;

/// <summary>
/// The authentication methods (amr, RFC 8176) a passkey sign-in records. A passkey proves possession of its key
/// (<c>hwk</c>); when the authenticator also verified the user with a PIN or biometric (the WebAuthn UV flag) it is a
/// multi-factor authenticator (NIST SP 800-63B), so the sign-in records <c>mfa</c> as well. A passkey that only
/// tested the user's presence is one factor.
/// </summary>
public static class PasskeyAuthenticationMethods
{
    public const string PossessionOfKey = "hwk";
    public const string MultiFactor = "mfa";

    private static readonly string[] Verified = { PossessionOfKey, MultiFactor };
    private static readonly string[] PresenceOnly = { PossessionOfKey };

    public static string[] Amr(bool userVerified) => userVerified ? Verified : PresenceOnly;
}

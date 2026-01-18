using Oluso.Core.Domain.Entities;

namespace Oluso.Enterprise.Fido2.Entities;

/// <summary>
/// FIDO2/WebAuthn credential stored for passkey authentication
/// </summary>
public class Fido2CredentialEntity : TenantEntity
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// User ID who owns this credential
    /// </summary>
    public string UserId { get; set; } = default!;

    /// <summary>
    /// Base64URL-encoded credential ID from the authenticator
    /// </summary>
    public string CredentialId { get; set; } = default!;

    /// <summary>
    /// Base64URL-encoded public key for verification
    /// </summary>
    public string PublicKey { get; set; } = default!;

    /// <summary>
    /// Base64URL-encoded user handle
    /// </summary>
    public string UserHandle { get; set; } = default!;

    /// <summary>
    /// Signature counter for replay attack prevention
    /// </summary>
    public uint SignatureCounter { get; set; }

    /// <summary>
    /// COSE credential type (e.g., -7 for ES256)
    /// </summary>
    public int CredentialType { get; set; }

    /// <summary>
    /// Authenticator AAGUID
    /// </summary>
    public Guid AaGuid { get; set; }

    /// <summary>
    /// User-friendly name for this credential
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// Type of authenticator: Platform or CrossPlatform
    /// </summary>
    public Fido2AuthenticatorType AuthenticatorType { get; set; }

    /// <summary>
    /// Attestation format (none, packed, tpm, android-key, etc.)
    /// </summary>
    public string? AttestationFormat { get; set; }

    /// <summary>
    /// Whether this is a discoverable credential (passkey)
    /// </summary>
    public bool IsDiscoverable { get; set; }

    /// <summary>
    /// Comma-separated list of transports (usb, nfc, ble, internal, hybrid)
    /// </summary>
    public string? Transports { get; set; }

    /// <summary>
    /// Whether this credential is active
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// When this credential was registered
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this credential was last used for authentication
    /// </summary>
    public DateTime? LastUsedAt { get; set; }

    /// <summary>
    /// Gets the transports as a list
    /// </summary>
    public List<string> GetTransports() =>
        string.IsNullOrEmpty(Transports)
            ? new List<string>()
            : Transports.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();

    /// <summary>
    /// Sets the transports from a list
    /// </summary>
    public void SetTransports(IEnumerable<string>? transports) =>
        Transports = transports != null ? string.Join(',', transports) : null;
}

public enum Fido2AuthenticatorType
{
    /// <summary>Built-in authenticator (Touch ID, Face ID, Windows Hello)</summary>
    Platform,
    /// <summary>External authenticator (USB security key, NFC, etc.)</summary>
    CrossPlatform
}

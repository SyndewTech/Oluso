namespace Oluso.Core.Storage;

/// <summary>
/// Deterministic blind-index + envelope encryption for entity references
/// stored in shared tables (today: <c>FileAttachments.EntityId</c>; tomorrow:
/// any cross-module table that wants sensitive / Article-9 joins hidden from
/// read-only observers).
///
/// <para>The protector resolves a per-tenant, per-purpose symmetric key
/// through the platform's existing <c>ISigningKeyService</c> + <c>IKeyMaterialProvider</c>
/// pipeline — so a tenant that has Azure Key Vault configured gets a
/// KV-backed protection key automatically, with no extra config and the
/// same rotation / revocation surface as JWT/SAML keys.</para>
///
/// <para>The <paramref name="purpose"/> label segregates keys: <c>"sensitive"</c>
/// for sensitive references, <c>"confidential"</c> for restricted attachments, etc.
/// Each purpose has its own <c>SigningKey</c> row per tenant.</para>
/// </summary>
public interface IEntityReferenceProtector
{
    /// <summary>
    /// Deterministic HMAC of <paramref name="plaintext"/> using the
    /// (tenant, purpose) key. Same input always produces the same output,
    /// so equality lookups work: <c>WHERE EntityIdHash = HMAC(plaintext)</c>.
    /// </summary>
    /// <remarks>
    /// The HMAC output is hex-encoded SHA-256, stable across releases.
    /// </remarks>
    Task<string> BlindIndexAsync(
        string tenantId, string purpose, string plaintext,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every hash <paramref name="plaintext"/> may have been indexed under: the current
    /// <see cref="BlindIndexAsync"/> one and those under the purpose's other keys. Look rows
    /// up with these so a row indexed before the index key settled is still found.
    /// </summary>
    Task<IReadOnlyList<string>> BlindIndexCandidatesAsync(
        string tenantId, string purpose, string plaintext,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// AES-GCM encryption with a random 96-bit nonce under the purpose's newest key. Output
    /// layout is <c>k1:{keyId}:base64(nonce(12) || tag(16) || ciphertext)</c>, naming the key
    /// it was made with; self-contained, no separate IV column needed.
    /// </summary>
    Task<string> ProtectAsync(
        string tenantId, string purpose, string plaintext,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Recover plaintext from <paramref name="protectedValue"/>. Throws if
    /// the ciphertext was produced under a different key version that has
    /// since been revoked (versus rotated — rotated keys remain decryptable).
    /// </summary>
    Task<string> UnprotectAsync(
        string tenantId, string purpose, string protectedValue,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Convenience: protect a value when non-null, return null otherwise.
    /// </summary>
    async Task<string?> ProtectNullableAsync(
        string tenantId, string purpose, string? plaintext,
        CancellationToken cancellationToken = default)
        => string.IsNullOrEmpty(plaintext) ? null : await ProtectAsync(tenantId, purpose, plaintext, cancellationToken);

    /// <summary>
    /// Convenience: unprotect a value when non-null, return null otherwise.
    /// </summary>
    async Task<string?> UnprotectNullableAsync(
        string tenantId, string purpose, string? protectedValue,
        CancellationToken cancellationToken = default)
        => string.IsNullOrEmpty(protectedValue) ? null : await UnprotectAsync(tenantId, purpose, protectedValue, cancellationToken);
}

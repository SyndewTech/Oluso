using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Core.Storage;

namespace Oluso.Storage;

/// <summary>
/// Default <see cref="IEntityReferenceProtector"/> implementation. Resolves a
/// per-(tenant, purpose) symmetric key through the platform's existing
/// <see cref="ISigningKeyService"/> + <see cref="IKeyMaterialProvider"/>
/// pipeline — so a tenant that has Azure Key Vault wired into ASP.NET Data
/// Protection automatically gets KV-backed protection without any extra
/// config in this module.
///
/// <para>The resolved key is used as a root from which we derive two
/// domain-separated subkeys via HKDF: one for HMAC blind indexing
/// (deterministic equality lookups), one for AES-GCM envelope encryption
/// (random-nonce AEAD). The root key is the same; the subkeys never collide.</para>
///
/// <para>Keys: every symmetric encryption key ever made for a (tenant, purpose) and not revoked stays readable. New
/// values are encrypted with the newest and name it in the payload (<c>k1:{keyId}:...</c>); values from before that
/// format are read by trying each key, which AES-GCM authentication makes safe. Blind indexes are made with the first
/// key, so they never change; <see cref="BlindIndexCandidatesAsync"/> also gives the hashes under the other keys, for
/// rows indexed before that rule. Key maintenance never expires or rotates these keys
/// (<see cref="DataProtectionKeys.ClientIdPrefix"/>).</para>
/// </summary>
public class EntityReferenceProtector : IEntityReferenceProtector
{
    // Each ciphertext is `nonce(12) || tag(16) || ciphertext(N)` base64-encoded.
    private const int NonceSize = 12;
    private const int TagSize = 16;

    // HKDF "info" labels for domain separation. Changing these is a breaking
    // change for stored data — treat as part of the protocol version.
    private static readonly byte[] BlindIndexInfo = Encoding.UTF8.GetBytes("oluso:erp:v1:blind-index");
    private static readonly byte[] AeadInfo       = Encoding.UTF8.GetBytes("oluso:erp:v1:aead");

    private readonly ISigningKeyStore _keyStore;
    private readonly ISigningKeyService _keyService;
    private readonly IKeyMaterialProviderRegistry _providers;
    private readonly ILogger<EntityReferenceProtector> _logger;

    // Cache the derived subkeys per (tenantId, purpose, keyId). Bounded by the
    // small number of (tenant × purpose) pairs in a single host — no eviction.
    private readonly ConcurrentDictionary<string, DerivedKeys> _cache = new();

    public EntityReferenceProtector(
        ISigningKeyStore keyStore,
        ISigningKeyService keyService,
        IKeyMaterialProviderRegistry providers,
        ILogger<EntityReferenceProtector> logger)
    {
        _keyStore = keyStore;
        _keyService = keyService;
        _providers = providers;
        _logger = logger;
    }

    public async Task<string> BlindIndexAsync(
        string tenantId, string purpose, string plaintext, CancellationToken cancellationToken = default)
    {
        ValidateInput(tenantId, purpose);
        if (plaintext == null) throw new ArgumentNullException(nameof(plaintext));

        // The index is always made with the purpose's first key, so it stays the same however keys move on.
        var keys = await DerivedAsync(tenantId, purpose, (await KeyRingAsync(tenantId, purpose, cancellationToken))[0], cancellationToken);
        return Hash(keys, plaintext);
    }

    public async Task<IReadOnlyList<string>> BlindIndexCandidatesAsync(
        string tenantId, string purpose, string plaintext, CancellationToken cancellationToken = default)
    {
        ValidateInput(tenantId, purpose);
        if (plaintext == null) throw new ArgumentNullException(nameof(plaintext));

        var hashes = new List<string>();
        foreach (var key in await KeyRingAsync(tenantId, purpose, cancellationToken))
            hashes.Add(Hash(await DerivedAsync(tenantId, purpose, key, cancellationToken), plaintext));
        return hashes.Distinct(StringComparer.Ordinal).ToList();
    }

    public async Task<string> ProtectAsync(
        string tenantId, string purpose, string plaintext, CancellationToken cancellationToken = default)
    {
        ValidateInput(tenantId, purpose);
        if (plaintext == null) throw new ArgumentNullException(nameof(plaintext));

        var key = (await KeyRingAsync(tenantId, purpose, cancellationToken))[^1];
        var keys = await DerivedAsync(tenantId, purpose, key, cancellationToken);
        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSize];

        using (var gcm = new AesGcm(keys.Aead, TagSize))
        {
            gcm.Encrypt(nonce, plaintextBytes, ciphertext, tag);
        }

        // Layout: nonce || tag || ciphertext, prefixed with the key it was made with.
        var payload = new byte[NonceSize + TagSize + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, payload, 0, NonceSize);
        Buffer.BlockCopy(tag, 0, payload, NonceSize, TagSize);
        Buffer.BlockCopy(ciphertext, 0, payload, NonceSize + TagSize, ciphertext.Length);
        return $"{KeyedPrefix}{key.KeyId}:{Convert.ToBase64String(payload)}";
    }

    public async Task<string> UnprotectAsync(
        string tenantId, string purpose, string protectedValue, CancellationToken cancellationToken = default)
    {
        ValidateInput(tenantId, purpose);
        if (string.IsNullOrEmpty(protectedValue))
            throw new ArgumentException("Protected value is empty", nameof(protectedValue));

        var ring = await KeyRingAsync(tenantId, purpose, cancellationToken);
        if (protectedValue.StartsWith(KeyedPrefix, StringComparison.Ordinal))
        {
            var rest = protectedValue[KeyedPrefix.Length..];
            var colon = rest.IndexOf(':');
            if (colon <= 0) throw new CryptographicException("Protected payload is malformed");
            var keyId = rest[..colon];
            var key = ring.FirstOrDefault(k => k.KeyId == keyId)
                ?? throw new CryptographicException($"The key {keyId} that protected this value is not available (revoked or removed)");
            return Decrypt(await DerivedAsync(tenantId, purpose, key, cancellationToken), Convert.FromBase64String(rest[(colon + 1)..]))
                ?? throw new CryptographicException("Protected value failed authentication");
        }

        // Values written before payloads named their key: try each of the purpose's keys, newest first. AES-GCM
        // authenticates, so a wrong key fails cleanly and only the right one returns plaintext.
        var legacy = Convert.FromBase64String(protectedValue);
        for (var i = ring.Count - 1; i >= 0; i--)
        {
            var plaintext = Decrypt(await DerivedAsync(tenantId, purpose, ring[i], cancellationToken), legacy);
            if (plaintext != null) return plaintext;
        }
        throw new CryptographicException("No data-protection key of this purpose can read the value");
    }

    // ─── Internal key resolution ─────────────────────────────────────────

    /// <summary>Client id the (tenant, purpose) keys are stored under; key maintenance leaves these keys alone.</summary>
    private static string ClientIdFor(string purpose) => DataProtectionKeys.ClientIdFor(purpose);

    private const string KeyedPrefix = "k1:";

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> CreateLocks = new();

    private static string Hash(DerivedKeys keys, string plaintext)
    {
        using var hmac = new HMACSHA256(keys.BlindIndex);
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(plaintext))).ToLowerInvariant();
    }

    private static string? Decrypt(DerivedKeys keys, byte[] payload)
    {
        if (payload.Length < NonceSize + TagSize) throw new CryptographicException("Protected payload is truncated");
        var ciphertextLength = payload.Length - NonceSize - TagSize;
        var plaintext = new byte[ciphertextLength];
        try
        {
            using var gcm = new AesGcm(keys.Aead, TagSize);
            gcm.Decrypt(payload.AsSpan(0, NonceSize), payload.AsSpan(NonceSize + TagSize, ciphertextLength), payload.AsSpan(NonceSize, TagSize), plaintext);
        }
        catch (AuthenticationTagMismatchException)
        {
            return null;
        }
        return Encoding.UTF8.GetString(plaintext);
    }

    /// <summary>
    /// The (tenant, purpose) data-protection keys, oldest first: every symmetric encryption key ever made for it that is
    /// not revoked (an expired key still reads what it wrote). The first is created when there is none, once, even
    /// when several requests ask at the same moment.
    /// </summary>
    private async Task<IReadOnlyList<SigningKey>> KeyRingAsync(string tenantId, string purpose, CancellationToken cancellationToken)
    {
        var ring = await LoadRingAsync(tenantId, purpose, cancellationToken);
        if (ring.Count > 0) return ring;

        var gate = CreateLocks.GetOrAdd($"{tenantId}|{purpose}", _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            ring = await LoadRingAsync(tenantId, purpose, cancellationToken);
            if (ring.Count > 0) return ring;

            _logger.LogInformation("Generating new data-protection key for tenant {TenantId} purpose {Purpose}", tenantId, purpose);
            var created = await _keyService.GenerateKeyAsync(new GenerateKeyRequest
            {
                TenantId = tenantId,
                ClientId = ClientIdFor(purpose),
                Name = $"Data Protection - {purpose}",
                KeyType = SigningKeyType.Symmetric,
                KeySize = 256,
                Algorithm = "HS256",
                Use = SigningKeyUse.Encryption,
                ActivateImmediately = true,
                Priority = 100,
            }, cancellationToken);
            return [created];
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<IReadOnlyList<SigningKey>> LoadRingAsync(string tenantId, string purpose, CancellationToken cancellationToken) =>
        (await _keyStore.GetByClientAsync(tenantId, ClientIdFor(purpose), cancellationToken))
            .Where(k => k.Use == SigningKeyUse.Encryption && k.KeyType == SigningKeyType.Symmetric && k.Status != SigningKeyStatus.Revoked)
            .OrderBy(k => k.CreatedAt).ThenBy(k => k.KeyId, StringComparer.Ordinal)
            .ToList();

    private async Task<DerivedKeys> DerivedAsync(string tenantId, string purpose, SigningKey rootKey, CancellationToken cancellationToken)
    {
        var cacheKey = $"{tenantId}|{purpose}|{rootKey.KeyId}";
        if (_cache.TryGetValue(cacheKey, out var cached)) return cached;

        var rootBytes = await ExtractKeyBytesAsync(rootKey, cancellationToken);
        var derived = new DerivedKeys(
            BlindIndex: HKDF.Expand(HashAlgorithmName.SHA256, rootBytes, 32, BlindIndexInfo),
            Aead:       HKDF.Expand(HashAlgorithmName.SHA256, rootBytes, 32, AeadInfo));

        // Wipe the root bytes; only the derived keys live on.
        CryptographicOperations.ZeroMemory(rootBytes);

        _cache[cacheKey] = derived;
        return derived;
    }

    private async Task<byte[]> ExtractKeyBytesAsync(SigningKey key, CancellationToken cancellationToken)
    {
        var provider = _providers.GetProvider(key.StorageProvider)
            ?? throw new InvalidOperationException(
                $"No IKeyMaterialProvider registered for {key.StorageProvider}; the data-protection key for tenant {key.TenantId} cannot be loaded.");

        var creds = await provider.GetSigningCredentialsAsync(key, cancellationToken)
            ?? throw new InvalidOperationException(
                $"Provider {key.StorageProvider} returned no credentials for key {key.KeyId}");

        if (creds.Key is not SymmetricSecurityKey symmetric)
            throw new InvalidOperationException(
                $"Data-protection key {key.KeyId} is {creds.Key.GetType().Name}, expected SymmetricSecurityKey. " +
                "EntityReferenceProtector requires a symmetric root key (HS256 / 256-bit).");

        // SymmetricSecurityKey holds a copy of the bytes; we get our own copy.
        return (byte[])symmetric.Key.Clone();
    }

    private static void ValidateInput(string tenantId, string purpose)
    {
        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("Tenant ID is required", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(purpose))
            throw new ArgumentException("Purpose is required", nameof(purpose));
    }

    private sealed record DerivedKeys(byte[] BlindIndex, byte[] Aead);
}

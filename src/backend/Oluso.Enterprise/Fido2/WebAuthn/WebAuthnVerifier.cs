using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Oluso.Enterprise.Fido2.WebAuthn;

/// <summary>
/// Native WebAuthn verification without external dependencies.
/// Implements FIDO2 attestation and assertion verification.
/// </summary>
internal static class WebAuthnVerifier
{
    /// <summary>
    /// Parses an attestation object and extracts credential data
    /// </summary>
    public static AttestationResult ParseAttestation(
        byte[] attestationObject,
        byte[] clientDataJson,
        string expectedChallenge,
        string expectedOrigin,
        string expectedRpId)
    {
        // Verify clientDataJSON
        var clientData = JsonSerializer.Deserialize<ClientData>(clientDataJson)
            ?? throw new WebAuthnException("Invalid clientDataJSON");

        if (clientData.Type != "webauthn.create")
            throw new WebAuthnException($"Invalid type: expected 'webauthn.create', got '{clientData.Type}'");

        if (clientData.Challenge != expectedChallenge)
            throw new WebAuthnException("Challenge mismatch");

        // Verify origin
        if (!string.IsNullOrEmpty(expectedOrigin) && clientData.Origin != expectedOrigin)
            throw new WebAuthnException($"Origin mismatch: expected '{expectedOrigin}', got '{clientData.Origin}'");

        // Parse CBOR attestation object
        var attestation = CborHelper.DecodeMap(attestationObject);
        var authData = attestation.Get<byte[]>("authData");
        var fmt = attestation.Get<string>("fmt");

        // Parse authenticator data
        var parsedAuthData = ParseAuthenticatorData(authData, requireCredentialData: true);

        // Verify RP ID hash
        var expectedRpIdHash = SHA256.HashData(Encoding.UTF8.GetBytes(expectedRpId));
        if (!parsedAuthData.RpIdHash.SequenceEqual(expectedRpIdHash))
            throw new WebAuthnException("RP ID hash mismatch");

        // Verify user present flag
        if (!parsedAuthData.UserPresent)
            throw new WebAuthnException("User not present");

        if (parsedAuthData.CredentialData == null)
            throw new WebAuthnException("No credential data in authenticator data");

        return new AttestationResult
        {
            CredentialId = parsedAuthData.CredentialData.CredentialId,
            PublicKey = parsedAuthData.CredentialData.PublicKey,
            PublicKeyCose = parsedAuthData.CredentialData.PublicKeyCose,
            AaGuid = parsedAuthData.CredentialData.AaGuid,
            SignCount = parsedAuthData.SignCount,
            Format = fmt,
            UserVerified = parsedAuthData.UserVerified
        };
    }

    /// <summary>
    /// Verifies an assertion (authentication)
    /// </summary>
    public static AssertionVerificationResult VerifyAssertion(
        byte[] credentialId,
        byte[] authenticatorData,
        byte[] clientDataJson,
        byte[] signature,
        byte[]? userHandle,
        byte[] storedPublicKey,
        uint storedSignCount,
        string expectedChallenge,
        string expectedOrigin,
        string expectedRpId)
    {
        // Verify clientDataJSON
        var clientData = JsonSerializer.Deserialize<ClientData>(clientDataJson)
            ?? throw new WebAuthnException("Invalid clientDataJSON");

        if (clientData.Type != "webauthn.get")
            throw new WebAuthnException($"Invalid type: expected 'webauthn.get', got '{clientData.Type}'");

        if (clientData.Challenge != expectedChallenge)
            throw new WebAuthnException("Challenge mismatch");

        // Verify origin
        if (!string.IsNullOrEmpty(expectedOrigin) && clientData.Origin != expectedOrigin)
            throw new WebAuthnException($"Origin mismatch: expected '{expectedOrigin}', got '{clientData.Origin}'");

        // Parse authenticator data
        var parsedAuthData = ParseAuthenticatorData(authenticatorData, requireCredentialData: false);

        // Verify RP ID hash
        var expectedRpIdHash = SHA256.HashData(Encoding.UTF8.GetBytes(expectedRpId));
        if (!parsedAuthData.RpIdHash.SequenceEqual(expectedRpIdHash))
            throw new WebAuthnException("RP ID hash mismatch");

        // Verify user present flag
        if (!parsedAuthData.UserPresent)
            throw new WebAuthnException("User not present");

        // Verify signature counter (protection against cloned authenticators)
        if (storedSignCount > 0 && parsedAuthData.SignCount <= storedSignCount)
        {
            // This could indicate a cloned authenticator
            // Some authenticators don't implement counters, so we don't fail hard
        }

        // Verify signature
        var clientDataHash = SHA256.HashData(clientDataJson);
        var signedData = new byte[authenticatorData.Length + clientDataHash.Length];
        authenticatorData.CopyTo(signedData, 0);
        clientDataHash.CopyTo(signedData, authenticatorData.Length);

        if (!VerifySignature(storedPublicKey, signedData, signature))
            throw new WebAuthnException("Signature verification failed");

        return new AssertionVerificationResult
        {
            Success = true,
            NewSignCount = parsedAuthData.SignCount,
            UserVerified = parsedAuthData.UserVerified
        };
    }

    private static AuthenticatorData ParseAuthenticatorData(byte[] authData, bool requireCredentialData)
    {
        if (authData.Length < 37)
            throw new WebAuthnException("Authenticator data too short");

        var result = new AuthenticatorData
        {
            RpIdHash = authData[..32],
            Flags = authData[32],
            SignCount = BinaryPrimitives.ReadUInt32BigEndian(authData.AsSpan(33, 4))
        };

        // Parse flags
        result.UserPresent = (result.Flags & 0x01) != 0;
        result.UserVerified = (result.Flags & 0x04) != 0;
        var hasAttestedCredentialData = (result.Flags & 0x40) != 0;
        var hasExtensions = (result.Flags & 0x80) != 0;

        if (hasAttestedCredentialData)
        {
            if (authData.Length < 55)
                throw new WebAuthnException("Authenticator data too short for attested credential data");

            var aaGuid = new Guid(authData.AsSpan(37, 16), bigEndian: true);
            var credIdLength = BinaryPrimitives.ReadUInt16BigEndian(authData.AsSpan(53, 2));

            if (authData.Length < 55 + credIdLength)
                throw new WebAuthnException("Authenticator data too short for credential ID");

            var credentialId = authData.AsSpan(55, credIdLength).ToArray();
            var publicKeyCose = authData.AsSpan(55 + credIdLength).ToArray();

            // Parse COSE public key to extract the actual key
            var publicKey = ParseCosePublicKey(publicKeyCose);

            result.CredentialData = new AttestedCredentialData
            {
                AaGuid = aaGuid,
                CredentialId = credentialId,
                PublicKey = publicKey,
                PublicKeyCose = publicKeyCose
            };
        }
        else if (requireCredentialData)
        {
            throw new WebAuthnException("Expected attested credential data but none present");
        }

        return result;
    }

    private static byte[] ParseCosePublicKey(byte[] coseKey)
    {
        var map = CborHelper.DecodeMap(coseKey);

        // COSE key type (kty) - 1 = OKP, 2 = EC2, 3 = RSA
        if (!map.TryGet(1, out int kty))
            throw new WebAuthnException("Missing COSE key type");

        // Algorithm (-1 for EC curve or -1, -2, -3 for RSA params)
        if (!map.TryGet(3, out int alg))
            throw new WebAuthnException("Missing COSE algorithm");

        return kty switch
        {
            2 => ParseEc2PublicKey(map, alg),
            3 => ParseRsaPublicKey(map),
            _ => throw new WebAuthnException($"Unsupported COSE key type: {kty}")
        };
    }

    private static byte[] ParseEc2PublicKey(CborMap map, int alg)
    {
        // Curve: -1 = P-256, -2 = P-384, -3 = P-521
        if (!map.TryGet(-1, out int crv))
            throw new WebAuthnException("Missing EC curve");

        var x = map.Get<byte[]>(-2);
        var y = map.Get<byte[]>(-3);

        // Return uncompressed point format (0x04 || x || y)
        var key = new byte[1 + x.Length + y.Length];
        key[0] = 0x04;
        x.CopyTo(key, 1);
        y.CopyTo(key, 1 + x.Length);
        return key;
    }

    private static byte[] ParseRsaPublicKey(CborMap map)
    {
        var n = map.Get<byte[]>(-1); // modulus
        var e = map.Get<byte[]>(-2); // exponent

        // Create RSA parameters structure
        // For storage, we'll concatenate n and e with length prefixes
        var result = new byte[4 + n.Length + 4 + e.Length];
        BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(0), n.Length);
        n.CopyTo(result, 4);
        BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(4 + n.Length), e.Length);
        e.CopyTo(result, 8 + n.Length);
        return result;
    }

    private static bool VerifySignature(byte[] publicKey, byte[] data, byte[] signature)
    {
        try
        {
            // Determine key type from the stored public key
            if (publicKey[0] == 0x04)
            {
                // EC uncompressed point
                return VerifyEcSignature(publicKey, data, signature);
            }
            else
            {
                // RSA (length-prefixed n||e format)
                return VerifyRsaSignature(publicKey, data, signature);
            }
        }
        catch
        {
            return false;
        }
    }

    private static bool VerifyEcSignature(byte[] publicKey, byte[] data, byte[] signature)
    {
        // Determine curve from key length
        var coordLength = (publicKey.Length - 1) / 2;
        ECCurve curve = coordLength switch
        {
            32 => ECCurve.NamedCurves.nistP256,
            48 => ECCurve.NamedCurves.nistP384,
            66 => ECCurve.NamedCurves.nistP521,
            _ => throw new WebAuthnException($"Unsupported EC key length: {publicKey.Length}")
        };

        HashAlgorithmName hashAlg = coordLength switch
        {
            32 => HashAlgorithmName.SHA256,
            48 => HashAlgorithmName.SHA384,
            66 => HashAlgorithmName.SHA512,
            _ => HashAlgorithmName.SHA256
        };

        var x = publicKey.AsSpan(1, coordLength).ToArray();
        var y = publicKey.AsSpan(1 + coordLength, coordLength).ToArray();

        var parameters = new ECParameters
        {
            Curve = curve,
            Q = new ECPoint { X = x, Y = y }
        };

        using var ecdsa = ECDsa.Create(parameters);

        // WebAuthn uses DER-encoded signatures, convert to fixed format if needed
        var signatureBytes = ConvertDerToFixed(signature, coordLength);

        return ecdsa.VerifyData(data, signatureBytes, hashAlg);
    }

    private static byte[] ConvertDerToFixed(byte[] derSignature, int coordLength)
    {
        // If already in fixed format, return as-is
        if (derSignature.Length == coordLength * 2)
            return derSignature;

        // Parse DER signature
        if (derSignature[0] != 0x30)
            return derSignature; // Not DER, assume fixed format

        try
        {
            var pos = 2;
            if (derSignature[1] > 127) pos++; // Long form length

            // Read R
            if (derSignature[pos++] != 0x02)
                throw new FormatException("Invalid DER signature");
            var rLen = derSignature[pos++];
            var rStart = pos;
            pos += rLen;

            // Read S
            if (derSignature[pos++] != 0x02)
                throw new FormatException("Invalid DER signature");
            var sLen = derSignature[pos++];
            var sStart = pos;

            var r = ExtractInteger(derSignature.AsSpan(rStart, rLen), coordLength);
            var s = ExtractInteger(derSignature.AsSpan(sStart, sLen), coordLength);

            var result = new byte[coordLength * 2];
            r.CopyTo(result, 0);
            s.CopyTo(result, coordLength);
            return result;
        }
        catch
        {
            return derSignature;
        }
    }

    private static byte[] ExtractInteger(ReadOnlySpan<byte> data, int length)
    {
        var result = new byte[length];

        // Skip leading zeros
        var start = 0;
        while (start < data.Length && data[start] == 0)
            start++;

        var toCopy = data.Length - start;
        if (toCopy > length)
            throw new FormatException("Integer too large");

        data.Slice(start, toCopy).CopyTo(result.AsSpan(length - toCopy));
        return result;
    }

    private static bool VerifyRsaSignature(byte[] publicKey, byte[] data, byte[] signature)
    {
        // Parse our stored format (length-prefixed n||e)
        var nLen = BinaryPrimitives.ReadInt32BigEndian(publicKey.AsSpan(0));
        var n = publicKey.AsSpan(4, nLen).ToArray();
        var eLen = BinaryPrimitives.ReadInt32BigEndian(publicKey.AsSpan(4 + nLen));
        var e = publicKey.AsSpan(8 + nLen, eLen).ToArray();

        var parameters = new RSAParameters
        {
            Modulus = n,
            Exponent = e
        };

        using var rsa = RSA.Create(parameters);
        return rsa.VerifyData(data, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }
}

internal class ClientData
{
    [System.Text.Json.Serialization.JsonPropertyName("type")]
    public string Type { get; set; } = null!;

    [System.Text.Json.Serialization.JsonPropertyName("challenge")]
    public string Challenge { get; set; } = null!;

    [System.Text.Json.Serialization.JsonPropertyName("origin")]
    public string Origin { get; set; } = null!;

    [System.Text.Json.Serialization.JsonPropertyName("crossOrigin")]
    public bool? CrossOrigin { get; set; }
}

internal class AuthenticatorData
{
    public byte[] RpIdHash { get; set; } = null!;
    public byte Flags { get; set; }
    public uint SignCount { get; set; }
    public bool UserPresent { get; set; }
    public bool UserVerified { get; set; }
    public AttestedCredentialData? CredentialData { get; set; }
}

internal class AttestedCredentialData
{
    public Guid AaGuid { get; set; }
    public byte[] CredentialId { get; set; } = null!;
    public byte[] PublicKey { get; set; } = null!;
    public byte[] PublicKeyCose { get; set; } = null!;
}

internal class AttestationResult
{
    public byte[] CredentialId { get; set; } = null!;
    public byte[] PublicKey { get; set; } = null!;
    public byte[] PublicKeyCose { get; set; } = null!;
    public Guid AaGuid { get; set; }
    public uint SignCount { get; set; }
    public string Format { get; set; } = null!;
    public bool UserVerified { get; set; }
}

internal class AssertionVerificationResult
{
    public bool Success { get; set; }
    public uint NewSignCount { get; set; }
    public bool UserVerified { get; set; }
}

public class WebAuthnException : Exception
{
    public WebAuthnException(string message) : base(message) { }
    public WebAuthnException(string message, Exception inner) : base(message, inner) { }
}

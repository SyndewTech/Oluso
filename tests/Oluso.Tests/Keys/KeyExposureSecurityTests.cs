using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using Oluso.Core.Domain.Entities;
using Xunit;

namespace Oluso.Tests.Keys;

/// <summary>
/// Security tests to ensure private key material is never exposed in DTOs or API responses.
/// These tests protect against accidental exposure during refactoring.
/// </summary>
public class KeyExposureSecurityTests
{
    #region SigningKey Entity Tests

    /// <summary>
    /// SigningKey entity should have PrivateKeyData marked with [JsonIgnore]
    /// to prevent accidental serialization in API responses.
    /// </summary>
    [Fact]
    public void SigningKey_PrivateKeyData_ShouldHaveJsonIgnore()
    {
        var property = typeof(SigningKey).GetProperty(nameof(SigningKey.PrivateKeyData));
        property.Should().NotBeNull();

        var hasJsonIgnore = property!.GetCustomAttribute<JsonIgnoreAttribute>() != null;

        hasJsonIgnore.Should().BeTrue(
            "PrivateKeyData MUST have [JsonIgnore] attribute to prevent accidental exposure in API responses. " +
            "Add [JsonIgnore] to SigningKey.PrivateKeyData property.");
    }

    /// <summary>
    /// SigningKey serialization should NEVER include PrivateKeyData
    /// </summary>
    [Fact]
    public void SigningKey_Serialization_ShouldNotIncludePrivateKeyData()
    {
        var key = new SigningKey
        {
            Id = "test-id",
            Name = "Test Key",
            KeyId = "kid-123",
            KeyType = SigningKeyType.RSA,
            Algorithm = "RS256",
            PrivateKeyData = "SUPER_SECRET_PRIVATE_KEY_DATA",
            PublicKeyData = "public-key-data"
        };

        var json = JsonSerializer.Serialize(key);

        json.Should().NotContain("SUPER_SECRET_PRIVATE_KEY_DATA",
            "Private key data should NEVER appear in serialized output");
        json.Should().NotContain("PrivateKeyData",
            "PrivateKeyData property should be excluded from serialization");
    }

    /// <summary>
    /// SigningKey serialization should NEVER include raw PublicKeyData
    /// (JWK endpoint provides public key in proper format)
    /// </summary>
    [Fact]
    public void SigningKey_Serialization_ShouldNotIncludePublicKeyData()
    {
        var key = new SigningKey
        {
            Id = "test-id",
            Name = "Test Key",
            KeyId = "kid-123",
            KeyType = SigningKeyType.RSA,
            Algorithm = "RS256",
            PrivateKeyData = "private",
            PublicKeyData = "RAW_PUBLIC_KEY_BYTES"
        };

        var json = JsonSerializer.Serialize(key);

        json.Should().NotContain("RAW_PUBLIC_KEY_BYTES",
            "Raw public key bytes should not be directly exposed - use JWKS endpoint");
    }

    /// <summary>
    /// KeyMaterialResult should NEVER expose EncryptedPrivateKey in serialization
    /// </summary>
    [Fact]
    public void KeyMaterialResult_Serialization_ShouldNotIncludeEncryptedPrivateKey()
    {
        var result = new Core.Services.KeyMaterialResult
        {
            KeyId = "kid-123",
            PublicKeyData = "public",
            EncryptedPrivateKey = "ENCRYPTED_PRIVATE_KEY_SHOULD_NOT_BE_HERE"
        };

        var json = JsonSerializer.Serialize(result);

        json.Should().NotContain("ENCRYPTED_PRIVATE_KEY_SHOULD_NOT_BE_HERE",
            "Encrypted private key should NEVER appear in serialized output");
    }

    #endregion

    #region DTO Property Verification Tests

    /// <summary>
    /// Verify that Oluso.Core types used as DTOs do NOT have properties that could expose private keys
    /// </summary>
    [Fact]
    public void CoreTypes_ShouldNotExposeDangerousPropertiesWithoutProtection()
    {
        var dangerousPropertyNames = new[]
        {
            "PrivateKeyData",
            "PrivateKey",
            "EncryptedPrivateKey",
            "SecretKey",
            "KeyMaterial",
            "RawPrivateKey"
        };

        // Check core entity types that might be serialized
        var coreAssembly = typeof(SigningKey).Assembly;
        var entityTypes = coreAssembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && t.Namespace?.Contains("Domain.Entities") == true);

        foreach (var entityType in entityTypes)
        {
            foreach (var dangerousProp in dangerousPropertyNames)
            {
                var property = entityType.GetProperty(dangerousProp, BindingFlags.Public | BindingFlags.Instance);
                if (property != null)
                {
                    // If a dangerous property exists, it MUST have [JsonIgnore]
                    var hasJsonIgnore = property.GetCustomAttribute<JsonIgnoreAttribute>() != null;
                    hasJsonIgnore.Should().BeTrue(
                        $"Entity type {entityType.Name} has dangerous property '{dangerousProp}' without [JsonIgnore]. " +
                        "Add [JsonIgnore] attribute to prevent accidental exposure.");
                }
            }
        }
    }

    /// <summary>
    /// Verify entity types that contain sensitive data have proper attributes
    /// </summary>
    [Theory]
    [InlineData(typeof(SigningKey), "PrivateKeyData")]
    [InlineData(typeof(SigningKey), "PublicKeyData")]
    public void SensitiveProperties_ShouldBeProtected(Type entityType, string propertyName)
    {
        var property = entityType.GetProperty(propertyName);
        property.Should().NotBeNull($"Property {propertyName} should exist on {entityType.Name}");

        var hasJsonIgnore = property!.GetCustomAttribute<JsonIgnoreAttribute>() != null;

        hasJsonIgnore.Should().BeTrue(
            $"Property {entityType.Name}.{propertyName} should have [JsonIgnore] attribute for defense in depth. " +
            "Even though DTOs filter this out, the entity itself should be protected against accidental serialization.");
    }

    #endregion

    #region Certificate Data Protection Tests

    /// <summary>
    /// CertificateMaterialResult should NOT expose encrypted private key in serialization
    /// </summary>
    [Fact]
    public void CertificateMaterialResult_Serialization_ShouldNotExposePrivateKey()
    {
        var result = new Core.Services.CertificateMaterialResult
        {
            Thumbprint = "ABC123",
            ThumbprintSha256 = "DEF456",
            CertificateData = "public-cert",
            EncryptedPrivateKey = "ENCRYPTED_CERTIFICATE_PRIVATE_KEY",
            Subject = "CN=Test",
            Issuer = "CN=Test",
            SerialNumber = "123456",
            NotBefore = DateTime.UtcNow,
            NotAfter = DateTime.UtcNow.AddYears(1)
        };

        var json = JsonSerializer.Serialize(result);

        json.Should().NotContain("ENCRYPTED_CERTIFICATE_PRIVATE_KEY",
            "Encrypted private key should never appear in serialized output");
    }

    #endregion

    #region KeyVault URI Protection Tests

    /// <summary>
    /// KeyVault URI should be included (it's not secret) but should not expose credentials
    /// </summary>
    [Fact]
    public void SigningKey_KeyVaultUri_ShouldNotContainCredentials()
    {
        var key = new SigningKey
        {
            Id = "test-id",
            Name = "Test Key",
            KeyId = "kid-123",
            KeyVaultUri = "https://myvault.vault.azure.net/keys/mykey/version"
        };

        // KeyVault URI itself is safe to expose (it's a reference, not the key)
        var json = JsonSerializer.Serialize(key);

        // But ensure it doesn't accidentally include credentials
        json.Should().NotContain("client_secret");
        json.Should().NotContain("password");
        json.Should().NotContain("access_token");
    }

    #endregion

    #region PersistedGrant Protection Tests

    /// <summary>
    /// PersistedGrant data field may contain tokens - ensure proper handling
    /// </summary>
    [Fact]
    public void PersistedGrant_Data_ShouldHaveJsonIgnore()
    {
        var property = typeof(PersistedGrant).GetProperty(nameof(PersistedGrant.Data));
        property.Should().NotBeNull();

        var hasJsonIgnore = property!.GetCustomAttribute<JsonIgnoreAttribute>() != null;

        // PersistedGrant.Data contains sensitive token data
        hasJsonIgnore.Should().BeTrue(
            "PersistedGrant.Data contains sensitive token data and MUST have [JsonIgnore] attribute");
    }

    #endregion

    #region Client Secret Protection Tests

    /// <summary>
    /// ClientSecret Value should have JsonIgnore to prevent exposure
    /// </summary>
    [Fact]
    public void ClientSecret_Value_ShouldHaveJsonIgnore()
    {
        var property = typeof(ClientSecret).GetProperty(nameof(ClientSecret.Value));
        property.Should().NotBeNull();

        var hasJsonIgnore = property!.GetCustomAttribute<JsonIgnoreAttribute>() != null;

        hasJsonIgnore.Should().BeTrue(
            "ClientSecret.Value contains hashed secret and MUST have [JsonIgnore] attribute");
    }

    #endregion
}

using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Oluso.Core;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Core.Storage;
using Oluso.EntityFramework.Stores;
using Oluso.Keys;
using Oluso.Tests.Roles;
using Xunit;

namespace Oluso.Tests.Keys;

/// <summary>
/// Expiry, rotation, and cache behaviour that keeps sessions alive across a
/// signing key's lifetime: expired keys stop signing but keep verifying for
/// the grace window, the scheduled pass rotates every tenant without an
/// ambient tenant context, and a token with an unknown key id refreshes the
/// cached validation set instead of being rejected.
/// </summary>
public class SigningKeyLifecycleTests
{
    private static SigningKey Key(string tenantId, string keyId, SigningKeyStatus status, DateTime expiresAt, int priority = 100) => new()
    {
        TenantId = tenantId,
        Name = keyId,
        KeyId = keyId,
        PrivateKeyData = "private",
        PublicKeyData = "public",
        Status = status,
        ActivatedAt = DateTime.UtcNow.AddDays(-60),
        ExpiresAt = expiresAt,
        Priority = priority,
        IncludeInJwks = true,
    };

    [Fact]
    public async Task Validation_and_jwks_keep_keys_expired_within_grace_and_drop_older_or_revoked_ones()
    {
        using var context = IdentityTestFactory.CreateContext();
        var store = new SigningKeyStore(context);
        var now = DateTime.UtcNow;
        context.SigningKeys.AddRange(
            Key("t1", "active", SigningKeyStatus.Active, now.AddDays(30)),
            Key("t1", "just-expired", SigningKeyStatus.Active, now.AddDays(-1)),
            Key("t1", "long-expired", SigningKeyStatus.Expired, now.AddDays(-45)),
            Key("t1", "revoked", SigningKeyStatus.Revoked, now.AddDays(-1)),
            Key("t2", "other-tenant", SigningKeyStatus.Active, now.AddDays(30)));
        await context.SaveChangesAsync();

        var withGrace = await store.GetValidationKeysForTenantAndPlatformAsync("t1", TimeSpan.FromDays(30));
        withGrace.Select(k => k.KeyId).Should().BeEquivalentTo(new[] { "active", "just-expired" });

        var noGrace = await store.GetValidationKeysForTenantAndPlatformAsync("t1");
        noGrace.Select(k => k.KeyId).Should().BeEquivalentTo(new[] { "active" });

        var jwks = await store.GetJwksKeysAsync("t1", TimeSpan.FromDays(30));
        jwks.Select(k => k.KeyId).Should().BeEquivalentTo(new[] { "active", "just-expired" });
    }

    [Fact]
    public async Task Data_protection_keys_never_verify_tokens_or_reach_jwks()
    {
        using var context = IdentityTestFactory.CreateContext();
        var store = new SigningKeyStore(context);
        var now = DateTime.UtcNow;
        var protection = Key("t1", "protects-data", SigningKeyStatus.Active, now.AddDays(30));
        protection.ClientId = DataProtectionKeys.ClientIdFor("emr");
        context.SigningKeys.AddRange(Key("t1", "active", SigningKeyStatus.Active, now.AddDays(30)), protection);
        await context.SaveChangesAsync();

        (await store.GetValidationKeysForTenantAndPlatformAsync("t1")).Select(k => k.KeyId).Should().Equal("active");
        (await store.GetJwksKeysAsync("t1")).Select(k => k.KeyId).Should().Equal("active");
        (await store.GetByClientAsync("t1", DataProtectionKeys.ClientIdFor("emr"))).Should().ContainSingle("the protector still finds its keys");
    }

    [Fact]
    public async Task Scheduled_pass_expires_overdue_keys_and_rotates_each_tenant_without_ambient_context()
    {
        using var context = IdentityTestFactory.CreateContext();
        var store = new SigningKeyStore(context);
        var now = DateTime.UtcNow;
        context.SigningKeys.AddRange(
            Key("t1", "t1-old", SigningKeyStatus.Active, now.AddDays(5)),
            Key("t2", "t2-old", SigningKeyStatus.Active, now.AddDays(-1)),
            Key("t3", "t3-fine", SigningKeyStatus.Active, now.AddDays(80)));
        await context.SaveChangesAsync();

        var encryption = new Mock<IKeyEncryptionService>();
        encryption.Setup(x => x.EncryptAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string s, CancellationToken _) => $"encrypted:{s}");
        encryption.Setup(x => x.DecryptAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string s, CancellationToken _) => s.Replace("encrypted:", ""));
        var provider = new LocalKeyMaterialProvider(encryption.Object, NullLogger<LocalKeyMaterialProvider>.Instance);
        var registry = new Mock<IKeyMaterialProviderRegistry>();
        registry.Setup(r => r.GetProvider(It.IsAny<KeyStorageProvider>())).Returns(provider);
        registry.Setup(r => r.GetDefaultProvider()).Returns(provider);
        var tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(c => c.TenantId).Returns((string?)null);

        var service = new SigningKeyService(store, tenantContext.Object, registry.Object, NullLogger<SigningKeyService>.Instance);
        await service.ProcessScheduledRotationsAsync();

        var keys = await context.SigningKeys.IgnoreQueryFilters().ToListAsync();
        keys.Single(k => k.KeyId == "t2-old").Status.Should().Be(SigningKeyStatus.Expired);
        keys.Should().NotContain(k => k.TenantId == null, "rotation must land on the key's tenant, not the ambient (missing) tenant");

        var t1New = keys.Where(k => k.TenantId == "t1" && k.KeyId != "t1-old").ToList();
        t1New.Should().ContainSingle(k => k.Status == SigningKeyStatus.Active && k.Priority == 200 && k.ExpiresAt > now.AddDays(60));
        keys.Single(k => k.KeyId == "t1-old").Priority.Should().Be(50, "the outgoing key drops to verify-only priority");

        keys.Where(k => k.TenantId == "t2" && k.Status == SigningKeyStatus.Active).Should().ContainSingle("a tenant whose only key expired gets a fresh one");
        keys.Where(k => k.TenantId == "t3").Should().ContainSingle("a key far from expiry is left alone");
    }

    [Fact]
    public async Task Credential_store_refreshes_validation_keys_once_when_a_token_kid_is_unknown()
    {
        SigningCredentialStore.InvalidateCache();
        var tenant = "kid-" + Guid.NewGuid().ToString("N");
        var keyA = Key(tenant, "A", SigningKeyStatus.Active, DateTime.UtcNow.AddDays(30));
        var keyB = Key(tenant, "B", SigningKeyStatus.Active, DateTime.UtcNow.AddDays(30));

        var store = new Mock<ISigningKeyStore>();
        store.Setup(s => s.GetRotationConfigAsync(tenant, null, It.IsAny<CancellationToken>())).ReturnsAsync((KeyRotationConfig?)null);
        store.SetupSequence(s => s.GetValidationKeysForTenantAndPlatformAsync(tenant, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<SigningKey> { keyA })
            .ReturnsAsync(new List<SigningKey> { keyA, keyB });

        var provider = new Mock<IKeyMaterialProvider>();
        provider.Setup(p => p.GetPublicKeyAsync(It.IsAny<SigningKey>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((SigningKey k, CancellationToken _) => (SecurityKey?)new RsaSecurityKey(RSA.Create(2048)) { KeyId = k.KeyId });
        var registry = new Mock<IKeyMaterialProviderRegistry>();
        registry.Setup(r => r.GetProvider(It.IsAny<KeyStorageProvider>())).Returns(provider.Object);
        registry.Setup(r => r.GetDefaultProvider()).Returns(provider.Object);
        var tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(c => c.TenantId).Returns(tenant);

        var credentialStore = new SigningCredentialStore(
            Mock.Of<ISigningKeyService>(), store.Object, tenantContext.Object, registry.Object, NullLogger<SigningCredentialStore>.Instance);

        (await credentialStore.GetValidationKeysAsync(tenant)).Select(k => k.Key.KeyId).Should().BeEquivalentTo(new[] { "A" });
        (await credentialStore.GetValidationKeysAsync(tenant, "A")).Should().HaveCount(1, "a known kid is served from the cache");
        (await credentialStore.GetValidationKeysAsync(tenant, "B")).Select(k => k.Key.KeyId).Should().BeEquivalentTo(new[] { "A", "B" });

        store.Verify(s => s.GetValidationKeysForTenantAndPlatformAsync(tenant, It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Credential_store_never_provisions_a_key_for_a_tenant_other_than_the_requests()
    {
        SigningCredentialStore.InvalidateCache();
        var keyService = new Mock<ISigningKeyService>();
        var tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(c => c.TenantId).Returns("tenant-" + Guid.NewGuid().ToString("N"));
        var credentialStore = new SigningCredentialStore(
            keyService.Object, Mock.Of<ISigningKeyStore>(), tenantContext.Object, Mock.Of<IKeyMaterialProviderRegistry>(),
            NullLogger<SigningCredentialStore>.Instance);

        var credentials = await credentialStore.GetSigningCredentialsAsync(Oluso.Core.PlatformConstants.TenantId, CancellationToken.None);

        credentials.Should().BeNull("a request routed to a tenant cannot create the platform's key");
        keyService.Verify(k => k.GenerateKeyAsync(It.IsAny<GenerateKeyRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

public class PlatformMaintenanceCallbackTests
{
    private static (Oluso.Controllers.PlatformMaintenanceController Controller, Mock<ISigningKeyService> Service) Build(string? configuredKey, string? providedKey)
    {
        var values = new Dictionary<string, string?>();
        if (configuredKey != null) values[Oluso.Controllers.PlatformMaintenanceController.ApiKeyConfigKey] = configuredKey;
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var service = new Mock<ISigningKeyService>();
        var controller = new Oluso.Controllers.PlatformMaintenanceController(
            service.Object, configuration, NullLogger<Oluso.Controllers.PlatformMaintenanceController>.Instance)
        {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext
            {
                HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext()
            }
        };
        if (providedKey != null)
        {
            controller.HttpContext.Request.Headers[Oluso.Controllers.PlatformMaintenanceController.ApiKeyHeader] = providedKey;
        }
        return (controller, service);
    }

    [Fact]
    public async Task Callback_runs_the_maintenance_pass_only_with_the_configured_key()
    {
        var (ok, service) = Build("secret", "secret");
        (await ok.MaintainSigningKeys(CancellationToken.None)).Should().BeOfType<Microsoft.AspNetCore.Mvc.OkObjectResult>();
        service.Verify(s => s.ProcessScheduledRotationsAsync(It.IsAny<CancellationToken>()), Times.Once);

        var (wrong, wrongService) = Build("secret", "nope");
        (await wrong.MaintainSigningKeys(CancellationToken.None)).Should().BeOfType<Microsoft.AspNetCore.Mvc.UnauthorizedResult>();
        wrongService.Verify(s => s.ProcessScheduledRotationsAsync(It.IsAny<CancellationToken>()), Times.Never);

        var (unconfigured, unconfiguredService) = Build(null, "secret");
        var result = await unconfigured.MaintainSigningKeys(CancellationToken.None);
        result.Should().BeOfType<Microsoft.AspNetCore.Mvc.ObjectResult>().Which.StatusCode.Should().Be(503);
        unconfiguredService.Verify(s => s.ProcessScheduledRotationsAsync(It.IsAny<CancellationToken>()), Times.Never);
    }
}

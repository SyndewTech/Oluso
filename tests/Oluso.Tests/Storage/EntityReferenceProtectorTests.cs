using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Services;
using Oluso.Core.Storage;
using Oluso.EntityFramework.Stores;
using Oluso.Keys;
using Oluso.Storage;
using Oluso.Tests.Roles;
using Xunit;

namespace Oluso.Tests.Storage;

/// <summary>
/// Protected entity references stay readable however the purpose's keys change: values name the key they were made
/// with, older values are found by trying each key, blind indexes do not move, and key maintenance never expires or
/// rotates a data-protection key.
/// </summary>
public class EntityReferenceProtectorTests
{
    private const string Tenant = "t1";
    private const string Purpose = "emr";

    private sealed class Harness : IDisposable
    {
        public required Oluso.EntityFramework.OlusoDbContext Context { get; init; }
        public required SigningKeyStore Store { get; init; }
        public required SigningKeyService Keys { get; init; }

        public EntityReferenceProtector NewProtector(IKeyMaterialProviderRegistry registry) =>
            new(Store, Keys, registry, NullLogger<EntityReferenceProtector>.Instance);

        public required IKeyMaterialProviderRegistry Registry { get; init; }

        public EntityReferenceProtector Protector() => NewProtector(Registry);

        public Task<List<SigningKey>> AllKeysAsync() => Context.SigningKeys.IgnoreQueryFilters().ToListAsync();

        public Task<SigningKey> AddSymmetricKeyAsync() => Keys.GenerateKeyAsync(new GenerateKeyRequest
        {
            TenantId = Tenant,
            ClientId = DataProtectionKeys.ClientIdFor(Purpose),
            Name = "later key",
            KeyType = SigningKeyType.Symmetric,
            KeySize = 256,
            Algorithm = "HS256",
            Use = SigningKeyUse.Encryption,
            ActivateImmediately = true,
            Priority = 100,
        });

        public void Dispose() => Context.Dispose();
    }

    private static Harness Create()
    {
        var context = IdentityTestFactory.CreateContext();
        var store = new SigningKeyStore(context);
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
        var keys = new SigningKeyService(store, tenantContext.Object, registry.Object, NullLogger<SigningKeyService>.Instance);
        return new Harness { Context = context, Store = store, Keys = keys, Registry = registry.Object };
    }

    private static string Legacy(string keyed) => keyed[(keyed.IndexOf(':', 3) + 1)..];

    [Fact]
    public async Task A_value_names_its_key_and_reads_back_after_newer_keys_and_a_rotated_in_signing_key_appear()
    {
        using var h = Create();
        var protector = h.Protector();
        var first = await protector.ProtectAsync(Tenant, Purpose, "order-1");
        var firstKey = (await h.AllKeysAsync()).Single();
        first.Should().StartWith($"k1:{firstKey.KeyId}:");

        // What the scheduled pass used to do: put a higher-priority RSA signing key under the purpose's client id.
        await h.Keys.GenerateKeyAsync(new GenerateKeyRequest
        {
            TenantId = Tenant, ClientId = DataProtectionKeys.ClientIdFor(Purpose), KeyType = SigningKeyType.RSA,
            Algorithm = "RS256", KeySize = 2048, ActivateImmediately = true, Priority = 200,
        });
        var later = await h.AddSymmetricKeyAsync();

        var fresh = h.Protector();
        (await fresh.UnprotectAsync(Tenant, Purpose, first)).Should().Be("order-1");
        var second = await fresh.ProtectAsync(Tenant, Purpose, "order-2");
        second.Should().StartWith($"k1:{later.KeyId}:", "new values use the newest data-protection key");
        (await h.Protector().UnprotectAsync(Tenant, Purpose, second)).Should().Be("order-2");
    }

    [Fact]
    public async Task Values_from_before_payloads_named_their_key_are_read_with_whichever_key_made_them()
    {
        using var h = Create();
        var older = Legacy(await h.Protector().ProtectAsync(Tenant, Purpose, "made-with-first"));
        await h.AddSymmetricKeyAsync();
        await h.AddSymmetricKeyAsync();
        var newer = Legacy(await h.Protector().ProtectAsync(Tenant, Purpose, "made-with-last"));

        var protector = h.Protector();
        (await protector.UnprotectAsync(Tenant, Purpose, older)).Should().Be("made-with-first");
        (await protector.UnprotectAsync(Tenant, Purpose, newer)).Should().Be("made-with-last");
    }

    [Fact]
    public async Task Using_the_protector_never_makes_another_key_once_one_exists()
    {
        using var h = Create();
        await h.Protector().ProtectAsync(Tenant, Purpose, "a");
        await h.Keys.GenerateKeyAsync(new GenerateKeyRequest
        {
            TenantId = Tenant, ClientId = DataProtectionKeys.ClientIdFor(Purpose), KeyType = SigningKeyType.RSA,
            Algorithm = "RS256", KeySize = 2048, ActivateImmediately = true, Priority = 200,
        });

        for (var i = 0; i < 3; i++)
        {
            var p = h.Protector();
            await p.UnprotectAsync(Tenant, Purpose, await p.ProtectAsync(Tenant, Purpose, $"v{i}"));
            await p.BlindIndexAsync(Tenant, Purpose, $"v{i}");
        }

        (await h.AllKeysAsync()).Count(k => k.KeyType == SigningKeyType.Symmetric).Should().Be(1);
    }

    [Fact]
    public async Task A_revoked_key_no_longer_reads_what_it_made()
    {
        using var h = Create();
        var value = await h.Protector().ProtectAsync(Tenant, Purpose, "x");
        var key = (await h.AllKeysAsync()).Single();
        key.Status = SigningKeyStatus.Revoked;
        await h.Store.UpdateAsync(key);
        await h.AddSymmetricKeyAsync();

        var act = () => h.Protector().UnprotectAsync(Tenant, Purpose, value);
        await act.Should().ThrowAsync<CryptographicException>();
    }

    [Fact]
    public async Task The_blind_index_stays_put_as_keys_are_added_and_candidates_cover_every_key()
    {
        using var h = Create();
        var before = await h.Protector().BlindIndexAsync(Tenant, Purpose, "patient-9");
        await h.AddSymmetricKeyAsync();

        var protector = h.Protector();
        (await protector.BlindIndexAsync(Tenant, Purpose, "patient-9")).Should().Be(before);
        var candidates = await protector.BlindIndexCandidatesAsync(Tenant, Purpose, "patient-9");
        candidates.Should().HaveCount(2).And.Contain(before);
    }

    [Fact]
    public async Task Key_maintenance_leaves_data_protection_keys_alone_and_will_not_rotate_them()
    {
        using var h = Create();
        await h.Protector().ProtectAsync(Tenant, Purpose, "x");
        var key = (await h.AllKeysAsync()).Single();
        key.ExpiresAt = DateTime.UtcNow.AddDays(-1);
        await h.Store.UpdateAsync(key);

        await h.Keys.ProcessScheduledRotationsAsync();

        var keys = await h.AllKeysAsync();
        keys.Should().ContainSingle();
        keys.Single().Status.Should().Be(SigningKeyStatus.Active);

        var tenantContext = new Mock<ITenantContext>();
        tenantContext.Setup(c => c.TenantId).Returns(Tenant);
        var service = new SigningKeyService(h.Store, tenantContext.Object, h.Registry, NullLogger<SigningKeyService>.Instance);
        var rotate = () => service.RotateKeysAsync(DataProtectionKeys.ClientIdFor(Purpose));
        await rotate.Should().ThrowAsync<InvalidOperationException>();
    }
}

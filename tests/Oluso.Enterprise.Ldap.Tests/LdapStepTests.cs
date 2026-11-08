using System.Formats.Asn1;
using System.Security.Claims;
using System.Text;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Services;
using Oluso.Core.UserJourneys;
using Oluso.Enterprise.Ldap.Authentication;
using Oluso.Enterprise.Ldap.Configuration;
using Oluso.Enterprise.Ldap.GroupMapping;
using Oluso.Enterprise.Ldap.Integration;
using Xunit;

namespace Oluso.Enterprise.Ldap.Tests;

/// <summary>
/// The LDAP step signs in against the organisation's chosen LDAP identity provider (with the
/// step's overrides) or the module's directory, keeps groups only when asked, and can let
/// users change their directory password before signing in.
/// </summary>
public class LdapStepTests
{
    private readonly Mock<ILdapAuthenticator> _authenticator = new();
    private readonly Mock<IExternalProviderConfigStore> _providers = new();
    private readonly Mock<IOlusoUserService> _users = new();
    private readonly Mock<ILdapGroupMapper> _groupMapper = new();

    public LdapStepTests()
    {
        _providers.Setup(p => p.GetBySchemeAsync("corp", true, It.IsAny<CancellationToken>())).ReturnsAsync(Provider("corp", "ldap"));
        _providers.Setup(p => p.GetBySchemeAsync("google", true, It.IsAny<CancellationToken>())).ReturnsAsync(Provider("google", "Google"));
        _providers.Setup(p => p.GetBySchemeAsync("off", true, It.IsAny<CancellationToken>())).ReturnsAsync(Provider("off", "ldap", enabled: false));
        _users.Setup(u => u.FindByUsernameAsync("jo", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OlusoUserInfo { Id = "u1", Username = "jo", Email = "jo@corp.test" });
    }

    private static ExternalProviderDefinition Provider(string scheme, string type, bool enabled = true) => new()
    {
        Id = scheme,
        Scheme = scheme,
        DisplayName = "Corp AD",
        ProviderType = type,
        ClientId = "",
        ClientSecret = "",
        Enabled = enabled,
        Properties = new Dictionary<string, string>
        {
            ["server"] = "ad.corp.test",
            ["baseDn"] = "dc=corp,dc=test",
            ["useSsl"] = "true",
            ["userSearchFilter"] = "(uid={0})",
        },
    };

    private StepExecutionContext Context(Dictionary<string, object> settings, Dictionary<string, object>? input = null)
    {
        var tenant = new Mock<ITenantContext>();
        tenant.SetupGet(t => t.TenantId).Returns("acme");
        var services = new ServiceCollection()
            .AddSingleton(_authenticator.Object)
            .AddSingleton(_providers.Object)
            .AddSingleton(_users.Object)
            .AddSingleton(_groupMapper.Object)
            .AddSingleton(tenant.Object)
            .AddSingleton(Mock.Of<IOlusoEventService>())
            .AddSingleton(Mock.Of<ITenantEmailDomainStore>())
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .BuildServiceProvider();

        return new StepExecutionContext
        {
            JourneyId = "j1",
            StepId = "ldap",
            Configuration = new JourneyStepConfiguration { Id = "ldap", Type = "ldap", Settings = settings },
            Input = new JourneyStepInput { StepId = "ldap", Values = input ?? new() },
            ServiceProvider = services,
        };
    }

    private static LdapAuthenticationResult SignedIn(params Claim[] claims) => LdapAuthenticationResult.Succeeded(
        new LdapUser { DistinguishedName = "uid=jo,dc=corp,dc=test", Username = "jo", Groups = new[] { "cn=Admins,dc=corp,dc=test" } },
        claims);

    [Fact]
    public async Task The_chosen_provider_is_used_with_the_steps_overrides()
    {
        var directory = await LdapStepHandler.ResolveDirectoryAsync(
            Context(new() { ["ldapProvider"] = "corp", ["baseDn"] = "ou=staff,dc=corp,dc=test", ["userSearchFilter"] = "(sAMAccountName={0})" }), default);

        directory.Error.Should().BeNull();
        (directory.Idp, directory.DisplayName).Should().Be(("corp", "Corp AD"));
        directory.Settings!.Server.Should().Be("ad.corp.test");
        directory.Settings.BaseDn.Should().Be("ou=staff,dc=corp,dc=test");
        directory.Settings.UserSearchFilter.Should().Be("(sAMAccountName={0})");
        directory.Settings.UseSsl.Should().BeTrue("an unset SSL override keeps the provider's setting");

        (await LdapStepHandler.ResolveDirectoryAsync(Context(new() { ["ldapProvider"] = "corp", ["useSsl"] = false }), default))
            .Settings!.UseSsl.Should().BeFalse();
    }

    [Fact]
    public async Task Without_a_provider_the_modules_directory_is_used()
    {
        var directory = await LdapStepHandler.ResolveDirectoryAsync(Context(new()), default);

        (directory.Settings, directory.Idp, directory.Error).Should().Be((null, "ldap", null));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("google")]
    [InlineData("off")]
    public async Task A_provider_that_is_missing_disabled_or_not_ldap_stops_the_step(string scheme)
    {
        var result = await new LdapStepHandler().ExecuteAsync(Context(new() { ["ldapProvider"] = scheme }));

        result.Outcome.Should().Be(StepOutcome.Failed);
        result.Error.Should().Be("ldap_provider_unavailable");
    }

    [Fact]
    public async Task Overrides_without_a_provider_are_rejected_when_the_journey_is_saved()
    {
        var handler = new LdapStepHandler();

        (await handler.ValidateConfigurationAsync(new Dictionary<string, object> { ["server"] = "x" })).IsValid.Should().BeFalse();
        (await handler.ValidateConfigurationAsync(new Dictionary<string, object> { ["server"] = "x", ["ldapProvider"] = "corp" })).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task Provider_sign_in_carries_its_groups_and_mapped_roles_unless_group_sync_is_off()
    {
        _authenticator.Setup(a => a.AuthenticateAsync("jo", "pw", It.IsAny<LdapProviderSettings>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(SignedIn(new Claim(ClaimTypes.Role, "Admins")));
        var input = new Dictionary<string, object> { ["username"] = "jo", ["password"] = "pw" };

        var synced = await new LdapStepHandler().ExecuteAsync(Context(new() { ["ldapProvider"] = "corp" }, input));

        synced.Outcome.Should().Be(StepOutcome.Continue);
        synced.OutputData!["idp"].Should().Be("corp");
        synced.OutputData["groups"].Should().BeEquivalentTo(new[] { "cn=Admins,dc=corp,dc=test" });
        synced.OutputData["roles"].Should().BeEquivalentTo(new[] { "Admins" });

        var unsynced = await new LdapStepHandler().ExecuteAsync(Context(new() { ["ldapProvider"] = "corp", ["syncGroups"] = false }, input));

        unsynced.OutputData!.Should().NotContainKey("groups").And.NotContainKey("roles");
    }

    [Fact]
    public async Task A_user_can_change_their_directory_password_and_is_signed_in_with_the_new_one()
    {
        _authenticator.Setup(a => a.ChangePasswordAsync("jo", "old", "new", It.IsAny<LdapProviderSettings?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(LdapPasswordChangeResult.Changed());
        _authenticator.Setup(a => a.AuthenticateAsync("jo", "new", It.IsAny<CancellationToken>())).ReturnsAsync(SignedIn());
        _groupMapper.Setup(g => g.GetUserGroupsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(Array.Empty<string>());
        _groupMapper.Setup(g => g.MapGroupsToRoles(It.IsAny<IEnumerable<string>>())).Returns(Array.Empty<string>());
        var settings = new Dictionary<string, object> { ["allowPasswordChange"] = true };

        var form = await new LdapStepHandler().ExecuteAsync(Context(settings, new() { ["action"] = "show_change_password" }));
        ((LdapLoginViewModel)form.StepResult!.ViewModel!).ChangePasswordMode.Should().BeTrue();

        var mismatch = await new LdapStepHandler().ExecuteAsync(Context(settings, new()
        {
            ["action"] = "change_password", ["username"] = "jo", ["password"] = "old", ["newPassword"] = "new", ["confirmPassword"] = "typo",
        }));
        mismatch.Outcome.Should().Be(StepOutcome.RequireInput);
        _authenticator.Verify(a => a.ChangePasswordAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<LdapProviderSettings?>(), It.IsAny<CancellationToken>()), Times.Never);

        var changed = await new LdapStepHandler().ExecuteAsync(Context(settings, new()
        {
            ["action"] = "change_password", ["username"] = "jo", ["password"] = "old", ["newPassword"] = "new", ["confirmPassword"] = "new",
        }));
        changed.Outcome.Should().Be(StepOutcome.Continue);
        changed.OutputData!["sub"].Should().Be("u1");
    }

    [Fact]
    public async Task Password_change_is_ignored_unless_the_step_allows_it()
    {
        _authenticator.Setup(a => a.AuthenticateAsync("jo", "old", It.IsAny<CancellationToken>()))
            .ReturnsAsync(LdapAuthenticationResult.Failed("Invalid username or password"));

        await new LdapStepHandler().ExecuteAsync(Context(new(), new()
        {
            ["action"] = "change_password", ["username"] = "jo", ["password"] = "old", ["newPassword"] = "new", ["confirmPassword"] = "new",
        }));

        _authenticator.Verify(a => a.ChangePasswordAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<LdapProviderSettings?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task The_builder_test_connects_to_the_chosen_provider_with_the_steps_overrides()
    {
        _authenticator.Setup(a => a.TestConnectionAsync(It.IsAny<LdapProviderSettings?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new LdapConnectionTestResult(true, "Connected to ad.corp.test", new[] { "Bind succeeded" }));

        var result = await new LdapStepHandler().TestConfigurationAsync(Context(new() { ["ldapProvider"] = "corp", ["baseDn"] = "ou=staff,dc=corp,dc=test" }));

        result.Success.Should().BeTrue();
        result.Details.Should().Contain("Provider: Corp AD").And.Contain("Bind succeeded");
        _authenticator.Verify(a => a.TestConnectionAsync(It.Is<LdapProviderSettings?>(s => s!.BaseDn == "ou=staff,dc=corp,dc=test"), It.IsAny<CancellationToken>()));

        (await new LdapStepHandler().TestConfigurationAsync(Context(new() { ["ldapProvider"] = "off" }))).Success.Should().BeFalse();
    }

    [Fact]
    public void Password_modify_request_is_encoded_per_rfc_3062()
    {
        var reader = new AsnReader(LdapAuthenticator.EncodePasswordModify("uid=jo,dc=corp", "old", "new"), AsnEncodingRules.BER);
        var sequence = reader.ReadSequence();

        Encoding.UTF8.GetString(sequence.ReadOctetString(new Asn1Tag(TagClass.ContextSpecific, 0))).Should().Be("uid=jo,dc=corp");
        Encoding.UTF8.GetString(sequence.ReadOctetString(new Asn1Tag(TagClass.ContextSpecific, 1))).Should().Be("old");
        Encoding.UTF8.GetString(sequence.ReadOctetString(new Asn1Tag(TagClass.ContextSpecific, 2))).Should().Be("new");
        sequence.HasData.Should().BeFalse();
    }

    [Fact]
    public void Active_directory_passwords_are_quoted_utf16()
    {
        LdapAuthenticator.ActiveDirectoryPassword("pw").Should().Equal(Encoding.Unicode.GetBytes("\"pw\""));
    }
}

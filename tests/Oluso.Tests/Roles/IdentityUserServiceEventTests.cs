using System.Security.Claims;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Microsoft.Data.Sqlite;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Events;
using Oluso.Core.Services;
using Oluso.EntityFramework;
using Oluso.EntityFramework.Services;
using Xunit;

namespace Oluso.Tests.Roles;

/// <summary>
/// The user service raises the user lifecycle events itself, once per fact, so
/// every path that creates, edits, verifies, deactivates or deletes an account
/// through it reaches webhooks, audit and identity sync without raising them.
/// </summary>
public class IdentityUserServiceEventTests : IDisposable
{
    private const string Tenant = "tenant-a";

    private readonly SqliteConnection _connection;
    private readonly TestTenantContext _tenant = new() { TenantId = Tenant };
    private readonly OlusoDbContext _context;
    private readonly Mock<ITenantEmailDomainStore> _emailDomains = new();
    private readonly CapturedEvents _captured;
    private readonly IdentityUserService _service;

    public IdentityUserServiceEventTests()
    {
        _captured = new CapturedEvents(configure: s => s.AddSingleton(_emailDomains.Object));
        _connection = IdentityTestFactory.OpenSqliteConnection();
        _context = IdentityTestFactory.CreateSqliteContext(_connection, _tenant);
        _service = IdentityTestFactory.CreateUserService(_context, _tenant, _captured.Services);
    }

    public void Dispose()
    {
        _context.Dispose();
        _connection.Dispose();
    }

    private List<OlusoEvent> Raised => _captured.Raised;

    private async Task<OlusoUser> UserAsync(string email = "u@a.test", bool emailConfirmed = false, bool isActive = true)
    {
        var user = await IdentityTestFactory.CreateUserAsync(_context, Tenant, email, "Una", "Obi");
        user.EmailConfirmed = emailConfirmed;
        user.IsActive = isActive;
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
        return user;
    }

    private static ClaimsPrincipal External(string providerKey, string? email) =>
        new(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, providerKey),
            new Claim(ClaimTypes.GivenName, "Ext"),
        }.Concat(email == null ? Array.Empty<Claim>() : new[] { new Claim(ClaimTypes.Email, email) }), "external"));

    // ------------------------------------------------------------------ create

    [Fact]
    public async Task Creating_a_user_raises_one_user_created_with_its_client()
    {
        var result = await _service.CreateUserAsync(new CreateUserRequest
        {
            Email = "new@a.test", Username = "newbie", ClientId = "web-app"
        });

        result.Succeeded.Should().BeTrue(result.Error);
        var created = Raised.Should().ContainSingle().Which.Should().BeOfType<UserRegisteredEvent>().Subject;
        created.SubjectId.Should().Be(result.UserId);
        created.TenantId.Should().Be(Tenant);
        created.Email.Should().Be("new@a.test");
        created.Username.Should().Be("newbie");
        created.ClientId.Should().Be("web-app");
    }

    [Fact]
    public async Task A_create_inside_a_journey_carries_the_journeys_policy()
    {
        UserOperationResult result;
        using (EventOriginScope.Begin(EventOrigin.Journey("signup-policy", "Sign up")))
        {
            result = await _service.CreateUserAsync(new CreateUserRequest { Email = "new@a.test" });
        }

        result.Succeeded.Should().BeTrue(result.Error);
        Raised.Should().ContainSingle().Which.Should().BeOfType<UserRegisteredEvent>()
            .Which.PolicyId.Should().Be("signup-policy");
    }

    [Fact]
    public async Task A_policy_the_caller_names_wins_over_the_origin_and_none_outside_a_journey()
    {
        using (EventOriginScope.Begin(EventOrigin.Journey("outer-policy")))
        {
            await _service.CreateUserAsync(new CreateUserRequest { Email = "named@a.test", PolicyId = "named-policy" });
        }
        using (EventOriginScope.Begin(EventOrigin.Admin("admin-1")))
        {
            await _service.CreateUserAsync(new CreateUserRequest { Email = "admin-made@a.test" });
        }

        Raised.Cast<UserRegisteredEvent>().Select(e => e.PolicyId).Should().Equal("named-policy", null);
    }

    [Fact]
    public async Task A_refused_create_raises_nothing()
    {
        var result = await _service.CreateUserAsync(new CreateUserRequest
        {
            Email = "root@a.test", TenantId = Oluso.Core.PlatformConstants.TenantId
        });

        result.Succeeded.Should().BeFalse();
        Raised.Should().BeEmpty();
    }

    [Fact]
    public async Task Registering_from_an_external_login_raises_one_user_created_with_its_client()
    {
        var result = await _service.CreateUserFromExternalLoginAsync(new CreateExternalUserRequest
        {
            Email = "ext@a.test", Provider = "google", ProviderKey = "g-1", ClientId = "web-app"
        });

        result.Succeeded.Should().BeTrue(result.Error);
        var created = Raised.Should().ContainSingle().Which.Should().BeOfType<UserRegisteredEvent>().Subject;
        created.SubjectId.Should().Be(result.UserId);
        created.Email.Should().Be("ext@a.test");
        created.ClientId.Should().Be("web-app");
    }

    [Fact]
    public async Task External_sign_in_raises_user_created_only_when_it_provisions_the_account()
    {
        var first = await _service.ProcessExternalLoginAsync("oidc:corp", External("k-1", "sso@a.test"), clientId: "web-app");
        var again = await _service.ProcessExternalLoginAsync("oidc:corp", External("k-1", "sso@a.test"), clientId: "web-app");

        first.IsNewUser.Should().BeTrue();
        again.IsNewUser.Should().BeFalse();
        var created = Raised.Should().ContainSingle().Which.Should().BeOfType<UserRegisteredEvent>().Subject;
        created.SubjectId.Should().Be(first.UserId);
        created.TenantId.Should().Be(Tenant);
        created.Email.Should().Be("sso@a.test");
        created.ClientId.Should().Be("web-app");
    }

    [Fact]
    public async Task External_provisioning_inside_a_journey_carries_the_journeys_policy()
    {
        using (EventOriginScope.Begin(EventOrigin.Journey("sso-policy")))
        {
            await _service.ProcessExternalLoginAsync("oidc:corp", External("k-9", "journey@a.test"), clientId: "web-app");
            await _service.CreateUserFromExternalLoginAsync(new CreateExternalUserRequest
            {
                Email = "ext-journey@a.test", Provider = "google", ProviderKey = "g-9"
            });
        }

        Raised.Cast<UserRegisteredEvent>().Select(e => e.PolicyId).Should().Equal("sso-policy", "sso-policy");
    }

    private void TenantVerified(string email) =>
        _emailDomains.Setup(d => d.IsVerifiedForTenantAsync(Tenant, email, It.IsAny<CancellationToken>())).ReturnsAsync(true);

    [Fact]
    public async Task Linking_an_external_login_to_an_existing_account_raises_nothing()
    {
        await UserAsync("known@a.test", emailConfirmed: true);
        TenantVerified("known@a.test");

        var result = await _service.ProcessExternalLoginAsync("saml:corp", External("k-2", "known@a.test"));

        result.Succeeded.Should().BeTrue();
        result.IsNewUser.Should().BeFalse();
        Raised.Should().BeEmpty();
    }

    [Fact]
    public async Task An_external_login_at_a_domain_the_tenant_has_not_verified_does_not_join_the_account_by_email()
    {
        var owner = await UserAsync("victim@a.test", emailConfirmed: true);

        var result = await _service.ProcessExternalLoginAsync("saml:corp", External("attacker", "victim@a.test"));

        result.Succeeded.Should().BeFalse();
        result.Error.Should().Be(ProcessExternalLoginResult.AccountLinkRequired);
        result.ExistingUserId.Should().Be(owner.Id);
        (await _service.FindByExternalLoginAsync("saml:corp", "attacker")).Should().BeNull("nothing is linked until the person proves the account");
    }

    [Fact]
    public async Task An_account_whose_own_email_was_never_verified_is_not_joined_by_email_even_at_a_verified_domain()
    {
        var owner = await UserAsync("squatted@a.test", emailConfirmed: false);
        TenantVerified("squatted@a.test");

        var result = await _service.ProcessExternalLoginAsync("saml:corp", External("k-3", "squatted@a.test"));

        result.Error.Should().Be(ProcessExternalLoginResult.AccountLinkRequired);
        result.ExistingUserId.Should().Be(owner.Id);
    }

    [Fact]
    public async Task A_new_external_account_is_verified_only_when_the_provider_can_vouch_for_the_address()
    {
        TenantVerified("trusted@a.test");

        var trusted = await _service.ProcessExternalLoginAsync("saml:corp", External("k-4", "trusted@a.test"));
        var untrusted = await _service.ProcessExternalLoginAsync("saml:corp", External("k-5", "someone@elsewhere.test"));

        (await _service.FindByIdAsync(trusted.UserId!))!.EmailVerified.Should().BeTrue();
        (await _service.FindByIdAsync(untrusted.UserId!))!.EmailVerified.Should().BeFalse();
    }

    // ------------------------------------------------------------------ update

    [Fact]
    public async Task Updating_raises_one_user_updated_naming_only_the_fields_that_changed()
    {
        var user = await UserAsync();

        await _service.UpdateUserAsync(user.Id, new UpdateUserRequest
        {
            Email = "u@a.test", FirstName = "Una", LastName = "Okafor", PhoneNumber = "+2348000000000"
        });

        var updated = Raised.Should().ContainSingle().Which.Should().BeOfType<UserUpdatedEvent>().Subject;
        updated.SubjectId.Should().Be(user.Id);
        updated.TenantId.Should().Be(Tenant);
        updated.Email.Should().Be("u@a.test");
        updated.ChangedFields.Should().Equal(new Dictionary<string, string>
        {
            ["lastName"] = "Okafor",
            ["phoneNumber"] = "+2348000000000",
        });
    }

    [Fact]
    public async Task An_update_that_changes_nothing_raises_nothing()
    {
        var user = await UserAsync();

        var result = await _service.UpdateUserAsync(user.Id, new UpdateUserRequest { FirstName = "Una", Email = "u@a.test" });

        result.Succeeded.Should().BeTrue();
        Raised.Should().BeEmpty();
    }

    [Fact]
    public async Task Custom_property_changes_are_named_by_key()
    {
        var user = await UserAsync();
        await _service.UpdateUserAsync(user.Id, new UpdateUserRequest
        {
            CustomProperties = new Dictionary<string, string> { ["plan"] = "basic", ["region"] = "eu" }
        });
        Raised.Clear();

        await _service.UpdateUserAsync(user.Id, new UpdateUserRequest
        {
            CustomProperties = new Dictionary<string, string> { ["plan"] = "pro", ["region"] = "eu" }
        });

        Raised.Should().ContainSingle().Which.Should().BeOfType<UserUpdatedEvent>()
            .Which.ChangedFields.Should().Equal(new Dictionary<string, string> { ["customProperties.plan"] = "pro" });
    }

    [Fact]
    public async Task A_verified_email_change_raises_user_updated_then_user_email_verified()
    {
        var user = await UserAsync(emailConfirmed: true);

        await _service.UpdateUserAsync(user.Id, new UpdateUserRequest { Email = "moved@a.test", EmailVerified = true });

        Raised.Select(e => e.GetType()).Should().Equal(typeof(UserUpdatedEvent), typeof(UserEmailVerifiedEvent));
        var verified = (UserEmailVerifiedEvent)Raised[1];
        verified.SubjectId.Should().Be(user.Id);
        verified.Email.Should().Be("moved@a.test");
    }

    [Fact]
    public async Task An_unverified_email_change_raises_user_updated_only()
    {
        var user = await UserAsync(emailConfirmed: true);

        await _service.UpdateUserAsync(user.Id, new UpdateUserRequest { Email = "moved@a.test" });

        Raised.Should().ContainSingle().Which.Should().BeOfType<UserUpdatedEvent>()
            .Which.ChangedFields.Should().ContainKey("email");
    }

    [Fact]
    public async Task Changing_email_or_phone_through_the_setters_raises_user_updated()
    {
        var user = await UserAsync();

        await _service.SetEmailAsync(user.Id, "set@a.test");
        await _service.SetPhoneNumberAsync(user.Id, "+44 20 7946 0000");
        await _service.SetPhoneNumberAsync(user.Id, "+442079460000");

        Raised.Should().HaveCount(2).And.AllBeOfType<UserUpdatedEvent>();
        ((UserUpdatedEvent)Raised[0]).ChangedFields.Should().Equal(new Dictionary<string, string> { ["email"] = "set@a.test" });
        ((UserUpdatedEvent)Raised[1]).ChangedFields.Should().Equal(new Dictionary<string, string> { ["phoneNumber"] = "+442079460000" });
    }

    // ------------------------------------------------------------ verification

    [Fact]
    public async Task Verifying_an_email_raises_user_email_verified_once()
    {
        var user = await UserAsync();

        await _service.SetEmailVerifiedAsync(user.Id, true);
        await _service.SetEmailVerifiedAsync(user.Id, true);

        var verified = Raised.Should().ContainSingle().Which.Should().BeOfType<UserEmailVerifiedEvent>().Subject;
        verified.SubjectId.Should().Be(user.Id);
        verified.TenantId.Should().Be(Tenant);
        verified.Email.Should().Be("u@a.test");
    }

    [Fact]
    public async Task Unverifying_an_email_raises_nothing()
    {
        var user = await UserAsync(emailConfirmed: true);

        await _service.SetEmailVerifiedAsync(user.Id, false);

        Raised.Should().BeEmpty();
    }

    // -------------------------------------------------------------- activation

    [Fact]
    public async Task Deactivating_and_reactivating_raise_their_events_once_per_change()
    {
        var user = await UserAsync();

        await _service.SetUserActiveAsync(user.Id, false);
        await _service.SetUserActiveAsync(user.Id, false);
        await _service.SetUserActiveAsync(user.Id, true);
        await _service.SetUserActiveAsync(user.Id, true);

        Raised.Select(e => e.GetType()).Should().Equal(typeof(UserDeactivatedEvent), typeof(UserReactivatedEvent));
        var deactivated = (UserDeactivatedEvent)Raised[0];
        deactivated.SubjectId.Should().Be(user.Id);
        deactivated.TenantId.Should().Be(Tenant);
        deactivated.Email.Should().Be("u@a.test");
    }

    // ------------------------------------------------------------------ delete

    [Fact]
    public async Task Deleting_raises_one_user_deleted_after_the_account_is_gone()
    {
        var user = await UserAsync("gone@a.test");

        using (EventOriginScope.Begin(EventOrigin.Admin("admin-1")))
        {
            (await _service.DeleteUserAsync(user.Id)).Succeeded.Should().BeTrue();
        }

        var deleted = Raised.Should().ContainSingle().Which.Should().BeOfType<UserDeletedEvent>().Subject;
        _context.Users.Any(u => u.Id == user.Id).Should().BeFalse();
        deleted.SubjectId.Should().Be(user.Id);
        deleted.TenantId.Should().Be(Tenant);
        deleted.Email.Should().Be("gone@a.test");
        deleted.DeletedByAdmin.Should().BeTrue();
        deleted.Origin.Should().Be(EventOrigin.Admin("admin-1"));
    }

    [Fact]
    public async Task A_user_deleting_their_own_account_is_not_an_admin_deletion()
    {
        var user = await UserAsync("self@a.test");

        using (EventOriginScope.Begin(EventOrigin.User(user.Id)))
        {
            (await _service.DeleteUserAsync(user.Id)).Succeeded.Should().BeTrue();
        }

        Raised.Should().ContainSingle().Which.Should().BeOfType<UserDeletedEvent>()
            .Which.DeletedByAdmin.Should().BeFalse();
    }

    [Fact]
    public async Task A_refused_delete_raises_nothing()
    {
        var result = await _service.DeleteUserAsync("missing");

        result.Succeeded.Should().BeFalse();
        Raised.Should().BeEmpty();
    }
}

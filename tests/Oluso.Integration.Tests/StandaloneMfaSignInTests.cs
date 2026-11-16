using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Protocols.Models;
using Oluso.Core.UserJourneys;
using Oluso.Integration.Tests.Fixtures;
using Xunit;

namespace Oluso.Integration.Tests;

/// <summary>
/// The second factor on the standard pages (/account/mfa): a tenant that requires MFA has a user without it set up an
/// authenticator app while signing in; a user who turned MFA on is asked for a code; wrong codes count towards the
/// account's lockout. The session, and the tokens, then record amr mfa.
/// </summary>
public class StandaloneMfaSignInTests : IntegrationTestBase
{
    private const string Username = "testuser@example.com";
    private const string Password = "Password123!";
    private const string JourneyClientId = "mfa-journey-browser-client";
    private const string PasswordOnlyPolicy = "password-only";
    private bool _tenantJourneyBefore;

    public StandaloneMfaSignInTests(OlusoWebApplicationFactory factory) : base(factory)
    {
    }

    public override async Task InitializeAsync()
    {
        await WithDbContextAsync(BrowserClientSignIn.EnsureClientAsync);
        await WithDbContextAsync(async db =>
        {
            _tenantJourneyBefore = (await db.Tenants.FindAsync("default"))!.UseJourneyFlow;
            if (await db.Clients.IgnoreQueryFilters().AnyAsync(c => c.ClientId == JourneyClientId)) return;
            var tenantId = await db.Clients.IgnoreQueryFilters()
                .Where(c => c.ClientId == BrowserClientSignIn.ClientId).Select(c => c.TenantId).FirstAsync();
            var client = new Client
            {
                ClientId = JourneyClientId,
                ClientName = "Journey browser client",
                TenantId = tenantId,
                Enabled = true,
                RequireClientSecret = false,
                TokenEndpointAuthMethod = "none",
                RequirePkce = false,
                RequireConsent = false,
                UseJourneyFlow = true,
            };
            client.AllowedGrantTypes.Add(new ClientGrantType { GrantType = "authorization_code", TenantId = tenantId });
            client.RedirectUris.Add(new ClientRedirectUri { RedirectUri = BrowserClientSignIn.RedirectUri, TenantId = tenantId });
            client.AllowedScopes.Add(new ClientScope { Scope = "openid", TenantId = tenantId });
            db.Clients.Add(client);
            await db.SaveChangesAsync();
        });
        using (var scope = Factory.Services.CreateScope())
        {
            var policies = scope.ServiceProvider.GetRequiredService<IJourneyPolicyStore>();
            if (await policies.GetBySlugAsync(PasswordOnlyPolicy) == null)
            {
                await policies.SaveAsync(new JourneyPolicy
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Slug = PasswordOnlyPolicy,
                    Name = "Password only",
                    TenantId = "default",
                    Type = JourneyType.SignIn,
                    Steps = new List<JourneyPolicyStep> { new() { Id = "login", Type = "local_login", Order = 1 } },
                });
            }
        }
        await ResetUserAsync();
        await SetTenantRequiresMfaAsync(false);
    }

    public override async Task DisposeAsync()
    {
        await ResetUserAsync();
        await SetTenantRequiresMfaAsync(false);
        await WithDbContextAsync(async db =>
        {
            (await db.Tenants.FindAsync("default"))!.UseJourneyFlow = _tenantJourneyBefore;
            await db.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task A_journey_without_an_mfa_step_hands_the_sign_in_to_the_mfa_page_before_the_session()
    {
        await SetTenantRequiresMfaAsync(true);
        await WithDbContextAsync(async db =>
        {
            (await db.Tenants.FindAsync("default"))!.UseJourneyFlow = true;
            await db.SaveChangesAsync();
        });
        var http = Browser();

        var authorize = await http.GetAsync("/connect/authorize?response_type=code&scope=openid&state=s1" +
            $"&client_id={JourneyClientId}&redirect_uri={Uri.EscapeDataString(BrowserClientSignIn.RedirectUri)}" +
            $"&policy={PasswordOnlyPolicy}");
        authorize.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var journey = authorize.Headers.Location!.ToString();
        journey.Should().Contain("/journey/");

        var loginPage = await (await http.GetAsync(journey)).Content.ReadAsStringAsync();
        var login = await http.PostAsync(journey, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = Username,
            ["password"] = Password,
            ["__RequestVerificationToken"] = Antiforgery(loginPage),
        }));
        login.StatusCode.Should().Be(HttpStatusCode.Redirect, await login.Content.ReadAsStringAsync());
        login.Headers.Location!.ToString().Should().Be("/account/mfa");

        var setupPage = await (await http.GetAsync("/account/mfa")).Content.ReadAsStringAsync();
        var totpPage = await (await PostAsync(http, setupPage, new() { ["setup_method"] = "totp" })).Content.ReadAsStringAsync();
        var recoveryPage = await (await PostAsync(http, totpPage, new() { ["setup_totp_code"] = await CurrentCodeAsync() }))
            .Content.ReadAsStringAsync();
        var done = await PostAsync(http, recoveryPage, new() { ["recovery_codes_confirmed"] = "true" });

        var idToken = await IdTokenAfterCallbackAsync(http, done, JourneyClientId);
        idToken.Claims.Where(c => c.Type == "amr").Select(c => c.Value).Should().Contain("mfa");
    }

    [Fact]
    public async Task A_tenant_that_requires_mfa_has_a_user_without_it_set_up_an_authenticator_while_signing_in()
    {
        await SetTenantRequiresMfaAsync(true);
        var http = Browser();

        var held = await PasswordSignInAsync(http);
        held.Headers.Location!.ToString().Should().Be("/account/mfa");

        var setup = await http.GetAsync("/account/mfa");
        var setupPage = await setup.Content.ReadAsStringAsync();
        setupPage.Should().Contain("name=\"setup_method\" value=\"totp\"");

        var totpSetup = await PostAsync(http, setupPage, new() { ["setup_method"] = "totp" });
        var totpPage = await totpSetup.Content.ReadAsStringAsync();
        totpPage.Should().Contain("class=\"secret-key\"");

        var recovery = await PostAsync(http, totpPage, new() { ["setup_totp_code"] = await CurrentCodeAsync() });
        var recoveryPage = await recovery.Content.ReadAsStringAsync();
        recoveryPage.Should().Contain("name=\"recovery_codes_confirmed\"");

        var done = await PostAsync(http, recoveryPage, new() { ["recovery_codes_confirmed"] = "true" });

        var idToken = await IdTokenAfterCallbackAsync(http, done);
        idToken.Claims.Where(c => c.Type == "amr").Select(c => c.Value).Should().Contain(new[] { "pwd", "mfa" });
        idToken.Claims.Should().Contain(c => c.Type == "acr" && c.Value == AuthenticationContextClasses.MultiFactor);
        (await WithUserAsync((users, user) => users.GetTwoFactorEnabledAsync(user))).Should().BeTrue();
    }

    [Fact]
    public async Task A_user_who_turned_mfa_on_is_asked_for_a_code_after_the_password()
    {
        await EnableAuthenticatorAsync();
        var http = Browser();

        var held = await PasswordSignInAsync(http);
        held.Headers.Location!.ToString().Should().Be("/account/mfa");

        var verify = await (await http.GetAsync("/account/mfa")).Content.ReadAsStringAsync();
        verify.Should().Contain("name=\"provider\" value=\"totp\"");

        var wrong = await PostAsync(http, verify, new() { ["provider"] = "totp", ["code"] = "000000" });
        wrong.StatusCode.Should().Be(HttpStatusCode.OK);
        var retry = await wrong.Content.ReadAsStringAsync();
        retry.Should().Contain("alert-danger");

        var done = await PostAsync(http, retry, new() { ["provider"] = "totp", ["code"] = await CurrentCodeAsync() });

        var idToken = await IdTokenAfterCallbackAsync(http, done);
        idToken.Claims.Where(c => c.Type == "amr").Select(c => c.Value).Should().Contain("mfa");
        (await WithUserAsync((users, user) => users.GetAccessFailedCountAsync(user))).Should().Be(0);
    }

    [Fact]
    public async Task Wrong_codes_lock_the_account_as_wrong_passwords_do()
    {
        await EnableAuthenticatorAsync();
        await WithUserAsync((users, user) => users.SetLockoutEnabledAsync(user, true));
        var http = Browser();
        await PasswordSignInAsync(http);

        var page = await (await http.GetAsync("/account/mfa")).Content.ReadAsStringAsync();
        var locked = false;
        for (var attempt = 0; attempt < 10 && !locked; attempt++)
        {
            page = await (await PostAsync(http, page, new() { ["provider"] = "totp", ["code"] = "000000" })).Content.ReadAsStringAsync();
            locked = !page.Contains("name=\"code\"", StringComparison.Ordinal);
        }

        locked.Should().BeTrue();
        page.Should().Contain("/account/login?returnUrl=");
        (await WithUserAsync((users, user) => users.IsLockedOutAsync(user))).Should().BeTrue();

        // The held sign-in is gone: nothing more can be tried on it.
        var after = await (await http.GetAsync("/account/mfa")).Content.ReadAsStringAsync();
        after.Should().NotContain("name=\"code\"");
    }

    [Fact]
    public async Task An_expired_password_of_an_account_with_mfa_is_changed_after_the_second_factor()
    {
        await EnableAuthenticatorAsync();
        await ExpirePasswordAsync();
        var http = Browser();

        var held = await PasswordSignInAsync(http);
        held.Headers.Location!.ToString().Should().Be("/account/mfa", "the second factor comes before the password change");

        var verify = await (await http.GetAsync("/account/mfa")).Content.ReadAsStringAsync();
        var change = await PostAsync(http, verify, new() { ["provider"] = "totp", ["code"] = await CurrentCodeAsync() });

        await ChangeExpiredPasswordAsync(http, change);
    }

    [Fact]
    public async Task A_journey_password_sign_in_by_an_account_with_mfa_goes_on_to_the_second_factor()
    {
        await EnableAuthenticatorAsync();
        var http = Browser();

        var completed = await JourneyPasswordSignInAsync(http);
        completed.Headers.Location!.ToString().Should().Be("/account/mfa");

        var verify = await (await http.GetAsync("/account/mfa")).Content.ReadAsStringAsync();
        var done = await PostAsync(http, verify, new() { ["provider"] = "totp", ["code"] = await CurrentCodeAsync() });

        var idToken = await IdTokenAfterCallbackAsync(http, done, JourneyClientId);
        idToken.Claims.Where(c => c.Type == "amr").Select(c => c.Value).Should().Contain(new[] { "pwd", "mfa" });
    }

    [Fact]
    public async Task A_journey_changes_the_expired_password_of_an_account_with_mfa_after_the_second_factor()
    {
        await EnableAuthenticatorAsync();
        await ExpirePasswordAsync();
        var http = Browser();

        var completed = await JourneyPasswordSignInAsync(http);
        completed.Headers.Location!.ToString().Should().Be("/account/mfa");

        var verify = await (await http.GetAsync("/account/mfa")).Content.ReadAsStringAsync();
        var change = await PostAsync(http, verify, new() { ["provider"] = "totp", ["code"] = await CurrentCodeAsync() });

        await ChangeExpiredPasswordAsync(http, change);
    }

    [Fact]
    public async Task The_settings_page_turns_the_requirement_on_for_the_tenant()
    {
        var admin = await SuperAdminClient.CreateAsync(Factory, "default");
        var settings = System.Text.Json.Nodes.JsonNode.Parse(await admin.GetStringAsync("/api/admin/settings"))!;
        settings["tenantSettings"]!["requireMfa"]!.GetValue<bool>().Should().BeFalse();

        settings["tenantSettings"]!["requireMfa"] = true;
        var saved = await admin.PutAsync("/api/admin/settings",
            new StringContent(settings.ToJsonString(), System.Text.Encoding.UTF8, "application/json"));
        saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());

        System.Text.Json.Nodes.JsonNode.Parse(await admin.GetStringAsync("/api/admin/settings"))!["tenantSettings"]!["requireMfa"]!
            .GetValue<bool>().Should().BeTrue();
        await WithDbContextAsync(async db => (await db.Tenants.FindAsync("default"))!.RequireMfa.Should().BeTrue());
    }

    /// <summary>The second factor passed: the reset page takes the new password; no session was issued.</summary>
    private async Task ChangeExpiredPasswordAsync(HttpClient http, HttpResponseMessage afterSecondFactor)
    {
        afterSecondFactor.StatusCode.Should().Be(HttpStatusCode.Redirect, await afterSecondFactor.Content.ReadAsStringAsync());
        var resetLocation = afterSecondFactor.Headers.Location!.ToString();
        resetLocation.Should().StartWith("/account/reset-password");

        var resetPage = await (await http.GetAsync(resetLocation)).Content.ReadAsStringAsync();
        resetPage.Should().Contain("name=\"Input.NewPassword\"");
        var changed = await http.PostAsync(resetLocation, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.NewPassword"] = "Changed-Password-456!",
            ["Input.ConfirmPassword"] = "Changed-Password-456!",
            ["__RequestVerificationToken"] = Antiforgery(resetPage),
        }));
        changed.StatusCode.Should().Be(HttpStatusCode.OK, await changed.Content.ReadAsStringAsync());

        (await WithUserAsync((users, user) => users.CheckPasswordAsync(user, "Changed-Password-456!"))).Should().BeTrue();
        // No session came of it: a silent sign-in still needs the user to sign in.
        var silent = await http.GetAsync("/connect/authorize?response_type=code&scope=openid&state=s1&prompt=none" +
            $"&client_id={BrowserClientSignIn.ClientId}&redirect_uri={Uri.EscapeDataString(BrowserClientSignIn.RedirectUri)}");
        HttpUtility.ParseQueryString(silent.Headers.Location!.Query)["error"].Should().Be("login_required");
    }

    /// <summary>Authorize through the password-only journey and sign in there; returns the journey's last response.</summary>
    private async Task<HttpResponseMessage> JourneyPasswordSignInAsync(HttpClient http)
    {
        await WithDbContextAsync(async db =>
        {
            (await db.Tenants.FindAsync("default"))!.UseJourneyFlow = true;
            await db.SaveChangesAsync();
        });

        var authorize = await http.GetAsync("/connect/authorize?response_type=code&scope=openid&state=s1" +
            $"&client_id={JourneyClientId}&redirect_uri={Uri.EscapeDataString(BrowserClientSignIn.RedirectUri)}" +
            $"&policy={PasswordOnlyPolicy}");
        authorize.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var journey = authorize.Headers.Location!.ToString();
        journey.Should().Contain("/journey/");

        var loginPage = await (await http.GetAsync(journey)).Content.ReadAsStringAsync();
        var login = await http.PostAsync(journey, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = Username,
            ["password"] = Password,
            ["__RequestVerificationToken"] = Antiforgery(loginPage),
        }));
        login.StatusCode.Should().Be(HttpStatusCode.Redirect, await login.Content.ReadAsStringAsync());
        return login;
    }

    /// <summary>The tenant's passwords expire after 30 days and the user's was set 60 days ago.</summary>
    private async Task ExpirePasswordAsync()
    {
        await WithDbContextAsync(async db =>
        {
            var tenant = (await db.Tenants.FindAsync("default"))!;
            tenant.PasswordPolicy ??= new TenantPasswordPolicy();
            tenant.PasswordPolicy.PasswordExpirationDays = 30;
            await db.SaveChangesAsync();
        });
        await WithUserAsync(async (users, user) =>
        {
            user.PasswordChangedAt = DateTime.UtcNow.AddDays(-60);
            return await users.UpdateAsync(user);
        });
    }

    private HttpClient Browser()
    {
        var http = Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        http.DefaultRequestHeaders.Add("X-Tenant-Id", "default");
        return http;
    }

    /// <summary>Authorize, then the login page with the password; returns the login POST's response.</summary>
    private static async Task<HttpResponseMessage> PasswordSignInAsync(HttpClient http)
    {
        var authorize = await http.GetAsync("/connect/authorize?response_type=code&scope=openid&state=s1" +
            $"&client_id={BrowserClientSignIn.ClientId}&redirect_uri={Uri.EscapeDataString(BrowserClientSignIn.RedirectUri)}");
        authorize.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var loginLocation = authorize.Headers.Location!.ToString();

        var loginPage = await (await http.GetAsync(loginLocation)).Content.ReadAsStringAsync();
        var returnUrl = HttpUtility.ParseQueryString(new Uri(new Uri("http://localhost"), loginLocation).Query)["returnUrl"];
        var login = await http.PostAsync(loginLocation, new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["Input.Username"] = Username,
            ["Input.Password"] = Password,
            ["Input.RememberMe"] = "false",
            ["ReturnUrl"] = returnUrl!,
            ["__RequestVerificationToken"] = Antiforgery(loginPage),
        }));
        login.StatusCode.Should().Be(HttpStatusCode.Redirect, await login.Content.ReadAsStringAsync());
        return login;
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient http, string page, Dictionary<string, string> fields)
    {
        fields["__RequestVerificationToken"] = Antiforgery(page);
        return http.PostAsync("/account/mfa", new FormUrlEncodedContent(fields));
    }

    private async Task<JwtSecurityToken> IdTokenAfterCallbackAsync(HttpClient http, HttpResponseMessage completed,
        string clientId = BrowserClientSignIn.ClientId)
    {
        completed.StatusCode.Should().Be(HttpStatusCode.Redirect, await completed.Content.ReadAsStringAsync());
        var callback = await http.GetAsync(completed.Headers.Location!.ToString());
        callback.StatusCode.Should().Be(HttpStatusCode.Redirect);
        var location = callback.Headers.Location!;
        location.ToString().Should().StartWith(BrowserClientSignIn.RedirectUri);
        var code = HttpUtility.ParseQueryString(location.Query)["code"];
        code.Should().NotBeNullOrEmpty(location.ToString());

        var redeemed = await Client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code!,
            ["redirect_uri"] = BrowserClientSignIn.RedirectUri,
            ["client_id"] = clientId,
        }));
        redeemed.StatusCode.Should().Be(HttpStatusCode.OK, await redeemed.Content.ReadAsStringAsync());
        var tokens = JsonDocument.Parse(await redeemed.Content.ReadAsStringAsync()).RootElement;
        return new JwtSecurityTokenHandler().ReadJwtToken(tokens.GetProperty("id_token").GetString());
    }

    private static string Antiforgery(string page) =>
        Regex.Match(page, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"").Groups[1].Value;

    /// <summary>The user's authenticator app code now (RFC 6238: HMAC-SHA1, 30 second steps, 6 digits).</summary>
    private async Task<string> CurrentCodeAsync()
    {
        var key = await WithUserAsync((users, user) => users.GetAuthenticatorKeyAsync(user));
        var secret = Base32Decode(key!);
        var counter = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        var message = BitConverter.GetBytes(counter);
        if (BitConverter.IsLittleEndian) Array.Reverse(message);
        var hash = HMACSHA1.HashData(secret, message);
        var offset = hash[^1] & 0x0f;
        var binary = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }

    private static byte[] Base32Decode(string value)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        var bits = 0;
        var buffer = 0;
        var output = new List<byte>();
        foreach (var c in value.TrimEnd('=').ToUpperInvariant())
        {
            buffer = (buffer << 5) | alphabet.IndexOf(c);
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)(buffer >> (bits - 8)));
                bits -= 8;
            }
        }
        return output.ToArray();
    }

    private Task EnableAuthenticatorAsync() => WithUserAsync(async (users, user) =>
    {
        await users.ResetAuthenticatorKeyAsync(user);
        await users.SetTwoFactorEnabledAsync(user, true);
        return true;
    });

    private async Task ResetUserAsync()
    {
        await WithDbContextAsync(async db =>
        {
            var tenant = (await db.Tenants.FindAsync("default"))!;
            if (tenant.PasswordPolicy != null) tenant.PasswordPolicy.PasswordExpirationDays = 0;
            await db.SaveChangesAsync();
        });
        await ResetUserAccountAsync();
    }

    private Task ResetUserAccountAsync() => WithUserAsync(async (users, user) =>
    {
        user.PasswordChangedAt = null;
        if (!await users.CheckPasswordAsync(user, Password))
        {
            await users.RemovePasswordAsync(user);
            await users.AddPasswordAsync(user, Password);
        }
        await users.SetTwoFactorEnabledAsync(user, false);
        await users.SetLockoutEnabledAsync(user, false);
        await users.RemoveAuthenticationTokenAsync(user, "[AspNetUserStore]", "AuthenticatorKey");
        await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);
        return true;
    });

    private async Task<T> WithUserAsync<T>(Func<UserManager<OlusoUser>, OlusoUser, Task<T>> action)
    {
        using var scope = Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<OlusoUser>>();
        var user = await users.FindByEmailAsync(Username);
        return await action(users, user!);
    }

    private Task SetTenantRequiresMfaAsync(bool required) => WithDbContextAsync(async db =>
    {
        var tenant = await db.Tenants.FindAsync("default");
        tenant!.RequireMfa = required;
        await db.SaveChangesAsync();
    });
}

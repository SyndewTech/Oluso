using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols.Validation;
using Oluso.Core.Services;
using Oluso.Integration.Tests.Fixtures;
using Xunit;

namespace Oluso.Integration.Tests;

/// <summary>
/// Prefix scopes (<see cref="ApiScopePrefixes"/>) end to end. Two set-ups: a generic prefix scope <c>files:</c> with
/// the default suffix rule (any scope-token characters), and the Matrix worked example from docs/prefix-scopes.md, the
/// API scope <c>urn:matrix:client:api:*</c> (an ordinary scope; the <c>*</c> is literal) and the prefix scope
/// <c>urn:matrix:client:device:</c> narrowed to the device-id characters Matrix recommends. The full requested value is
/// granted and travels through the code, the token response, the access token and introspection, and survives a
/// refresh; a refresh can't swap it for another suffix; an invalid suffix is left out, or refused for a strict client.
/// The admin API reads and writes the prefix settings.
/// </summary>
public class PrefixScopeTests : IntegrationTestBase, IDisposable
{
    private const string Tenant = "default";
    private const string AppClientId = "matrix-prefix-app";
    private const string ServiceClientId = "matrix-prefix-svc";
    private const string StrictClientId = "matrix-prefix-strict";
    private const string Secret = "matrix-prefix-secret";
    private const string UserEmail = "testuser@example.com";

    private const string ApiScope = "urn:matrix:client:api:*";
    private const string DevicePrefix = "urn:matrix:client:device:";
    private const string Device = DevicePrefix + "AbC123xyz_-.~";
    private const string MatrixDevicePattern = "[A-Za-z0-9._~-]+";
    private const string FilesPrefix = "files:";
    private const string WidePrefix = "wide:";
    private const int WideMaxLength = 40;

    private static readonly string[] AppAllowed = { "openid", "offline_access", ApiScope, DevicePrefix };

    private readonly ECDsa _key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private readonly NativeAppSignIn _app;

    public PrefixScopeTests(OlusoWebApplicationFactory factory) : base(factory)
    {
        _app = new NativeAppSignIn(factory, _key, AppClientId);
    }

    public void Dispose() => _key.Dispose();

    public override async Task InitializeAsync()
    {
        await WithDbContextAsync(async db =>
        {
            if (await db.Clients.IgnoreQueryFilters().AnyAsync(c => c.ClientId == ServiceClientId))
                return;

            // A public, PKCE + DPoP, refresh-token client like a Matrix client registers.
            await NativeAppSignIn.EnsurePlatformClientAsync(db, AppClientId, AppAllowed);

            using var _ = db.BeginSeedMode();
            db.ApiScopes.Add(new ApiScope { Name = ApiScope, DisplayName = "Matrix account", TenantId = Tenant, Enabled = true });
            db.ApiScopes.Add(new ApiScope
            {
                Name = DevicePrefix, DisplayName = "Matrix device", TenantId = Tenant, Enabled = true,
                Properties = new List<ApiScopeProperty>
                {
                    new() { Key = ApiScopePrefixes.MatchProperty, Value = ApiScopePrefixes.PrefixMatch },
                    new() { Key = ApiScopePrefixes.SuffixPatternProperty, Value = MatrixDevicePattern },
                },
            });
            db.ApiScopes.Add(new ApiScope
            {
                Name = FilesPrefix, DisplayName = "Files", TenantId = Tenant, Enabled = true,
                Properties = new List<ApiScopeProperty> { new() { Key = ApiScopePrefixes.MatchProperty, Value = ApiScopePrefixes.PrefixMatch } },
            });
            db.ApiScopes.Add(new ApiScope
            {
                Name = WidePrefix, DisplayName = "Wide", TenantId = Tenant, Enabled = true,
                Properties = new List<ApiScopeProperty>
                {
                    new() { Key = ApiScopePrefixes.MatchProperty, Value = ApiScopePrefixes.PrefixMatch },
                    new() { Key = ApiScopePrefixes.SuffixMaxLengthProperty, Value = WideMaxLength.ToString() },
                },
            });

            foreach (var (clientId, strict) in new[] { (ServiceClientId, false), (StrictClientId, true) })
            {
                var client = new Client
                {
                    ClientId = clientId, ClientName = clientId, TenantId = Tenant, Enabled = true,
                    RequireClientSecret = true, RequirePkce = false,
                };
                client.ClientSecrets.Add(new ClientSecret { Value = HashSecret(Secret), Type = "SharedSecret" });
                client.AllowedGrantTypes.Add(new ClientGrantType { GrantType = "client_credentials" });
                client.AllowedScopes.Add(new ClientScope { Scope = ApiScope });
                client.AllowedScopes.Add(new ClientScope { Scope = DevicePrefix });
                client.AllowedScopes.Add(new ClientScope { Scope = FilesPrefix });
                client.AllowedScopes.Add(new ClientScope { Scope = WidePrefix });
                if (strict)
                    client.Properties.Add(new ClientProperty { Key = ScopeValidationContext.StrictScopesProperty, Value = "true" });
                db.Clients.Add(client);
            }

            await db.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task The_device_scope_is_granted_as_requested_and_kept_on_refresh_but_cannot_be_swapped()
    {
        var asked = new[] { "openid", "offline_access", ApiScope, Device };
        var tokens = await _app.SignInAsync(_app.HttpFor(Tenant), UserEmail, asked);

        ScopesOf(tokens.GetProperty("access_token").GetString()!).Should().Contain(new[] { ApiScope, Device });

        // A refresh that names no scope carries the grant forward, device scope included.
        var refreshed = await TokensAsync(await _app.RefreshAsync(Tenant, tokens.GetProperty("refresh_token").GetString()!));
        refreshed.GetProperty("scope").GetString()!.Split(' ').Should().BeEquivalentTo(asked);
        ScopesOf(refreshed.GetProperty("access_token").GetString()!).Should().Contain(Device);

        // Naming it again is fine.
        var again = await TokensAsync(await _app.RefreshAsync(Tenant, refreshed.GetProperty("refresh_token").GetString()!, string.Join(' ', asked)));
        again.GetProperty("scope").GetString()!.Split(' ').Should().Contain(Device);

        // Another device id is beyond the original grant: invalid_scope, and the refresh token stays usable.
        var token = again.GetProperty("refresh_token").GetString()!;
        var swapped = await _app.RefreshAsync(Tenant, token, $"openid {ApiScope} {DevicePrefix}OTHERDEVICE");
        swapped.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await swapped.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString().Should().Be("invalid_scope");
        (await _app.RefreshAsync(Tenant, token)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_device_id_outside_the_configured_pattern_is_left_out_at_authorize()
    {
        var asked = new[] { "openid", ApiScope, DevicePrefix + "has/slash" };

        var tokens = await _app.SignInAsync(_app.HttpFor(Tenant), UserEmail, asked, granted: new[] { "openid", ApiScope });

        ScopesOf(tokens.GetProperty("access_token").GetString()!).Should().NotContain(s => s.StartsWith(DevicePrefix));
    }

    [Fact]
    public async Task Token_response_access_token_and_introspection_carry_the_full_scope()
    {
        var response = await ClientCredentialsAsync(ServiceClientId, $"{ApiScope} {Device}");
        var body = await TokensAsync(response);
        body.GetProperty("scope").GetString()!.Split(' ').Should().BeEquivalentTo(ApiScope, Device);

        var accessToken = body.GetProperty("access_token").GetString()!;
        ScopesOf(accessToken).Should().BeEquivalentTo(ApiScope, Device);

        var introspection = await _app.HttpFor(Tenant).PostAsync("/connect/introspect", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = accessToken,
            ["client_id"] = ServiceClientId,
            ["client_secret"] = Secret,
        }));
        introspection.StatusCode.Should().Be(HttpStatusCode.OK, await introspection.Content.ReadAsStringAsync());
        var result = await introspection.Content.ReadFromJsonAsync<JsonElement>();
        result.GetProperty("active").GetBoolean().Should().BeTrue();
        result.GetProperty("scope").GetString()!.Split(' ').Should().BeEquivalentTo(ApiScope, Device);
    }

    [Theory]
    [InlineData("https://files.example/folder/42")]
    [InlineData("team:7/reports")]
    [InlineData("!#$%&'()*+,;=?@[]^`{|}~")]
    public async Task By_default_a_suffix_may_hold_any_scope_token_characters(string suffix)
    {
        var body = await TokensAsync(await ClientCredentialsAsync(StrictClientId, FilesPrefix + suffix));

        body.GetProperty("scope").GetString().Should().Be(FilesPrefix + suffix);
        ScopesOf(body.GetProperty("access_token").GetString()!).Should().Equal(FilesPrefix + suffix);
    }

    [Theory]
    [InlineData("a\"b")]
    [InlineData("a\\b")]
    public async Task A_quote_or_backslash_can_never_be_part_of_a_suffix(string suffix)
    {
        var refused = await ClientCredentialsAsync(ServiceClientId, FilesPrefix + suffix);

        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString().Should().Be("invalid_scope");
    }

    [Fact]
    public async Task A_space_splits_the_value_so_the_rest_is_not_part_of_the_suffix()
    {
        var body = await TokensAsync(await ClientCredentialsAsync(ServiceClientId, $"{FilesPrefix}a b"));

        // "files:a" is granted; "b" is an unknown scope of its own and is left out.
        body.GetProperty("scope").GetString().Should().Be(FilesPrefix + "a");
    }

    [Fact]
    public async Task A_suffix_longer_than_the_default_maximum_is_refused()
    {
        var max = ApiScopePrefixes.DefaultSuffixMaxLength;
        (await TokensAsync(await ClientCredentialsAsync(StrictClientId, FilesPrefix + new string('a', max))))
            .GetProperty("scope").GetString().Should().Be(FilesPrefix + new string('a', max));

        var refused = await ClientCredentialsAsync(StrictClientId, FilesPrefix + new string('a', max + 1));
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task With_no_scope_requested_the_prefix_name_itself_is_not_issued()
    {
        var body = await TokensAsync(await ClientCredentialsAsync(ServiceClientId, scope: null));

        body.GetProperty("scope").GetString()!.Split(' ').Should().Equal(ApiScope);
    }

    [Fact]
    public async Task A_strict_client_is_refused_an_invalid_device_id()
    {
        var refused = await ClientCredentialsAsync(StrictClientId, $"{ApiScope} {DevicePrefix}bad/id");
        refused.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await refused.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString().Should().Be("invalid_scope");

        var granted = await TokensAsync(await ClientCredentialsAsync(StrictClientId, $"{ApiScope} {Device}"));
        granted.GetProperty("scope").GetString()!.Split(' ').Should().BeEquivalentTo(ApiScope, Device);
    }

    [Fact]
    public async Task Discovery_advertises_the_prefix_scope_with_a_wildcard()
    {
        var discovery = await _app.HttpFor(Tenant).GetFromJsonAsync<JsonElement>("/.well-known/openid-configuration");
        var scopes = discovery.GetProperty("scopes_supported").EnumerateArray().Select(e => e.GetString()).ToList();

        scopes.Should().Contain(ApiScope).And.Contain(DevicePrefix + "*").And.NotContain(DevicePrefix);
    }

    [Fact]
    public async Task Dynamic_registration_accepts_a_value_of_an_allowed_prefix_scope_as_requested()
    {
        await WithDbContextAsync(async db =>
        {
            var tenant = await db.Tenants.FindAsync(Tenant);
            tenant!.ProtocolConfiguration ??= new TenantProtocolConfiguration { TenantId = tenant.Id };
            tenant.ProtocolConfiguration.EnableDynamicClientRegistration = true;
            tenant.ProtocolConfiguration.AllowOpenDynamicRegistration = true;
            tenant.ProtocolConfiguration.DynamicRegistrationAllowedScopesJson =
                JsonSerializer.Serialize(new[] { "openid", "offline_access", ApiScope, DevicePrefix });
            tenant.ProtocolConfiguration.DynamicRegistrationAllowedGrantTypesJson = "[\"authorization_code\", \"refresh_token\"]";
            await db.SaveChangesAsync();
        });

        var response = await _app.HttpFor(Tenant).PostAsJsonAsync("/connect/register", new Dictionary<string, object>
        {
            ["client_name"] = "Matrix client",
            ["client_uri"] = "https://matrix-client.example",
            ["redirect_uris"] = new[] { "https://matrix-client.example/callback" },
            ["grant_types"] = new[] { "authorization_code", "refresh_token" },
            ["token_endpoint_auth_method"] = "none",
            ["scope"] = $"openid {ApiScope} {Device}",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        // Registered as asked: the client gets that one value, not every value of the prefix scope.
        body.GetProperty("scope").GetString()!.Split(' ').Should().BeEquivalentTo("openid", ApiScope, Device);

        // A value the prefix scope's pattern rejects isn't covered by it.
        var invalid = await _app.HttpFor(Tenant).PostAsJsonAsync("/connect/register", new Dictionary<string, object>
        {
            ["client_name"] = "Matrix client",
            ["client_uri"] = "https://matrix-client.example",
            ["redirect_uris"] = new[] { "https://matrix-client.example/callback" },
            ["grant_types"] = new[] { "authorization_code" },
            ["token_endpoint_auth_method"] = "none",
            ["scope"] = $"openid {DevicePrefix}has/slash",
        });
        invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_admin_api_reads_and_writes_the_suffix_rule()
    {
        var admin = await SuperAdminClientAsync();
        var name = $"adm-{Guid.NewGuid():N}:";

        var created = await admin.PostAsJsonAsync("/api/admin/api-scopes", new
        {
            name, isPrefix = true, suffixPattern = "[0-9]+", suffixMaxLength = 6,
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var dto = await created.Content.ReadFromJsonAsync<JsonElement>();
        var id = dto.GetProperty("id").GetInt32();
        dto.GetProperty("isPrefix").GetBoolean().Should().BeTrue();
        dto.GetProperty("suffixPattern").GetString().Should().Be("[0-9]+");
        dto.GetProperty("suffixMaxLength").GetInt32().Should().Be(6);
        (await PropertiesOfAsync(id)).Should().BeEquivalentTo(new Dictionary<string, string>
        {
            [ApiScopePrefixes.MatchProperty] = ApiScopePrefixes.PrefixMatch,
            [ApiScopePrefixes.SuffixPatternProperty] = "[0-9]+",
            [ApiScopePrefixes.SuffixMaxLengthProperty] = "6",
        });

        var read = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/api-scopes/{id}");
        read.GetProperty("suffixPattern").GetString().Should().Be("[0-9]+");
        read.GetProperty("suffixMaxLength").GetInt32().Should().Be(6);

        // Changing one field and clearing the other removes the cleared property.
        var updated = await admin.PutAsJsonAsync($"/api/admin/api-scopes/{id}", Update(read, isPrefix: true, pattern: "", maxLength: 12));
        updated.StatusCode.Should().Be(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());
        var afterUpdate = await updated.Content.ReadFromJsonAsync<JsonElement>();
        afterUpdate.GetProperty("suffixPattern").ValueKind.Should().Be(JsonValueKind.Null);
        afterUpdate.GetProperty("suffixMaxLength").GetInt32().Should().Be(12);
        (await PropertiesOfAsync(id)).Keys.Should().BeEquivalentTo(ApiScopePrefixes.MatchProperty, ApiScopePrefixes.SuffixMaxLengthProperty);

        // An invalid regex or length is refused on save and leaves the scope as it was.
        var badPattern = await admin.PutAsJsonAsync($"/api/admin/api-scopes/{id}", Update(read, isPrefix: true, pattern: "[0-9", maxLength: null));
        badPattern.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await badPattern.Content.ReadAsStringAsync()).Should().Contain("not a valid regular expression");
        var badLength = await admin.PutAsJsonAsync($"/api/admin/api-scopes/{id}", Update(read, isPrefix: true, pattern: null, maxLength: 0));
        badLength.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await PropertiesOfAsync(id))[ApiScopePrefixes.SuffixMaxLengthProperty].Should().Be("12");

        // Turning the prefix off removes the suffix rule with it.
        (await admin.PutAsJsonAsync($"/api/admin/api-scopes/{id}", Update(read, isPrefix: false, pattern: "[a-z]+", maxLength: 3)))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        (await PropertiesOfAsync(id)).Should().BeEmpty();

        var createBad = await admin.PostAsJsonAsync("/api/admin/api-scopes", new
        {
            name = $"adm-{Guid.NewGuid():N}:", isPrefix = true, suffixPattern = "(unclosed",
        });
        createBad.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task The_tenant_default_suffix_length_is_a_protocol_setting_the_validator_uses()
    {
        var admin = await SuperAdminClientAsync();

        var settings = JsonNode.Parse(await admin.GetStringAsync("/api/admin/settings"))!;
        settings["protocolSettings"]!["prefixScopeSuffixMaxLength"]!.GetValue<int>().Should().Be(ApiScopePrefixes.DefaultSuffixMaxLength);

        try
        {
            // Out of range: refused by both endpoints that write it.
            foreach (var bad in new[] { 0, 2049 })
            {
                (await PutSettingsDefaultAsync(admin, settings, bad)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
                (await admin.PutAsJsonAsync($"/api/admin/tenants/{Tenant}/protocol-configuration", new { prefixScopeSuffixMaxLength = bad }))
                    .StatusCode.Should().Be(HttpStatusCode.BadRequest);
            }

            // Round trip through the Settings page's API; the tenant endpoint reads the same record.
            var saved = await PutSettingsDefaultAsync(admin, settings, 20);
            saved.StatusCode.Should().Be(HttpStatusCode.OK, await saved.Content.ReadAsStringAsync());
            JsonNode.Parse(await admin.GetStringAsync("/api/admin/settings"))!["protocolSettings"]!["prefixScopeSuffixMaxLength"]!
                .GetValue<int>().Should().Be(20);
            (await admin.GetFromJsonAsync<JsonElement>($"/api/admin/tenants/{Tenant}/protocol-configuration"))
                .GetProperty("prefixScopeSuffixMaxLength").GetInt32().Should().Be(20);

            // The validator takes the tenant's default for a scope without its own maximum...
            (await TokensAsync(await ClientCredentialsAsync(StrictClientId, FilesPrefix + new string('a', 20))))
                .GetProperty("scope").GetString().Should().Be(FilesPrefix + new string('a', 20));
            (await ClientCredentialsAsync(StrictClientId, FilesPrefix + new string('a', 21)))
                .StatusCode.Should().Be(HttpStatusCode.BadRequest);

            // ...while a scope's own maximum still wins.
            (await TokensAsync(await ClientCredentialsAsync(StrictClientId, WidePrefix + new string('a', WideMaxLength))))
                .GetProperty("scope").GetString().Should().Be(WidePrefix + new string('a', WideMaxLength));
            (await ClientCredentialsAsync(StrictClientId, WidePrefix + new string('a', WideMaxLength + 1)))
                .StatusCode.Should().Be(HttpStatusCode.BadRequest);

            // The tenant endpoint writes it too.
            (await admin.PutAsJsonAsync($"/api/admin/tenants/{Tenant}/protocol-configuration", new { prefixScopeSuffixMaxLength = 1000 }))
                .StatusCode.Should().Be(HttpStatusCode.OK);
            (await TokensAsync(await ClientCredentialsAsync(StrictClientId, FilesPrefix + new string('a', 1000))))
                .GetProperty("scope").GetString().Should().Be(FilesPrefix + new string('a', 1000));
        }
        finally
        {
            (await PutSettingsDefaultAsync(admin, settings, ApiScopePrefixes.DefaultSuffixMaxLength))
                .StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // Back at the built-in default (stored as unset).
        await WithDbContextAsync(async db =>
        {
            var tenant = await db.Tenants!.IgnoreQueryFilters().AsNoTracking().FirstAsync(t => t.Id == Tenant);
            tenant.ProtocolConfiguration!.PrefixScopeSuffixMaxLength.Should().BeNull();
        });
        (await ClientCredentialsAsync(StrictClientId, FilesPrefix + new string('a', ApiScopePrefixes.DefaultSuffixMaxLength + 1)))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static Task<HttpResponseMessage> PutSettingsDefaultAsync(HttpClient admin, JsonNode settings, int value)
    {
        var body = settings.DeepClone();
        body["protocolSettings"]!["prefixScopeSuffixMaxLength"] = value;
        return admin.PutAsync("/api/admin/settings", new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"));
    }

    private static Dictionary<string, object?> Update(JsonElement dto, bool isPrefix, string? pattern, int? maxLength) => new()
    {
        ["id"] = dto.GetProperty("id").GetInt32(),
        ["name"] = dto.GetProperty("name").GetString(),
        ["displayName"] = dto.GetProperty("displayName").GetString(),
        ["enabled"] = true,
        ["showInDiscoveryDocument"] = true,
        ["userClaims"] = Array.Empty<string>(),
        ["isPrefix"] = isPrefix,
        ["suffixPattern"] = pattern,
        ["suffixMaxLength"] = maxLength,
    };

    private async Task<Dictionary<string, string>> PropertiesOfAsync(int id)
    {
        var properties = new Dictionary<string, string>();
        await WithDbContextAsync(async db =>
        {
            var scope = await db.ApiScopes.IgnoreQueryFilters().Include(s => s.Properties).AsNoTracking().SingleAsync(s => s.Id == id);
            foreach (var p in scope.Properties)
                properties[p.Key] = p.Value;
        });
        return properties;
    }

    /// <summary>An admin API client for the super admin, signed in to admin-portal and working in <see cref="Tenant"/>.</summary>
    private async Task<HttpClient> SuperAdminClientAsync()
    {
        string userId = null!, clientId = null!, redirectUri = null!;
        await WithDbContextAsync(async db =>
        {
            userId = (await db.Users.IgnoreQueryFilters().FirstAsync(u => u.Email == "superadmin@oluso.local")).Id;
            var portal = await db.Clients.IgnoreQueryFilters().Include(c => c.RedirectUris).FirstAsync(c => c.ClientId == "admin-portal");
            clientId = portal.ClientId;
            redirectUri = portal.RedirectUris.First().RedirectUri;
        });

        var verifier = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var code = $"ps_{Guid.NewGuid():N}";
        using (var scope = Factory.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IAuthorizationCodeStore>().StoreAsync(new AuthorizationCode
            {
                Code = code, ClientId = clientId, SubjectId = userId, RedirectUri = redirectUri,
                Scopes = new List<string> { "openid", "profile" },
                CodeChallenge = challenge, CodeChallengeMethod = "S256", TenantId = "_platform",
                CreationTime = DateTime.UtcNow, Expiration = DateTime.UtcNow.AddMinutes(5),
            });
        }

        var tokens = await TokensAsync(await Client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = redirectUri,
            ["client_id"] = clientId, ["code_verifier"] = verifier,
        })));

        var http = Factory.CreateClient();
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GetProperty("access_token").GetString());
        http.DefaultRequestHeaders.Add("X-Tenant-Id", Tenant);
        return http;
    }

    private Task<HttpResponseMessage> ClientCredentialsAsync(string clientId, string? scope)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
            ["client_secret"] = Secret,
        };
        if (scope != null)
            form["scope"] = scope;
        return _app.HttpFor(Tenant).PostAsync("/connect/token", new FormUrlEncodedContent(form));
    }

    private static async Task<JsonElement> TokensAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private static List<string> ScopesOf(string accessToken) =>
        new JsonWebToken(accessToken).Claims.Where(c => c.Type == "scope").SelectMany(c => c.Value.Split(' ')).ToList();

    private static string HashSecret(string secret) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret)));
}

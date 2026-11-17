using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Protocols.Models;
using Oluso.Integration.Tests.Fixtures;
using Xunit;

namespace Oluso.Integration.Tests;

/// <summary>
/// A client's public keys: added by an administrator (a JWK or a certificate, public material only) and used to
/// authenticate with private_key_jwt (RFC 7523); and the dynamic registration side (RFC 7591/7592), where a jwks_uri is
/// kept (https on a public host), the registration access token does not expire and is replaced by every update, and an
/// update replaces only the keys the registration holds.
/// </summary>
public class ClientPublicKeyTests : IntegrationTestBase
{
    private const string RegisterEndpoint = "/connect/register";
    private const string KeyClientId = "private-key-jwt-client";

    public ClientPublicKeyTests(OlusoWebApplicationFactory factory) : base(factory)
    {
        Client.DefaultRequestHeaders.Add("X-Tenant-Id", "default");
    }

    public override async Task InitializeAsync()
    {
        await WithDbContextAsync(async db =>
        {
            var tenant = (await db.Tenants.FindAsync("default"))!;
            tenant.ProtocolConfiguration ??= new TenantProtocolConfiguration { TenantId = tenant.Id };
            tenant.ProtocolConfiguration.EnableDynamicClientRegistration = true;
            tenant.ProtocolConfiguration.AllowOpenDynamicRegistration = true;
            tenant.ProtocolConfiguration.DynamicRegistrationAllowedScopesJson = "[\"openid\"]";
            tenant.ProtocolConfiguration.DynamicRegistrationAllowedGrantTypesJson = "[\"authorization_code\", \"client_credentials\"]";

            if (!await db.Clients.IgnoreQueryFilters().AnyAsync(c => c.ClientId == KeyClientId))
            {
                // The tenant the admin API and the token requests below work in.
                const string tenantId = "default";
                var client = new Client
                {
                    ClientId = KeyClientId,
                    ClientName = "Signs its client assertions",
                    TenantId = tenantId,
                    Enabled = true,
                    RequireClientSecret = true,
                    TokenEndpointAuthMethod = "private_key_jwt",
                };
                client.AllowedGrantTypes.Add(new ClientGrantType { GrantType = "client_credentials", TenantId = tenantId });
                client.AllowedScopes.Add(new ClientScope { Scope = "openid", TenantId = tenantId });
                db.Clients.Add(client);
            }
            await db.SaveChangesAsync();
        });
    }

    [Fact]
    public async Task A_clients_only_public_key_can_be_removed_but_not_its_last_shared_secret()
    {
        using var rsa = RSA.Create(2048);
        var admin = await SuperAdminClient.CreateAsync(Factory, "default");

        // A private_key_jwt client with one key: removing it is allowed (rotation adds the new key first).
        var keyClientId = $"one-key-{Guid.NewGuid():N}"[..20];
        var secretClientId = $"one-secret-{Guid.NewGuid():N}"[..22];
        await WithDbContextAsync(async db =>
        {
            db.Clients.Add(new Client
            {
                ClientId = keyClientId, TenantId = "default", Enabled = true, RequireClientSecret = true,
                TokenEndpointAuthMethod = "private_key_jwt",
                ClientSecrets = new List<ClientSecret> { new() { Type = ClientPublicKeys.JsonWebKeyType, Value = PublicJwk(rsa, "only"), TenantId = "default" } },
            });
            db.Clients.Add(new Client
            {
                ClientId = secretClientId, TenantId = "default", Enabled = true, RequireClientSecret = true,
                TokenEndpointAuthMethod = "client_secret_basic",
                ClientSecrets = new List<ClientSecret> { new() { Value = "hashed", TenantId = "default" } },
            });
            await db.SaveChangesAsync();
        });

        var keyClient = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/clients/{await ClientRowIdAsync(keyClientId)}");
        var keyId = keyClient.GetProperty("secrets")[0].GetProperty("id").GetInt32();
        var removed = await admin.DeleteAsync($"/api/admin/clients/{keyClient.GetProperty("id").GetInt32()}/secrets/{keyId}");
        removed.IsSuccessStatusCode.Should().BeTrue(await removed.Content.ReadAsStringAsync());

        // A client that signs in with a shared secret keeps its last one.
        var secretClient = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/clients/{await ClientRowIdAsync(secretClientId)}");
        var secretId = secretClient.GetProperty("secrets")[0].GetProperty("id").GetInt32();
        (await admin.DeleteAsync($"/api/admin/clients/{secretClient.GetProperty("id").GetInt32()}/secrets/{secretId}"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_key_an_administrator_adds_authenticates_the_client_with_private_key_jwt()
    {
        using var rsa = RSA.Create(2048);
        var admin = await SuperAdminClient.CreateAsync(Factory, "default");
        var id = await ClientRowIdAsync(KeyClientId);

        var added = await admin.PostAsJsonAsync($"/api/admin/clients/{id}/keys", new { type = "jwk", value = PublicJwk(rsa, "pk-1") });
        added.StatusCode.Should().Be(HttpStatusCode.OK, await added.Content.ReadAsStringAsync());
        var dto = await added.Content.ReadFromJsonAsync<JsonElement>();
        dto.GetProperty("type").GetString().Should().Be(ClientPublicKeys.JsonWebKeyType);
        dto.GetProperty("keyId").GetString().Should().Be("pk-1");

        (await TokenWithAssertionAsync(rsa, "pk-1")).StatusCode.Should().Be(HttpStatusCode.OK);

        // A key the client was never given does not.
        using var other = RSA.Create(2048);
        (await TokenWithAssertionAsync(other, "pk-1")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Only_public_key_material_can_be_added()
    {
        using var rsa = RSA.Create(2048);
        var p = rsa.ExportParameters(true);
        var admin = await SuperAdminClient.CreateAsync(Factory, "default");
        var id = await ClientRowIdAsync(KeyClientId);

        var privateJwk = JsonSerializer.Serialize(new
        {
            kty = "RSA", kid = "private", n = Base64UrlEncoder.Encode(p.Modulus), e = Base64UrlEncoder.Encode(p.Exponent), d = Base64UrlEncoder.Encode(p.D)
        });
        var symmetric = JsonSerializer.Serialize(new { kty = "oct", k = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32)) });
        var certificate = SelfSigned(rsa, DateTimeOffset.UtcNow.AddDays(30));
        var withPrivateKey = certificate.ExportCertificatePem() + "\n" + rsa.ExportPkcs8PrivateKeyPem();
        var expired = SelfSigned(rsa, DateTimeOffset.UtcNow.AddDays(-1));

        foreach (var (type, value) in new[]
                 {
                     ("jwk", privateJwk), ("jwk", symmetric), ("jwk", "not json"),
                     ("certificate", withPrivateKey), ("certificate", expired.ExportCertificatePem()), ("certificate", "not a certificate"),
                     ("password", "whatever"),
                 })
        {
            var response = await admin.PostAsJsonAsync($"/api/admin/clients/{id}/keys", new { type, value });
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"{type}: {value[..Math.Min(20, value.Length)]}");
        }

        var good = await admin.PostAsJsonAsync($"/api/admin/clients/{id}/keys", new { type = "certificate", value = certificate.ExportCertificatePem() });
        good.StatusCode.Should().Be(HttpStatusCode.OK, await good.Content.ReadAsStringAsync());
        var dto = await good.Content.ReadFromJsonAsync<JsonElement>();
        dto.GetProperty("type").GetString().Should().Be(ClientPublicKeys.CertificateType);
        dto.GetProperty("keyId").GetString().Should().Be(Convert.ToHexString(SHA256.HashData(certificate.RawData)));
        DateTime.Parse(dto.GetProperty("expiration").GetString()!).ToUniversalTime()
            .Should().BeCloseTo(certificate.NotAfter.ToUniversalTime(), TimeSpan.FromSeconds(1));

        // The same certificate twice is refused.
        (await admin.PostAsJsonAsync($"/api/admin/clients/{id}/keys", new { type = "certificate", value = certificate.ExportCertificatePem() }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Registration_keeps_a_jwks_uri_and_an_update_replaces_it()
    {
        var response = await Client.PostAsJsonAsync(RegisterEndpoint, new
        {
            redirect_uris = new[] { "https://example.com/callback" },
            token_endpoint_auth_method = "private_key_jwt",
            jwks_uri = "https://keys.example.com/jwks.json",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        var registered = await response.Content.ReadFromJsonAsync<JsonElement>();
        registered.GetProperty("jwks_uri").GetString().Should().Be("https://keys.example.com/jwks.json");
        var clientId = registered.GetProperty("client_id").GetString()!;
        (await JwksUriAsync(clientId)).Should().Be("https://keys.example.com/jwks.json");

        // An update with keys in jwks instead: the URL goes, as the update replaces the metadata.
        var updated = await UpdateAsync(clientId, registered.GetProperty("registration_access_token").GetString()!,
            Jwks(RSA.Create(2048), "inline"));
        updated.StatusCode.Should().Be(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());
        (await updated.Content.ReadFromJsonAsync<JsonElement>()).TryGetProperty("jwks_uri", out _).Should().BeFalse();
        (await JwksUriAsync(clientId)).Should().BeNull();
    }

    [Theory]
    [InlineData("http://keys.example.com/jwks.json")]
    [InlineData("https://10.1.2.3/jwks.json")]
    [InlineData("jwks.json")]
    public async Task Registration_refuses_a_jwks_uri_that_is_not_https_on_a_public_host(string jwksUri)
    {
        var response = await Client.PostAsJsonAsync(RegisterEndpoint, new
        {
            redirect_uris = new[] { "https://example.com/callback" },
            token_endpoint_auth_method = "private_key_jwt",
            jwks_uri = jwksUri,
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("error").GetString().Should().Be("invalid_client_metadata");
    }

    [Fact]
    public async Task Registration_refuses_jwks_and_jwks_uri_together()
    {
        var response = await Client.PostAsJsonAsync(RegisterEndpoint, new
        {
            redirect_uris = new[] { "https://example.com/callback" },
            token_endpoint_auth_method = "private_key_jwt",
            jwks_uri = "https://keys.example.com/jwks.json",
            jwks = Jwks(RSA.Create(2048), "inline"),
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task An_administrator_sets_and_removes_a_clients_jwks_uri()
    {
        var admin = await SuperAdminClient.CreateAsync(Factory, "default");
        var id = await ClientRowIdAsync(KeyClientId);

        (await admin.PutAsJsonAsync($"/api/admin/clients/{id}", new { jwksUri = "http://keys.example.com/jwks.json" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var set = await admin.PutAsJsonAsync($"/api/admin/clients/{id}", new { jwksUri = "https://keys.example.com/jwks.json" });
        set.StatusCode.Should().Be(HttpStatusCode.OK, await set.Content.ReadAsStringAsync());
        (await set.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("jwksUri").GetString().Should().Be("https://keys.example.com/jwks.json");

        (await admin.PutAsJsonAsync($"/api/admin/clients/{id}", new { jwksUri = "" })).StatusCode.Should().Be(HttpStatusCode.OK);
        (await JwksUriAsync(KeyClientId)).Should().BeNull();
    }

    [Fact]
    public async Task An_update_replaces_the_registration_access_token_which_does_not_expire()
    {
        var registered = await RegisterAsync(Jwks(RSA.Create(2048), "reg-1"));
        var clientId = registered.GetProperty("client_id").GetString()!;
        var firstToken = registered.GetProperty("registration_access_token").GetString()!;
        (await RegistrationTokenExpiriesAsync(clientId)).Should().ContainSingle().Which.Should().BeNull();

        var updated = await UpdateAsync(clientId, firstToken, Jwks(RSA.Create(2048), "reg-2"));
        updated.StatusCode.Should().Be(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());
        var rotatedToken = (await updated.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("registration_access_token").GetString();
        rotatedToken.Should().NotBeNullOrEmpty().And.NotBe(firstToken);

        (await ReadAsync(clientId, firstToken)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await ReadAsync(clientId, rotatedToken!)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await RegistrationTokenExpiriesAsync(clientId)).Should().ContainSingle().Which.Should().BeNull();
    }

    [Fact]
    public async Task An_update_replaces_the_registration_keys_and_keeps_those_an_administrator_added()
    {
        using var adminKey = RSA.Create(2048);
        var registered = await RegisterAsync(Jwks(RSA.Create(2048), "reg-1"));
        var clientId = registered.GetProperty("client_id").GetString()!;
        var token = registered.GetProperty("registration_access_token").GetString()!;

        var admin = await SuperAdminClient.CreateAsync(Factory, "default");
        (await admin.PostAsJsonAsync($"/api/admin/clients/{await ClientRowIdAsync(clientId)}/keys",
            new { type = "jwk", value = PublicJwk(adminKey, "admin-1") })).StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await UpdateAsync(clientId, token, Jwks(RSA.Create(2048), "reg-2"));
        updated.StatusCode.Should().Be(HttpStatusCode.OK, await updated.Content.ReadAsStringAsync());
        var body = await updated.Content.ReadFromJsonAsync<JsonElement>();

        // The registration shows, and replaced, only its own keys; the administrator's key is still on the client.
        body.GetProperty("jwks").GetProperty("keys").EnumerateArray().Select(k => k.GetProperty("kid").GetString())
            .Should().Equal("reg-2");
        (await KeyIdsAsync(clientId)).Should().BeEquivalentTo("reg-2", "admin-1");
    }

    private async Task<JsonElement> RegisterAsync(object jwks)
    {
        var response = await Client.PostAsJsonAsync(RegisterEndpoint, new
        {
            redirect_uris = new[] { "https://example.com/callback" },
            token_endpoint_auth_method = "private_key_jwt",
            jwks,
        });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }

    private Task<HttpResponseMessage> UpdateAsync(string clientId, string token, object jwks)
    {
        var http = ManagementClient(token);
        return http.PutAsJsonAsync($"{RegisterEndpoint}/{clientId}", new
        {
            client_id = clientId,
            redirect_uris = new[] { "https://example.com/callback" },
            token_endpoint_auth_method = "private_key_jwt",
            jwks,
        });
    }

    private Task<HttpResponseMessage> ReadAsync(string clientId, string token) =>
        ManagementClient(token).GetAsync($"{RegisterEndpoint}/{clientId}");

    private HttpClient ManagementClient(string token)
    {
        var http = Factory.CreateClient();
        http.DefaultRequestHeaders.Add("X-Tenant-Id", "default");
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return http;
    }

    private async Task<HttpResponseMessage> TokenWithAssertionAsync(RSA key, string kid)
    {
        var discovery = await Client.GetFromJsonAsync<JsonElement>("/.well-known/openid-configuration");
        var tokenEndpoint = discovery.GetProperty("token_endpoint").GetString()!;
        var now = DateTime.UtcNow;
        var assertion = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: KeyClientId,
            audience: tokenEndpoint,
            claims: new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, KeyClientId),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
            },
            notBefore: now,
            expires: now.AddMinutes(2),
            signingCredentials: new SigningCredentials(new RsaSecurityKey(key) { KeyId = kid }, SecurityAlgorithms.RsaSha256)));

        return await Client.PostAsync("/connect/token", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["scope"] = "openid",
            ["client_assertion_type"] = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer",
            ["client_assertion"] = assertion,
        }));
    }

    private static string PublicJwk(RSA rsa, string kid)
    {
        var p = rsa.ExportParameters(false);
        return JsonSerializer.Serialize(new { kty = "RSA", kid, use = "sig", n = Base64UrlEncoder.Encode(p.Modulus), e = Base64UrlEncoder.Encode(p.Exponent) });
    }

    private static object Jwks(RSA rsa, string kid) =>
        new { keys = new[] { JsonSerializer.Deserialize<JsonElement>(PublicJwk(rsa, kid)) } };

    private static X509Certificate2 SelfSigned(RSA rsa, DateTimeOffset notAfter)
    {
        var request = new CertificateRequest("CN=client.example", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var certificate = request.CreateSelfSigned(notAfter.AddDays(-60), notAfter);
        return X509CertificateLoader.LoadCertificate(certificate.Export(X509ContentType.Cert));
    }

    private async Task<string?> JwksUriAsync(string clientId)
    {
        string? uri = null;
        await WithDbContextAsync(async db =>
            uri = await db.Clients.IgnoreQueryFilters().AsNoTracking().Where(c => c.ClientId == clientId).Select(c => c.JwksUri).FirstAsync());
        return uri;
    }

    private async Task<int> ClientRowIdAsync(string clientId)
    {
        var id = 0;
        await WithDbContextAsync(async db =>
            id = await db.Clients.IgnoreQueryFilters().Where(c => c.ClientId == clientId).Select(c => c.Id).FirstAsync());
        return id;
    }

    private async Task<List<DateTime?>> RegistrationTokenExpiriesAsync(string clientId)
    {
        var expiries = new List<DateTime?>();
        await WithDbContextAsync(async db => expiries = await db.PersistedGrants.IgnoreQueryFilters()
            .Where(g => g.ClientId == clientId && g.Type == OidcConstants.PersistedGrantTypes.RegistrationAccessToken)
            .Select(g => g.Expiration).ToListAsync());
        return expiries;
    }

    private async Task<List<string?>> KeyIdsAsync(string clientId)
    {
        var values = new List<string>();
        await WithDbContextAsync(async db => values = await db.Clients.IgnoreQueryFilters()
            .Where(c => c.ClientId == clientId)
            .SelectMany(c => c.ClientSecrets)
            .Where(s => s.Type == ClientPublicKeys.JsonWebKeyType)
            .Select(s => s.Value).ToListAsync());
        return values.Select(v => JsonDocument.Parse(v).RootElement.GetProperty("kid").GetString()).ToList();
    }
}

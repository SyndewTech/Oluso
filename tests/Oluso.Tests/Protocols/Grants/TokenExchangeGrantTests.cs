using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Oluso.Core.Authentication;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Services;
using Oluso.Protocols.Grants;
using Oluso.Protocols.Services;
using Oluso.Tests.Protocols.DPoP;
using Xunit;

namespace Oluso.Tests.Protocols.Grants;

/// <summary>
/// Token exchange (RFC 8693): DPoP-bound subjects, the audience policy, and one-time tickets
/// (<see cref="OidcConstants.TokenTypes.Ticket"/>) — issued to the app for an audience client and
/// redeemed once by that client.
/// </summary>
public class TokenExchangeGrantTests : IDisposable
{
    private const string Issuer = "https://acme.example.com";
    private const string Tenant = "tenant-acme";

    private readonly RSA _rsa = RSA.Create(2048);
    private readonly SigningCredentials _signing;
    private readonly DPoPTestKey _device = new();
    private readonly DPoPTestKey _attacker = new();
    private readonly InMemoryActionTokens _actionTokens = new();
    private readonly List<Client> _clients = new()
    {
        new Client { ClientId = "native-app", TenantId = Tenant, Enabled = true },
        new Client { ClientId = "suite", TenantId = Tenant, Enabled = true },
        new Client { ClientId = "partner", TenantId = Tenant, Enabled = true },
        new Client { ClientId = "disabled-app", TenantId = Tenant, Enabled = false },
        new Client { ClientId = "other-tenant-app", TenantId = "tenant-other", Enabled = true },
    };
    private readonly Mock<IPersistedGrantStore> _grants = new();
    private readonly TokenExchangeGrantHandler _handler;
    private readonly TokenService _tokens;

    public TokenExchangeGrantTests()
    {
        _signing = new SigningCredentials(new RsaSecurityKey(_rsa) { KeyId = "k1" }, SecurityAlgorithms.RsaSha256);

        var keys = new Mock<ISigningCredentialStore>();
        keys.Setup(k => k.GetValidationKeysAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new SecurityKeyInfo { Key = _signing.Key, SigningAlgorithm = SecurityAlgorithms.RsaSha256 } });
        keys.Setup(k => k.GetSigningCredentialsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(_signing);

        var issuer = new Mock<IIssuerResolver>();
        issuer.Setup(i => i.GetIssuerAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Issuer);

        var clients = new Mock<IClientStore>();
        clients.Setup(c => c.FindClientByIdAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) => _clients.FirstOrDefault(c => c.ClientId == id));

        var profiles = new Mock<IProfileService>();
        profiles.Setup(p => p.IsActiveAsync(It.IsAny<IsActiveContext>())).Returns(Task.CompletedTask);
        profiles.Setup(p => p.GetProfileDataAsync(It.IsAny<ProfileDataRequestContext>())).Returns(Task.CompletedTask);

        _handler = new TokenExchangeGrantHandler(keys.Object, _grants.Object, issuer.Object,
            ClientUserAccessKit.For(profiles.Object), NullLogger<TokenExchangeGrantHandler>.Instance, clients.Object, profiles.Object,
            new DefaultActionTokenService(_actionTokens), new ConfigurationBuilder().Build());

        var settings = new Mock<ITenantSettingsProvider>();
        settings.Setup(s => s.GetTokenSettingsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new TenantTokenSettings());
        settings.Setup(s => s.GetProtocolSettingsAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new TenantProtocolSettings());
        var claimProviders = new Mock<IClaimsProviderRegistry>();
        claimProviders.Setup(c => c.GetAllClaimsAsync(It.IsAny<ClaimsProviderContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, object>());
        var resources = new Mock<IResourceStore>();
        _tokens = new TokenService(keys.Object, _grants.Object, settings.Object, claimProviders.Object, issuer.Object,
            new IdentityScopeClaimResolver(resources.Object), new AudienceResolver(resources.Object, settings.Object),
            Mock.Of<ITenantContext>(), Mock.Of<ITenantStore>(), NullLogger<TokenService>.Instance);
    }

    public void Dispose()
    {
        _rsa.Dispose();
        _device.Dispose();
        _attacker.Dispose();
    }

    private string AccessToken(string clientId = "native-app", string? jkt = "device", params string[] scopes)
    {
        if (scopes.Length == 0) scopes = new[] { "openid", "offline_access", "oluso.approvals.act", "suite.pulse" };
        var claims = new List<Claim>
        {
            new("sub", "user-1"), new("client_id", clientId), new("azp", clientId), new("tenant_id", Tenant), new("sid", "session-1"),
        };
        claims.AddRange(scopes.Select(s => new Claim("scope", s)));
        var jwt = new JwtSecurityToken(Issuer, clientId, claims, DateTime.UtcNow, DateTime.UtcNow.AddMinutes(10), _signing);
        var bound = jkt == "device" ? _device.Jkt : jkt;
        if (bound != null)
            jwt.Payload["cnf"] = new Dictionary<string, object> { ["jkt"] = bound };
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private static ValidatedClient NativeApp(string audiences = "suite disabled-app other-tenant-app") => new()
    {
        ClientId = "native-app",
        TenantId = Tenant,
        AuthenticationMethod = ClientAuthenticationMethod.None,
        AccessTokenLifetime = 300,
        IdentityTokenLifetime = 300,
        AllowedScopes = new List<string> { "openid", "offline_access", "oluso.approvals.act", "suite.pulse" },
        Properties = new Dictionary<string, string> { [OidcConstants.TokenExchangeClientProperties.Audiences] = audiences },
    };

    private static ValidatedClient Suite(string clientId = "suite", ClientAuthenticationMethod method = ClientAuthenticationMethod.ClientSecretBasic) => new()
    {
        ClientId = clientId,
        TenantId = Tenant,
        AuthenticationMethod = method,
        AccessTokenLifetime = 300,
        IdentityTokenLifetime = 300,
        AllowedScopes = new List<string> { "openid", "suite.pulse" },
    };

    private TokenRequest Exchange(string subject, string? dpopJkt, string? audience = null, string? requestedType = null,
        ValidatedClient? client = null, params string[] scopes)
    {
        var request = new TokenRequest
        {
            GrantType = OidcConstants.GrantTypes.TokenExchange,
            Client = client ?? NativeApp(),
            SubjectToken = subject,
            SubjectTokenType = OidcConstants.TokenTypes.AccessToken,
            RequestedTokenType = requestedType,
            DPoPKeyThumbprint = dpopJkt,
            RequestedScopes = scopes.ToList(),
        };
        if (audience != null) request.Raw["audience"] = audience;
        return request;
    }

    private TokenRequest TicketRequest(string? dpopJkt = "device", string audience = "suite", string subjectClient = "native-app", params string[] scopes) =>
        Exchange(AccessToken(subjectClient), dpopJkt == "device" ? _device.Jkt : dpopJkt, audience, OidcConstants.TokenTypes.Ticket,
            scopes: scopes.Length == 0 ? new[] { "suite.pulse" } : scopes);

    private static TokenRequest Redeem(string ticket, ValidatedClient? client = null) => new()
    {
        GrantType = OidcConstants.GrantTypes.TokenExchange,
        Client = client ?? Suite(),
        SubjectToken = ticket,
        SubjectTokenType = OidcConstants.TokenTypes.Ticket,
    };

    private async Task<string> IssueTicketAsync()
    {
        var request = TicketRequest();
        var grant = await _handler.HandleAsync(request);
        grant.IsValid.Should().BeTrue(grant.ErrorDescription);
        return (await _tokens.CreateTokenResponseAsync(grant, request)).AccessToken!;
    }

    // ---- DPoP-bound subject tokens ----

    [Fact]
    public async Task A_bound_subject_without_a_proof_is_invalid_grant()
    {
        var result = await _handler.HandleAsync(Exchange(AccessToken(), dpopJkt: null));
        result.Error.Should().Be(OidcConstants.Errors.InvalidGrant);
    }

    [Fact]
    public async Task A_bound_subject_with_a_proof_by_another_key_is_invalid_grant()
    {
        var result = await _handler.HandleAsync(Exchange(AccessToken(), _attacker.Jkt));
        result.Error.Should().Be(OidcConstants.Errors.InvalidGrant);
    }

    [Fact]
    public async Task A_bound_subject_with_its_key_is_exchanged_without_inheriting_cnf_or_the_client()
    {
        var request = Exchange(AccessToken(), _device.Jkt);
        var result = await _handler.HandleAsync(request);

        result.IsValid.Should().BeTrue(result.ErrorDescription);
        result.Claims.Keys.Should().NotContain(new[] { "cnf", "client_id", "azp", "scope" });
        result.Claims["tenant_id"].Should().Be(Tenant);

        // The issued token is bound to the proof of this request, not copied from the subject.
        var response = await _tokens.CreateTokenResponseAsync(result, request);
        response.TokenType.Should().Be("DPoP");
        var issued = new JwtSecurityTokenHandler().ReadJwtToken(response.AccessToken);
        issued.Claims.Where(c => c.Type == "cnf").Should().ContainSingle()
            .Which.Value.Should().Contain(_device.Jkt);
        issued.Claims.Single(c => c.Type == "client_id").Value.Should().Be("native-app");
    }

    [Fact]
    public async Task An_unbound_subject_keeps_the_existing_behaviour()
    {
        var result = await _handler.HandleAsync(Exchange(AccessToken(jkt: null), dpopJkt: null));
        result.IsValid.Should().BeTrue(result.ErrorDescription);
    }

    [Fact]
    public async Task A_refresh_token_of_another_client_cannot_be_exchanged()
    {
        _grants.Setup(g => g.GetAsync("rt", It.IsAny<CancellationToken>())).ReturnsAsync(new PersistedGrant
        {
            Key = "rt", Type = "refresh_token", ClientId = "partner", SubjectId = "user-1", Expiration = DateTime.UtcNow.AddDays(1),
            Data = JsonSerializer.Serialize(new RefreshTokenData { CreatedAt = DateTime.UtcNow, Scopes = new[] { "openid" } }),
        });
        var request = Exchange("rt", null);
        request.SubjectTokenType = OidcConstants.TokenTypes.RefreshToken;

        (await _handler.HandleAsync(request)).Error.Should().Be(OidcConstants.Errors.InvalidGrant);
    }

    // ---- audience policy ----

    [Theory]
    [InlineData("partner")]          // not listed in token_exchange_audiences
    [InlineData("disabled-app")]     // listed, but disabled
    [InlineData("other-tenant-app")] // listed, but another tenant's client
    [InlineData("unknown-app")]      // not a client at all
    public async Task An_audience_outside_the_policy_is_invalid_target(string audience)
    {
        var result = await _handler.HandleAsync(Exchange(AccessToken(), _device.Jkt, audience));
        result.Error.Should().Be(OidcConstants.Errors.InvalidTarget);
    }

    [Fact]
    public async Task A_client_without_the_property_may_not_name_an_audience()
    {
        var client = NativeApp();
        client.Properties.Clear();
        var result = await _handler.HandleAsync(Exchange(AccessToken(), _device.Jkt, "suite", client: client));
        result.Error.Should().Be(OidcConstants.Errors.InvalidTarget);
    }

    // ---- tickets ----

    [Fact]
    public async Task A_ticket_is_a_short_lived_unbound_jwt_for_the_audience()
    {
        var request = TicketRequest();
        var grant = await _handler.HandleAsync(request);
        grant.IsValid.Should().BeTrue(grant.ErrorDescription);

        var response = await _tokens.CreateTokenResponseAsync(grant, request);

        response.IssuedTokenType.Should().Be(OidcConstants.TokenTypes.Ticket);
        response.TokenType.Should().Be("N_A");
        response.ExpiresIn.Should().BeInRange(1, 60);
        response.Scope.Should().Be("suite.pulse");
        response.RefreshToken.Should().BeNull();
        response.IdToken.Should().BeNull();

        var ticket = new JwtSecurityTokenHandler().ReadJwtToken(response.AccessToken);
        ticket.Header.Typ.Should().Be(OidcConstants.JwtTypes.Ticket);
        ticket.Issuer.Should().Be(Issuer);
        ticket.Subject.Should().Be("user-1");
        ticket.Audiences.Should().Equal("suite");
        ticket.Claims.Single(c => c.Type == "azp").Value.Should().Be("native-app");
        ticket.Claims.Single(c => c.Type == "scope").Value.Should().Be("suite.pulse");
        ticket.Claims.Single(c => c.Type == "tenant_id").Value.Should().Be(Tenant);
        ticket.Id.Should().NotBeNullOrEmpty();
        ticket.Claims.Should().NotContain(c => c.Type == "cnf" || c.Type == "client_id");
        (ticket.ValidTo - ticket.IssuedAt).Should().BeLessThanOrEqualTo(TimeSpan.FromSeconds(60));
    }

    [Fact]
    public async Task A_ticket_needs_the_subjects_dpop_key()
    {
        (await _handler.HandleAsync(TicketRequest(dpopJkt: null))).Error.Should().Be(OidcConstants.Errors.InvalidGrant);
        (await _handler.HandleAsync(TicketRequest(dpopJkt: _attacker.Jkt))).Error.Should().Be(OidcConstants.Errors.InvalidGrant);
    }

    [Fact]
    public async Task A_ticket_needs_a_subject_token_issued_to_the_requesting_client()
    {
        var result = await _handler.HandleAsync(TicketRequest(subjectClient: "partner"));
        result.Error.Should().Be(OidcConstants.Errors.InvalidGrant);
    }

    [Fact]
    public async Task A_ticket_scope_must_be_within_the_subject_tokens_scopes()
    {
        var result = await _handler.HandleAsync(TicketRequest(scopes: new[] { "suite.vault.invoices" }));
        result.Error.Should().Be(OidcConstants.Errors.InvalidScope);
    }

    [Fact]
    public async Task A_ticket_needs_an_audience()
    {
        var request = TicketRequest();
        request.Raw.Remove("audience");
        (await _handler.HandleAsync(request)).Error.Should().Be(OidcConstants.Errors.InvalidRequest);
    }

    [Fact]
    public async Task A_ticket_is_redeemed_once_by_its_audience_for_its_own_token()
    {
        var ticket = await IssueTicketAsync();

        var first = await _handler.HandleAsync(Redeem(ticket));
        first.IsValid.Should().BeTrue(first.ErrorDescription);
        first.SubjectId.Should().Be("user-1");
        first.Scopes.Should().Equal("suite.pulse");
        first.CustomData["issued_token_type"].Should().Be(OidcConstants.TokenTypes.AccessToken);
        first.Claims["tenant_id"].Should().Be(Tenant);

        var response = await _tokens.CreateTokenResponseAsync(first, Redeem(ticket));
        var issued = new JwtSecurityTokenHandler().ReadJwtToken(response.AccessToken);
        issued.Claims.Single(c => c.Type == "client_id").Value.Should().Be("suite");
        issued.Subject.Should().Be("user-1");

        var second = await _handler.HandleAsync(Redeem(ticket));
        second.IsValid.Should().BeFalse();
        second.Error.Should().Be(OidcConstants.Errors.InvalidGrant);
    }

    [Fact]
    public async Task Concurrent_redemptions_of_one_ticket_let_exactly_one_through()
    {
        var ticket = await IssueTicketAsync();
        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => _handler.HandleAsync(Redeem(ticket)))));
        results.Count(r => r.IsValid).Should().Be(1);
    }

    [Fact]
    public async Task Only_the_audience_client_can_redeem_a_ticket()
    {
        var ticket = await IssueTicketAsync();
        (await _handler.HandleAsync(Redeem(ticket, Suite("partner")))).Error.Should().Be(OidcConstants.Errors.InvalidGrant);
        (await _handler.HandleAsync(Redeem(ticket, Suite("suite", ClientAuthenticationMethod.None))))
            .Error.Should().Be(OidcConstants.Errors.UnauthorizedClient, "a public client cannot redeem");

        // Neither attempt consumed it.
        (await _handler.HandleAsync(Redeem(ticket))).IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task A_ticket_cannot_be_used_as_an_exchangeable_access_token()
    {
        var ticket = await IssueTicketAsync();
        var result = await _handler.HandleAsync(Exchange(ticket, null));
        result.Error.Should().Be(OidcConstants.Errors.InvalidGrant);
    }

    [Fact]
    public async Task An_access_token_cannot_be_redeemed_as_a_ticket()
    {
        var result = await _handler.HandleAsync(Redeem(AccessToken("suite", jkt: null)));
        result.Error.Should().Be(OidcConstants.Errors.InvalidGrant);
    }

    /// <summary>Action tokens in memory, with the store's atomic consume.</summary>
    private sealed class InMemoryActionTokens : IActionTokenStore
    {
        private readonly List<ActionToken> _tokens = new();
        private readonly object _gate = new();

        public Task<ActionToken> CreateAsync(ActionToken token, CancellationToken cancellationToken = default)
        {
            lock (_gate) _tokens.Add(token);
            return Task.FromResult(token);
        }

        public Task<ActionToken?> GetByHashAsync(string tokenHash, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                var found = _tokens.FirstOrDefault(t => t.TokenHash == tokenHash);
                return Task.FromResult(found == null ? null : new ActionToken
                {
                    Id = found.Id, TenantId = found.TenantId, TokenHash = found.TokenHash, UserId = found.UserId, Action = found.Action,
                    SubjectId = found.SubjectId, ExpiresAt = found.ExpiresAt, ConsumedAt = found.ConsumedAt,
                });
            }
        }

        public Task<bool> TryConsumeAsync(string id, DateTime consumedAtUtc, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                var token = _tokens.FirstOrDefault(t => t.Id == id && t.ConsumedAt == null);
                if (token == null) return Task.FromResult(false);
                token.ConsumedAt = consumedAtUtc;
                return Task.FromResult(true);
            }
        }

        public Task ReleaseAsync(string id, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<int> RemoveExpiredAsync(DateTime nowUtc, CancellationToken cancellationToken = default) => Task.FromResult(0);
    }
}

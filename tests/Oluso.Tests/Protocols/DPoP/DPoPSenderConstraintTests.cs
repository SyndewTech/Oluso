using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Oluso.Core.Domain.Entities;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols.DPoP;
using Oluso.Core.Protocols.Models;
using Oluso.Core.Protocols.Validation;
using Oluso.Core.Services;
using Oluso.Protocols.DPoP;
using Oluso.Protocols.Grants;
using Oluso.Protocols.Oidc;
using Oluso.Protocols.Validation;
using Xunit;

namespace Oluso.Tests.Protocols.DPoP;

/// <summary>
/// Sender-constrained (DPoP) tokens end to end on the server side: proof replay, the resource check that
/// JwtBearer runs for every API call, and the key binding of refresh tokens and authorization codes.
/// </summary>
public class DPoPSenderConstraintTests : IDisposable
{
    private const string Api = "https://acme.example.com/api/workspace/approvals/42/approve";
    private readonly DPoPTestKey _device = new();
    private readonly DPoPTestKey _attacker = new();

    public void Dispose()
    {
        _device.Dispose();
        _attacker.Dispose();
    }

    // ---- replay ----

    [Fact]
    public async Task A_replayed_proof_is_rejected()
    {
        var validator = DPoPTestKey.NewValidator(out _);
        var proof = _device.Proof("POST", "https://acme.example.com/connect/token");
        var context = new DPoPValidationContext { Proof = proof, HttpMethod = "POST", HttpUri = "https://acme.example.com/connect/token" };

        (await validator.ValidateAsync(context)).IsValid.Should().BeTrue();
        var replay = await validator.ValidateAsync(context);

        replay.IsValid.Should().BeFalse();
        replay.Error.Should().Be("invalid_dpop_proof");
    }

    [Fact]
    public async Task Concurrent_replays_of_one_jti_let_exactly_one_through()
    {
        var cache = new LocalDPoPReplayCache();
        var results = await Task.WhenAll(Enumerable.Range(0, 64)
            .Select(_ => Task.Run(() => cache.TryAddAsync("same-jti", TimeSpan.FromMinutes(1)))));
        results.Count(r => r).Should().Be(1);
    }

    [Fact]
    public async Task A_jti_is_free_again_once_it_has_expired()
    {
        var clock = new ManualClock();
        var cache = new LocalDPoPReplayCache(null, clock);
        (await cache.TryAddAsync("jti", TimeSpan.FromSeconds(70))).Should().BeTrue();
        (await cache.TryAddAsync("jti", TimeSpan.FromSeconds(70))).Should().BeFalse();
        clock.Advance(TimeSpan.FromSeconds(71));
        (await cache.TryAddAsync("jti", TimeSpan.FromSeconds(70))).Should().BeTrue();
    }

    [Fact]
    public async Task The_token_endpoint_demands_a_nonce_from_a_client_that_requires_dpop()
    {
        var validator = DPoPTestKey.NewValidator(out _);
        const string tokenEndpoint = "https://acme.example.com/connect/token";

        var first = await validator.ValidateAsync(new DPoPValidationContext
        {
            Proof = _device.Proof("POST", tokenEndpoint), HttpMethod = "POST", HttpUri = tokenEndpoint, RequireNonce = true, ClientId = "native-app",
        });
        first.Error.Should().Be("use_dpop_nonce");
        first.ServerNonce.Should().NotBeNullOrEmpty();

        var retry = await validator.ValidateAsync(new DPoPValidationContext
        {
            Proof = _device.Proof("POST", tokenEndpoint, nonce: first.ServerNonce), HttpMethod = "POST", HttpUri = tokenEndpoint,
            RequireNonce = true, ClientId = "native-app",
        });
        retry.IsValid.Should().BeTrue();
        retry.JwkThumbprint.Should().Be(_device.Jkt);
    }

    // ---- resource side (JwtBearer OnTokenValidated → IDPoPResourceValidator) ----

    private static DPoPResourceValidator Resource() => new(DPoPTestKey.NewValidator(out _));

    private static HttpRequest Request(string method, string url, string? proof, string? pathBase = null)
    {
        var uri = new Uri(url);
        var http = new DefaultHttpContext();
        http.Request.Method = method;
        http.Request.Scheme = uri.Scheme;
        http.Request.Host = new HostString(uri.Authority);
        if (pathBase != null)
        {
            http.Request.PathBase = pathBase;
            http.Request.Path = uri.AbsolutePath[pathBase.Length..];
        }
        else
        {
            http.Request.Path = uri.AbsolutePath;
        }
        if (proof != null)
            http.Request.Headers["DPoP"] = proof;
        return http.Request;
    }

    [Fact]
    public async Task A_bound_token_with_a_proof_by_its_key_is_accepted()
    {
        const string token = "header.payload.sig";
        var result = await Resource().ValidateAsync(Request("POST", Api, _device.Proof("POST", Api, token)), "DPoP", token, _device.Jkt);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task A_copied_bound_token_without_a_proof_is_rejected()
    {
        var result = await Resource().ValidateAsync(Request("POST", Api, proof: null), "DPoP", "header.payload.sig", _device.Jkt);
        result.IsValid.Should().BeFalse();
        result.Error.Should().Be("invalid_dpop_proof");
    }

    [Fact]
    public async Task A_copied_bound_token_with_the_thiefs_own_proof_is_rejected()
    {
        const string token = "header.payload.sig";
        var result = await Resource().ValidateAsync(Request("POST", Api, _attacker.Proof("POST", Api, token)), "DPoP", token, _device.Jkt);
        result.IsValid.Should().BeFalse();
        result.Error.Should().Be("invalid_dpop_proof");
    }

    [Fact]
    public async Task A_bound_token_presented_as_bearer_is_rejected()
    {
        const string token = "header.payload.sig";
        var result = await Resource().ValidateAsync(Request("POST", Api, _device.Proof("POST", Api, token)), "Bearer", token, _device.Jkt);
        result.IsValid.Should().BeFalse();
        result.Error.Should().Be("invalid_token");
    }

    [Fact]
    public async Task A_bound_token_in_the_query_string_is_rejected()
    {
        // The SignalR query-string path presents no Authorization scheme at all.
        var result = await Resource().ValidateAsync(Request("GET", "https://acme.example.com/hubs/realtime", proof: null), null, "t", _device.Jkt);
        result.IsValid.Should().BeFalse();
        result.Error.Should().Be("invalid_token");
    }

    [Fact]
    public async Task A_proof_for_another_token_method_or_url_is_rejected()
    {
        const string token = "header.payload.sig";
        var resource = Resource();
        (await resource.ValidateAsync(Request("POST", Api, _device.Proof("POST", Api, "another.token.here")), "DPoP", token, _device.Jkt))
            .Error.Should().Be("invalid_dpop_proof", "ath names another token");
        (await resource.ValidateAsync(Request("POST", Api, _device.Proof("GET", Api, token)), "DPoP", token, _device.Jkt))
            .Error.Should().Be("invalid_dpop_proof", "htm differs");
        (await resource.ValidateAsync(Request("POST", Api, _device.Proof("POST", "https://acme.example.com/api/other", token)), "DPoP", token, _device.Jkt))
            .Error.Should().Be("invalid_dpop_proof", "htu differs");
        (await resource.ValidateAsync(Request("POST", Api, _device.Proof("POST", Api, token, iat: DateTimeOffset.UtcNow.AddMinutes(-5))), "DPoP", token, _device.Jkt))
            .Error.Should().Be("invalid_dpop_proof", "iat is outside the window");
    }

    [Fact]
    public async Task The_same_resource_proof_cannot_be_replayed()
    {
        const string token = "header.payload.sig";
        var resource = Resource();
        var proof = _device.Proof("POST", Api, token);
        (await resource.ValidateAsync(Request("POST", Api, proof), "DPoP", token, _device.Jkt)).IsValid.Should().BeTrue();
        (await resource.ValidateAsync(Request("POST", Api, proof), "DPoP", token, _device.Jkt)).IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Htu_includes_the_path_base_a_reverse_proxy_forwards()
    {
        const string token = "header.payload.sig";
        const string url = "https://platform.example.com/oluso/api/workspace/approvals";
        var result = await Resource().ValidateAsync(Request("GET", url, _device.Proof("GET", url, token), pathBase: "/oluso"), "DPoP", token, _device.Jkt);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task An_unbound_token_stays_a_bearer_token()
    {
        (await Resource().ValidateAsync(Request("GET", Api, null), "Bearer", "t", null)).IsValid.Should().BeTrue();
        (await Resource().ValidateAsync(Request("GET", Api, null), "DPoP", "t", null)).Error.Should().Be("invalid_token");
    }

    // ---- JWTs that are not access tokens ----

    [Theory]
    [InlineData(OidcConstants.JwtTypes.Ticket, false)]
    [InlineData(OidcConstants.JwtTypes.Logout, false)]
    [InlineData("JWT", true)]
    [InlineData("at+jwt", true)]
    public void Only_access_token_types_pass_the_access_token_type_check(string typ, bool accepted)
    {
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "k" };
        var jwt = new JwtSecurityToken("https://acme.example.com", "suite", new[] { new Claim("sub", "user-1") },
            DateTime.UtcNow, DateTime.UtcNow.AddMinutes(1), new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
        jwt.Header["typ"] = typ;
        var raw = new JwtSecurityTokenHandler().WriteToken(jwt);

        var validate = () => new JwtSecurityTokenHandler().ValidateToken(raw, new TokenValidationParameters
        {
            ValidIssuer = "https://acme.example.com", ValidAudience = "suite", IssuerSigningKey = key,
            TypeValidator = AccessTokenTypes.Validate,
        }, out _);

        if (accepted) validate.Should().NotThrow();
        else validate.Should().Throw<SecurityTokenInvalidTypeException>();
    }

    // ---- refresh tokens and codes stay bound ----

    private static PersistedGrant BoundRefreshGrant(string jkt) => new()
    {
        Key = "rt", Type = "refresh_token", ClientId = "native-app", SubjectId = "user-1",
        CreationTime = DateTime.UtcNow, Expiration = DateTime.UtcNow.AddDays(1),
        Data = JsonSerializer.Serialize(new RefreshTokenData { CreatedAt = DateTime.UtcNow, Scopes = new[] { "openid" }, DPoPJkt = jkt }),
    };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_bound_refresh_token_needs_a_proof_by_its_key(bool withWrongKey)
    {
        var grants = new Mock<IPersistedGrantStore>();
        grants.Setup(g => g.GetAsync("rt", It.IsAny<CancellationToken>())).ReturnsAsync(BoundRefreshGrant(_device.Jkt));
        var handler = new RefreshTokenGrantHandler(grants.Object, Mock.Of<IProfileService>(), Mock.Of<IClientStore>(),
            Mock.Of<IOlusoUserService>(), Mock.Of<ITenantStore>(), Mock.Of<ITenantContext>(), ClientUserAccessKit.Open());

        var result = await handler.HandleAsync(new TokenRequest
        {
            RefreshToken = "rt",
            Client = new ValidatedClient { ClientId = "native-app", RefreshTokenExpiration = (int)TokenExpiration.Absolute },
            DPoPKeyThumbprint = withWrongKey ? _attacker.Jkt : null,
        });

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(OidcConstants.Errors.InvalidDPoPProof);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task A_code_bound_with_dpop_jkt_needs_a_proof_by_that_key(bool withProof, bool rightKey)
    {
        var codes = new Mock<IAuthorizationCodeStore>();
        codes.Setup(c => c.GetAsync("code", It.IsAny<CancellationToken>())).ReturnsAsync(new AuthorizationCode
        {
            Code = "code", ClientId = "native-app", SubjectId = "user-1", RedirectUri = "nativeapp://oluso-callback",
            Scopes = new List<string> { "openid" }, Expiration = DateTime.UtcNow.AddMinutes(5),
            Properties = new Dictionary<string, string> { [OidcProtocolService.AuthorizationCodeDPoPJktProperty] = _device.Jkt },
        });
        var profiles = new Mock<IProfileService>();
        profiles.Setup(p => p.IsActiveAsync(It.IsAny<IsActiveContext>())).Returns(Task.CompletedTask);
        profiles.Setup(p => p.GetProfileDataAsync(It.IsAny<ProfileDataRequestContext>())).Returns(Task.CompletedTask);
        var handler = new AuthorizationCodeGrantHandler(codes.Object, Mock.Of<IPkceValidator>(), profiles.Object,
            Mock.Of<IPersistedGrantStore>(), Mock.Of<IClientStore>(), ClientUserAccessKit.For(profiles.Object),
            NullLogger<AuthorizationCodeGrantHandler>.Instance);

        var result = await handler.HandleAsync(new TokenRequest
        {
            Code = "code",
            RedirectUri = "nativeapp://oluso-callback",
            Client = new ValidatedClient { ClientId = "native-app" },
            DPoPKeyThumbprint = !withProof ? null : rightKey ? _device.Jkt : _attacker.Jkt,
        });

        if (withProof && rightKey)
        {
            result.IsValid.Should().BeTrue();
        }
        else
        {
            result.IsValid.Should().BeFalse();
            result.Error.Should().Be(OidcConstants.Errors.InvalidDPoPProof);
        }
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan by) => _now += by;
    }
}

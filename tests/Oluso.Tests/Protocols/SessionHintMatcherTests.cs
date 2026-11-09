using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Oluso.Core.Domain.Interfaces;
using Oluso.Core.Protocols;
using Oluso.Core.Services;
using Oluso.Protocols;
using Xunit;

namespace Oluso.Tests.Protocols;

/// <summary>
/// The session stands in for a journey's sign-in only for the person the request names.
/// An id_token_hint names them by the sub this server issued to the client (pairwise or
/// not), and only if this server signed it for that client; a login_hint names them by
/// email, username or phone.
/// </summary>
public class SessionHintMatcherTests
{
    private const string Issuer = "https://auth.example.test";
    private static readonly RsaSecurityKey Key = new(RSA.Create(2048)) { KeyId = "k1" };

    private static SessionHintMatcher Matcher()
    {
        var keys = new Mock<ISigningCredentialStore>();
        keys.Setup(k => k.GetValidationKeysAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { new SecurityKeyInfo { Key = Key, SigningAlgorithm = SecurityAlgorithms.RsaSha256 } });
        var issuer = new Mock<IIssuerResolver>();
        issuer.Setup(i => i.GetIssuerAsync(It.IsAny<CancellationToken>())).ReturnsAsync(Issuer);
        var users = new Mock<IOlusoUserService>();
        users.Setup(u => u.FindByIdAsync("u1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new OlusoUserInfo { Id = "u1", Username = "jo", Email = "Jo@Acme.test", PhoneNumber = "+447700900001" });
        return new SessionHintMatcher(keys.Object, issuer.Object, users.Object, NullLogger<SessionHintMatcher>.Instance);
    }

    private static string IdToken(string sub, string audience = "app", SecurityKey? key = null, DateTime? expires = null) =>
        new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken(
            issuer: Issuer,
            audience: audience,
            claims: new[] { new Claim("sub", sub) },
            notBefore: (expires ?? DateTime.UtcNow.AddMinutes(5)).AddHours(-1),
            expires: expires ?? DateTime.UtcNow.AddMinutes(5),
            signingCredentials: new SigningCredentials(key ?? Key, SecurityAlgorithms.RsaSha256)));

    [Fact]
    public async Task No_hint_matches()
    {
        (await Matcher().MatchesAsync("u1", "app", null, null, null)).Should().BeTrue();
    }

    [Fact]
    public async Task An_id_token_for_the_session_user_matches_even_when_expired()
    {
        var hint = IdToken("u1", expires: DateTime.UtcNow.AddDays(-2));

        (await Matcher().MatchesAsync("u1", "app", null, hint, null)).Should().BeTrue();
    }

    [Fact]
    public async Task A_pairwise_id_token_for_the_session_user_matches()
    {
        var hint = IdToken(SubjectIdentifiers.ForClient("u1", "app", "salt"));

        (await Matcher().MatchesAsync("u1", "app", "salt", hint, null)).Should().BeTrue();
    }

    [Fact]
    public async Task An_id_token_for_someone_else_does_not_match()
    {
        (await Matcher().MatchesAsync("u1", "app", null, IdToken("u2"), null)).Should().BeFalse();
    }

    [Fact]
    public async Task An_id_token_issued_to_another_client_does_not_match()
    {
        (await Matcher().MatchesAsync("u1", "app", null, IdToken("u1", audience: "other-app"), null)).Should().BeFalse();
    }

    [Fact]
    public async Task An_id_token_this_server_did_not_sign_does_not_match()
    {
        var forged = IdToken("u1", key: new RsaSecurityKey(RSA.Create(2048)) { KeyId = "k1" });

        (await Matcher().MatchesAsync("u1", "app", null, forged, null)).Should().BeFalse();
    }

    [Fact]
    public async Task Garbage_does_not_match()
    {
        (await Matcher().MatchesAsync("u1", "app", null, "not-a-token", null)).Should().BeFalse();
    }

    [Theory]
    [InlineData("jo@acme.test")]
    [InlineData(" JO ")]
    [InlineData("+447700900001")]
    public async Task A_login_hint_naming_the_session_user_matches(string loginHint)
    {
        (await Matcher().MatchesAsync("u1", "app", null, null, loginHint)).Should().BeTrue();
    }

    [Fact]
    public async Task A_login_hint_naming_someone_else_does_not_match()
    {
        (await Matcher().MatchesAsync("u1", "app", null, null, "someone@else.test")).Should().BeFalse();
    }

    [Fact]
    public async Task Both_hints_must_name_the_session_user()
    {
        (await Matcher().MatchesAsync("u1", "app", null, IdToken("u1"), "someone@else.test")).Should().BeFalse();
    }
}
